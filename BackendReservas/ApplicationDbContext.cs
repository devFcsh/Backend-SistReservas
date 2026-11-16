using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Data
{
    // ==========================================================================
    // Tablas: `reservas` (la que ya existía), y `usuarios` + `sesiones` para el
    // inicio de sesión del personal. El catálogo de
    // equipos y el control de cierre automático viven en memoria (ver
    // Services/EquiposCatalogoService.cs y Services/CierreAutomaticoBackgroundService.cs),
    // precisamente para no requerir ningún cambio de esquema en la base de datos.
    // ==========================================================================
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Reserva> Reservas { get; set; } = null!;

        // Autenticación del personal (ver Services/AuthInicializador.cs, que
        // crea estas dos tablas al arrancar si no existen).
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<Sesion> Sesiones { get; set; } = null!;
    }
}
