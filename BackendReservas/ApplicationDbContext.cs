using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Data
{
    // ==========================================================================
    // Única tabla usada: `reservas` (la misma que ya existía). El catálogo de
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
    }
}
