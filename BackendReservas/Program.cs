using System.Data;
using MySql.Data.MySqlClient;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================================
// 1.  CONEXIÓN MYSQL (cambiar contraseña 'root')
// ==========================================================================
string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

// Si es nula (significa que estás en la pc local)
if (string.IsNullOrEmpty(connectionString))
{
    connectionString = "Server=localhost;Database=control_laboratorio;Uid=root;Pwd=root;";
}

// Configuración de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("PermitirFrontend");
app.MapControllers();

// ==========================================================================
// 2. ENDPOINT: REGISTRAR ENTRADA (POST /api/entrada)
// ==========================================================================
app.MapPost("/api/entrada", async (EntradaDto entrada) =>
{
    try
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string verificarSql = "SELECT COUNT(*) FROM reservas WHERE matricula = @matricula AND hora_salida IS NULL";
        using var verificarCmd = new MySqlCommand(verificarSql, connection);
        verificarCmd.Parameters.AddWithValue("@matricula", entrada.Matricula);
        long existe = Convert.ToInt64(await verificarCmd.ExecuteScalarAsync());

        if (existe > 0)
        {
            return Results.BadRequest(new { error = "El alumno ya cuenta con un registro de entrada activo." });
        }

        // AGREGADO: Columna 'equipo' en el INSERT
        string insertSql = @"INSERT INTO reservas (matricula, nombre, apellido, carrera, laboratorio, equipo) 
                             VALUES (@matricula, @nombre, @apellido, @carrera, 'L002', @equipo)";
        
        using var cmd = new MySqlCommand(insertSql, connection);
        cmd.Parameters.AddWithValue("@matricula", entrada.Matricula);
        cmd.Parameters.AddWithValue("@nombre", entrada.Nombre);
        cmd.Parameters.AddWithValue("@apellido", entrada.Apellido);
        cmd.Parameters.AddWithValue("@carrera", entrada.Carrera);
        cmd.Parameters.AddWithValue("@equipo", entrada.Equipo); // AGREGADO: Parámetro para el equipo

        await cmd.ExecuteNonQueryAsync();

        return Results.Ok(new { message = $"Entrada registrada con éxito para la matrícula {entrada.Matricula}" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error en el servidor: {ex.Message}");
    }
});

// ==========================================================================
// 3. ENDPOINT: REGISTRAR SALIDA (PUT /api/salida) 
app.MapPut("/api/salida", async (SalidaDto salida) =>
{
    try
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string buscarSql = "SELECT id, hora_inicio FROM reservas WHERE matricula = @matricula AND hora_salida IS NULL ORDER BY hora_inicio DESC LIMIT 1";
        using var buscarCmd = new MySqlCommand(buscarSql, connection);
        buscarCmd.Parameters.AddWithValue("@matricula", salida.Matricula);

        using var reader = await buscarCmd.ExecuteReaderAsync();
        
        if (!await reader.ReadAsync())
        {
            return Results.BadRequest(new { message = "No se encontró una entrada activa para esta matrícula." });
        }

        int idRegistro = reader.GetInt32("id");
        DateTime horaInicio = reader.GetDateTime("hora_inicio");
        await reader.CloseAsync();

        // ------------------------------------------------------------------
        //  CONVERSIÓN DE TIEMPO
        // ------------------------------------------------------------------
        DateTime horaSalida = DateTime.Now;
        int minutosTotales = (int)(horaSalida - horaInicio).TotalMinutes;
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
        // ------------------------------------------------------------------

        string updateSql = @"UPDATE reservas 
                             SET hora_salida = @hora_salida, tiempo_promedio = @tiempo_promedio 
                             WHERE id = @id";

        using var updateCmd = new MySqlCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("@hora_salida", horaSalida);
        updateCmd.Parameters.AddWithValue("@tiempo_promedio", textoTiempo); 
        updateCmd.Parameters.AddWithValue("@id", idRegistro);

        await updateCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { message = $"Salida registrada con éxito. Tiempo de uso: {textoTiempo}." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error en el servidor: {ex.Message}");
    }
});

// ==========================================================================
// 4. ENDPOINT: OBTENER REPORTE (GET /api/reporte)
// ==========================================================================
app.MapGet("/api/reporte", async () =>
{
    try
    {
        var listaReporte = new List<Dictionary<string, object>>();

        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        // AGREGADO: Se incluyó 'equipo' en la consulta SQL
        string selectSql = "SELECT nombre, apellido, matricula, carrera, equipo, hora_inicio, hora_salida, tiempo_promedio FROM reservas ORDER BY hora_inicio DESC";
        using var cmd = new MySqlCommand(selectSql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var fila = new Dictionary<string, object>
            {
                { "nombre", reader.IsDBNull(reader.GetOrdinal("nombre")) ? "" : reader.GetString("nombre") },
                { "apellido", reader.IsDBNull(reader.GetOrdinal("apellido")) ? "" : reader.GetString("apellido") },
                { "matricula", reader.IsDBNull(reader.GetOrdinal("matricula")) ? "" : reader.GetString("matricula") },
                { "carrera", reader.IsDBNull(reader.GetOrdinal("carrera")) ? "" : reader.GetString("carrera") },
                // AGREGADO: Mapeo de la columna equipo hacia el objeto JSON enviado al frontend
                { "equipo", reader.IsDBNull(reader.GetOrdinal("equipo")) ? "N/A" : reader.GetString("equipo") },
                { "hora_inicio", reader.GetDateTime("hora_inicio").ToString("yyyy-MM-dd HH:mm:ss") },
                { "hora_salida", reader.IsDBNull(reader.GetOrdinal("hora_salida")) ? "En uso" : reader.GetDateTime("hora_salida").ToString("yyyy-MM-dd HH:mm:ss") },
                { "tiempo_promedio", reader.IsDBNull(reader.GetOrdinal("tiempo_promedio")) ? "N/A" : reader.GetString("tiempo_promedio") }
            };
            listaReporte.Add(fila);
        }

        return Results.Ok(listaReporte);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error al generar reporte: {ex.Message}");
    }
});

app.Run();

// AGREGADO: Se añade 'string Equipo' en el Record para recibir el dato de React
public record EntradaDto(string Matricula, string Nombre, string Apellido, string Carrera, string Equipo);
public record SalidaDto(string Matricula);