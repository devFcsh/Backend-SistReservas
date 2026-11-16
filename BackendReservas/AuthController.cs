using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
using Backend_SistReservas.Services;

namespace Backend_SistReservas.Controllers
{
    // ==========================================================================
    // Inicio y cierre de sesión del personal.
    //
    // Flujo para el frontend:
    //   1. POST api/auth/login  -> devuelve { token, usuario, rol }
    //   2. Guardar el token (localStorage) y enviarlo en cada petición:
    //        Authorization: Bearer <token>
    //   3. POST api/auth/logout -> cierra la sesión; el token deja de servir.
    // Si la API responde 401, la sesión ya no es válida: volver al login.
    // ==========================================================================
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private const string ErrorCredenciales = "Usuario o contraseña incorrectos.";

        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<Usuario> _hasher;
        private readonly TokenService _tokens;
        private readonly RelojLaboratorio _reloj;

        public AuthController(ApplicationDbContext context, IPasswordHasher<Usuario> hasher, TokenService tokens, RelojLaboratorio reloj)
        {
            _reloj = reloj;
            _context = context;
            _hasher = hasher;
            _tokens = tokens;
        }

        public class LoginRequest
        {
            public string Usuario { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class CambiarPasswordRequest
        {
            public string PasswordActual { get; set; } = string.Empty;
            public string PasswordNueva { get; set; } = string.Empty;
        }

        // ==========================================================================
        // POST: api/auth/login -> iniciar sesión (único endpoint público)
        // ==========================================================================
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { error = "Usuario y contraseña son requeridos." });

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.NombreUsuario == request.Usuario.Trim());

            // Mismo mensaje para "no existe", "desactivado" y "contraseña mala",
            // para no revelar qué usuarios existen.
            if (usuario == null || !usuario.Activo)
                return Unauthorized(new { error = ErrorCredenciales });

            var resultado = _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.Password);
            if (resultado == PasswordVerificationResult.Failed)
                return Unauthorized(new { error = ErrorCredenciales });

            if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
                usuario.PasswordHash = _hasher.HashPassword(usuario, request.Password);

            var sesion = new Sesion { UsuarioId = usuario.Id, CreadaEn = _reloj.Ahora };
            _context.Sesiones.Add(sesion);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                token = _tokens.GenerarToken(usuario, sesion),
                usuario = usuario.NombreUsuario,
                rol = usuario.Rol
            });
        }

        // ==========================================================================
        // POST: api/auth/logout -> cerrar la sesión actual
        // ==========================================================================
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var sesionId = User.ObtenerSesionId();
            var sesion = await _context.Sesiones.FirstOrDefaultAsync(s => s.Id == sesionId);

            if (sesion != null && sesion.CerradaEn == null)
            {
                sesion.CerradaEn = _reloj.Ahora;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Sesión cerrada." });
        }

        // ==========================================================================
        // GET: api/auth/me -> datos del usuario de la sesión actual
        // (útil para que el frontend sepa el rol al recargar la página)
        // ==========================================================================
        [HttpGet("me")]
        public async Task<IActionResult> Yo()
        {
            var usuario = await _context.Usuarios.AsNoTracking()
                .FirstAsync(u => u.Id == User.ObtenerUsuarioId());

            return Ok(new { id = usuario.Id, usuario = usuario.NombreUsuario, rol = usuario.Rol });
        }

        // ==========================================================================
        // PUT: api/auth/password -> cambiar MI contraseña.
        // Cierra mis otras sesiones abiertas (la actual sigue activa).
        // ==========================================================================
        [HttpPut("password")]
        public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Datos no válidos." });

            var errorPassword = ValidacionesUsuario.ValidarPassword(request.PasswordNueva);
            if (errorPassword != null)
                return BadRequest(new { error = errorPassword });

            var usuario = await _context.Usuarios.FirstAsync(u => u.Id == User.ObtenerUsuarioId());

            if (_hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.PasswordActual)
                == PasswordVerificationResult.Failed)
            {
                return BadRequest(new { error = "La contraseña actual no es correcta." });
            }

            usuario.PasswordHash = _hasher.HashPassword(usuario, request.PasswordNueva);
            await SesionesHelper.CerrarSesionesAsync(_context, usuario.Id, _reloj.Ahora, exceptoSesionId: User.ObtenerSesionId());
            await _context.SaveChangesAsync();

            return Ok(new { message = "Contraseña actualizada." });
        }
    }
}
