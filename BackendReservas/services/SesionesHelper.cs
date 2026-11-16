using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;

namespace Backend_SistReservas.Services
{
    public static class SesionesHelper
    {
        // Marca como cerradas las sesiones abiertas de un usuario (opcionalmente
        // conservando una, p. ej. la del propio usuario que hace el cambio).
        // NO guarda: el llamador hace SaveChangesAsync junto con sus otros cambios.
        public static async Task<int> CerrarSesionesAsync(ApplicationDbContext context, int usuarioId, Guid? exceptoSesionId = null)
        {
            var abiertas = await context.Sesiones
                .Where(s => s.UsuarioId == usuarioId && s.CerradaEn == null && s.Id != exceptoSesionId)
                .ToListAsync();

            var ahora = DateTime.Now;
            foreach (var sesion in abiertas)
                sesion.CerradaEn = ahora;

            return abiertas.Count;
        }
    }
}
