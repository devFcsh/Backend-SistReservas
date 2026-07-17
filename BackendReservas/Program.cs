using Microsoft.EntityFrameworkCore;
using Backend_SistReservas.Data;
using Backend_SistReservas.Services;

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

// Inyección de dependencias para Entity Framework Core con MySQL Pomelo.
//
// IMPORTANTE: en vez de ServerVersion.AutoDetect(connectionString) —que
// abre una conexión real a MySQL solo para detectar la versión del
// servidor, y lo hace en el momento en que el DbContext se construye para
// CADA request, antes de que cualquier try/catch dentro de un controlador
// pueda atraparlo— se fija una versión conocida de antemano. Así, construir
// el DbContext nunca requiere una conexión viva; el error de conexión (si
// las credenciales están mal, o MySQL no responde) solo aparece cuando se
// ejecuta una consulta real, que es exactamente donde los controladores
// (ver EquiposController) sí pueden atraparlo y responder con tolerancia.
ServerVersion serverVersion;
try
{
    serverVersion = ServerVersion.AutoDetect(connectionString);
}
catch
{
    // No se pudo conectar para detectar la versión (credenciales incorrectas,
    // MySQL apagado, etc.). Se usa una versión razonable por defecto para
    // que el backend arranque igual; los errores de conexión reales seguirán
    // apareciendo al ejecutar consultas, pero ya no impiden que el backend
    // inicie ni se cuelan "crudos" antes de los try/catch de los controladores.
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

// Catálogo de equipos EN MEMORIA (sembrado desde "Laboratorio:Equipos").
// No requiere ninguna tabla nueva en la base de datos.
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
