using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Backend_SistReservas.Services;

namespace Backend_SistReservas.Controllers
{
    // ==========================================================================
    // Estado de RED de los equipos.
    //
    // El mapeo equipo -> IP ya no vive hardcodeado en un diccionario suelto en
    // este archivo: ahora lo gestiona EquiposCatalogoService (sembrado desde
    // appsettings.json -> "Laboratorio:Equipos", modificable en caliente vía
    // EquiposController). El resultado del último ping periódico (ejecutado
    // por PingBackgroundService) también se guarda ahí, en memoria.
    //
    // Por eso, para el listado completo simplemente leemos el valor cacheado
    // en memoria (rápido, no bloquea al frontend con pings en vivo en cada
    // refresco del panel). Para una verificación puntual de UN equipo sí se
    // ofrece un ping en vivo bajo demanda.
    // ==========================================================================
    [ApiController]
    [Route("api")]
    public class EstadoEquiposController : ControllerBase
    {
        private readonly EquiposCatalogoService _catalogo;
        private readonly PingService _pingService;

        public EstadoEquiposController(EquiposCatalogoService catalogo, PingService pingService)
        {
            _catalogo = catalogo;
            _pingService = pingService;
        }

        // ==========================================================================
        // GET: api/equipos-estado-red/{equipo} -> ping EN VIVO de un equipo puntual
        // Ejemplo: GET /api/equipos-estado-red/Equipo%201
        // ==========================================================================
        [HttpGet("equipos-estado-red/{equipo}")]
        public async Task<IActionResult> VerificarEquipo(string equipo)
        {
            var registro = _catalogo.ObtenerPorNombre(equipo);

            if (registro == null)
                return NotFound(new { error = $"El equipo '{equipo}' no existe en el catálogo." });

            if (string.IsNullOrWhiteSpace(registro.Ip))
            {
                return Ok(new
                {
                    equipo,
                    ip = (string?)null,
                    estado = EquipoEstadoHelper.NoDisponible,
                    tiempoRespuestaMs = (long?)null
                });
            }

            var resultado = await _pingService.VerificarPcAsync(registro.Ip);

            return Ok(new
            {
                equipo,
                ip = resultado.Ip,
                estado = resultado.Estado.ToString(),
                tiempoRespuestaMs = resultado.TiempoRespuestaMs
            });
        }

        // ==========================================================================
        // GET: api/equipos-estado-red -> estado CACHEADO (última actualización del
        // proceso de background) de todos los equipos con IP configurada.
        // ==========================================================================
        [HttpGet("equipos-estado-red")]
        public IActionResult ObtenerEstadoCacheado()
        {
            var equipos = _catalogo.ObtenerTodos()
                .Where(e => e.Ip != null)
                .OrderBy(e => e.Id)
                .ToList();

            if (equipos.Count == 0)
            {
                return Ok(new { mensaje = "Aún no hay IPs configuradas para los equipos.", equipos = Array.Empty<object>() });
            }

            var resultado = equipos.Select(e => new
            {
                equipo = e.Nombre,
                ip = e.Ip,
                estado = e.Encendido ? "Encendida" : "Apagada",
                ultimoPing = e.UltimoPing
            });

            return Ok(resultado);
        }
    }
}
