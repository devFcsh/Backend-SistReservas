using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
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

        public ReservasController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==========================================================================
        // POST: api/entrada -> Registrar nueva reserva de laboratorio
        // ==========================================================================
        [HttpPost("entrada")]
        public async Task<IActionResult> RegistrarEntrada([FromBody] Reserva entrada)
        {
            if (entrada == null) 
                return BadRequest(new { error = "Los datos de ingreso enviados no son válidos." });

            // Validación de concurrencia: Evita que se dupliquen registros activos
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
        [HttpGet("reporte")]
        public async Task<IActionResult> ObtenerReporte()
        {
            var reporte = await _context.Reservas
                .OrderByDescending(r => r.HoraInicio)
                .Select(r => new {
                    r.Nombre,
                    r.Apellido,
                    r.Matricula,
                    r.Carrera,
                    r.Equipo, 
                    Fecha = r.HoraInicio.ToString("yyyy-MM-dd"),
                    HoraInicio = r.HoraInicio.ToString("HH:mm:ss"),
                    HoraSalida = r.HoraSalida.HasValue ? r.HoraSalida.Value.ToString("HH:mm:ss") : "En uso",
                    TiempoPromedio = r.TiempoPromedio ?? "N/A",
                    r.Periodo
                })
                .ToListAsync();

            return Ok(reporte);
        }

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
        // GET: estado de equipos
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
        // POST: Si se esta en clase, todas los equipos cambian a OCUPADOS
        // ==========================================================================

        [HttpPost("en-clase")]
        public async Task<IActionResult> MarcarTodosComoOcupados()
        {
            // Obtener equipos que ya están ocupados
            var ocupados = await _context.Reservas
                .Where(r => r.HoraSalida == null)
                .Select(r => r.Equipo)
                .ToListAsync();

            // Crear lista de equipos disponibles
            var todosLosEquipos = Enumerable.Range(1, 46).Select(i => $"Equipo {i}");
            var disponibles = todosLosEquipos.Except(ocupados);

            // Registrar entrada "En Clase" para cada disponible
            foreach (var equipo in disponibles)
            {
                _context.Reservas.Add(new Reserva {
                    Nombre = "En Clase",
                    Apellido = "N/A",
                    Matricula = "000000000",
                    Carrera = "Reservado por Profesor",
                    Periodo = "N/A",
                    Equipo = equipo,
                    Laboratorio = "L002",
                    HoraInicio = DateTime.Now,
                    HoraSalida = null
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "En clase:Todos los equipos han sido marcados como ocupados. " });
        }


        // ==========================================================================
        // PUT: una vez acabada la clase, regresar todos los equipos a DISPONIBLE
        // ==========================================================================
        [HttpPut("finalizar-clase")]
        public async Task<IActionResult> FinalizarClase()
        {
            // Buscamos todas las reservas abiertas de la matrícula genérica "000000000"
            var reservasEnClase = await _context.Reservas
                .Where(r => r.Matricula == "000000000" && r.HoraSalida == null)
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

    // Objeto de transferencia de datos de salida para el tipado estricto de la petición PUT
    public class SalidaRequest
    {
        public string Matricula { get; set; } = string.Empty;
    }


    
}