using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using ATEC.PM.Server.Services;
using ATEC.PM.Server;
using ATEC.PM.Server.Controllers;
using ATEC.PM.Server.Hubs;
using Serilog;


ExcelPackage.License.SetNonCommercialOrganization("ATEC");

// WIN1252 per le connessioni Firebird/Danea (migrazione catalogo): su .NET Core i
// codepage Windows richiedono la registrazione esplicita del provider.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

if (args.Contains("--cleanup-base64-images"))
{
    await QuoteHtmlCleanup.RunCliAsync(args);
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        path: @"C:\ATEC_PM\Logs\server-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

Log.Information("Ambiente: {Env}", builder.Environment.EnvironmentName);

builder.Host.UseSerilog();

// --- Auto-cifratura: se non esiste il file criptato, lo genera da appsettings.json ---
// ProtectedConfigHelper usa DPAPI: disponibile solo su Windows.
if (OperatingSystem.IsWindows())
{
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
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // CamelCase obbligatorio per il client web (altrimenti name/Name non allineati
        // e le liste FE risultano vuote, es. trattamenti DDP).
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("All", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Compressione risposte JSON (gzip + brotli) per client web
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o =>
    o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o =>
    o.Level = System.IO.Compression.CompressionLevel.Fastest);

string? jwtKeyValue = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKeyValue))
{
    throw new InvalidOperationException(
        "Jwt:Key non configurata. Impostare la chiave in appsettings.json (verrà cifrata al primo avvio) " +
        "o in secrets/variabile d'ambiente. L'avvio del server è interrotto per impedire l'uso di una chiave nota.");
}
// Difesa-in-profondità: blocca chiavi note/storiche se finissero in configurazione.
if (jwtKeyValue.Contains("ChangeMeInProduction", StringComparison.OrdinalIgnoreCase) ||
    jwtKeyValue.Contains("SuperSecretKey", StringComparison.OrdinalIgnoreCase) ||
    jwtKeyValue.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key non valida o di default. Generare una chiave casuale di almeno 32 caratteri e configurarla in modo sicuro.");
}
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
        // SignalR: WebSocket non può inviare l'header Authorization → il client .NET passa il
        // token come query string `access_token`. Lo leggiamo solo per il path dell'hub.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? accessToken = context.Request.Query["access_token"];
                PathString path = context.HttpContext.Request.Path;
                // Tutti gli hub SignalR (es. /hubs/resource-planner, /hubs/project).
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSignalR();

// Fallback policy: ogni endpoint senza [Authorize]/[AllowAnonymous] richiede comunque autenticazione.
// Difesa in profondità: se un nuovo controller dimentica [Authorize], NON resta aperto per sbaglio.
// Endpoint pubblici (es. /api/auth/login) DEVONO marcare esplicitamente [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<FeatureAccessService>();
builder.Services.AddSingleton<QuoteDbService>();
builder.Services.AddSingleton<GammaRobotDbService>();
builder.Services.AddSingleton<MoMDbService>();
builder.Services.AddSingleton<CheckListDbService>();
builder.Services.AddSingleton<ResourcesDbService>();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddSingleton<QuotePdfService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<ProjectTemplateCopyService>();
builder.Services.AddSingleton<CodexGeneratorService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<PlanNotificationService>();

// BackgroundServices — abilitabili da appsettings.json sezione "Services"
bool svcNotifications = builder.Configuration.GetValue("Services:Notifications", true);
bool svcCodexSync = builder.Configuration.GetValue("Services:CodexSync", true);
bool svcDaneaSync = builder.Configuration.GetValue("Services:DaneaSync", true);
bool svcBackup = builder.Configuration.GetValue("Services:Backup", true);
bool svcPlanDigest = builder.Configuration.GetValue("Services:PlanDigest", false);

if (svcNotifications)
    builder.Services.AddHostedService<NotificationBackgroundService>();

builder.Services.AddSingleton<CodexSyncService>();
if (svcCodexSync)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CodexSyncService>());

builder.Services.AddSingleton<DaneaSyncService>();
builder.Services.AddSingleton<DaneaMappingService>();
builder.Services.AddSingleton<DaneaMigrationService>();
builder.Services.AddSingleton<DaneaOrderService>();
if (svcDaneaSync)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DaneaSyncService>());

builder.Services.AddScoped<BackupController>();
if (svcBackup)
    builder.Services.AddHostedService<BackupBackgroundService>();

// Email digest: la coda di invio (EmailService) gira sempre (accoda a vuoto se non configurata);
// lo scheduler automatico (PlanDigestService) resta spento finché l'admin non lo abilita da config
// (di default "false": niente invii finché SMTP non è configurato e il digest attivato consapevolmente).
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailService>());
if (svcPlanDigest)
    builder.Services.AddHostedService<PlanDigestService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("All");
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Serve file statici dalla cartella CMS uploads (allegati prodotti, immagini, ecc.)
var cmsUploadsPath = app.Configuration["Uploads:CmsPath"] ?? Path.Combine(AppContext.BaseDirectory, "uploads", "cms");
Directory.CreateDirectory(cmsUploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(cmsUploadsPath),
    RequestPath = "/uploads/cms"
});

// Client web React (atec-pm-web) — stesso host dell'API
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ResourcePlannerHub>("/hubs/resource-planner");
app.MapHub<ProjectHub>("/hubs/project");
app.MapHub<CodexHub>("/hubs/codex");
app.MapFallbackToFile("index.html");
try
{
    string envName = app.Environment.EnvironmentName;
    Console.WriteLine($"[Startup] Ambiente: {envName}");

    DbService db = app.Services.GetRequiredService<DbService>();

    // Retry connessione DB con backoff (utile in Docker/compose)
    int maxRetries = 5;
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            using var testConn = db.Open();
            Console.WriteLine($"[Startup] Connessione DB verificata (tentativo {attempt}/{maxRetries})");
            break;
        }
        catch (Exception connEx) when (attempt < maxRetries)
        {
            int waitMs = attempt * 2000;
            Console.WriteLine($"[Startup] DB non raggiungibile (tentativo {attempt}/{maxRetries}): {connEx.Message}. Riprovo tra {waitMs}ms...");
            Thread.Sleep(waitMs);
        }
    }

    bool isProduction = !app.Environment.IsDevelopment();
    db.InitDatabase(productionMode: isProduction);
    Console.WriteLine($"[Startup] InitDatabase completato — ATEC PM Server avviato ({envName})");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERRORE InitDatabase] {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.WriteLine("Premi un tasto per uscire...");
    Console.ReadKey();
    return;
}

// In Debug (Development): avvia il dev server Vite (atec-pm-web) e apre il browser su 5173,
// così premendo Avvia (F5) partono insieme API e client web. In Release non fa nulla.
if (app.Environment.IsDevelopment())
{
    DevSpaLauncher.Launch(app.Logger, app.Environment.ContentRootPath);
}

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}