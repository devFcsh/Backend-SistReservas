using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend_SistReservas.Models
{
    // ==========================================================================
    // Sesión iniciada por un usuario. El token JWT no vence por tiempo: es
    // válido mientras su sesión esté abierta (CerradaEn == null). Al cerrar
    // sesión, o si el admin desactiva/elimina al usuario, la sesión se cierra
    // y el token deja de funcionar de inmediato.
    // ==========================================================================
    [Table("sesiones")]
    public class Sesion
    {
        // Identificador aleatorio (GUID, CHAR(36) en MySQL): no se puede
        // adivinar ni enumerar.
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("usuario_id")]
        public int UsuarioId { get; set; }

        [Column("creada_en")]
        public DateTime CreadaEn { get; set; } = DateTime.Now;

        [Column("cerrada_en")]
        public DateTime? CerradaEn { get; set; }

        public Usuario? Usuario { get; set; }
    }
}
