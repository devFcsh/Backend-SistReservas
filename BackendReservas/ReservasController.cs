using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Backend_SistReservas.Controllers
{
    [ApiController]
    [Route("api")] // Raíz base unificada con las peticiones de React
    public class ReservasController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReservasController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==========================================================================
        // POST: api/entrada -> Registrar nueva reserva de laboratorio
        // ==========================================================================
        [HttpPost("entrada")]
        public async Task<IActionResult> RegistrarEntrada([FromBody] Reserva entrada)
        {
            if (entrada == null) 
                return BadRequest(new { error = "Los datos de ingreso enviados no son válidos." });

            // Validación de concurrencia: Evita que se dupliquen registros activos
            bool tieneEntradaActiva = await _context.Reservas
                .AnyAsync(r => r.Matricula == entrada.Matricula && r.HoraSalida == null);

            if (tieneEntradaActiva)
            {
                return BadRequest(new { error = "El alumno ya cuenta con un registro de entrada activo en el laboratorio." });
            }

            // Sanitización e inicialización segura en el servidor
            entrada.Laboratorio = "L002";
            entrada.HoraInicio = DateTime.Now;
            entrada.HoraSalida = null;
            entrada.TiempoPromedio = null;

            _context.Reservas.Add(entrada);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Entrada registrada con éxito para la matrícula {entrada.Matricula}" });
        }

        // ==========================================================================
        // PUT: api/salida -> Cerrar sesión activa calculando tiempos de uso
        // ==========================================================================
        [HttpPut("salida")]
        public async Task<IActionResult> RegistrarSalida([FromBody] SalidaRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Matricula)) 
                return BadRequest(new { message = "La matrícula es requerida para el registro." });

            // Busca el último registro abierto correspondiente a la matrícula
            var ultimaReserva = await _context.Reservas
                .Where(r => r.Matricula == request.Matricula && r.HoraSalida == null)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (ultimaReserva == null)
            {
                return BadRequest(new { message = "No se encontró una entrada activa para esta matrícula." });
            }

            DateTime horaActual = DateTime.Now;
            ultimaReserva.HoraSalida = horaActual;
            
            // Cálculo del tiempo transcurrido en el sistema
            TimeSpan diferencia = horaActual - ultimaReserva.HoraInicio;
            int minutosTotales = (int)diferencia.TotalMinutes;
            if (minutosTotales < 1) minutosTotales = 1; // Margen de seguridad para salidas instantáneas

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
            
            ultimaReserva.TiempoPromedio = textoTiempo; 

            _context.Reservas.Update(ultimaReserva);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Salida registrada con éxito. Tiempo de uso: {textoTiempo}." });
        }

        // ==========================================================================
        // GET: api/reporte -> Obtener el listado histórico detallado para tablas/Excel
        // ==========================================================================
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
                    r.Equipo, 
                    Fecha = r.HoraInicio.ToString("yyyy-MM-dd"),
                    HoraInicio = r.HoraInicio.ToString("HH:mm:ss"),
                    HoraSalida = r.HoraSalida.HasValue ? r.HoraSalida.Value.ToString("HH:mm:ss") : "En uso",
                    TiempoPromedio = r.TiempoPromedio ?? "N/A"
                })
                .ToListAsync();

            return Ok(reporte);
        }

        // ==========================================================================
        // GET: api/alumno/{matricula} -> Autocompletado/Historial veloz por alumno
        // ==========================================================================
        [HttpGet("alumno/{matricula}")]
        public async Task<IActionResult> ObtenerAlumnoPorMatricula(string matricula)
        {
            var ultimoRegistro = await _context.Reservas
                .Where(r => r.Matricula == matricula)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            if (ultimoRegistro != null)
            {
                return Ok(new { 
                    nombre = ultimoRegistro.Nombre, 
                    apellido = ultimoRegistro.Apellido, 
                    encontrado = true 
                });
            }

            return Ok(new { encontrado = false });
        }
    }

    // Objeto de transferencia de datos de salida para el tipado estricto de la petición PUT
    public class SalidaRequest
    {
        public string Matricula { get; set; } = string.Empty;
    }
}