namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Roles del sistema.
    //   - Admin: todos los permisos (usuarios, catálogo de equipos, borrar filas).
    //   - Laboratorio: operación diaria (entrada/salida, clases, ver paneles,
    //     editar filas de reservas).
    // Los endpoints sin [Authorize(Roles = ...)] explícito quedan disponibles
    // para AMBOS roles (ver la política por defecto en Program.cs).
    // ==========================================================================
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Laboratorio = "Laboratorio";

        public static readonly string[] Todos = { Admin, Laboratorio };

        public static bool EsValido(string? rol) => rol == Admin || rol == Laboratorio;
    }
}
