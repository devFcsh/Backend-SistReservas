using System;

namespace Backend_SistReservas.Models
{
    // ==========================================================================
    // Estado de un equipo del laboratorio, EN MEMORIA (ya no es una tabla de
    // base de datos). Se siembra al arrancar el backend desde appsettings.json
    // (sección "Laboratorio:Equipos") y vive mientras el proceso esté corriendo.
    //
    // Por qué en memoria y no en BD: para no requerir ningún cambio de esquema
    // sobre la base de datos existente. La única tabla que este sistema sigue
    // usando es `reservas`, que ya existía.
    //
    // Trade-off importante: cambios hechos en caliente vía la API (agregar un
    // equipo, cambiar su IP, marcarlo inactivo) se pierden si el backend se
    // reinicia -> vuelve a lo configurado en appsettings.json. Para un cambio
    // que deba sobrevivir un reinicio (por ejemplo, la IP real de un equipo),
    // hay que editar appsettings.json y reiniciar el backend.
    // ==========================================================================
    public class Equipo
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;

        // ¿Está físicamente colocado en el laboratorio?
        public bool Activo { get; set; } = true;

        // Dirección IP registrada para hacer ping. Null = sin IP configurada.
        public string? Ip { get; set; }

        // Último resultado conocido del ping periódico en background.
        public bool Encendido { get; set; } = false;

        public DateTime? UltimoPing { get; set; }
    }
}
