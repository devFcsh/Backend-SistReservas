using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Se ejecuta UNA vez al arrancar el backend:
    //   1. Crea las tablas `usuarios` y `sesiones` si todavía no existen
    //      (no toca la tabla `reservas`).
    //   2. Si no hay ningún admin activo, crea el admin inicial con los datos
    //      de appsettings -> "Auth:AdminInicial".
    // Si la base de datos no responde, se registra el error y el backend
    // arranca igual (como ya ocurre con el resto del sistema).
    // ==========================================================================
    public static class AuthInicializador
    {
        private const string SqlCrearUsuarios = @"
CREATE TABLE IF NOT EXISTS usuarios (
    id INT AUTO_INCREMENT PRIMARY KEY,
    usuario VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    rol VARCHAR(20) NOT NULL,
    activo TINYINT(1) NOT NULL DEFAULT 1,
    creado_en DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);";

        private const string SqlCrearSesiones = @"
CREATE TABLE IF NOT EXISTS sesiones (
    id CHAR(36) PRIMARY KEY,
    usuario_id INT NOT NULL,
    creada_en DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cerrada_en DATETIME NULL,
    INDEX idx_sesiones_usuario (usuario_id),
    CONSTRAINT fk_sesiones_usuario FOREIGN KEY (usuario_id)
        REFERENCES usuarios(id) ON DELETE CASCADE
);";

        public static async Task EjecutarAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(AuthInicializador));

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var opciones = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
                var reloj = scope.ServiceProvider.GetRequiredService<RelojLaboratorio>();

                await context.Database.ExecuteSqlRawAsync(SqlCrearUsuarios);
                await context.Database.ExecuteSqlRawAsync(SqlCrearSesiones);

                bool hayAdmin = await context.Usuarios.AnyAsync(u => u.Rol == Roles.Admin && u.Activo);
                if (hayAdmin) return;

                var datos = opciones.AdminInicial;
                if (string.IsNullOrWhiteSpace(datos.Usuario) || string.IsNullOrWhiteSpace(datos.Password))
                {
                    logger.LogWarning(
                        "No existe ningún admin y no se configuró 'Auth:AdminInicial' (Usuario/Password). " +
                        "Nadie podrá administrar usuarios hasta que se configure.");
                    return;
                }

                var existente = await context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == datos.Usuario);
                if (existente != null)
                {
                    logger.LogWarning(
                        "No hay admin activo y el usuario '{Usuario}' ya existe con otro rol o desactivado; no se modifica.",
                        datos.Usuario);
                    return;
                }

                var admin = new Usuario
                {
                    NombreUsuario = datos.Usuario.Trim(),
                    Rol = Roles.Admin,
                    Activo = true,
                    CreadoEn = reloj.Ahora
                };
                admin.PasswordHash = hasher.HashPassword(admin, datos.Password);

                context.Usuarios.Add(admin);
                await context.SaveChangesAsync();

                logger.LogWarning(
                    "Se creó el admin inicial '{Usuario}'. Cambia su contraseña si es la de ejemplo.",
                    admin.NombreUsuario);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudieron preparar las tablas de usuarios/sesiones (¿la base de datos está disponible?).");
            }
        }
    }
}
