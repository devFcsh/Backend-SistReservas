using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

[ApiController]
[Route("api/[controller]")]
public class ReservasController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ReservasController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. REGISTRO DE ENTRADA (POST: api/reservas/entrada)
    [HttpPost("entrada")]
    public async Task<IActionResult> RegistrarEntrada([FromBody] Reserva entrada)
    {
        if (entrada == null) return BadRequest();

        entrada.Laboratorio = "L002";
        entrada.HoraInicio = DateTime.Now;
        entrada.HoraSalida = null;
        entrada.TiempoPromedio = null;

        _context.Reservas.Add(entrada);
        await _context.SaveChangesAsync();

        return Ok(new { message = "el registro se realizo con exito" });
    }

    // 2. REGISTRO DE SALIDA (PUT: api/reservas/salida)
    [HttpPut("salida")]
    public async Task<IActionResult> RegistrarSalida([FromBody] SalidaRequest request)
    {
        // Busca el último registro de esa matrícula que no haya marcado salida
        var ultimaReserva = await _context.Reservas
            .Where(r => r.Matricula == request.Matricula && r.HoraSalida == null)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        if (ultimaReserva == null)
        {
            return NotFound(new { message = "No se encontró una entrada activa para esta matrícula" });
        }

        ultimaReserva.HoraSalida = DateTime.Now;
        
        // Calcula la diferencia exacta en minutos enteros
        TimeSpan diferencia = ultimaReserva.HoraSalida.Value - ultimaReserva.HoraInicio;
        ultimaReserva.TiempoPromedio = (int)diferencia.TotalMinutes;

        _context.Reservas.Update(ultimaReserva);
        await _context.SaveChangesAsync();

        return Ok(new { message = "se registro la salida con exito" });
    }

    // 3. OBTENER DATOS PARA EL EXCEL (GET: api/reservas/reporte)
    [HttpGet("reporte")]
    public async Task<IActionResult> ObtenerReporte()
    {
        var reporte = await _context.Reservas
            .Select(r => new {
                r.Nombre,
                r.Apellido,
                r.Matricula,
                r.Carrera,
                r.HoraInicio,
                r.HoraSalida,
                r.TiempoPromedio
            })
            .ToListAsync();

        return Ok(reporte);
    }
}

// Clase auxiliar para recibir solo la matrícula en la salida
public class SalidaRequest
{
    public string Matricula { get; set; } = string.Empty;
}