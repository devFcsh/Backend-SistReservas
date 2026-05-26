using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

[ApiController]
[Route("api/reservas")] // Ajustado para mantener consistencia
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

        // Validar si ya existe una entrada activa desde el controlador también
        bool existe = await _context.Reservas
            .AnyAsync(r => r.Matricula == entrada.Matricula && r.HoraSalida == null);

        if (existe)
        {
            return BadRequest(new { error = "El alumno ya cuenta con un registro de entrada activo." });
        }

        entrada.Laboratorio = "L002";
        entrada.HoraInicio = DateTime.Now;
        entrada.HoraSalida = null;
        entrada.TiempoPromedio = null;

        _context.Reservas.Add(entrada);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Entrada registrada con éxito para la matrícula {entrada.Matricula}" });
    }

    // 2. REGISTRO DE SALIDA (PUT: api/reservas/salida)
    [HttpPut("salida")]
    public async Task<IActionResult> RegistrarSalida([FromBody] SalidaRequest request)
    {
        var ultimaReserva = await _context.Reservas
            .Where(r => r.Matricula == request.Matricula && r.HoraSalida == null)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        if (ultimaReserva == null)
        {
            return NotFound(new { message = "No se encontró una entrada activa para esta matrícula." });
        }

        ultimaReserva.HoraSalida = DateTime.Now;
        
        // --- CÁLCULO DE TIEMPO FORMATEADO EN TEXTO (VARCHAR) ---
        TimeSpan diferencia = ultimaReserva.HoraSalida.Value - ultimaReserva.HoraInicio;
        int minutosTotales = (int)diferencia.TotalMinutes;
        if (minutosTotales < 1) minutosTotales = 1;

        string textoTiempo;
        if (minutosTotales >= 60)
        {
            int horas = minutosTotales / 60;
            int minutosRestantes = minutosTotales % 60;
            textoTiempo = minutosRestantes == 0 ? $"{horas} h" : $"{horas} h {minutosRestantes} min";
        }
        else
        {
            textoTiempo = $"{minutosTotales} min";
        }
        
        ultimaReserva.TiempoPromedio = textoTiempo; // Guardamos el string formateado

        _context.Reservas.Update(ultimaReserva);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Salida registrada con éxito. Tiempo de uso: {textoTiempo}." });
    }

    // 3. OBTENER DATOS (GET: api/reservas/reporte)
    [HttpGet("reporte")]
    public async Task<IActionResult> ObtenerReporte()
    {
        var reporte = await _context.Reservas
            .OrderByDescending(r => r.HoraInicio)
            .Select(r => new {
                r.Nombre,
                r.Apellido,
                r.Matricula,
                r.Carrera,
                r.Equipo, // AGREGADO AL SELECT
                HoraInicio = r.HoraInicio.ToString("yyyy-MM-dd HH:mm:ss"),
                HoraSalida = r.HoraSalida.HasValue ? r.HoraSalida.Value.ToString("yyyy-MM-dd HH:mm:ss") : "En uso",
                TiempoPromedio = r.TiempoPromedio ?? "N/A"
            })
            .ToListAsync();

        return Ok(reporte);
    }
}

public class SalidaRequest
{
    public string Matricula { get; set; } = string.Empty;
}