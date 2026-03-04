using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ATEC.PM.Server.Services;
using ATEC.PM.Server;

ExcelPackage.License.SetNonCommercialOrganization("ATEC");

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseCors("All");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Services.GetRequiredService<DbService>().InitDatabase();

Console.WriteLine("ATEC PM Server avviato");
app.Run();