using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Backend_SistReservas.Data;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Mantiene sincronizado el RelojLaboratorio con una fuente de hora
    // confiable, para no depender del reloj del servidor.
    //
    // Orden de fuentes (se usa la primera que responda):
    //   1. NTP: servidores públicos de hora (time.google.com, time.cloudflare.com,
    //      pool.ntp.org). Puerto UDP 123.
    //   2. HTTPS: encabezado "Date" de sitios confiables (por si la red de la
    //      institución bloquea NTP). Precisión de ~1 segundo.
    //   3. Base de datos: SELECT UTC_TIMESTAMP() del servidor MySQL. Solo es
    //      útil si MySQL corre en OTRA máquina con el reloj correcto.
    //
    // Se sincroniza al arrancar (antes de atender peticiones) y luego cada
    // Laboratorio:IntervaloSincronizacionHoraMinutos (por defecto 30). Si
    // todas fallan, se reintenta cada minuto; mientras tanto el reloj sigue
    // contando desde la última sincronización buena.
    // ==========================================================================
    public class SincronizacionHoraService : BackgroundService
    {
        private static readonly string[] ServidoresNtp = { "time.google.com", "time.cloudflare.com", "pool.ntp.org" };
        private static readonly string[] SitiosHttps = { "https://www.google.com", "https://www.cloudflare.com", "https://www.microsoft.com" };
        private static readonly TimeSpan TiempoEspera = TimeSpan.FromSeconds(3);
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private readonly RelojLaboratorio _reloj;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly LaboratorioOptions _opciones;
        private readonly ILogger<SincronizacionHoraService> _logger;

        public SincronizacionHoraService(
            RelojLaboratorio reloj,
            IServiceScopeFactory scopeFactory,
            IOptions<LaboratorioOptions> opciones,
            ILogger<SincronizacionHoraService> logger)
        {
            _reloj = reloj;
            _scopeFactory = scopeFactory;
            _opciones = opciones.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalo = TimeSpan.FromMinutes(Math.Max(1, _opciones.IntervaloSincronizacionHoraMinutos));

            while (!stoppingToken.IsCancellationRequested)
            {
                // La primera sincronización ya se hizo en Program.cs al arrancar.
                bool ok = _reloj.EstaSincronizado;
                try
                {
                    await Task.Delay(ok ? intervalo : TimeSpan.FromMinutes(1), stoppingToken);
                    await SincronizarAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        // Devuelve true si alguna fuente respondió.
        public async Task<bool> SincronizarAsync(CancellationToken ct = default)
        {
            foreach (var servidor in ServidoresNtp)
            {
                var hora = await ConsultarNtpAsync(servidor, ct);
                if (hora != null) return Registrar(hora.Value, $"NTP {servidor}");
            }

            foreach (var sitio in SitiosHttps)
            {
                var hora = await ConsultarHttpsAsync(sitio, ct);
                if (hora != null) return Registrar(hora.Value, $"HTTPS {new Uri(sitio).Host}");
            }

            var horaBd = await ConsultarBaseDatosAsync(ct);
            if (horaBd != null) return Registrar(horaBd.Value, "Base de datos (MySQL)");

            _logger.LogError(
                "No se pudo obtener la hora de ninguna fuente (NTP, HTTPS ni base de datos). Se usa: {Fuente}.",
                _reloj.Fuente);
            return false;
        }

        private bool Registrar(DateTime utcReal, string fuente)
        {
            _reloj.Sincronizar(utcReal, fuente);

            var desfase = _reloj.DesfaseServidor ?? TimeSpan.Zero;
            if (Math.Abs(desfase.TotalMinutes) >= 1)
            {
                _logger.LogWarning(
                    "Hora sincronizada con {Fuente}: {Hora:yyyy-MM-dd HH:mm:ss} ({Zona}). El reloj del servidor difiere en {Desfase}; se ignora.",
                    fuente, _reloj.Ahora, _reloj.ZonaHoraria, desfase);
            }
            else
            {
                _logger.LogInformation(
                    "Hora sincronizada con {Fuente}: {Hora:yyyy-MM-dd HH:mm:ss} ({Zona}).",
                    fuente, _reloj.Ahora, _reloj.ZonaHoraria);
            }
            return true;
        }

        // Consulta SNTP básica (RFC 4330): paquete de 48 bytes, se lee el
        // "transmit timestamp" y se corrige con la mitad del tiempo de ida y vuelta.
        private async Task<DateTime?> ConsultarNtpAsync(string servidor, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TiempoEspera);

                using var udp = new UdpClient();
                await udp.Client.ConnectAsync(servidor, 123, cts.Token);

                var solicitud = new byte[48];
                solicitud[0] = 0x1B; // LI = 0, Versión = 3, Modo = 3 (cliente)

                long inicio = Stopwatch.GetTimestamp();
                await udp.Client.SendAsync(solicitud, SocketFlags.None, cts.Token);

                var respuesta = new byte[48];
                int leidos = await udp.Client.ReceiveAsync(respuesta, SocketFlags.None, cts.Token);
                var idaYVuelta = Stopwatch.GetElapsedTime(inicio);

                if (leidos < 48) return null;

                ulong segundos = LeerUInt32(respuesta, 40);
                ulong fraccion = LeerUInt32(respuesta, 44);
                if (segundos == 0) return null;

                var milisegundos = segundos * 1000 + (fraccion * 1000 / 0x100000000UL);
                var epocaNtp = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var horaServidor = epocaNtp.AddMilliseconds(milisegundos);

                return EsRazonable(horaServidor) ? horaServidor + idaYVuelta / 2 : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "NTP {Servidor} no respondió.", servidor);
                return null;
            }
        }

        private async Task<DateTime?> ConsultarHttpsAsync(string url, CancellationToken ct)
        {
            try
            {
                long inicio = Stopwatch.GetTimestamp();
                using var solicitud = new HttpRequestMessage(HttpMethod.Head, url);
                using var respuesta = await Http.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, ct);
                var idaYVuelta = Stopwatch.GetElapsedTime(inicio);

                var fecha = respuesta.Headers.Date;
                if (fecha == null || !EsRazonable(fecha.Value.UtcDateTime)) return null;

                return fecha.Value.UtcDateTime + idaYVuelta / 2;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "HTTPS {Url} no respondió.", url);
                return null;
            }
        }

        private async Task<DateTime?> ConsultarBaseDatosAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var hora = await context.Database
                    .SqlQueryRaw<DateTime>("SELECT UTC_TIMESTAMP() AS Value")
                    .SingleAsync(ct);
                return EsRazonable(hora) ? DateTime.SpecifyKind(hora, DateTimeKind.Utc) : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "La base de datos no devolvió la hora.");
                return null;
            }
        }

        // Descarta respuestas absurdas (fuente rota o mal configurada).
        private static bool EsRazonable(DateTime utc) => utc.Year >= 2024 && utc.Year <= 2100;

        private static ulong LeerUInt32(byte[] datos, int posicion) =>
            (ulong)datos[posicion] << 24 | (ulong)datos[posicion + 1] << 16 |
            (ulong)datos[posicion + 2] << 8 | datos[posicion + 3];
    }
}
