using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Genera el token JWT que el frontend envía en cada petición
    // (encabezado "Authorization: Bearer <token>").
    //
    // El token dura Auth:DuracionSesionHoras (8 h por defecto) desde el login
    // y además su sesión (claim "sid") debe seguir abierta en la tabla
    // `sesiones`. Ambas cosas se revisan en cada petición (ver
    // OnTokenValidated en Program.cs) usando el RelojLaboratorio, no el reloj
    // del servidor. El "exp" del token es informativo para el frontend.
    // ==========================================================================
    public class TokenService
    {
        public const string ClaimSesion = "sid";

        private readonly AuthOptions _opciones;
        private readonly RelojLaboratorio _reloj;

        public TokenService(IOptions<AuthOptions> opciones, RelojLaboratorio reloj)
        {
            _reloj = reloj;
            _opciones = opciones.Value;
        }

        public static SymmetricSecurityKey CrearClave(string jwtKey) =>
            new(Encoding.UTF8.GetBytes(jwtKey));

        public string GenerarToken(Usuario usuario, Sesion sesion)
        {
            // "name" y "role" van solo como información para el frontend (por
            // ejemplo, para mostrar u ocultar botones). La API NO confía en
            // ellos: en cada petición vuelve a leer el rol desde la base.
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(ClaimSesion, sesion.Id.ToString()),
                new Claim("name", usuario.NombreUsuario),
                new Claim("role", usuario.Rol),
            };

            var ahoraUtc = _reloj.UtcAhora;
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = _opciones.Issuer,
                Audience = _opciones.Issuer,
                IssuedAt = ahoraUtc,
                Expires = ahoraUtc + _opciones.DuracionSesion,
                SigningCredentials = new SigningCredentials(
                    CrearClave(_opciones.JwtKey), SecurityAlgorithms.HmacSha256)
            };

            var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };
            return handler.WriteToken(handler.CreateToken(descriptor));
        }
    }
}
