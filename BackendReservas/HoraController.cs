using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend_SistReservas.Services;

namespace Backend_SistReservas.Controllers
{
    // ==========================================================================
    // GET: api/hora -> hora oficial del sistema (Ecuador), la misma con la que
    // se registran entradas, salidas y el cierre automático.
    //
    // El frontend debe usar ESTA hora para mostrar el reloj o calcular fechas,
    // no el reloj de la PC donde está abierto el navegador. También sirve de
    // diagnóstico: indica de dónde salió la hora y cuánto difiere el reloj del
    // servidor. Es pública (la hora no es un dato sensible).
    // ==========================================================================
    [ApiController]
    [Route("api/hora")]
    [AllowAnonymous]
    public class HoraController : ControllerBase
    {
        private readonly RelojLaboratorio _reloj;

        public HoraController(RelojLaboratorio reloj)
        {
            _reloj = reloj;
        }

        [HttpGet]
        public IActionResult ObtenerHora()
        {
            var ahora = _reloj.Ahora;

            return Ok(new
            {
                fecha = ahora.ToString("yyyy-MM-dd"),
                hora = ahora.ToString("HH:mm:ss"),
                fechaHora = ahora.ToString("yyyy-MM-ddTHH:mm:ss"),
                zonaHoraria = _reloj.ZonaHoraria,
                fuente = _reloj.Fuente,
                sincronizado = _reloj.EstaSincronizado,
                ultimaSincronizacion = _reloj.UltimaSincronizacion?.ToString("yyyy-MM-ddTHH:mm:ss"),
                desfaseServidorSegundos = _reloj.DesfaseServidor is { } d ? (long?)Math.Round(d.TotalSeconds) : null
            });
        }
    }
}
