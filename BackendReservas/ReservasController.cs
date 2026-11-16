using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
using Backend_SistReservas.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Backend_SistReservas.Controllers
{
    [ApiController]
    [Route("api")] // Raíz base unificada con las peticiones de React
    public class ReservasController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EquiposCatalogoService _catalogo;
        private static readonly KeyedAsyncLock Bloqueos = new();

        // Matrícula genérica de las filas creadas por "En clase".
        private const string MatriculaClase = "000000000";

        public ReservasController(ApplicationDbContext context, EquiposCatalogoService catalogo)
        {
            _context = context;
            _catalogo = catalogo;
        }

        // ==========================================================================
        // POST: api/entrada -> Registrar nueva reserva de laboratorio
        //
        // Todas las reglas de negocio se validan aquí en el backend (fuente de
        // verdad), sin importar si la petición viene del formulario de React o
        // llega manipulada directamente a la API:
        //   - Matrícula: exactamente 9 dígitos numéricos.
        //   - Nombre / Apellido: solo letras (con tildes/ñ) y espacios.
        //   - El equipo debe existir, estar colocado (activo), tener IP y estar
        //     encendido según el último ping (estado == "Disponible").
        //   - Ni la matrícula ni el equipo pueden tener ya una reserva activa.
        // ==========================================================================
        [HttpPost("entrada")]
        public async Task<IActionResult> RegistrarEntrada([FromBody] Reserva entrada)
        {
            if (entrada == null)
                return BadRequest(new { error = "Los datos de ingreso enviados no son válidos." });

            if (!ValidacionesHelper.EsMatriculaValida(entrada.Matricula))
                return BadRequest(new { error = "La matrícula debe contener exactamente 9 dígitos numéricos." });

            if (!ValidacionesHelper.EsNombreValido(entrada.Nombre))
                return BadRequest(new { error = "El nombre solo puede contener letras y espacios." });

            if (!ValidacionesHelper.EsNombreValido(entrada.Apellido))
                return BadRequest(new { error = "El apellido solo puede contener letras y espacios." });

            if (string.IsNullOrWhiteSpace(entrada.Equipo))
                return BadRequest(new { error = "Debe seleccionar un equipo." });

            entrada.Matricula = entrada.Matricula.Trim();
            entrada.Nombre = entrada.Nombre.Trim();
            entrada.Apellido = entrada.Apellido.Trim();
            entrada.Equipo = entrada.Equipo.Trim();

            // Bloqueo en memoria por equipo + por matrícula (en ese orden fijo,
            // para no arriesgar un deadlock entre dos peticiones cruzadas).
            // Esto serializa entradas concurrentes para el MISMO equipo o la
            // MISMA matrícula, sin depender de ningún candado a nivel de base
            // de datos (el catálogo de equipos ya no vive en una tabla).
            using var _lockEquipo = await Bloqueos.AcquireAsync($"equipo:{entrada.Equipo}");
            using var _lockMatricula = await Bloqueos.AcquireAsync($"matricula:{entrada.Matricula}");

            var equipo = _catalogo.ObtenerPorNombre(entrada.Equipo);

            if (equipo == null)
            {
                return BadRequest(new { error = $"El equipo '{entrada.Equipo}' no existe en el catálogo del laboratorio." });
            }

            bool equipoOcupado = await _context.Reservas
                .AnyAsync(r => r.Equipo == entrada.Equipo && r.HoraSalida == null);

            if (!EquipoEstadoHelper.EsReservable(equipo, equipoOcupado))
            {
                string estadoActual = EquipoEstadoHelper.CalcularEstado(equipo, equipoOcupado);
                return BadRequest(new { error = $"El equipo '{entrada.Equipo}' no está disponible para reservar (estado: {estadoActual})." });
            }

            bool tieneEntradaActiva = await _context.Reservas
                .AnyAsync(r => r.Matricula == entrada.Matricula && r.HoraSalida == null);

            if (tieneEntradaActiva)
            {
                return BadRequest(new { error = "El alumno ya cuenta con un registro de entrada activo en el laboratorio." });
            }

            // Sanitización e inicialización segura en el servidor
            entrada.Laboratorio = "L002";
            entrada.HoraInicio = DateTime.Now;
            entrada.HoraSalida = null;
            entrada.TiempoPromedio = null;
            entrada.Año = DateTime.Now.Year;

            _context.Reservas.Add(entrada);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Entrada registrada con éxito para la matrícula {entrada.Matricula}" });
        }

        // ==========================================================================
        // PUT: api/salida -> Cerrar sesión activa calculando tiempos de uso
        // ==========================================================================
        [HttpPut("salida")]
        public async Task<IActionResult> RegistrarSalida([FromBody] SalidaRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Matricula)) 
                return BadRequest(new { message = "La matrícula es requerida para el registro." });

            // Busca el último registro abierto correspondiente a la matrícula
            var ultimaReserva = await _context.Reservas
                .Where(r => r.Matricula == request.Matricula && r.HoraSalida == null)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (ultimaReserva == null)
            {
                return BadRequest(new { message = "No se encontró una entrada activa para esta matrícula." });
            }

            DateTime horaActual = DateTime.Now;
            ultimaReserva.HoraSalida = horaActual;
            
            // Uso del método privado
            ultimaReserva.TiempoPromedio = FormatearTiempo(ultimaReserva.HoraInicio, horaActual);

            _context.Reservas.Update(ultimaReserva);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Salida registrada con éxito. Tiempo de uso: {ultimaReserva.TiempoPromedio}." });
        }

        // ==========================================================================
        // GET: api/reporte -> Obtener el listado histórico detallado para tablas/Excel
        // ==========================================================================
        // Incluye el "id" de cada fila: el frontend lo necesita para editar
        // (PUT api/reservas/{id}) o eliminar (DELETE api/reservas/{id}).
        [HttpGet("reporte")]
        public async Task<IActionResult> ObtenerReporte()
        {
            var reservas = await _context.Reservas.AsNoTracking()
                .OrderByDescending(r => r.HoraInicio)
                .ToListAsync();

            return Ok(reservas.Select(ProyectarFilaReporte));
        }

        // ==========================================================================
        // PUT: api/reservas/{id} -> editar una fila del panel de reservas
        // (roles Admin y Laboratorio).
        //
        // Solo se pueden corregir los datos del alumno y el periodo. Equipo,
        // horas y tiempo de uso los maneja el sistema y no se editan aquí.
        // Los campos que no se envíen (null) se dejan como están.
        // ==========================================================================
        [HttpPut("reservas/{id}")]
        public async Task<IActionResult> EditarReserva(int id, [FromBody] EditarReservaRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Los datos enviados no son válidos." });

            var reserva = await _context.Reservas.FirstOrDefaultAsync(r => r.Id == id);
            if (reserva == null)
                return NotFound(new { error = "Reserva no encontrada." });

            bool esFilaDeClase = reserva.Matricula == MatriculaClase;

            if (request.Matricula != null)
            {
                var nuevaMatricula = request.Matricula.Trim();

                if (!ValidacionesHelper.EsMatriculaValida(nuevaMatricula))
                    return BadRequest(new { error = "La matrícula debe contener exactamente 9 dígitos numéricos." });

                if (nuevaMatricula != reserva.Matricula)
                {
                    // La matrícula genérica marca las filas creadas por "En clase";
                    // cambiarla haría que "Finalizar clase" ya no las libere.
                    if (esFilaDeClase || nuevaMatricula == MatriculaClase)
                        return BadRequest(new { error = $"La matrícula {MatriculaClase} es reservada para las clases y no se puede asignar ni cambiar." });

                    // Si la fila sigue en uso, la nueva matrícula no puede tener ya otra entrada activa.
                    if (reserva.HoraSalida == null)
                    {
                        using var _lock = await Bloqueos.AcquireAsync($"matricula:{nuevaMatricula}");
                        bool tieneOtraActiva = await _context.Reservas
                            .AnyAsync(r => r.Id != id && r.Matricula == nuevaMatricula && r.HoraSalida == null);
                        if (tieneOtraActiva)
                            return BadRequest(new { error = "Esa matrícula ya tiene otra entrada activa en el laboratorio." });
                    }

                    reserva.Matricula = nuevaMatricula;
                }
            }

            if (request.Nombre != null)
            {
                if (!ValidacionesHelper.EsNombreValido(request.Nombre))
                    return BadRequest(new { error = "El nombre solo puede contener letras y espacios." });
                reserva.Nombre = request.Nombre.Trim();
            }

            if (request.Apellido != null)
            {
                if (!ValidacionesHelper.EsNombreValido(request.Apellido))
                    return BadRequest(new { error = "El apellido solo puede contener letras y espacios." });
                reserva.Apellido = request.Apellido.Trim();
            }

            if (request.Carrera != null)
            {
                var carrera = request.Carrera.Trim();
                if (carrera.Length == 0 || carrera.Length > 100)
                    return BadRequest(new { error = "La carrera es requerida (máximo 100 caracteres)." });
                reserva.Carrera = carrera;
            }

            if (request.Periodo != null)
            {
                var periodo = request.Periodo.Trim();
                if (periodo.Length == 0 || periodo.Length > 10)
                    return BadRequest(new { error = "El periodo es requerido (máximo 10 caracteres)." });
                reserva.Periodo = periodo;
            }

            await _context.SaveChangesAsync();

            return Ok(ProyectarFilaReporte(reserva));
        }

        // ==========================================================================
        // DELETE: api/reservas/{id} -> eliminar una fila (SOLO Admin).
        // Si la fila estaba en uso, el equipo queda libre de inmediato.
        // ==========================================================================
        [Authorize(Roles = Roles.Admin)]
        [HttpDelete("reservas/{id}")]
        public async Task<IActionResult> EliminarReserva(int id)
        {
            var reserva = await _context.Reservas.FirstOrDefaultAsync(r => r.Id == id);
            if (reserva == null)
                return NotFound(new { error = "Reserva no encontrada." });

            bool estabaActiva = reserva.HoraSalida == null;

            _context.Reservas.Remove(reserva);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = estabaActiva
                    ? $"Reserva eliminada. El {reserva.Equipo} quedó libre."
                    : "Reserva eliminada."
            });
        }

        // Forma de cada fila del panel de reservas / Excel.
        private static object ProyectarFilaReporte(Reserva r) => new
        {
            r.Id,
            r.Nombre,
            r.Apellido,
            r.Matricula,
            r.Carrera,
            r.Equipo,
            Fecha = r.HoraInicio.ToString("yyyy-MM-dd"),
            HoraInicio = r.HoraInicio.ToString("HH:mm:ss"),
            HoraSalida = r.HoraSalida.HasValue ? r.HoraSalida.Value.ToString("HH:mm:ss") : "En uso",
            TiempoPromedio = r.TiempoPromedio ?? "N/A",
            r.Periodo,
            r.Año
        };

        // ==========================================================================
        // GET: api/alumno/{matricula} -> Autocompletado/Historial por alumno
        // ==========================================================================
        [HttpGet("alumno/{matricula}")]
        public async Task<IActionResult> ObtenerAlumnoPorMatricula(string matricula)
        {
            var ultimoRegistro = await _context.Reservas
                .Where(r => r.Matricula == matricula)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (ultimoRegistro != null)
            {
                return Ok(new { 
                    nombre = ultimoRegistro.Nombre, 
                    apellido = ultimoRegistro.Apellido, 
                    encontrado = true 
                });
            }

            return Ok(new { encontrado = false });
        }


        // ==========================================================================
        // GET: estado de equipos (lista simple de nombres ocupados; se mantiene
        // por compatibilidad con el frontend existente). Para el estado completo
        // (Disponible/Ocupado/No disponible/No colocado) usar GET /api/equipos.
        // ==========================================================================
        [HttpGet("equipos-estado")]
        public async Task<IActionResult> ObtenerEstadoEquipos()
        {
            // Obtenemos solo las reservas activas (sin hora de salida)
            var ocupados = await _context.Reservas
                .Where(r => r.HoraSalida == null)
                .Select(r => r.Equipo)
                .ToListAsync();

            return Ok(ocupados);
        }

        // ==========================================================================
        // POST: Si se esta en clase, todas los equipos REALMENTE reservables
        // (colocados, con IP y encendidos) cambian a OCUPADOS.
        // ==========================================================================
         public class EnClaseRequest 
        {
            public string Docente { get; set; } = string.Empty;
            public string Periodo { get; set; } = string.Empty;
        }

        [HttpPost("en-clase")]
        public async Task<IActionResult> MarcarTodosComoOcupados ([FromBody] EnClaseRequest request)
        {

            if (string.IsNullOrEmpty(request.Docente)) request.Docente = "Profesor";
            if (string.IsNullOrEmpty(request.Periodo)) request.Periodo = "I PAO";

        
            // Obtener equipos que ya están ocupados
            var ocupados = await _context.Reservas
                .Where(r => r.HoraSalida == null)
                .Select(r => r.Equipo)
                .ToListAsync();

            // Catálogo dinámico (en memoria): solo equipos realmente reservables
            // ahora mismo (colocados, con IP registrada y respondiendo al ping).
            var todosLosEquiposReservables = _catalogo.ObtenerTodos()
                .Where(e => e.Activo && !string.IsNullOrWhiteSpace(e.Ip) && e.Encendido)
                .Select(e => e.Nombre)
                .ToList();

            var disponibles = todosLosEquiposReservables.Except(ocupados);

            // Registrar entrada "En Clase" para cada disponible
            foreach (var equipo in disponibles)
            {
                _context.Reservas.Add(new Reserva {
                    Nombre = "En Clase",
                    Apellido = "N/A",
                    Matricula = MatriculaClase,
                    Carrera = $"Reservado por {request.Docente}",
                    Periodo = request.Periodo,
                    Equipo = equipo,
                    Laboratorio = "L002",
                    HoraInicio = DateTime.Now,
                    HoraSalida = null,
                    Año = DateTime.Now.Year,

                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "En clase: Todos los equipos disponibles han sido marcados como ocupados. " });
        }


        // ==========================================================================
        // PUT: una vez acabada la clase, regresar todos los equipos a DISPONIBLE
        // ==========================================================================
        [HttpPut("finalizar-clase")]
        public async Task<IActionResult> FinalizarClase()
        {
            // Buscamos todas las reservas abiertas de la matrícula genérica "000000000"
            var reservasEnClase = await _context.Reservas
                .Where(r => r.Matricula == MatriculaClase && r.HoraSalida == null)
                .ToListAsync();


            DateTime horaActual = DateTime.Now;


            foreach (var r in reservasEnClase)
            {
                r.HoraSalida = horaActual;
                // Uso del método privado
                r.TiempoPromedio = FormatearTiempo(r.HoraInicio, horaActual);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Fin de clase! Todos los equipos han sido liberados." });
        }

        // ==========================================================================
        // MÉTODO PRIVADO : para calcular el tiempo promedio
        // ==========================================================================
        private string FormatearTiempo(DateTime inicio, DateTime fin)
        {
            int minutosTotales = (int)(fin - inicio).TotalMinutes;
            if (minutosTotales < 1) minutosTotales = 1;

            if (minutosTotales >= 60)
            {
                int horas = minutosTotales / 60;
                int minutosRestantes = minutosTotales % 60;
                return minutosRestantes == 0 ? $"{horas} h" : $"{horas} h {minutosRestantes} min";
            }
            return $"{minutosTotales} min";
        }




    }

    // Datos editables de una fila de reservas (PUT api/reservas/{id}).
    // Todos opcionales: lo que venga en null no se modifica.
    public class EditarReservaRequest
    {
        public string? Matricula { get; set; }
        public string? Nombre { get; set; }
        public string? Apellido { get; set; }
        public string? Carrera { get; set; }
        public string? Periodo { get; set; }
    }

    // Objeto de transferencia de datos de salida para el tipado estricto de la petición PUT
    public class SalidaRequest
    {
        public string Matricula { get; set; } = string.Empty;
    }


    
}
