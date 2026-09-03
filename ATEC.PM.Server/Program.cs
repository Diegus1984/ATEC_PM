using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Server.Services.RisorseSync;
using ATEC.PM.Server;
using ATEC.PM.Server.Controllers;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Middleware;
using ATEC.PM.Shared.DTOs;
using Microsoft.Extensions.Hosting.WindowsServices;
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

// Come servizio Windows la directory corrente è C:\Windows\System32: senza forzare la
// content root sulla cartella del programma, appsettings.json e wwwroot non verrebbero
// trovati. ContentRootPath va passato QUI (CreateBuilder legge subito la configurazione):
// impostarlo dopo, con UseWindowsService(), sarebbe troppo tardi.
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

// Ciclo di vita da servizio Windows (no-op quando si avvia normalmente da console/VS).
builder.Host.UseWindowsService();

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

// L'avviso va dato all'avvio, dove si legge: se la lista delle origini manca, l'API resta
// aperta a qualunque sito e nessuno se ne accorgerebbe mai.
if (builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() is not { Length: > 0 })
    Log.Warning("[CORS] Security:AllowedOrigins non configurato: l'API accetta richieste da QUALUNQUE origine. " +
                "Impostare la lista in appsettings.json (es. http://192.168.2.150:5150).");

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

builder.Services.AddControllers(options =>
    {
        // Il filtro unico dei dati sensibili (rebuild permessi §12.3): azzera i prezzi in
        // uscita e respinge le scritture di prezzi a chi non ha il micro <voce>.prices.
        // Scatta SOLO sugli endpoint le cui voci di catalogo dichiarano il micro.
        options.Filters.Add<ATEC.PM.Server.Authorization.PrezziSensibiliFilter>();
    })
    .AddJsonOptions(options =>
    {
        // CamelCase obbligatorio per il client web (altrimenti name/Name non allineati
        // e le liste FE risultano vuote, es. trattamenti DDP).
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// Validazione dei DTO: la risposta deve parlare la lingua di tutte le altre.
//
// Di suo ASP.NET, quando un [Required]/[Range] non è soddisfatto, risponde 400 con un
// `ProblemDetails` — un formato che il client web NON sa leggere: cerca { success, message },
// non trova né l'uno né l'altro e mostra un errore generico (o niente). Qui la stessa
// informazione esce come ApiResponse.Fail con l'elenco dei campi, così l'utente legge cosa
// deve correggere invece di «errore imprevisto».
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => RispostaValidazione.Crea(context.ModelState);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// CORS: lista chiusa di origini, non «chiunque».
//
// In produzione il client è servito dallo STESSO host dell'API, quindi le sue chiamate sono
// same-origin e il CORS non le riguarda nemmeno: a usarlo davvero è solo il dev server Vite
// (localhost:5173 → localhost:5150). Aprire a tutti non serviva a niente e lasciava che
// qualunque pagina web, aperta da un PC in azienda, parlasse con il gestionale usando il token
// di chi la sta guardando.
//
// Se `Security:AllowedOrigins` non è configurato si resta al comportamento di prima (tutti),
// con un avviso nel log: meglio un avviso che un gestionale che smette di funzionare per una
// chiave dimenticata in un file di configurazione che gli aggiornamenti non sovrascrivono.
string[] originiAmmesse = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>()
                          ?? Array.Empty<string>();

builder.Services.AddCors(o => o.AddPolicy("All", p =>
{
    if (originiAmmesse.Length > 0)
        p.WithOrigins(originiAmmesse).AllowAnyMethod().AllowAnyHeader();
    else
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
}));

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
            },
            // Chi non è più in forza deve uscire SUBITO, non alla scadenza del token: il token
            // dura 8 ore e fin qui `status` si guardava solo al login (AuthController), quindi
            // un cessato continuava a lavorare per mezza giornata — il gesto «se ne va» del
            // PIANO-PERMESSI (§4.3, §8) non buttava fuori nessuno.
            //
            // Il controllo sta QUI perché è l'unico punto attraversato da ogni richiesta
            // autenticata, hub SignalR compresi, e perché fallire qui dà 401 (il client torna
            // al login) e non un 403 che sembrerebbe un problema di permessi.
            // Costo: una lettura da cache in memoria (30 s), non una query per richiesta —
            // vedi FeatureAccessService.IsUtenteAttivo.
            OnTokenValidated = context =>
            {
                FeatureAccessService access = context.HttpContext.RequestServices
                    .GetRequiredService<FeatureAccessService>();
                string? idClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // Token senza id della persona: non si può nemmeno sapere di chi si tratta.
                if (!int.TryParse(idClaim, out int employeeId) || !access.IsUtenteAttivo(employeeId))
                    context.Fail("Utenza non più attiva.");

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

// Le anagrafiche tenute in memoria (blocco E4). Singleton: la cache è del processo, e il
// gestionale gira in UNA sola istanza (servizio Windows AtecPmServer).
builder.Services.AddSingleton<AnagraficheCache>();
builder.Services.AddSingleton<FeatureAccessService>();
// Cancello delle scritture su commessa non attiva (#88): sa la regola, non la applica da sé —
// la applicano `[RequireProjectWritable]` e le poche azioni che lo chiamano a mano.
builder.Services.AddSingleton<ProjectWriteGuard>();
builder.Services.AddSingleton<PermissionChangeService>();
builder.Services.AddSingleton<PermissionSeedService>();
builder.Services.AddSingleton<PermissionAdminService>();
builder.Services.AddSingleton<QuoteDbService>();
builder.Services.AddSingleton<GammaRobotDbService>();
builder.Services.AddSingleton<MoMDbService>();
builder.Services.AddSingleton<CheckListDbService>();
builder.Services.AddSingleton<BugReportsDbService>();
builder.Services.AddSingleton<ResourcesDbService>();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddSingleton<QuotePdfService>();
builder.Services.AddSingleton<NotificationService>();
// Campanella del planner Risorse (#148): dipende da DbService e NotificationService, mai dal motore di sync.
builder.Services.AddSingleton<AllocazioniCampanella>();
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
// Sessioni SMB autenticate verso le share di rete (cartella immagini Danea): il servizio
// gira come account locale, che il server della share non conosce.
builder.Services.AddSingleton<NetworkShareConnector>();
builder.Services.AddSingleton<DaneaMigrationService>();
builder.Services.AddSingleton<DaneaOrderService>();
// #129: ripescaggio automatico dal vecchio archivio (articoli oltre lo spartiacque)
builder.Services.AddSingleton<DaneaOldPullService>();
if (svcDaneaSync)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DaneaSyncService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DaneaOldPullService>());
}

// Modulo HR presenze (PIANO-HR-PRESENZE.md): client Ecos + import timbrature.
// Senza credenziali (sezione "Ecos") tutto resta a riposo: in sviluppo è la norma.
builder.Services.AddSingleton<EcosClient>();
builder.Services.AddSingleton<HrChangeNotifier>();
builder.Services.AddSingleton<HrAttendanceService>();
bool svcHrSync = builder.Configuration.GetValue("Services:HrSync", true);
if (svcHrSync)
    builder.Services.AddHostedService<HrSyncBackgroundService>();

builder.Services.AddSingleton<FullBackupService>();
builder.Services.AddScoped<BackupController>();
if (svcBackup)
    builder.Services.AddHostedService<BackupBackgroundService>();

// Email digest: la coda di invio (EmailService) gira sempre (accoda a vuoto se non configurata);
// lo scheduler automatico (PlanDigestService) resta spento finché l'admin non lo abilita da config
// (di default "false": niente invii finché SMTP non è configurato e il digest attivato consapevolmente).
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailService>());
if (svcPlanDigest)
    builder.Services.AddHostedService<PlanDigestService>();

// Sincronizzazione Risorse ATEC PM ⇄ VPS (PIANO-SYNC-RISORSE.md). Il singleton c'è sempre
// (ResourcesController lo usa per Trigger e per il pannello); il loop in sottofondo parte solo
// con Services:RisorseSync (default true) — e comunque resta a riposo, senza toccare la rete,
// finché dal pannello non si accende e non si mettono indirizzo del VPS, utente e password.
// Le impostazioni vivono in res_settings (chiavi sync.*); la sezione "RisorseSync" di
// appsettings.json (Enabled/BaseUrl/Username/Password) è solo il RIPIEGO per il primo avvio,
// stessa via della configurazione SMTP.
builder.Services.AddSingleton<RisorseSyncService>();
bool svcRisorseSync = builder.Configuration.GetValue("Services:RisorseSync", true);
if (svcRisorseSync)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RisorseSyncService>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// PRIMO della catena, prima di tutto il resto: da qui in giù qualunque eccezione non gestita
// diventa una risposta JSON nel contratto ApiResponse invece della pagina HTML di errore di
// ASP.NET, che il client web riceverebbe dove si aspetta { success, data, message }.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Blocco E1 — misurare prima di correggere: una riga di log per ogni richiesta oltre i 500 ms
// (soglia in Diagnostics:SlowRequestMs, 0 per spegnere). Sta QUI, subito dentro il middleware
// degli errori, per due motivi: più in alto misurerebbe anche il proprio guscio; più in basso
// perderebbe compressione, file statici e autenticazione — cioè pezzi di ciò che l'utente aspetta.
app.UseMiddleware<RichiesteLenteMiddleware>();

app.UseCors("All");
app.UseResponseCompression();

// In LAN il server gira in HTTP puro (nessun certificato sulla macchina aziendale):
// con Security:RequireHttps=false il redirect a HTTPS va spento, altrimenti ogni
// richiesta verrebbe rimbalzata su una porta che non esiste.
bool requireHttps = !app.Environment.IsDevelopment()
    && app.Configuration.GetValue("Security:RequireHttps", true);
if (requireHttps)
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

// Client web React (atec-pm-web) — stesso host dell'API.
// Due file NON devono mai finire in cache:
//  • index.html — è l'unico con nome fisso e contiene i riferimenti agli asset con hash.
//    Se resta in cache, dopo un aggiornamento il browser continua a chiedere il bundle
//    vecchio finché non si fa Ctrl+F5.
//  • version.json — è il file che le schede già aperte interrogano ogni minuto per
//    accorgersi che è stata pubblicata una nuova versione (barra «Aggiorna adesso»).
//    Servito dalla cache, l'avviso non comparirebbe mai.
// JS/CSS invece hanno l'hash nel nome, quindi restano cacheabili a vita.
static void NoCacheForShellFiles(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext ctx)
{
    bool alwaysFresh =
        ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
        ctx.File.Name.Equals("version.json", StringComparison.OrdinalIgnoreCase);
    if (!alwaysFresh) return;
    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    ctx.Context.Response.Headers.Pragma = "no-cache";
    ctx.Context.Response.Headers.Expires = "0";
}

var spaFileOptions = new StaticFileOptions { OnPrepareResponse = NoCacheForShellFiles };
app.UseDefaultFiles();
app.UseStaticFiles(spaFileOptions);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Sonda di stato senza login: la usano gli script di installazione/aggiornamento
// (e chiunque voglia sapere se il servizio è vivo) senza doversi autenticare.
//
// ⚠️ Questa dice SOLO «il processo risponde». Non guarda il database: serve a sapere che
// Kestrel è in piedi. Per «il gestionale funziona davvero» c'è /api/health/ready qui sotto.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    environment = app.Environment.EnvironmentName,
    version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
    // Quando è stato installato il server che sta rispondendo: il build id in basso a
    // sinistra è quello del CLIENT, e un aggiornamento di solo C# non lo muove.
    build = VersioneServer.Build,
    utc = DateTime.UtcNow
})).AllowAnonymous();

// Sonda VERA: il database risponde? Fino al 14/08/2026 gli script di deploy si fidavano di
// /api/health, che dice «ok» anche con MySQL spento — cioè un aggiornamento che rompe la
// connessione al database veniva dichiarato riuscito e il rollback automatico non scattava.
//
// 503 (e non 500) quando il database non c'è: è il codice che significa «sono vivo ma non
// posso servire», ed è quello che gli script sanno leggere.
//
// 🪤 NON aggiungere qui i percorsi di rete (Backup:Path, cartella documenti su share): uno
// share momentaneamente giù farebbe fallire l'aggiornamento con il database perfettamente
// sano, cioè un rollback per il motivo sbagliato. Se un domani serve sorvegliarli, un
// endpoint separato che gli script NON interrogano.
app.MapGet("/api/health/ready", async (DbService db, ILogger<Program> log) =>
{
    string? errore = await db.ProvaDatabaseAsync();

    if (errore == null)
        return Results.Ok(new
        {
            status = "ready",
            database = "ok",
            environment = app.Environment.EnvironmentName,
            utc = DateTime.UtcNow
        });

    log.LogError("[Health] Database non raggiungibile: {Messaggio}", errore);
    return Results.Json(new
    {
        status = "degraded",
        database = "ko",
        message = errore,
        utc = DateTime.UtcNow
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapHub<ResourcePlannerHub>("/hubs/resource-planner");
app.MapHub<ProjectHub>("/hubs/project");
app.MapHub<CodexHub>("/hubs/codex");
// Un endpoint /api inesistente deve rispondere 404 JSON, non la SPA: con il solo
// catch-all sotto, una chiamata sbagliata tornava 200 con l'HTML di index.html —
// il client la leggeva come riuscita e, nei test dei permessi, un endpoint scritto
// male sembrava "aperto a tutti". Questo fallback è più specifico e vince su quello
// generico. Vale solo per le rotte NON gestite dai controller.
app.MapFallback("/api/{**path}", (HttpContext ctx) =>
        Results.NotFound(ApiResponse<string>.Fail($"Endpoint non trovato: {ctx.Request.Path}")))
    .AllowAnonymous();

// AllowAnonymous obbligatorio: il catch-all della SPA è un endpoint e senza questo
// ricade nella policy globale "serve un utente autenticato", quindi il browser si
// prende un 401 al posto della pagina e non riesce nemmeno ad arrivare al login.
// (In sviluppo non si vede: lì la pagina la serve Vite.)
app.MapFallbackToFile("index.html", spaFileOptions).AllowAnonymous();
try
{
    string envName = app.Environment.EnvironmentName;
    Console.WriteLine($"[Startup] Ambiente: {envName}");

    DbService db = app.Services.GetRequiredService<DbService>();

    // Attesa del server MySQL con backoff (utile in Docker/servizio Windows al boot) e
    // creazione del database se manca. Si connette SENZA database perché al primo avvio
    // non esiste ancora: un test con db.Open() qui fallirebbe con «Unknown database».
    bool dbCreated = db.EnsureDatabaseExists();
    Console.WriteLine(dbCreated
        ? "[Startup] Database non presente: creato ora (primo avvio)"
        : "[Startup] Connessione DB verificata");

    bool isProduction = !app.Environment.IsDevelopment();
    db.InitDatabase(productionMode: isProduction);
    Console.WriteLine($"[Startup] InitDatabase completato — ATEC PM Server avviato ({envName})");
}
catch (Exception ex)
{
    // Serilog e NON Console.WriteLine: come servizio Windows stdout non va da nessuna parte, e
    // questo è l'unico punto in cui si legge PERCHÉ il servizio non è partito. Da quando una
    // migrazione fallita interrompe l'avvio (Migrations:StopOnError), è il messaggio che serve
    // a chi in azienda vede solo un servizio che non parte. CloseAndFlush obbligatorio: senza,
    // il processo esce prima che il file di log sia scritto.
    Log.Fatal(ex, "[Startup] Avvio interrotto: {Message}", ex.Message);
    Console.WriteLine($"[ERRORE InitDatabase] {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Log.CloseAndFlush();

    // La pausa ha senso solo con una console vera: come servizio Windows stdin è chiuso
    // e Console.ReadKey() solleverebbe un'eccezione mascherando l'errore originale.
    if (Environment.UserInteractive && !Console.IsInputRedirected)
    {
        Console.WriteLine("Premi un tasto per uscire...");
        Console.ReadKey();
    }
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