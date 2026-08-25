using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "[HH:mm:ss] ";
});
builder.Logging.AddDebug();

// ==========================================================================
// 1. CONFIGURACIÓN DE CADENA DE CONEXIÓN (Local vs Producción)
// ==========================================================================
string? connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (string.IsNullOrEmpty(connectionString))
{
    // Conexión por defecto para entorno local
    connectionString = "Server=localhost;Database=control_laboratorio;Uid=root;Pwd=root;";
}


ServerVersion serverVersion;
try
{
    serverVersion = ServerVersion.AutoDetect(connectionString);
}
catch
{
    
    serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

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

// ==========================================================================
// 3. CONFIGURACIÓN DEL LABORATORIO (hora de cierre, intervalo de ping)
// Único lugar donde vive la hora de cierre: appsettings.json -> "Laboratorio"
// ==========================================================================
builder.Services.Configure<LaboratorioOptions>(
    builder.Configuration.GetSection(LaboratorioOptions.SeccionConfig));

builder.Services.AddSingleton<PingService>();


builder.Services.AddSingleton<EquiposCatalogoService>();

// ==========================================================================
// 4. PROCESOS EN SEGUNDO PLANO
//    - PingBackgroundService: ping periódico a los equipos con IP registrada.
//    - CierreAutomaticoBackgroundService: cierre diario del laboratorio.
// No dependen de que el frontend esté abierto.
// ==========================================================================
builder.Services.AddHostedService<PingBackgroundService>();
builder.Services.AddHostedService<CierreAutomaticoBackgroundService>();

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
