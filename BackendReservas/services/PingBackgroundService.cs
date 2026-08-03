using System;
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
    // Proceso en background que:
    //   1. Cada N segundos (configurable) hace ping a cada equipo con IP
    //      registrada en el catálogo en memoria (EquiposCatalogoService) y
    //      actualiza su estado "encendido".
    //   2. Si un equipo estaba en uso (reserva activa en la tabla `reservas`)
    //      y deja de responder "encendido" (se apaga, se desconecta, o se le
    //      quita la IP), cierra automáticamente esa reserva (hora_salida =
    //      ahora) y libera el equipo.
    //
    // No depende del frontend: corre siempre que el backend esté encendido.
    // No toca ninguna tabla de equipos en base de datos (no existe); solo usa
    // `reservas`, que ya existía, para cerrar sesiones activas.
    // ==========================================================================
    public class PingBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PingService _pingService;
        private readonly EquiposCatalogoService _catalogo;
        private readonly ILogger<PingBackgroundService> _logger;
        private readonly LaboratorioOptions _opciones;

        public PingBackgroundService(
            IServiceScopeFactory scopeFactory,
            PingService pingService,
            EquiposCatalogoService catalogo,
            IOptions<LaboratorioOptions> opciones,
            ILogger<PingBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _pingService = pingService;
            _catalogo = catalogo;
            _opciones = opciones.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalo = TimeSpan.FromSeconds(Math.Max(5, _opciones.IntervaloPingSegundos));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EjecutarCicloDePingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Un fallo en un ciclo (ej. la BD momentáneamente inaccesible)
                    // no debe tumbar el servicio; se reintenta en el próximo ciclo.
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            _logger.LogError(ex, "Error durante el ciclo de ping a equipos.");
                        }
                        catch
                        {
                            // Ignorar fallos de logging durante el apagado del host.
                        }
                    }
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

        private async Task EjecutarCicloDePingAsync(CancellationToken stoppingToken)
        {
            var equiposConIp = _catalogo.ObtenerTodos()
                .Where(e => !string.IsNullOrWhiteSpace(e.Ip))
                .ToList();

            if (equiposConIp.Count == 0) return;

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var equipo in equiposConIp)
            {
                if (stoppingToken.IsCancellationRequested) break;

                bool encendidoAnterior = equipo.Encendido;

                var resultado = await _pingService.VerificarPcAsync(equipo.Ip!);
                // Cualquier respuesta distinta de "Encendida" (timeout, host
                // inalcanzable, error) se trata como No disponible.
                bool encendidoAhora = resultado.Estado == EstadoPing.Encendida;

                _catalogo.ActualizarEstadoPing(equipo.Nombre, encendidoAhora, DateTime.Now);

                // El equipo se apagó / dejó de responder mientras estaba en uso:
                // cerrar automáticamente su reserva activa.
                if (encendidoAnterior && !encendidoAhora)
                {
                    await CerrarReservaActivaSiExiste(context, equipo.Nombre, stoppingToken);
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private static async Task CerrarReservaActivaSiExiste(ApplicationDbContext context, string nombreEquipo, CancellationToken stoppingToken)
        {
            var reservaActiva = await context.Reservas
                .Where(r => r.Equipo == nombreEquipo && r.HoraSalida == null)
                .FirstOrDefaultAsync(stoppingToken);

            if (reservaActiva == null) return;

            var ahora = DateTime.Now;
            reservaActiva.HoraSalida = ahora;
            reservaActiva.TiempoPromedio = FormatearTiempo(reservaActiva.HoraInicio, ahora);
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
