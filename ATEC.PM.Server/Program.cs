using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ATEC.PM.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Cerca la riga builder.Services.AddControllers() e modificala così:
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Evita che riferimenti circolari (es. Commessa -> Fase -> Commessa) rompano il JSON
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        // Gestisce correttamente i valori null senza interrompere il token JSON
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("All", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "ATEC-PM-SuperSecretKey-ChangeMeInProduction-2026!";
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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
