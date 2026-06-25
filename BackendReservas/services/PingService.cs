using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // ESTADOS POSIBLES DE UN EQUIPO (solo 2: encendida o apagada)
    // ==========================================================================
    public enum EstadoPing
    {
        Encendida,  // 🟠 Naranja - Respondió al ping
        Apagada     // 🟣 Morado  - No respondió (timeout, inaccesible, o cualquier error)
    }

    public class ResultadoPing
    {
        public string Ip { get; set; } = string.Empty;
        public EstadoPing Estado { get; set; }
        public string Color { get; set; } = string.Empty;
        public long? TiempoRespuestaMs { get; set; }
    }

    // ==========================================================================
    // SERVICIO DE PING NATIVO (System.Net.NetworkInformation.Ping)
    // Reemplaza la necesidad de invocar un proceso externo o librerías de Node.
    // ==========================================================================
    public class PingService
    {
        private const int TimeoutMs = 1000; // equivalente al "-n 1" con espera corta

        public async Task<ResultadoPing> VerificarPcAsync(string ip)
        {
            var resultado = new ResultadoPing { Ip = ip };

            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(ip, TimeoutMs);

                // Cualquier estado distinto de Success se considera "apagada":
                // timeout, host/red/puerto inaccesible, o cualquier otra falla de ping.
                resultado.Estado = reply.Status == IPStatus.Success
                    ? EstadoPing.Encendida
                    : EstadoPing.Apagada;

                if (reply.Status == IPStatus.Success)
                {
                    resultado.TiempoRespuestaMs = reply.RoundtripTime;
                }
            }
            catch (PingException)
            {
                // IP inválida, sin resolución DNS, error de socket, etc. -> se considera apagada
                resultado.Estado = EstadoPing.Apagada;
            }

            resultado.Color = ObtenerColor(resultado.Estado);
            return resultado;
        }

        // ==========================================================================
        // Mapeo de estado -> color del semáforo (solo 2 colores)
        // ==========================================================================
        private static string ObtenerColor(EstadoPing estado)
        {
            return estado == EstadoPing.Encendida
                ? "naranja"  // 🟠 Encendido
                : "morado";  // 🟣 Apagado
        }
    }
}