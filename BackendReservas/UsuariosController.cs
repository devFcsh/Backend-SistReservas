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
    // Gestión de cuentas del personal. SOLO rol Admin.
    //
    // Reglas de protección:
    //   - Un admin no puede eliminarse, desactivarse ni quitarse el rol Admin
    //     a sí mismo.
    //   - Nunca puede quedar el sistema sin al menos un admin activo.
    //   - Desactivar o eliminar un usuario, o resetear su contraseña, cierra
    //     sus sesiones abiertas al instante.
    // ==========================================================================
    [ApiController]
    [Route("api/usuarios")]
    [Authorize(Roles = Roles.Admin)]
    public class UsuariosController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<Usuario> _hasher;

        public UsuariosController(ApplicationDbContext context, IPasswordHasher<Usuario> hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        public class CrearUsuarioRequest
        {
            public string Usuario { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Rol { get; set; } = Roles.Laboratorio;
        }

        // Campos opcionales: solo se cambia lo que venga en la petición.
        public class EditarUsuarioRequest
        {
            public string? Rol { get; set; }
            public bool? Activo { get; set; }
        }

        public class ResetPasswordRequest
        {
            public string Password { get; set; } = string.Empty;
        }

        // ==========================================================================
        // GET: api/usuarios -> listado de cuentas (sin contraseñas)
        // ==========================================================================
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var usuarios = await _context.Usuarios.AsNoTracking()
                .OrderBy(u => u.NombreUsuario)
                .Select(u => new
                {
                    u.Id,
                    Usuario = u.NombreUsuario,
                    u.Rol,
                    u.Activo,
                    u.CreadoEn,
                    SesionesAbiertas = _context.Sesiones.Count(s => s.UsuarioId == u.Id && s.CerradaEn == null)
                })
                .ToListAsync();

            return Ok(usuarios);
        }

        // ==========================================================================
        // POST: api/usuarios -> crear cuenta
        // ==========================================================================
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] CrearUsuarioRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Datos no válidos." });

            var error = ValidacionesUsuario.ValidarNombreUsuario(request.Usuario)
                        ?? ValidacionesUsuario.ValidarPassword(request.Password)
                        ?? ValidacionesUsuario.ValidarRol(request.Rol);
            if (error != null)
                return BadRequest(new { error });

            var nombre = request.Usuario.Trim();
            if (await _context.Usuarios.AnyAsync(u => u.NombreUsuario == nombre))
                return BadRequest(new { error = $"Ya existe un usuario '{nombre}'." });

            var usuario = new Usuario
            {
                NombreUsuario = nombre,
                Rol = request.Rol,
                Activo = true,
                CreadoEn = DateTime.Now
            };
            usuario.PasswordHash = _hasher.HashPassword(usuario, request.Password);

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            return Ok(new { usuario.Id, Usuario = usuario.NombreUsuario, usuario.Rol, usuario.Activo });
        }

        // ==========================================================================
        // PUT: api/usuarios/{id} -> cambiar rol y/o activar/desactivar
        // ==========================================================================
        [HttpPut("{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] EditarUsuarioRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Datos no válidos." });

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null)
                return NotFound(new { error = "Usuario no encontrado." });

            if (request.Rol != null)
            {
                var errorRol = ValidacionesUsuario.ValidarRol(request.Rol);
                if (errorRol != null)
                    return BadRequest(new { error = errorRol });
            }

            var nuevoRol = request.Rol ?? usuario.Rol;
            var nuevoActivo = request.Activo ?? usuario.Activo;
            bool esYo = usuario.Id == User.ObtenerUsuarioId();

            if (esYo && (!nuevoActivo || nuevoRol != Roles.Admin))
                return BadRequest(new { error = "No puede desactivarse ni quitarse el rol Admin a sí mismo." });

            bool dejaDeSerAdminActivo = usuario.Rol == Roles.Admin && usuario.Activo
                                        && (nuevoRol != Roles.Admin || !nuevoActivo);
            if (dejaDeSerAdminActivo && !await HayOtroAdminActivo(usuario.Id))
                return BadRequest(new { error = "Debe existir al menos un admin activo." });

            usuario.Rol = nuevoRol;
            usuario.Activo = nuevoActivo;

            if (!nuevoActivo)
                await SesionesHelper.CerrarSesionesAsync(_context, usuario.Id);

            await _context.SaveChangesAsync();

            return Ok(new { usuario.Id, Usuario = usuario.NombreUsuario, usuario.Rol, usuario.Activo });
        }

        // ==========================================================================
        // PUT: api/usuarios/{id}/password -> el admin asigna una nueva contraseña
        // ==========================================================================
        [HttpPut("{id}/password")]
        public async Task<IActionResult> ResetearPassword(int id, [FromBody] ResetPasswordRequest request)
        {
            var errorPassword = ValidacionesUsuario.ValidarPassword(request?.Password);
            if (errorPassword != null)
                return BadRequest(new { error = errorPassword });

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null)
                return NotFound(new { error = "Usuario no encontrado." });

            usuario.PasswordHash = _hasher.HashPassword(usuario, request!.Password);

            // Si el admin cambia su propia contraseña por aquí, conserva su sesión actual.
            await SesionesHelper.CerrarSesionesAsync(_context, usuario.Id, exceptoSesionId: User.ObtenerSesionId());
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Contraseña de '{usuario.NombreUsuario}' actualizada. Sus sesiones abiertas fueron cerradas." });
        }

        // ==========================================================================
        // POST: api/usuarios/{id}/cerrar-sesiones -> forzar cierre de sesión
        // (p. ej. si alguien dejó la sesión abierta en otra PC)
        // ==========================================================================
        [HttpPost("{id}/cerrar-sesiones")]
        public async Task<IActionResult> CerrarSesiones(int id)
        {
            var usuario = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null)
                return NotFound(new { error = "Usuario no encontrado." });

            int cerradas = await SesionesHelper.CerrarSesionesAsync(_context, id, exceptoSesionId: User.ObtenerSesionId());
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Se cerraron {cerradas} sesión(es) de '{usuario.NombreUsuario}'." });
        }

        // ==========================================================================
        // DELETE: api/usuarios/{id} -> eliminar cuenta (y sus sesiones)
        // ==========================================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null)
                return NotFound(new { error = "Usuario no encontrado." });

            if (usuario.Id == User.ObtenerUsuarioId())
                return BadRequest(new { error = "No puede eliminar su propia cuenta." });

            if (usuario.Rol == Roles.Admin && usuario.Activo && !await HayOtroAdminActivo(usuario.Id))
                return BadRequest(new { error = "Debe existir al menos un admin activo." });

            _context.Usuarios.Remove(usuario); // Las sesiones se borran en cascada.
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Usuario '{usuario.NombreUsuario}' eliminado." });
        }

        private Task<bool> HayOtroAdminActivo(int excluirId) =>
            _context.Usuarios.AnyAsync(u => u.Id != excluirId && u.Rol == Roles.Admin && u.Activo);
    }
}
