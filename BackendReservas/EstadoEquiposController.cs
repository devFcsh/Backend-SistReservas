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
            { "Equipo 1",  "200.126.2.70" },
            { "Equipo 2",  "200.126.2.71" },
            { "Equipo 3",  "200.126.2.72" },
            { "Equipo 4",  "200.126.2.73" },
            { "Equipo 5",  "200.126.2.74" },
            { "Equipo 6",  "200.126.2.75" },
            { "Equipo 7",  "200.126.2.76" },
            { "Equipo 8",  "200.126.2.77" },
            { "Equipo 9",  "200.126.2.78" },
            { "Equipo 10", "200.126.2.79" },
            { "Equipo 11", "200.126.2.80" },
            { "Equipo 12", "200.126.2.81" },
            { "Equipo 13", "200.126.2.82" },
            { "Equipo 14", "200.126.2.83" },
            { "Equipo 15", "200.126.2.84" },
            { "Equipo 16", "200.126.2.85" },
            { "Equipo 17", "200.126.2.86" },
            { "Equipo 18", "200.126.2.87" },
            { "Equipo 19", "200.126.2.88" },
            { "Equipo 20", "200.126.2.89" },
            { "Equipo 21", "200.126.2.90" },
            { "Equipo 22", "200.126.2.91" },
            { "Equipo 23", "200.126.2.92" },
            { "Equipo 24", "200.126.2.93" },
            { "Equipo 25", "200.126.2.94" },
            { "Equipo 26", "200.126.2.95" },
            { "Equipo 27", "200.126.2.96" },
            { "Equipo 28", "200.126.2.97" },
            { "Equipo 29", "200.126.2.98" },
            { "Equipo 30", "200.126.2.99" },
            { "Equipo 31", "200.126.2.100" },
            { "Equipo 32", "200.126.2.101" },
            { "Equipo 33", "200.126.2.102" },
            { "Equipo 34", "200.126.2.103" },
            { "Equipo 35", "200.126.2.104" },
            { "Equipo 36", "200.126.2.105" },
            { "Equipo 37", "200.126.2.106" }
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