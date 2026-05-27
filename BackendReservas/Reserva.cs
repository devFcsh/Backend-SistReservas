using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend_SistReservas.Models
{
    [Table("reservas")] // Nombre exacto de la tabla en MySQL
    public class Reserva
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }
        
        [Required]
        [Column("matricula")]
        public string Matricula { get; set; } = string.Empty;
        
        [Required]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;
        
        [Required]
        [Column("apellido")]
        public string Apellido { get; set; } = string.Empty;
        
        [Required]
        [Column("carrera")]
        public string Carrera { get; set; } = string.Empty;
        
        [Column("laboratorio")]
        public string Laboratorio { get; set; } = "L002";

        [Required]
        [Column("equipo")] 
        public string Equipo { get; set; } = string.Empty;
        
        [Column("hora_inicio")]
        public DateTime HoraInicio { get; set; } = DateTime.Now;
        
        [Column("hora_salida")]
        public DateTime? HoraSalida { get; set; }
        
        [Column("tiempo_promedio")] 
        public string? TiempoPromedio { get; set; } // Soporta VARCHAR(20) en MySQL

        [Column("periodo")]
        public string Periodo { get; set; }= string.Empty;
    }
}