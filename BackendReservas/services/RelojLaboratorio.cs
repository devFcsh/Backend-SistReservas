using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Reloj ÚNICO del sistema. Todo el backend debe pedir la hora aquí y
    // NUNCA usar DateTime.Now / DateTime.UtcNow directamente.
    //
    // Por qué: el reloj del servidor donde corre el backend puede estar mal
    // (fecha u hora incorrecta, o zona horaria distinta). Este reloj:
    //   1. Toma la hora real desde una fuente externa (internet o la base de
    //      datos; ver SincronizacionHoraService).
    //   2. A partir de ese momento cuenta el tiempo con un cronómetro interno
    //      (Stopwatch), que NO se ve afectado si alguien cambia el reloj del
    //      servidor mientras el backend está corriendo.
    //   3. Convierte siempre a la zona horaria del laboratorio
    //      (Laboratorio:ZonaHoraria, por defecto America/Guayaquil, UTC-5).
    //
    // Mientras no se haya logrado sincronizar ni una vez, usa el reloj del
    // servidor como último recurso y lo indica en "Fuente".
    // ==========================================================================
    public class RelojLaboratorio
    {
        public const string FuenteSinSincronizar = "Servidor (sin sincronizar)";

        private readonly TimeZoneInfo _zona;
        private readonly object _candado = new();

        // Hora UTC real en el instante de la última sincronización, y la
        // marca del cronómetro en ese mismo instante.
        private DateTime? _utcEnSincronizacion;
        private long _marcaCronometro;

        public string Fuente { get; private set; } = FuenteSinSincronizar;
        public DateTime? UltimaSincronizacion { get; private set; }

        // Diferencia detectada entre la hora real y el reloj del servidor
        // (positivo: el servidor está atrasado). Solo informativo.
        public TimeSpan? DesfaseServidor { get; private set; }

        public RelojLaboratorio(IOptions<LaboratorioOptions> opciones, ILogger<RelojLaboratorio> logger)
        {
            _zona = ObtenerZona(opciones.Value.ZonaHoraria, logger);
        }

        public bool EstaSincronizado
        {
            get { lock (_candado) return _utcEnSincronizacion.HasValue; }
        }

        // Hora UTC real.
        public DateTime UtcAhora
        {
            get
            {
                lock (_candado)
                {
                    if (_utcEnSincronizacion == null)
                        return DateTime.UtcNow;

                    var transcurrido = Stopwatch.GetElapsedTime(_marcaCronometro);
                    return _utcEnSincronizacion.Value + transcurrido;
                }
            }
        }

        // Hora local del laboratorio (Ecuador). Es la que se guarda en la base.
        public DateTime Ahora =>
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(UtcAhora, _zona), DateTimeKind.Unspecified);

        public DateTime Hoy => Ahora.Date;

        public string ZonaHoraria => _zona.Id;

        // Registra una hora UTC real obtenida de una fuente confiable.
        public void Sincronizar(DateTime utcReal, string fuente)
        {
            lock (_candado)
            {
                _utcEnSincronizacion = DateTime.SpecifyKind(utcReal, DateTimeKind.Utc);
                _marcaCronometro = Stopwatch.GetTimestamp();
                Fuente = fuente;
                DesfaseServidor = utcReal - DateTime.UtcNow;
            }
            UltimaSincronizacion = Ahora;
        }

        private static TimeZoneInfo ObtenerZona(string? id, ILogger logger)
        {
            id = string.IsNullOrWhiteSpace(id) ? "America/Guayaquil" : id.Trim();
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception)
            {
                // Respaldo: Ecuador continental es UTC-5 todo el año (sin horario de verano).
                logger.LogWarning("No se encontró la zona horaria '{Zona}'; se usa UTC-5 fijo.", id);
                return TimeZoneInfo.CreateCustomTimeZone("UTC-05", TimeSpan.FromHours(-5), "UTC-05", "UTC-05");
            }
        }
    }
}
