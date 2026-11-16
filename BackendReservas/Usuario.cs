using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend_SistReservas.Models
{
    // ==========================================================================
    // Cuenta del personal que usa el sistema (los estudiantes NO tienen cuenta).
    // La contraseña nunca se guarda en texto plano: solo su hash.
    // ==========================================================================
    [Table("usuarios")]
    public class Usuario
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("usuario")]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        // Ver Services/Roles.cs: "Admin" o "Laboratorio".
        [Required]
        [Column("rol")]
        public string Rol { get; set; } = string.Empty;

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("creado_en")]
        public DateTime CreadoEn { get; set; }
    }
}
