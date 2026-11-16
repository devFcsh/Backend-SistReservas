using System.Security.Claims;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Atajos para leer, dentro de un controlador, quién hizo la petición.
    // Los claims los arma OnTokenValidated (Program.cs) con datos frescos de
    // la base de datos, no con lo que venía escrito en el token.
    // ==========================================================================
    public static class UsuarioActual
    {
        public static int ObtenerUsuarioId(this ClaimsPrincipal user) =>
            int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public static Guid ObtenerSesionId(this ClaimsPrincipal user) =>
            Guid.Parse(user.FindFirstValue(TokenService.ClaimSesion)!);
    }
}
