using Backend_SistReservas.Models;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Única fuente de verdad para calcular el estado visible de un equipo y si
    // puede reservarse. Se usa tanto en los controladores (para validar y para
    // responder al frontend) como en los procesos de background, para que la
    // regla nunca quede duplicada / inconsistente entre archivos.
    //
    // Reglas (en orden de prioridad):
    //   1. Activo == false                    -> "No colocado"   (no reservable)
    //   2. Ip == null (sin IP registrada)      -> "No disponible" (no reservable)
    //   3. Encendido == false (ping falló)     -> "No disponible" (no reservable)
    //   4. Encendido == true y ocupado         -> "Ocupado"       (no reservable)
    //   5. Encendido == true y libre           -> "Disponible"   (reservable)
    //
    // Nota de diseño: el requerimiento original mencionaba también una
    // etiqueta "Apagado", pero el texto funcional (sección 2, casos
    // especiales 1 y 2) es explícito en que TANTO la falta de IP COMO
    // cualquier respuesta de ping distinta a "encendido" deben mostrarse
    // como "No disponible" y bloquearse de igual manera. Para evitar un
    // estado ambiguo que no bloquee reservas por error, se unificaron
    // ambos casos bajo "No disponible" (ver resumen de cambios).
    // ==========================================================================
    public static class EquipoEstadoHelper
    {
        public const string NoColocado = "No colocado";
        public const string NoDisponible = "No disponible";
        public const string Ocupado = "Ocupado";
        public const string Disponible = "Disponible";

        public static string CalcularEstado(Equipo equipo, bool ocupado)
        {
            if (!equipo.Activo) return NoColocado;
            if (string.IsNullOrWhiteSpace(equipo.Ip)) return NoDisponible;
            if (!equipo.Encendido) return NoDisponible;
            return ocupado ? Ocupado : Disponible;
        }

        // Un equipo solo puede aceptar una NUEVA reserva si su estado es
        // exactamente "Disponible" (colocado, con IP, encendido y libre).
        public static bool EsReservable(Equipo equipo, bool ocupado)
        {
            return CalcularEstado(equipo, ocupado) == Disponible;
        }

        // Puede seguir usándose una vez reservado (para detectar si hay que
        // cerrar automáticamente una sesión activa): deja de poder usarse si
        // ya no está colocado, o dejó de responder al ping.
        public static bool PuedeSeguirEnUso(Equipo equipo)
        {
            return equipo.Activo && !string.IsNullOrWhiteSpace(equipo.Ip) && equipo.Encendido;
        }
    }
}
