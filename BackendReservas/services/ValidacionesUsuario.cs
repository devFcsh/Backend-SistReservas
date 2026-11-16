using System.Text.RegularExpressions;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Reglas para cuentas del personal. Devuelven el mensaje de error, o null
    // si el valor es válido.
    // ==========================================================================
    public static class ValidacionesUsuario
    {
        public const int LongitudMinimaPassword = 8;

        // 3 a 50 caracteres: letras sin tilde, números, punto, guion y guion bajo.
        private static readonly Regex UsuarioRegex = new(@"^[A-Za-z0-9._-]{3,50}$", RegexOptions.Compiled);

        public static string? ValidarNombreUsuario(string? usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario) || !UsuarioRegex.IsMatch(usuario.Trim()))
                return "El usuario debe tener de 3 a 50 caracteres: letras, números, punto, guion o guion bajo.";
            return null;
        }

        public static string? ValidarPassword(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < LongitudMinimaPassword)
                return $"La contraseña debe tener al menos {LongitudMinimaPassword} caracteres.";
            return null;
        }

        public static string? ValidarRol(string? rol)
        {
            if (!Roles.EsValido(rol))
                return $"Rol no válido. Use '{Roles.Admin}' o '{Roles.Laboratorio}'.";
            return null;
        }
    }
}
