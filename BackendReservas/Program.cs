using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================================
// 1. CONFIGURACIÓN DE CADENA DE CONEXIÓN (Local vs Producción)
// ==========================================================================
string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (string.IsNullOrEmpty(connectionString))
{
    // Conexión por defecto para entorno local
    connectionString = "Server=localhost;Database=control_laboratorio;Uid=root;Pwd=root;";
}

// Inyección de dependencias para Entity Framework Core con MySQL Pomelo
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ==========================================================================
// 2. CONFIGURACIÓN DE CORS (Conexión directa con Vite/React)
// ==========================================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Importante: El middleware de CORS debe ir antes de MapControllers
app.UseCors("PermitirFrontend");

app.UseAuthorization();
app.MapControllers();

app.Run();