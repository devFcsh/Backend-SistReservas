using System.Collections.Generic;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Opciones configurables del laboratorio, enlazadas desde appsettings.json
    // (sección "Laboratorio"). Mantener esto en UN solo lugar evita tener la
    // hora de cierre o el intervalo de ping hardcodeados en varios archivos.
    // ==========================================================================
    public class LaboratorioOptions
    {
        public const string SeccionConfig = "Laboratorio";

        // Hora oficial de cierre, formato "HH:mm" (24h). Confirmada por
        // Andrés Pinos: 17:00 (5:00 p.m.). Si cambia, actualizar solo aquí.
        public string HoraCierre { get; set; } = "17:00";

        // Cada cuántos segundos se revisa la hora de cierre.
        public int IntervaloVerificacionCierreSegundos { get; set; } = 60;

        // Cada cuántos segundos se hace ping a los equipos con IP registrada.
        public int IntervaloPingSegundos { get; set; } = 45;

        // Zona horaria del laboratorio (IANA). Toda hora del sistema se expresa
        // en esta zona, sin importar la configuración del servidor.
        public string ZonaHoraria { get; set; } = "America/Guayaquil";

        // Cada cuántos minutos se vuelve a pedir la hora real a internet.
        public int IntervaloSincronizacionHoraMinutos { get; set; } = 30;

        // Catálogo de equipos del laboratorio (nombre, IP, si está colocado).
        // Si se deja vacío, se generan por defecto "Equipo 1".."Equipo 46"
        // sin IP. Ver EquiposCatalogoService para el detalle de la siembra.
        public List<EquipoConfig> Equipos { get; set; } = new();
    }
}
