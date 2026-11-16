namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Configuración de autenticación, sección "Auth" de appsettings.
    //
    // En producción NO dejes estos valores en el archivo: defínelos como
    // variables de entorno, por ejemplo:
    //   Auth__JwtKey=<clave larga y aleatoria, mínimo 32 caracteres>
    //   Auth__AdminInicial__Usuario=admin
    //   Auth__AdminInicial__Password=<contraseña segura>
    // ==========================================================================
    public class AuthOptions
    {
        public const string SeccionConfig = "Auth";

        // Clave con la que se firman los tokens. Si cambia, todos los tokens
        // emitidos dejan de ser válidos (todos deben volver a iniciar sesión).
        public string JwtKey { get; set; } = string.Empty;

        public string Issuer { get; set; } = "BackendReservas";

        // Solo se usa si NO existe ningún admin en la tabla `usuarios`.
        public AdminInicialOptions AdminInicial { get; set; } = new();
    }

    public class AdminInicialOptions
    {
        public string Usuario { get; set; } = "admin";
        public string Password { get; set; } = string.Empty;
    }
}
