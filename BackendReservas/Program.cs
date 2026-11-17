using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Backend_SistReservas.Data;
using Backend_SistReservas.Models;
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

// ==========================================================================
// 3. AUTENTICACIÓN (JWT) Y PERMISOS POR ROL
//    - El token vale mientras su sesión esté abierta en la tabla `sesiones`
//      Y no hayan pasado Auth:DuracionSesionHoras (8 h) desde el login.
//      Se verifica en CADA petición (OnTokenValidated) con el RelojLaboratorio.
//    - Por defecto TODO endpoint exige sesión iniciada con rol Admin o
//      Laboratorio. Los que son solo de Admin llevan [Authorize(Roles = Roles.Admin)].
//      El único público es POST api/auth/login ([AllowAnonymous]).
// ==========================================================================
var authOpciones = builder.Configuration.GetSection(AuthOptions.SeccionConfig).Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOpciones.JwtKey) || authOpciones.JwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Falta configurar 'Auth:JwtKey' (mínimo 32 caracteres). En producción defínala con la variable de entorno Auth__JwtKey.");
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SeccionConfig));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOpciones.Issuer,
            ValidateAudience = true,
            ValidAudience = authOpciones.Issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.CrearClave(authOpciones.JwtKey),
            // El vencimiento NO se valida aquí porque usaría el reloj del
            // servidor; se valida en OnTokenValidated con el RelojLaboratorio.
            ValidateLifetime = false,
            RequireExpirationTime = false,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                if (!Guid.TryParse(ctx.Principal?.FindFirstValue(TokenService.ClaimSesion), out var sesionId))
                {
                    ctx.Fail("Token sin sesión.");
                    return;
                }

                var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var reloj = ctx.HttpContext.RequestServices.GetRequiredService<RelojLaboratorio>();
                var ahora = reloj.Ahora;

                var sesion = await db.Sesiones
                    .Include(s => s.Usuario)
                    .FirstOrDefaultAsync(s => s.Id == sesionId && s.CerradaEn == null && s.Usuario!.Activo,
                        ctx.HttpContext.RequestAborted);

                if (sesion == null)
                {
                    ctx.Fail("La sesión fue cerrada o el usuario está desactivado.");
                    return;
                }

                // Vencida por tiempo: se marca como cerrada para que quede registro.
                if (ahora - sesion.CreadaEn >= authOpciones.DuracionSesion)
                {
                    sesion.CerradaEn = sesion.CreadaEn + authOpciones.DuracionSesion;
                    await db.SaveChangesAsync(ctx.HttpContext.RequestAborted);
                    ctx.Fail("La sesión expiró. Inicie sesión nuevamente.");
                    return;
                }

                var usuario = sesion.Usuario!;

                // Rol y nombre SIEMPRE desde la base (si el admin cambia el
                // rol de alguien, aplica desde su siguiente petición).
                var identidad = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                    new Claim(ClaimTypes.Name, usuario.NombreUsuario),
                    new Claim(ClaimTypes.Role, usuario.Rol),
                    new Claim(TokenService.ClaimSesion, sesionId.ToString()),
                }, JwtBearerDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);

                ctx.Principal = new ClaimsPrincipal(identidad);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Todos)
        .Build();
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ==========================================================================
// 4. CONFIGURACIÓN DEL LABORATORIO (hora de cierre, intervalo de ping)
// Único lugar donde vive la hora de cierre: appsettings.json -> "Laboratorio"
// ==========================================================================
builder.Services.Configure<LaboratorioOptions>(
    builder.Configuration.GetSection(LaboratorioOptions.SeccionConfig));

// Reloj único del sistema: hora real de Ecuador tomada de internet, sin
// depender del reloj del servidor (ver Services/RelojLaboratorio.cs).
builder.Services.AddSingleton<RelojLaboratorio>();
builder.Services.AddSingleton<SincronizacionHoraService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SincronizacionHoraService>());

builder.Services.AddSingleton<PingService>();


builder.Services.AddSingleton<EquiposCatalogoService>();

// ==========================================================================
// 5. PROCESOS EN SEGUNDO PLANO
//    - PingBackgroundService: ping periódico a los equipos con IP registrada.
//    - CierreAutomaticoBackgroundService: cierre diario del laboratorio.
// No dependen de que el frontend esté abierto.
// ==========================================================================
builder.Services.AddHostedService<PingBackgroundService>();
builder.Services.AddHostedService<CierreAutomaticoBackgroundService>();

var app = builder.Build();

// Obtiene la hora real ANTES de atender peticiones o registrar nada.
await app.Services.GetRequiredService<SincronizacionHoraService>().SincronizarAsync();

// Crea las tablas `usuarios`/`sesiones` si no existen y el admin inicial.
await AuthInicializador.EjecutarAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

// Importante: El middleware de CORS debe ir antes de MapControllers
app.UseCors("PermitirFrontend");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
