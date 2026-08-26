using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dapper;

// EPPlus (OfficeOpenXml, in GlobalUsing) espone un CompressionLevel omonimo: qui serve
// sempre e solo quello degli zip.
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Backup e ripristino COMPLETI: database + cartelle su disco (documenti di commessa,
/// allegati/immagini caricate). Il backup "classico" (solo .sql) resta quello che gira
/// ogni notte: leggero e veloce. Questo produce invece un unico .zip che contiene tutto
/// ed è pensato per essere portato via dalla macchina — è l'unico modo per rimettere in
/// piedi il gestionale se il server si rompe.
///
/// Lavora in background con un identificativo di operazione (le cartelle possono pesare
/// parecchi GB: una richiesta HTTP sincrona andrebbe in timeout).
/// </summary>
public class FullBackupService
{
    private readonly DbService _db;
    private readonly IConfiguration _config;
    private readonly ILogger<FullBackupService> _log;
    private readonly AnagraficheCache _cache;
    private readonly FeatureAccessService _access;
    private readonly NetworkShareConnector _share;
    private readonly ConcurrentDictionary<string, BackupJob> _jobs = new();

    // Comprimere di nuovo ciò che è già compresso (foto, video, PDF-immagine, archivi)
    // costa molto tempo e non guadagna spazio: quei file entrano nello zip così come sono.
    private static readonly HashSet<string> GiaCompressi = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp",
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".mp3", ".m4a",
        ".zip", ".7z", ".rar", ".gz", ".tgz", ".cab", ".msi", ".exe"
    };

    public FullBackupService(DbService db, IConfiguration config, ILogger<FullBackupService> log,
        AnagraficheCache cache, FeatureAccessService access, NetworkShareConnector share)
    {
        _db = db;
        _config = config;
        _log = log;
        _cache = cache;
        _access = access;
        _share = share;
    }

    // ══════════════════════════════════════════════════════════════
    // STATO DELLE OPERAZIONI
    // ══════════════════════════════════════════════════════════════

    public class BackupJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Tipo { get; set; } = "backup";      // backup | ripristino
        public string Stato { get; set; } = "in_corso";   // in_corso | completato | errore
        public string Passo { get; set; } = "";
        public int Percentuale { get; set; }
        public string Messaggio { get; set; } = "";
        public string FileName { get; set; } = "";
        public double DimensioneMB { get; set; }
        public int FileSaltati { get; set; }
        public DateTime Inizio { get; set; } = DateTime.Now;
        public DateTime? Fine { get; set; }

        // Diario dell'operazione, mostrato a video riga per riga: quando qualcosa si
        // pianta si vede subito a che punto è arrivato.
        private readonly List<string> _righe = new();
        private const int MassimoRighe = 2000;

        public IReadOnlyList<string> Righe
        {
            get { lock (_righe) { return _righe.ToArray(); } }
        }

        public void Scrivi(string testo)
        {
            lock (_righe)
            {
                _righe.Add($"{DateTime.Now:HH:mm:ss}  {testo}");
                if (_righe.Count > MassimoRighe) _righe.RemoveRange(0, _righe.Count - MassimoRighe);
            }
        }
    }

    public BackupJob? GetJob(string id) => _jobs.TryGetValue(id, out BackupJob? j) ? j : null;

    public BackupJob? GetJobAttivo() =>
        _jobs.Values.Where(j => j.Stato == "in_corso").OrderByDescending(j => j.Inizio).FirstOrDefault();

    // ══════════════════════════════════════════════════════════════
    // PERCORSI
    // ══════════════════════════════════════════════════════════════

    /// <summary>Cartella dei pacchetti completi. Può puntare a un NAS: in quel caso la
    /// copia nasce già fuori dal server, che è lo scopo del backup.</summary>
    public string GetPackageDir()
    {
        string dir = _config["Backup:PackagePath"]
            ?? Path.Combine(_config["Backup:Path"] ?? @"C:\ATEC_Backups", "Pacchetti");
        // Destinazione UNC (NAS): il servizio gira come account LOCALE e una share in
        // workgroup risponde «accesso negato» senza sessione autenticata — stessa storia
        // della cartella immagini Danea. Credenziali dedicate Backup:ShareUser/SharePassword
        // se un giorno il NAS sarà un'altra macchina; oggi si ripiega su quelle della share
        // Danea (stesso file server). Se la sessione non si apre si RIFIUTA con l'errore
        // parlante del connettore: proseguire scriverebbe il pacchetto chissà dove o
        // fallirebbe a metà con un «accesso negato» che non spiega niente.
        if (NetworkShareConnector.ShareRoot(dir) != null)
        {
            string? utente = _config["Backup:ShareUser"];
            string? password = _config["Backup:SharePassword"];
            if (string.IsNullOrWhiteSpace(utente))
            {
                utente = _config["DaneaSync:SmbUser"];
                password = _config["DaneaSync:SmbPassword"];
            }
            string? errore = _share.Connect(dir, utente, password);
            if (errore != null)
                throw new InvalidOperationException(errore);
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Le cartelle di file incluse nel pacchetto, con il nome che avranno dentro lo zip.</summary>
    private List<(string Nome, string Percorso)> GetCartelle()
    {
        var lista = new List<(string, string)>();

        string documenti = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        if (!string.IsNullOrWhiteSpace(documenti)) lista.Add(("documenti", documenti));

        string uploads = UploadPaths.Cms(_config);
        if (!string.IsNullOrWhiteSpace(uploads)) lista.Add(("allegati", uploads));

        // Screenshot delle segnalazioni: stanno fuori da /uploads/cms (non sono pubblici),
        // quindi vanno aggiunti a parte o il ripristino li perderebbe.
        string bugs = UploadPaths.Bugs(_config);
        if (!string.IsNullOrWhiteSpace(bugs)) lista.Add(("segnalazioni", bugs));

        return lista;
    }

    /// <summary>Quanto peserebbe un pacchetto completo, senza crearlo: serve a mostrare
    /// la dimensione prima di partire e a verificare che ci sia spazio.</summary>
    public object StimaDimensione()
    {
        var dettaglio = new List<object>();
        long totale = 0;

        foreach ((string nome, string percorso) in GetCartelle())
        {
            long byteCartella = 0;
            int file = 0;
            if (Directory.Exists(percorso))
            {
                foreach (string f in EnumeraFile(percorso))
                {
                    try { byteCartella += new FileInfo(f).Length; file++; } catch { }
                }
            }
            totale += byteCartella;
            dettaglio.Add(new
            {
                Nome = nome,
                Percorso = percorso,
                Esiste = Directory.Exists(percorso),
                File = file,
                DimensioneMB = Math.Round(byteCartella / 1024.0 / 1024.0, 1)
            });
        }

        long liberi = SpazioLibero(GetPackageDir());
        return new
        {
            Cartelle = dettaglio,
            TotaleFileMB = Math.Round(totale / 1024.0 / 1024.0, 1),
            SpazioLiberoDestinazioneGB = Math.Round(liberi / 1024.0 / 1024.0 / 1024.0, 1),
            Destinazione = GetPackageDir()
        };
    }

    public List<object> ListaPacchetti()
    {
        string dir = GetPackageDir();
        if (!Directory.Exists(dir)) return new List<object>();

        return Directory.GetFiles(dir, "atec_pm_completo_*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => (object)new
            {
                FileName = f.Name,
                SizeMB = Math.Round(f.Length / 1024.0 / 1024.0, 1),
                Date = f.CreationTime.ToString("dd/MM/yyyy HH:mm:ss"),
                Contenuto = LeggiManifest(f.FullName)
            })
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // BACKUP COMPLETO
    // ══════════════════════════════════════════════════════════════

    public BackupJob AvviaBackup()
    {
        BackupJob? attivo = GetJobAttivo();
        if (attivo != null) return attivo;   // una operazione per volta

        var job = new BackupJob { Tipo = "backup", Passo = "preparazione" };
        _jobs[job.Id] = job;

        _ = Task.Run(() =>
        {
            try { EseguiBackup(job); }
            catch (Exception ex)
            {
                job.Stato = "errore";
                job.Messaggio = ex.Message;
                job.Fine = DateTime.Now;
                job.Scrivi($"ERRORE: {ex.Message}");
                _log.LogError(ex, "[BackupCompleto] Errore");
            }
        });

        return job;
    }

    private void EseguiBackup(BackupJob job)
    {
        string dir = GetPackageDir();
        string nomeFile = $"atec_pm_completo_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        string percorso = Path.Combine(dir, nomeFile);
        string temporaneo = percorso + ".parziale";

        var cartelle = GetCartelle();
        job.Scrivi($"Backup completo — destinazione {dir}");

        // Elenco dei file PRIMA di iniziare: serve per la percentuale e per accorgersi
        // subito se lo spazio non basta (uno zip a metà è peggio di nessuno zip).
        job.Passo = "conteggio file";
        var daCopiare = new List<(string Nome, string Radice, string File, long Dimensione)>();
        foreach ((string nome, string radice) in cartelle)
        {
            if (!Directory.Exists(radice))
            {
                job.Scrivi($"Cartella '{nome}' non trovata ({radice}): salto");
                continue;
            }
            job.Scrivi($"Leggo la cartella '{nome}' ({radice})...");
            int prima = daCopiare.Count;
            foreach (string f in EnumeraFile(radice))
            {
                try { daCopiare.Add((nome, radice, f, new FileInfo(f).Length)); } catch { }
            }
            job.Scrivi($"  '{nome}': {daCopiare.Count - prima} file");
        }

        long totaleByte = daCopiare.Sum(x => x.Dimensione);
        long liberi = SpazioLibero(dir);
        job.Scrivi($"Totale da archiviare: {daCopiare.Count} file, {totaleByte / 1024 / 1024} MB " +
                   $"(spazio libero: {(liberi > 0 ? $"{liberi / 1024 / 1024 / 1024} GB" : "non rilevabile")})");
        if (liberi > 0 && totaleByte + 200L * 1024 * 1024 > liberi)
        {
            throw new InvalidOperationException(
                $"Spazio insufficiente in {dir}: servono almeno {totaleByte / 1024 / 1024} MB " +
                $"ma ce ne sono {liberi / 1024 / 1024}. Libera spazio o imposta una destinazione diversa (Backup:PackagePath).");
        }

        var manifest = new Dictionary<string, object>
        {
            ["creato"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["macchina"] = Environment.MachineName,
            ["cartelle"] = cartelle.Select(c => new { nome = c.Nome, percorso = c.Percorso }).ToList()
        };

        try
        {
            using (var zip = new ZipArchive(System.IO.File.Create(temporaneo), ZipArchiveMode.Create))
            {
                // 1) Database
                job.Passo = "database";
                job.Percentuale = 1;
                job.Scrivi("Salvataggio del database...");
                ZipArchiveEntry voceDb = zip.CreateEntry("database.sql", CompressionLevel.Optimal);
                using (Stream s = voceDb.Open())
                using (var sw = new StreamWriter(s, new UTF8Encoding(false)))
                {
                    (int tabelle, int righe) = ScriviDumpDatabase(sw, "completo", job.Scrivi);
                    manifest["database"] = new { tabelle, righe, schema = LeggiVersioneSchema() };
                    job.Scrivi($"Database salvato: {tabelle} tabelle, {righe} righe");
                }

                // 2) File
                long fatti = 0;
                int saltati = 0;
                int indice = 0;
                foreach ((string nome, string radice, string file, long dimensione) in daCopiare)
                {
                    indice++;
                    string relativo = Path.GetRelativePath(radice, file).Replace('\\', '/');
                    string voce = $"{nome}/{relativo}";

                    try
                    {
                        CompressionLevel livello = GiaCompressi.Contains(Path.GetExtension(file))
                            ? CompressionLevel.NoCompression
                            : CompressionLevel.Fastest;

                        ZipArchiveEntry e = zip.CreateEntry(voce, livello);
                        e.LastWriteTime = System.IO.File.GetLastWriteTime(file);
                        using Stream dest = e.Open();
                        // FileShare.ReadWrite: un documento aperto da un collega non deve
                        // far fallire tutto il backup.
                        using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        src.CopyTo(dest);
                    }
                    catch (Exception ex)
                    {
                        saltati++;
                        job.Scrivi($"  SALTATO {file}: {ex.Message}");
                        _log.LogWarning("[BackupCompleto] File saltato {File}: {Errore}", file, ex.Message);
                    }

                    fatti += dimensione;
                    if (indice % 20 == 0 || indice == daCopiare.Count)
                    {
                        job.Passo = $"file ({indice}/{daCopiare.Count})";
                        job.Percentuale = totaleByte > 0 ? (int)Math.Min(99, 2 + fatti * 97 / totaleByte) : 99;
                        job.Scrivi($"Archiviati {indice}/{daCopiare.Count} file ({fatti / 1024 / 1024} MB)");
                    }
                }

                job.FileSaltati = saltati;
                manifest["file"] = new { totali = daCopiare.Count, saltati };

                // 3) Manifest
                ZipArchiveEntry voceManifest = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using Stream ms = voceManifest.Open();
                using var msw = new StreamWriter(ms, new UTF8Encoding(false));
                msw.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }

            System.IO.File.Move(temporaneo, percorso, overwrite: true);
        }
        catch
        {
            // Niente pacchetti a metà in giro: chi li vede penserebbe di avere un backup.
            try { if (System.IO.File.Exists(temporaneo)) System.IO.File.Delete(temporaneo); } catch { }
            throw;
        }

        var info = new FileInfo(percorso);
        job.FileName = nomeFile;
        job.DimensioneMB = Math.Round(info.Length / 1024.0 / 1024.0, 1);
        job.Percentuale = 100;
        job.Stato = "completato";
        job.Passo = "fatto";
        job.Messaggio = $"Pacchetto creato: {nomeFile} ({job.DimensioneMB} MB)"
            + (job.FileSaltati > 0 ? $", {job.FileSaltati} file non leggibili saltati" : "");
        job.Fine = DateTime.Now;
        job.Scrivi($"FATTO in {(job.Fine.Value - job.Inizio).TotalSeconds:F0}s — {job.Messaggio}");

        PulisciVecchi();
        _log.LogInformation("[BackupCompleto] {Messaggio}", job.Messaggio);
    }

    // ══════════════════════════════════════════════════════════════
    // RIPRISTINO COMPLETO
    // ══════════════════════════════════════════════════════════════

    /// <param name="ripristinaDatabase">se false tocca solo le cartelle</param>
    /// <param name="ripristinaFile">se false tocca solo il database</param>
    public BackupJob AvviaRipristino(string fileName, bool ripristinaDatabase, bool ripristinaFile,
        string? backupPreventivo = null)
    {
        BackupJob? attivo = GetJobAttivo();
        if (attivo != null) return attivo;

        var job = new BackupJob { Tipo = "ripristino", FileName = fileName, Passo = "preparazione" };
        _jobs[job.Id] = job;
        if (!string.IsNullOrEmpty(backupPreventivo))
            job.Scrivi($"Copia di sicurezza del database attuale: {backupPreventivo}");

        _ = Task.Run(() =>
        {
            try { EseguiRipristino(job, fileName, ripristinaDatabase, ripristinaFile); }
            catch (Exception ex)
            {
                job.Stato = "errore";
                job.Messaggio = ex.Message;
                job.Fine = DateTime.Now;
                job.Scrivi($"ERRORE: {ex.Message}");
                _log.LogError(ex, "[RipristinoCompleto] Errore");
            }
        });

        return job;
    }

    private void EseguiRipristino(BackupJob job, string fileName, bool ripristinaDatabase, bool ripristinaFile)
    {
        string percorso = Path.Combine(GetPackageDir(), fileName);
        if (!System.IO.File.Exists(percorso)) throw new FileNotFoundException($"Pacchetto non trovato: {fileName}");

        job.Scrivi($"Ripristino da {fileName} " +
                   $"({(ripristinaDatabase && ripristinaFile ? "database e cartelle" : ripristinaDatabase ? "solo database" : "solo cartelle")})");

        using var zip = ZipFile.OpenRead(percorso);
        var risultati = new List<string>();

        // 1) Database
        if (ripristinaDatabase)
        {
            job.Passo = "database";
            job.Percentuale = 2;

            ZipArchiveEntry? voceDb = zip.GetEntry("database.sql")
                ?? throw new InvalidOperationException("Il pacchetto non contiene il database.");

            // Quante righe aspettarsi: sta nel manifest, e serve per far avanzare la barra
            // invece di lasciarla ferma per tutta la durata del ripristino.
            int righeAttese = LeggiRigheDalManifest(zip);

            using (Stream s = voceDb.Open())
            using (var sr = new StreamReader(s, Encoding.UTF8))
            {
                (int importati, int errori) = RipristinaDatabase(sr, righeAttese, (fatte, totale) =>
                {
                    job.Passo = totale > 0
                        ? $"database ({fatte}/{totale} record)"
                        : $"database ({fatte} record)";
                    job.Percentuale = totale > 0 ? 2 + (int)Math.Min(22, (long)fatte * 22 / totale) : 2;
                    job.Scrivi($"  importati {fatte}{(totale > 0 ? $"/{totale}" : "")} record");
                }, job.Scrivi);
                risultati.Add($"database: {importati} record importati" + (errori > 0 ? $" ({errori} errori ignorati)" : ""));
                job.Scrivi($"Database ripristinato: {importati} record" + (errori > 0 ? $", {errori} righe scartate" : ""));
            }
            job.Percentuale = 25;
        }

        // 2) Cartelle
        if (ripristinaFile)
        {
            string marcatore = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            foreach ((string nome, string destinazione) in GetCartelle())
            {
                var voci = zip.Entries.Where(e => e.FullName.StartsWith(nome + "/") && e.Length >= 0
                                                  && !e.FullName.EndsWith("/")).ToList();
                if (voci.Count == 0) continue;

                job.Passo = $"cartella {nome}";

                // La cartella attuale non si cancella: si mette da parte. Se il ripristino
                // fosse sbagliato, i file di prima sono ancora lì accanto.
                job.Scrivi($"Cartella '{nome}' ({destinazione}): {voci.Count} file da rimettere");
                if (Directory.Exists(destinazione) && EnumeraFile(destinazione).Any())
                {
                    string daParte = $"{destinazione.TrimEnd('\\')}.prima-ripristino-{marcatore}";
                    Directory.Move(destinazione, daParte);
                    risultati.Add($"{nome}: cartella precedente spostata in {Path.GetFileName(daParte)}");
                    job.Scrivi($"  la cartella di prima è stata messa da parte in {daParte}");
                }
                Directory.CreateDirectory(destinazione);

                int i = 0;
                foreach (ZipArchiveEntry e in voci)
                {
                    string relativo = e.FullName.Substring(nome.Length + 1);
                    string dest = Path.GetFullPath(Path.Combine(destinazione, relativo.Replace('/', '\\')));

                    // Difesa contro percorsi manomessi dentro lo zip (zip slip).
                    if (!dest.StartsWith(Path.GetFullPath(destinazione), StringComparison.OrdinalIgnoreCase))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, overwrite: true);

                    i++;
                    if (i % 20 == 0 || i == voci.Count)
                    {
                        job.Passo = $"cartella {nome} ({i}/{voci.Count})";
                        job.Percentuale = Math.Min(99, 25 + i * 74 / Math.Max(1, voci.Count));
                        job.Scrivi($"  rimessi {i}/{voci.Count} file");
                    }
                }
                risultati.Add($"{nome}: {i} file ripristinati");
            }
        }

        job.Percentuale = 100;
        job.Stato = "completato";
        job.Passo = "fatto";
        job.Messaggio = "Ripristino completato — " + string.Join("; ", risultati);
        job.Fine = DateTime.Now;
        job.Scrivi($"FATTO in {(job.Fine.Value - job.Inizio).TotalSeconds:F0}s — {job.Messaggio}");
        _log.LogInformation("[RipristinoCompleto] {Messaggio}", job.Messaggio);
    }

    // ══════════════════════════════════════════════════════════════
    // DATABASE: dump e ripristino (stesso formato del backup .sql classico)
    // ══════════════════════════════════════════════════════════════

    public (int Tabelle, int Righe) ScriviDumpDatabase(
        TextWriter sw, string etichetta = "completo", Action<string>? diario = null)
    {
        using var c = _db.Open();
        var tabelle = c.Query<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'"
        ).ToList();

        sw.WriteLine($"-- ATEC PM Backup ({etichetta}) {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sw.WriteLine($"-- Tabelle: {tabelle.Count}");
        sw.WriteLine();
        sw.WriteLine("SET FOREIGN_KEY_CHECKS=0;");
        sw.WriteLine();

        int totale = 0;
        int fatte = 0;
        foreach (string t in tabelle)
        {
            var righe = c.Query($"SELECT * FROM `{t}`").ToList();
            fatte++;
            if (righe.Count == 0) continue;

            if (diario != null && (righe.Count >= 1000 || fatte % 20 == 0))
                diario($"  tabella {t}: {righe.Count} righe ({fatte}/{tabelle.Count})");

            sw.WriteLine($"-- ── {t}: {righe.Count} righe ──");
            foreach (IDictionary<string, object> riga in righe)
            {
                string cols = string.Join(",", riga.Keys.Select(k => $"`{k}`"));
                string vals = string.Join(",", riga.Values.Select(FormattaValore));
                sw.WriteLine($"INSERT INTO `{t}` ({cols}) VALUES ({vals});");
            }
            sw.WriteLine();
            totale += righe.Count;
        }

        sw.WriteLine("SET FOREIGN_KEY_CHECKS=1;");
        sw.WriteLine($"-- Totale: {totale} righe");
        return (tabelle.Count, totale);
    }

    /// <param name="righeAttese">totale righe dal manifest, per calcolare l'avanzamento (0 = sconosciuto)</param>
    /// <param name="avanzamento">richiamata ogni blocco con il numero di record già importati</param>
    public (int Importati, int Errori) RipristinaDatabase(
        TextReader sr, int righeAttese = 0, Action<int, int>? avanzamento = null, Action<string>? diario = null)
    {
        const int RighePerBlocco = 500;

        using var c = _db.Open();

        // Lo stesso lock delle migrazioni (blocco A2): il ripristino è l'ALTRO processo che
        // riscrive lo schema da cima a fondo — svuota tutte le tabelle, schema_migrations
        // compresa — e gira a server acceso, quindi può capitare mentre una seconda istanza sta
        // migrando. I due lavori insieme lasciano un database che non è né quello del pacchetto
        // né quello di prima. Chi arriva secondo si ferma e lo dice.
        string nomeLock = DbService.NomeLockMigrazioni(new MySqlConnector.MySqlConnectionStringBuilder(c.ConnectionString).Database);
        int attesa = Math.Max(1, _config.GetValue("Migrations:LockTimeoutSeconds", 30));

        // L'acquisizione sta DENTRO il try, come in DbService.InitDatabase: se solleva a metà, il
        // rilascio deve girare lo stesso. Rilasciare un lock che non si ha non fa niente.
        try
        {
            // commandTimeout più lungo dell'attesa, o sarebbe il driver a interrompere la query
            // prima che GET_LOCK abbia finito di aspettare.
            int? lockPreso = c.ExecuteScalar<int?>("SELECT GET_LOCK(@N, @S)",
                new { N = nomeLock, S = attesa }, commandTimeout: attesa + 15);

            if (lockPreso != 1)
                throw new InvalidOperationException(
                    "Ripristino non avviato: un altro processo sta aggiornando lo schema del database " +
                    $"(lock '{nomeLock}' occupato). Riprovare fra qualche minuto; se il server è appena " +
                    "stato aggiornato, aspettare che abbia finito di partire.");

            return RipristinaDatabaseSottoLock(c, sr, righeAttese, avanzamento, diario, RighePerBlocco);
        }
        finally
        {
            try { c.ExecuteScalar<int?>("SELECT RELEASE_LOCK(@N)", new { N = nomeLock }); }
            catch (Exception ex) { _log.LogWarning("[Ripristino] Rilascio del lock non riuscito: {Errore}", ex.Message); }
        }
    }

    private (int Importati, int Errori) RipristinaDatabaseSottoLock(
        MySqlConnector.MySqlConnection c, TextReader sr, int righeAttese,
        Action<int, int>? avanzamento, Action<string>? diario, int righePerBlocco)
    {
        // Da qui in poi il database non è più quello che l'applicazione ha in memoria, e lo
        // resta anche se il ripristino si INTERROMPE a metà: il try/finally serve a quello.
        // Senza, un import fallito (commit andato storto, zip corrotto, disco pieno) lasciava in
        // memoria le aggregazioni, la matrice degli stati e le REGOLE DEI PERMESSI del database
        // di prima, senza scadenza e senza che niente lo dicesse — fino al riavvio del servizio.
        try
        {
            return SvuotaEImporta(c, sr, righeAttese, avanzamento, diario, righePerBlocco);
        }
        finally
        {
            DimenticaTuttoQuelloCheEraInMemoria();
        }
    }

    /// <summary>
    /// Butta via <b>tutto</b> ciò che il processo tiene in memoria sul conto del database.
    /// <para>Non basta la cache delle anagrafiche: il ripristino svuota e riscrive anche
    /// <c>auth_levels</c>, <c>auth_features</c>, <c>auth_role_features</c> e <c>app_config</c>,
    /// che <c>FeatureAccessService</c> tiene <b>senza scadenza</b>. Chi ripristina un pacchetto
    /// di ieri per annullare un cambio di permessi sbagliato si ritroverebbe il database giusto e
    /// i permessi sbagliati ancora in vigore.</para>
    /// </summary>
    private void DimenticaTuttoQuelloCheEraInMemoria()
    {
        try { _cache.InvalidaTutto(); }
        catch (Exception ex) { _log.LogWarning("[Ripristino] Cache anagrafiche non svuotata: {Errore}", ex.Message); }

        try { _access.Reload(); }
        catch (Exception ex) { _log.LogWarning("[Ripristino] Permessi non ricaricati: {Errore}", ex.Message); }
    }

    private (int Importati, int Errori) SvuotaEImporta(
        MySqlConnector.MySqlConnection c, TextReader sr, int righeAttese,
        Action<int, int>? avanzamento, Action<string>? diario, int righePerBlocco)
    {
        c.Execute("SET FOREIGN_KEY_CHECKS=0");

        var tabelle = c.Query<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'"
        ).ToList();
        diario?.Invoke($"Svuoto {tabelle.Count} tabelle...");
        foreach (string t in tabelle)
        {
            // TRUNCATE è molto più veloce di DELETE e azzera da solo l'AUTO_INCREMENT
            // (qui è consentito perché i controlli sulle chiavi esterne sono disattivati).
            try { c.Execute($"TRUNCATE TABLE `{t}`"); }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "[Ripristino] TRUNCATE non riuscito su {Tabella}: uso DELETE", t);
                c.Execute($"DELETE FROM `{t}`");
                try { c.Execute($"ALTER TABLE `{t}` AUTO_INCREMENT = 1"); }
                catch (Exception ex2) { _log.LogDebug(ex2, "[Ripristino] AUTO_INCREMENT non azzerato per {Tabella}", t); }
            }
        }

        diario?.Invoke("Tabelle svuotate, importo i record...");

        int importati = 0, errori = 0, nelBlocco = 0;
        string? riga;

        // Gli INSERT vanno in transazioni da 500 righe: uno per uno significherebbe una
        // scrittura su disco per record (decine di minuti su un archivio vero).
        MySqlConnector.MySqlTransaction tx = c.BeginTransaction();
        try
        {
            while ((riga = sr.ReadLine()) != null)
            {
                string s = riga.TrimStart();
                if (s.Length == 0 || s.StartsWith("--") || s.StartsWith("SET FOREIGN_KEY_CHECKS")) continue;
                if (!riga.TrimEnd().EndsWith(";")) continue;

                try { c.Execute(riga, transaction: tx); importati++; }
                catch (Exception ex)
                {
                    errori++;
                    if (errori <= 20)
                    {
                        diario?.Invoke($"  SCARTATA: {ex.Message}");
                        _log.LogWarning("[Ripristino] Errore su riga: {Errore}", ex.Message);
                    }
                    else if (errori == 21) diario?.Invoke("  (altre righe scartate: non le elenco tutte)");
                }

                if (++nelBlocco >= righePerBlocco)
                {
                    tx.Commit();
                    tx.Dispose();
                    tx = c.BeginTransaction();
                    nelBlocco = 0;
                    avanzamento?.Invoke(importati, righeAttese);
                }
            }
            tx.Commit();
        }
        finally { tx.Dispose(); }

        c.Execute("SET FOREIGN_KEY_CHECKS=1");

        avanzamento?.Invoke(importati, righeAttese);
        return (importati, errori);
    }

    // ══════════════════════════════════════════════════════════════
    // UTILITÀ
    // ══════════════════════════════════════════════════════════════

    private static IEnumerable<string> EnumeraFile(string radice)
    {
        // EnumerateFiles ricorsivo si ferma alla prima cartella non accessibile: qui si
        // scende a mano per poter saltare le singole cartelle problematiche.
        var code = new Queue<string>();
        code.Enqueue(radice);
        while (code.Count > 0)
        {
            string cartella = code.Dequeue();
            string[] file;
            try { file = Directory.GetFiles(cartella); }
            catch { continue; }
            foreach (string f in file) yield return f;

            try { foreach (string d in Directory.GetDirectories(cartella)) code.Enqueue(d); }
            catch { }
        }
    }

    private static long SpazioLibero(string percorso)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(percorso))!).AvailableFreeSpace; }
        catch { return 0; }   // percorso di rete: non si sa, si prova comunque
    }

    private int LeggiVersioneSchema()
    {
        try
        {
            using var c = _db.Open();
            // success = 1: una migrazione fallita lascia la sua riga (con l'errore dentro) ma non
            // è applicata. Senza il filtro il manifest del pacchetto dichiarerebbe uno schema più
            // avanti di quello che il pacchetto contiene davvero.
            return c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_migrations WHERE success = 1") ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>Righe di database dichiarate nel manifest del pacchetto (0 se non c'è).</summary>
    private static int LeggiRigheDalManifest(ZipArchive zip)
    {
        try
        {
            ZipArchiveEntry? e = zip.GetEntry("manifest.json");
            if (e == null) return 0;
            using Stream s = e.Open();
            using var sr = new StreamReader(s);
            JsonElement root = JsonSerializer.Deserialize<JsonElement>(sr.ReadToEnd());
            if (root.TryGetProperty("database", out JsonElement db) &&
                db.TryGetProperty("righe", out JsonElement righe))
            {
                return righe.GetInt32();
            }
        }
        catch { }
        return 0;
    }

    private static object? LeggiManifest(string zipPath)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? e = zip.GetEntry("manifest.json");
            if (e == null) return null;
            using Stream s = e.Open();
            using var sr = new StreamReader(s);
            return JsonSerializer.Deserialize<JsonElement>(sr.ReadToEnd());
        }
        catch { return null; }
    }

    private void PulisciVecchi()
    {
        int daTenere = _config.GetValue("Backup:PackageKeep", 3);
        try
        {
            foreach (FileInfo f in Directory.GetFiles(GetPackageDir(), "atec_pm_completo_*.zip")
                         .Select(x => new FileInfo(x))
                         .OrderByDescending(x => x.CreationTime)
                         .Skip(daTenere))
            {
                f.Delete();
                _log.LogInformation("[BackupCompleto] Rimosso pacchetto vecchio {File}", f.Name);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "[BackupCompleto] Pulizia pacchetti non riuscita"); }
    }

    /// <summary>
    /// Formattazione dei valori per gli INSERT. È l'UNICA implementazione: la usa anche il
    /// backup .sql classico (BackupController), così i due formati non possono divergere.
    /// </summary>
    public static string FormattaValore(object? v)
    {
        if (v is null or DBNull) return "NULL";
        if (v is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
        if (v is DateOnly d) return $"'{d:yyyy-MM-dd}'";
        if (v is TimeSpan t) return $"'{t:hh\\:mm\\:ss}'";
        if (v is bool b) return b ? "1" : "0";
        if (v is byte[] bytes) return $"X'{Convert.ToHexString(bytes)}'";
        if (v is decimal or int or long or double or float or short or byte)
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;

        string s = v.ToString()!
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n")
            .Replace("\t", "\\t");
        return $"'{s}'";
    }
}
