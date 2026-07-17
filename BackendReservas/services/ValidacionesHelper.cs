using System.Text.RegularExpressions;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Validaciones de formulario centralizadas. Es la fuente de verdad en el
    // backend: aunque el frontend filtre mientras se escribe, una petición
    // manipulada que llegue directo a la API debe pasar por aquí también.
    // ==========================================================================
    public static class ValidacionesHelper
    {
        // Exactamente 9 dígitos numéricos, nada más.
        private static readonly Regex MatriculaRegex = new(@"^\d{9}$", RegexOptions.Compiled);

        // Solo letras (con tildes y ñ/Ñ) y espacios. Requiere al menos un
        // carácter no-espacio para evitar cadenas vacías o solo espacios.
        private static readonly Regex SoloLetrasRegex =
            new(@"^[A-Za-zÁÉÍÓÚÜáéíóúüÑñ]+(?:\s[A-Za-zÁÉÍÓÚÜáéíóúüÑñ]+)*$", RegexOptions.Compiled);

        public static bool EsMatriculaValida(string? matricula)
        {
            return !string.IsNullOrWhiteSpace(matricula) && MatriculaRegex.IsMatch(matricula.Trim());
        }

        public static bool EsNombreValido(string? nombre)
        {
            return !string.IsNullOrWhiteSpace(nombre) && SoloLetrasRegex.IsMatch(nombre.Trim());
        }
    }
}
