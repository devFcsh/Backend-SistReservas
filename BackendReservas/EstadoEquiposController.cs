using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Backend_SistReservas.Services;

namespace Backend_SistReservas.Controllers
{
    [ApiController]
    [Route("api")]
    public class EstadoEquiposController : ControllerBase
    {
        private readonly PingService _pingService;

        // ==========================================================================
        // MAPEO EQUIPO -> IP
        //
        // ⚠️ IPS DE PRUEBA: estas NO son las IPs reales del laboratorio.
        // Sirven únicamente para validar que el flujo ping -> color funciona
        // correctamente antes de configurar las IPs reales de las 46 PCs.
        //
        //   - 127.0.0.1  -> loopback, siempre responde     => debería salir "naranja"
        //   - 8.8.8.8    -> DNS público de Google, responde => debería salir "naranja"
        //   - 192.0.2.1  -> IP reservada para pruebas (RFC 5737, TEST-NET-1),
        //                   no existe en ninguna red real, nunca responde
        //                   => debería salir "morado" de forma consistente
        //
        // Cuando tengas las IPs reales de las PCs del laboratorio, simplemente
        // reemplaza estas 3 líneas y agrega las 46 entradas (Equipo 1 .. Equipo 46).
        // ==========================================================================
        private static readonly Dictionary<string, string> EquiposIp = new()
        {
            { "Equipo 1", "127.0.0.1" },   // Prueba: debería dar "naranja" (encendida)
            { "Equipo 2", "8.8.8.8" },     // Prueba: debería dar "naranja" (encendida)
            { "Equipo 3", "192.0.2.1" },   // Prueba: debería dar "morado" (apagada)

            // "Equipo 4"  => "192.168.1.4",
            // ...
            // "Equipo 46" => "192.168.1.46",
        };

        public EstadoEquiposController(PingService pingService)
        {
            _pingService = pingService;
        }

        // ==========================================================================
        // GET: api/equipos-estado-red/{equipo} -> Verifica el estado de un equipo puntual
        // Ejemplo: GET /api/equipos-estado-red/Equipo%201
        // ==========================================================================
        [HttpGet("equipos-estado-red/{equipo}")]
        public async Task<IActionResult> VerificarEquipo(string equipo)
        {
            if (!EquiposIp.TryGetValue(equipo, out var ip))
            {
                return NotFound(new { error = $"No hay una IP configurada para '{equipo}'." });
            }

            var resultado = await _pingService.VerificarPcAsync(ip);

            return Ok(new
            {
                equipo,
                ip = resultado.Ip,
                estado = resultado.Estado.ToString(),
                color = resultado.Color,
                tiempoRespuestaMs = resultado.TiempoRespuestaMs
            });
        }

        // ==========================================================================
        // GET: api/equipos-estado-red -> Verifica el estado de TODOS los equipos
        // con IP configurada, en paralelo.
        // ==========================================================================
        [HttpGet("equipos-estado-red")]
        public async Task<IActionResult> VerificarTodosLosEquipos()
        {
            if (EquiposIp.Count == 0)
            {
                return Ok(new { mensaje = "Aún no hay IPs configuradas para los equipos.", equipos = new List<object>() });
            }

            var tareas = EquiposIp.Select(async kv =>
            {
                var (equipo, ip) = (kv.Key, kv.Value);
                var resultado = await _pingService.VerificarPcAsync(ip);

                return new
                {
                    equipo,
                    ip = resultado.Ip,
                    estado = resultado.Estado.ToString(),
                    color = resultado.Color,
                    tiempoRespuestaMs = resultado.TiempoRespuestaMs
                };
            });

            var resultados = await Task.WhenAll(tareas);

            return Ok(resultados);
        }
    }
}