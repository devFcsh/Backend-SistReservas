using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
using Backend_SistReservas.Services;

namespace Backend_SistReservas.Controllers
{
    [ApiController]
    [Route("api")]
    public class EquiposController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EquiposCatalogoService _catalogo;
        private readonly ILogger<EquiposController> _logger;
        private readonly RelojLaboratorio _reloj;

        public EquiposController(ApplicationDbContext context, EquiposCatalogoService catalogo, ILogger<EquiposController> logger, RelojLaboratorio reloj)
        {
            _reloj = reloj;
            _context = context;
            _catalogo = catalogo;
            _logger = logger;
        }

        public class EquipoDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public bool TieneIp { get; set; }
            public bool Encendido { get; set; }
            public bool Ocupado { get; set; }
            public string Estado { get; set; } = string.Empty;
        }

        // ==========================================================================
        // GET: api/equipos -> catálogo completo (para el Panel de Equipos / admin)
        // El catálogo en sí vive en memoria (EquiposCatalogoService); lo único
        // que se consulta en base de datos es qué equipos están ocupados ahora
        // mismo (tabla `reservas`, que ya existía).
        // ==========================================================================
        [HttpGet("equipos")]
        public async Task<IActionResult> ObtenerEquipos()
        {
            var equipos = _catalogo.ObtenerTodos();
            var (ocupados, huboError) = await ObtenerNombresOcupados();

            var resultado = equipos.Select(e => Proyectar(e, ocupados.Contains(e.Nombre))).ToList();

            if (huboError)
            {
                // No se pudo confirmar qué equipos están ocupados (ej. la base de
                // datos no responde), pero el catálogo en sí (en memoria) sigue
                // disponible: se devuelve igual, avisando del problema, en vez de
                // fallar por completo y dejar el Panel de Equipos vacío.
                return Ok(new
                {
                    advertencia = "No se pudo consultar qué equipos están ocupados (falla de conexión a la base de datos). Los equipos se muestran, pero el campo 'ocupado' podría no ser exacto.",
                    equipos = resultado
                });
            }

            return Ok(resultado);
        }

        // ==========================================================================
        // GET: api/equipos/disponibles -> solo equipos reservables ahora mismo.
        // Es la fuente que debe usar el <select> del formulario de entrada.
        // ==========================================================================
        [HttpGet("equipos/disponibles")]
        public async Task<IActionResult> ObtenerDisponibles()
        {
            var equipos = _catalogo.ObtenerTodos();
            var (ocupados, _) = await ObtenerNombresOcupados();

            // Misma regla que usa POST /api/entrada (EquipoEstadoHelper.EsReservable),
            // para que el <select> del frontend nunca ofrezca un equipo que luego
            // el backend vaya a rechazar.
            var disponibles = equipos
                .Where(e => EquipoEstadoHelper.EsReservable(e, ocupados.Contains(e.Nombre)))
                .Select(e => new { e.Id, e.Nombre })
                .ToList();

            return Ok(disponibles);
        }

        // ==========================================================================
        // POST: api/equipos -> registrar un nuevo equipo en el catálogo (EN MEMORIA)
        //
        // ADVERTENCIA: como el catálogo vive en memoria, este equipo se pierde si
        // el backend se reinicia. Para que sobreviva un reinicio, agrégalo en
        // appsettings.json -> "Laboratorio:Equipos".
        // ==========================================================================
        public class CrearEquipoRequest
        {
            public string Nombre { get; set; } = string.Empty;
            public string? Ip { get; set; }
            public bool Activo { get; set; } = true;
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpPost("equipos")]
        public IActionResult CrearEquipo([FromBody] CrearEquipoRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Nombre))
                return BadRequest(new { error = "El nombre del equipo es requerido." });

            var (ok, error, equipo) = _catalogo.Crear(request.Nombre.Trim(), request.Ip, request.Activo);
            if (!ok || equipo == null)
                return BadRequest(new { error });

            return Ok(Proyectar(equipo, ocupado: false));
        }

        // ==========================================================================
        // PUT: api/equipos/{id}/activo -> marcar como colocado / retirado (en memoria)
        // ==========================================================================
        public class ActivoRequest
        {
            public bool Activo { get; set; }
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpPut("equipos/{id}/activo")]
        public async Task<IActionResult> ActualizarActivo(int id, [FromBody] ActivoRequest request)
        {
            var equipo = _catalogo.ActualizarActivo(id, request.Activo);
            if (equipo == null) return NotFound(new { error = "Equipo no encontrado." });

            if (!request.Activo)
            {
                await CerrarReservaActivaDelEquipo(equipo.Nombre);
            }

            return Ok(Proyectar(equipo, ocupado: false));
        }

        // ==========================================================================
        // PUT: api/equipos/{id}/ip -> asignar/actualizar la IP para el ping (en memoria)
        //
        // ADVERTENCIA: no sobrevive un reinicio del backend. Para una IP
        // permanente, configúrala en appsettings.json -> "Laboratorio:Equipos".
        // ==========================================================================
        public class IpRequest
        {
            public string? Ip { get; set; }
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpPut("equipos/{id}/ip")]
        public async Task<IActionResult> ActualizarIp(int id, [FromBody] IpRequest request)
        {
            var equipo = _catalogo.ActualizarIp(id, request.Ip);
            if (equipo == null) return NotFound(new { error = "Equipo no encontrado." });

            if (equipo.Ip == null)
            {
                await CerrarReservaActivaDelEquipo(equipo.Nombre);
            }

            return Ok(Proyectar(equipo, ocupado: false));
        }

        // ==========================================================================
        // PUT: api/equipos/{id}/encendido -> override manual (uso: pruebas / soporte).
        // El próximo ciclo del ping en background puede sobrescribir este valor
        // si el equipo tiene IP registrada.
        // ==========================================================================
        public class EncendidoRequest
        {
            public bool Encendido { get; set; }
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpPut("equipos/{id}/encendido")]
        public async Task<IActionResult> ActualizarEncendido(int id, [FromBody] EncendidoRequest request)
        {
            var equipo = _catalogo.ActualizarEncendido(id, request.Encendido);
            if (equipo == null) return NotFound(new { error = "Equipo no encontrado." });

            if (!request.Encendido)
            {
                await CerrarReservaActivaDelEquipo(equipo.Nombre);
            }

            return Ok(Proyectar(equipo, ocupado: false));
        }

        // ==========================================================================
        // DELETE: api/equipos/{id} -> eliminar del catálogo (en memoria)
        // ==========================================================================
        [Authorize(Roles = Roles.Admin)]
        [HttpDelete("equipos/{id}")]
        public async Task<IActionResult> EliminarEquipo(int id)
        {
            var equipo = _catalogo.ObtenerPorId(id);
            if (equipo == null) return NotFound(new { error = "Equipo no encontrado." });

            bool tieneReservaActiva = await _context.Reservas
                .AnyAsync(r => r.Equipo == equipo.Nombre && r.HoraSalida == null);

            if (tieneReservaActiva)
            {
                return BadRequest(new
                {
                    error = "No se puede eliminar un equipo con una reserva activa. Registre la salida primero."
                });
            }

            _catalogo.Eliminar(id);
            return Ok(new { message = $"Equipo '{equipo.Nombre}' eliminado del catálogo." });
        }

        // ==========================================================================
        // Helpers privados
        // ==========================================================================
        // Devuelve los equipos ocupados y si hubo un error consultando la BD.
        // Tolerante a fallos: si la base de datos no responde (ej. credenciales
        // incorrectas, servidor caído), no se deja caer toda la petición — se
        // informa el problema y se sigue adelante con lo que sí se puede mostrar
        // (el catálogo en memoria).
        private async Task<(HashSet<string> ocupados, bool huboError)> ObtenerNombresOcupados()
        {
            try
            {
                var ocupados = await _context.Reservas
                    .Where(r => r.HoraSalida == null)
                    .Select(r => r.Equipo)
                    .ToListAsync();
                return (ocupados.ToHashSet(), false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo consultar la tabla 'reservas' para saber qué equipos están ocupados.");
                return (new HashSet<string>(), true);
            }
        }

        private async Task CerrarReservaActivaDelEquipo(string nombreEquipo)
        {
            var activa = await _context.Reservas
                .Where(r => r.Equipo == nombreEquipo && r.HoraSalida == null)
                .FirstOrDefaultAsync();

            if (activa == null) return;

            var ahora = _reloj.Ahora;
            activa.HoraSalida = ahora;
            activa.TiempoPromedio = FormatearTiempo(activa.HoraInicio, ahora);
            await _context.SaveChangesAsync();
        }

        private static string FormatearTiempo(DateTime inicio, DateTime fin)
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

        private static EquipoDto Proyectar(Equipo e, bool ocupado)
        {
            return new EquipoDto
            {
                Id = e.Id,
                Nombre = e.Nombre,
                Activo = e.Activo,
                TieneIp = !string.IsNullOrWhiteSpace(e.Ip),
                Encendido = e.Encendido,
                Ocupado = ocupado,
                Estado = EquipoEstadoHelper.CalcularEstado(e, ocupado)
            };
        }
    }
}
