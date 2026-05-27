using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Reserva> Reservas { get; set; } = null!;
    }
}