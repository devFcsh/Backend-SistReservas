using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backend_SistReservas.Data;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Cierre automático diario del laboratorio.
    //
    // Cada IntervaloVerificacionCierreSegundos (por defecto 60s) revisa si ya
    // pasó la hora de cierre configurada (Laboratorio:HoraCierre, ej. "17:00")
    // y si el cierre de HOY todavía no se ha ejecutado. Si corresponde:
    //   1. Busca todas las reservas con hora_salida IS NULL (tabla `reservas`,
    //      la única tabla que usa este proceso; no requiere ningún cambio de
    //      esquema en la base de datos).
    //   2. Les asigna la hora actual como hora_salida.
    //   3. Calcula tiempo_promedio igual que en un registro de salida manual.
    //
    // Control de "una sola vez por día": se guarda en un campo en memoria de
    // esta misma instancia del servicio (vive mientras el proceso del backend
    // esté corriendo). Si el backend se reinicia EXACTAMENTE durante la
    // ventana de cierre (mismo día, después de la hora configurada), existe
    // una probabilidad pequeña de volver a cerrar reservas que ya estaban
    // cerradas (no genera error: simplemente no habría reservas activas que
    // cerrar, así que el efecto práctico es nulo). Si se necesitara una
    // garantía absoluta ante reinicios, la alternativa es persistir esta
    // marca en base de datos, lo cual implicaría una tabla adicional.
    // ==========================================================================
    public class CierreAutomaticoBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CierreAutomaticoBackgroundService> _logger;
        private readonly LaboratorioOptions _opciones;
        private readonly RelojLaboratorio _reloj;
        private DateTime? _ultimaFechaEjecutada;

        public CierreAutomaticoBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<LaboratorioOptions> opciones,
            ILogger<CierreAutomaticoBackgroundService> logger,
            RelojLaboratorio reloj)
        {
            _reloj = reloj;
            _scopeFactory = scopeFactory;
            _opciones = opciones.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalo = TimeSpan.FromSeconds(Math.Max(10, _opciones.IntervaloVerificacionCierreSegundos));

            if (!TimeSpan.TryParseExact(_opciones.HoraCierre, "hh\\:mm", CultureInfo.InvariantCulture, out var horaCierre))
            {
                _logger.LogError(
                    "Laboratorio:HoraCierre ('{HoraCierre}') no tiene el formato HH:mm esperado. " +
                    "El cierre automático NO se ejecutará hasta corregir la configuración.",
                    _opciones.HoraCierre);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await VerificarYCerrarSiCorrespondeAsync(horaCierre, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error durante la verificación de cierre automático del laboratorio.");
                }

                try
                {
                    await Task.Delay(intervalo, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Apagado normal del servicio.
                }
            }
        }

        private async Task VerificarYCerrarSiCorrespondeAsync(TimeSpan horaCierre, CancellationToken stoppingToken)
        {
            var ahora = _reloj.Ahora;

            // Todavía no llega la hora de cierre de hoy.
            if (ahora.TimeOfDay < horaCierre) return;

            var hoy = ahora.Date;

            // Ya se ejecutó hoy (marca en memoria): no hacer nada más hasta mañana.
            if (_ultimaFechaEjecutada == hoy) return;

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var reservasActivas = await context.Reservas
                .Where(r => r.HoraSalida == null)
                .ToListAsync(stoppingToken);

            foreach (var reserva in reservasActivas)
            {
                reserva.HoraSalida = ahora;
                reserva.TiempoPromedio = FormatearTiempo(reserva.HoraInicio, ahora);
            }

            await context.SaveChangesAsync(stoppingToken);

            // Se marca como ejecutado SOLO después de guardar con éxito.
            _ultimaFechaEjecutada = hoy;

            _logger.LogInformation(
                "Cierre automático del laboratorio ejecutado a las {Hora}. Reservas cerradas: {Cantidad}.",
                ahora, reservasActivas.Count);
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
    }
}
