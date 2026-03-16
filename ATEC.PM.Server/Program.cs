using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ATEC.PM.Server.Services;
using ATEC.PM.Server;
using ATEC.PM.Server.Controllers;
using Serilog;


ExcelPackage.License.SetNonCommercialOrganization("ATEC");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        path: @"C:\ATEC_PM\Logs\server-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

// --- Auto-cifratura: se non esiste il file criptato, lo genera da appsettings.json ---
if (!ProtectedConfigHelper.IsConfigured())
{
    string connStr = builder.Configuration["ConnectionStrings:Default"] ?? "";
    string jwt = builder.Configuration["Jwt:Key"] ?? "";

    if (!string.IsNullOrWhiteSpace(connStr) && !connStr.StartsWith("RUN:") &&
        !string.IsNullOrWhiteSpace(jwt) && !jwt.StartsWith("RUN:"))
    {
        ProtectedConfigHelper.GenerateSecretsFile(connStr, jwt);
        ProtectedConfigHelper.CleanAppSettings();
        Console.WriteLine("[Config] Segreti criptati automaticamente al primo avvio.");
    }
}

// --- Carica segreti criptati ---
Dictionary<string, string?> secrets = ProtectedConfigHelper.LoadSecrets();
if (secrets.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(secrets);
    Console.WriteLine("[Config] Segreti criptati caricati.");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("All", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var jwtKeyValue = builder.Configuration["Jwt:Key"] ?? "ATEC-PM-SuperSecretKey-ChangeMeInProduction-2026!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "ATEC.PM",
            ValidAudience = "ATEC.PM",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKeyValue))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddHostedService<NotificationBackgroundService>();
builder.Services.AddSingleton<CodexGeneratorService>();
builder.Services.AddSingleton<CodexSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CodexSyncService>());
builder.Services.AddSingleton<DaneaSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DaneaSyncService>());
builder.Services.AddScoped<BackupController>();
builder.Services.AddHostedService<BackupBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseCors("All");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
try
{
    app.Services.GetRequiredService<DbService>().InitDatabase();
    Console.WriteLine("ATEC PM Server avviato");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERRORE InitDatabase] {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.WriteLine("Premi un tasto per uscire...");
    Console.ReadKey();
    return;
}

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}