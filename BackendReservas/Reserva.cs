using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Reserva
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public string Matricula { get; set; } = string.Empty;
    
    [Required]
    public string Nombre { get; set; } = string.Empty;
    
    [Required]
    public string Apellido { get; set; } = string.Empty;
    
    [Required]
    public string Carrera { get; set; } = string.Empty;
    
    public string Laboratorio { get; set; } = "L002";
    
    public DateTime HoraInicio { get; set; } = DateTime.Now;
    
    public DateTime? HoraSalida { get; set; }
    
    public int? TiempoPromedio { get; set; } // Guardará los minutos transcurridos
}