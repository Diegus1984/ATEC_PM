# Analisi del progetto ATEC PM

> Analisi statica del codice (client + server + shared) eseguita il **20/05/2026**.
> Aggiornamento al **22/05/2026**: vedi sezione 0 per il delta WIP.
> Non include analisi a runtime né accesso al database, quindi lo stato della roadmap (che vive nella tabella `roadmap_items`) non è coperto in dettaglio.

---

## 0. Aggiornamento 22/05/2026 — Delta rispetto al 20/05

Ultimo commit su `master`: `037aa4d` del **03/04/2026** (working tree allineato con `origin/master`). Tutto ciò che segue è **modificato ma non committato** (`git status` mostra 38 file `M` e un `DbService.cs.bak` untracked). Il delta complessivo è `+562 / −381` righe su 38 file.

### 0.1 Cosa è stato affrontato dal precedente report

| # | Raccomandazione 20/05 | Stato al 22/05 |
|---|---|---|
| 3 | Proteggere `ImportController` | ✅ **Fatto.** Ora `[Authorize(Roles = "ADMIN,PM")]`, whitelist tabelle (`AllowedEasyfattTables`), validazione estensione file (`.eft`/`.fdb`), `tableName` ripulito e validato prima dell'interpolazione. |
| 7 | Bonificare i `catch` vuoti | ✅ **Fatto in larga parte.** I 89 `catch { }` silenziosi del precedente report sono scesi a ~8 casi residui. Pattern adottato: `catch (Exception ex) { _logger.LogWarning(ex, "...") }` lato server e `Trace.WriteLine(ex.Message)` lato client. `ILogger<T>` iniettato in `DbService`, `ProjectsController`, `PhasesController`, `PreventiviController`, `BackupController` (e altri). |
| — | Robustezza `BackupController` | ✅ Bonus: `SHOW TABLES` sostituito con query a `information_schema.tables` filtrata su `BASE TABLE` (esclude le view, più robusto al backup/restore). |

### 0.2 Cosa NON è ancora stato toccato

| # | Raccomandazione 20/05 | Stato al 22/05 |
|---|---|---|
| 1 | Rimuovere fallback JWT in `Program.cs` | ❌ Riga 61 ancora identica: `var jwtKeyValue = builder.Configuration["Jwt:Key"] ?? "ATEC-PM-SuperSecretKey-ChangeMeInProduction-2026!";`. Da rimuovere e far fallire l'avvio se la chiave manca. |
| 2 | Ripulire i segreti da git (`.claude/`, `ROADMAP.md`, `*.csproj.Backup.tmp`) | ❌ `.gitignore` contiene solo `bin/`, `obj/`, `.vs/`, `*.user`, `appsettings.json`. Risultano ancora tracciati: `.claude/mcp.json`, `.claude/settings.local.json`, `ATEC.PM.Client/ROADMAP.md`, `ATEC.PM.Server/ATEC.PM.Server.csproj.Backup.tmp` e `(1).tmp`. La password `Atec2005` è quindi tuttora versionata. |
| 4 | Autorizzazione lato server su endpoint con dati economici | ❌ Nessun nuovo `[Authorize(Roles=...)]` su costing / budget / cash flow / revenue. Le decisioni continuano a dipendere dal `PermissionEngine` lato client. |
| 5 | Estendere cifratura a Codex/Danea | ❌ Nessuna modifica a `EncryptedSettingsBootstrap` o equivalente. |
| 6 | Introdurre test automatici | ❌ Ancora 0 progetti di test. |
| 8 | Restringere CORS e `AllowedHosts` | ❌ Policy `"All"` con `AllowAnyOrigin/Method/Header` ancora in `Program.cs:59`. |
| 9 | Refactoring file > 800 righe | ❌ I file giganti del client (CostingTreeControl 2.135, CostSectionsTreePage 1.815, QuoteCatalogPage 1.172, ProjectsPage 1.137) non sono ancora stati spezzati. Sul server `DbService.cs` è invece **calato da 1.028 → ~740 righe** (vedi 0.3). |

### 0.3 Cambiamenti funzionali significativi nel WIP

- **`DbService.cs` −246 / +44 righe**: rimosso un grosso blocco di codice di migrazione "one-off" (rename colonna `is_default → is_default_project` su `cost_section_templates`, aggiunta `child_catalog_id` su `codex_compositions`, decine di `AddIndexIfMissing` ora replicati direttamente nei `CREATE TABLE` con `INDEX idx_...`). Indici "performance" ora dichiarati inline nelle definizioni di tabella; gli helper `AddUniqueIndexIfMissing`/`AddIndexIfMissing` rimossi. **Nota**: l'eliminazione del codice di migrazione presuppone che tutti i DB esistenti abbiano già ricevuto quelle modifiche — se esistono installazioni non aggiornate, vanno migrate manualmente prima del deploy.
- **CashFlow**: nuova colonna `start_date DATE NULL` su `project_cashflow` (data di inizio cash flow configurabile, prima implicita).
- **Budget vs Actual** (`BudgetVsActualController` + `BvaCostingVM`): se la commessa non ha gruppi di costing (`hasQuoteSections == false`), il budget mostrato fa fallback su `projects.budget_total` invece che sulla somma calcolata dai gruppi. Sblocca BvA per commesse senza preventivo associato.
- **Progetto ↔ Preventivo**: `ProjectListItem` e `ProjectSaveRequest` (Shared) ora espongono `LinkedQuoteId` (query subselect su `quotes WHERE project_id = p.id LIMIT 1`); diverse pagine client (`ProjectDialog`, `ProjectsPage`, `QuoteDetailPage`, `QuotesHomePage`) sono state aggiornate di conseguenza.
- **`LookupEmployees`**: ora accetta filtro opzionale `?role=...` per recuperare solo dipendenti di un certo ruolo (utile per popolare le combo dei PM).
- **`project_cost_sections`**: nuova colonna `linked_source VARCHAR(100) NULL` (origine della sezione, presumibilmente per tracciare l'aggancio a un template o a un capitolo di preventivo).
- **File untracked da bonificare**: `ATEC.PM.Server/Services/DbService.cs.bak` — scoria di un refactoring, da rimuovere o aggiungere a `.gitignore`.

### 0.4 Conseguenza sui contatori del report

- `catch` vuoti: **89 → ~8** (resto è in pattern legittimi tipo `catch (FormatException) { /* default già impostato */ }`).
- Righe di C#: variazione netta marginale (~−180 righe nette, concentrate su `DbService`).
- Endpoint: invariato (nessun controller nuovo, nessun endpoint rimosso).
- Test automatici: **ancora 0**.

---

## 1. Sintesi esecutiva

ATEC PM è un gestionale di project management desktop sviluppato internamente da ATEC — Automation Technology S.r.l. (Torino). È un'applicazione **a tre livelli** con client WPF (.NET 8), backend ASP.NET Core Web API (.NET 8) e una libreria di DTO condivisi, con database MySQL via Dapper. Copre preventivazione/CMS, costing di commessa, budget vs actual, cash flow, timesheet, dashboard, chat di commessa, gestione documentale, anagrafiche e integrazioni con sistemi esterni (Codex, Danea/Easyfatt via Firebird).

Il codice è **funzionalmente ricco e ben organizzato per moduli**, con alcune buone scelte di sicurezza (bcrypt con migrazione da SHA2, rate limiting sul login, query parametrizzate con whitelist, cifratura DPAPI dei segreti a runtime sul server). Allo stesso tempo emergono alcuni **problemi di sicurezza importanti** (segreti in chiaro versionati in git, chiave JWT di fallback hardcoded, un controller di import esposto senza autenticazione, autorizzazione applicata prevalentemente lato client) e un **debito tecnico** tipico di un progetto cresciuto in fretta (assenza totale di test automatici, file enormi in code-behind, soppressione silenziosa delle eccezioni).

| Dimensione | Valore |
|---|---|
| Righe di C# (esclusi bin/obj/Assets) | ~39.400 (Client 23.460 · Server 13.511 · Shared 2.464) |
| File sorgente | 160 `.cs` + 74 `.xaml` |
| Controller API | 30 |
| Endpoint HTTP | ~282 (93 GET · 81 POST · 50 PUT · 41 DELETE · 17 PATCH) |
| Gruppi di DTO | 27 |
| Background service | 4 (Notifiche, CodexSync, DaneaSync, Backup) |
| Test automatici | **0** |
| Branch git | solo `master` |

---

## 2. Architettura

### 2.1 Struttura della soluzione

La soluzione `ATEC.PM.sln` contiene tre progetti:

- **ATEC.PM.Client** — applicazione desktop WPF (.NET 8). Contiene le viste (`Views/`), i controlli utente, le risorse XAML, i ViewModel di alcuni moduli e i servizi client (`ApiClient`, `UserPreferences`).
- **ATEC.PM.Server** — API ASP.NET Core (.NET 8). Contiene i controller REST, i servizi di dominio (`DbService`, `QuoteDbService`, `QuotePdfService`, `NotificationService`, `CodexGeneratorService`) e i background service.
- **ATEC.PM.Shared** — class library con i DTO condivisi tra client e server, i modelli di dominio e il `PermissionEngine`.

Il flusso dati è lineare: **WPF → HTTP/JSON → API → Dapper → MySQL**. Il client non parla mai direttamente al database; ogni operazione passa dall'API.

### 2.2 Pattern applicativi

- **Client**: code-behind con MVVM parziale. Alcuni moduli (Costing, CashFlow, Budget vs Actual, Utenti) hanno ViewModel dedicati; molte pagine restano gestite interamente nel code-behind del `.xaml.cs`.
- **`ApiClient`** è una classe **statica** che incapsula un unico `HttpClient` statico (scelta corretta per evitare l'esaurimento dei socket) e restituisce stringhe JSON grezze: la deserializzazione avviene nei punti di chiamata. Gestisce centralmente il 401 (sessione scaduta → ritorno al login) e aggiunge il token Bearer da `App.Token`.
- **Server**: controller "spessi" che contengono direttamente la logica e le query Dapper (poco strato di servizio intermedio, tranne per preventivi/quote e PDF). `DbService` espone `Open()` (una connessione MySQL nuova per chiamata, con pooling gestito dal driver) e helper come `UpdateField` con whitelist.
- **`PermissionEngine`** (in Shared) è una classe **statica** che mantiene in campi statici il livello e le feature dell'utente loggato, caricati al login. È un motore a livelli "VisiWin-style" configurabile da DB.

### 2.3 Autenticazione e contratto API

- Autenticazione **JWT Bearer**, token valido 8 ore, claim `NameIdentifier` / `Name` / `Role`. Firma HMAC-SHA256.
- Tutte le risposte usano il wrapper generico `ApiResponse<T>` (`Core_DTOs.cs`) con esito `Ok`/`Fail`.
- JSON configurato con `ReferenceHandler.IgnoreCycles`, omissione dei null e case-insensitive.

### 2.4 Integrazioni e librerie

- **Codex**: secondo database MySQL (`SERVER-CODEX`) sincronizzato da `CodexSyncService` (ogni 6h, disattivabile).
- **Danea / Easyfatt**: lettura da database **Firebird** (`fbclient.dll` incluso nel server) tramite `DaneaSyncService` e `ImportController`.
- **Rich text**: TinyMCE 5 self-hosted dentro `WebView2`.
- **UI client**: ampio uso della suite commerciale **Syncfusion WPF** (griglie, grafici, gantt, scheduler, kanban, gauge, temi multipli) — dipendenza licenziata da tenere d'occhio per i costi e i rinnovi.
- **CAD**: **ACadSharp** alimenta `CadViewerControl` per la visualizzazione di disegni.
- **Grafici**: OxyPlot.Wpf 2.2 · **PDF**: QuestPDF (licenza Community) · **Excel**: ClosedXML (client) + EPPlus (server, licenza non-commercial impostata in `Program.cs`) · **Word→HTML**: Mammoth (server).
- **Logging**: Serilog su console e file giornaliero (`C:\ATEC_PM\Logs`, retention 30 giorni).
- **Background service**: Notifiche, CodexSync, DaneaSync, Backup — tutti attivabili/disattivabili da `appsettings.json` (sezione `Services`).

### 2.5 Moduli funzionali

Preventivi/CMS (catalogo prodotti, varianti, generazione PDF), Costing di commessa, Budget vs Actual, Cash Flow, Codex (composizione/generazione codici), Timesheet (versamento ore), Dashboard, Chat di commessa, Document Manager, Anagrafiche (Clienti, Fornitori, Materiali, Destinazioni DDP), Utenti e livelli di autorizzazione, Backup.

---

## 3. Qualità del codice e debito tecnico

### 3.1 File molto grandi (candidati al refactoring)

I file più estesi concentrano molta logica e sono difficili da mantenere e testare:

| Righe | File |
|---|---|
| 2.135 | `Client/Views/Preventivi/CostingTreeControl.xaml.cs` |
| 1.815 | `Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs` |
| 1.359 | `Server/Controllers/ProjectsController.cs` |
| 1.172 | `Client/Views/Cms/QuoteCatalogPage.xaml.cs` |
| 1.137 | `Client/Views/Commesse/ProjectsPage.xaml.cs` |
| 1.071 | `Server/Controllers/QuotesController.cs` |
| 1.041 | `Server/Services/QuotePdfService.cs` |
| 1.028 | `Server/Services/DbService.cs` |

Sul client il problema è amplificato dall'uso intensivo del code-behind: la logica di business vive dentro i `.xaml.cs` invece che in ViewModel testabili.

### 3.2 Gestione delle eccezioni

Sono presenti **89 blocchi `catch` vuoti** (65 nel client, 24 nel server) che sopprimono silenziosamente gli errori. In un gestionale che muove dati economici questo rende difficile diagnosticare malfunzionamenti e può mascherare bug (es. un salvataggio fallito che non segnala nulla all'utente). Da convertire almeno in log esplicito.

### 3.3 Assenza di test

Non esiste **alcun progetto di test** nella soluzione e nessun file di test. Su ~39.000 righe, con calcoli economici (margini, costi, totali preventivo, budget vs actual), l'assenza di test automatici è il rischio di manutenibilità più grosso: ogni refactoring è una scommessa.

### 3.4 Igiene del repository

- File di backup versionati: `ATEC.PM.Server.csproj.Backup.tmp` e `ATEC.PM.Server.csproj.Backup (1).tmp` sono **tracciati in git** (vanno rimossi e ignorati).
- Cronologia git rumorosa: la maggior parte dei commit ha messaggio `v1`, che rende inutile la storia per capire *cosa* è cambiato e *perché*.
- Nessun TODO/FIXME nel codice: la pianificazione è esternalizzata nel DB (`roadmap_items`), quindi il codice resta pulito da note sparse — aspetto positivo.

---

## 4. Sicurezza

> Nota: il login usa bcrypt con migrazione automatica dal vecchio SHA2, rate limiting con lockout (5 tentativi / 5 minuti) e query parametrizzate. Sono buone basi. I punti sotto sono le aree da correggere, ordinate per gravità.

### 4.1 ALTA — Segreti in chiaro versionati in git

Il file `appsettings.json` è correttamente in `.gitignore` (buona pratica), **ma** la stessa password del database root finisce comunque nel repository attraverso altri file *tracciati*:

- `ATEC.PM.Client/ROADMAP.md` (riga 95): comando con `-pAtec2005` in chiaro.
- `.claude/mcp.json`: `"MYSQL_PASSWORD": "Atec2005"`.
- `.claude/settings.local.json`: decine di comandi bash con `-pAtec2005` hardcoded.

La cartella `.claude/` non è in `.gitignore`, quindi tutti questi file sono committati. Chiunque acceda al repo (`github.com/Diegus1984/ATEC_PM`) ottiene la password di `root@localhost`. **Azione**: ruotare la password, ignorare `.claude/` (o ripulire i comandi), e ripulire la storia git se il repo non è strettamente privato.

### 4.2 ALTA — Chiave JWT di fallback hardcoded

In `Program.cs` (riga 61) e in `appsettings.json` la chiave di firma JWT è una stringa nota e versionata:

```
"ATEC-PM-SuperSecretKey-ChangeMeInProduction-2026!"
```

Se il caricamento dei segreti cifrati fallisce o non avviene, il server firma i token con questa chiave pubblica: chiunque la conosca può **forgiare token validi** per qualsiasi ruolo (incluso ADMIN) e bypassare del tutto l'autenticazione. **Azione**: rimuovere il fallback, far fallire l'avvio se la chiave non è configurata, e generare una chiave casuale per ogni ambiente.

### 4.3 ALTA — `ImportController` esposto senza autenticazione

`ImportController` è marcato `[AllowAnonymous]`. L'endpoint `GET /api/import/easyfatt/preview` accetta da query un `filePath` arbitrario (apre una connessione Firebird a qualsiasi file indicato) e un `tableName` che viene **interpolato direttamente nella query SQL**:

```csharp
new FbCommand($"SELECT FIRST {maxRows} * FROM \"{tableName}\"", conn);
```

Questo è sia un accesso a dati non autenticato sia un vettore di **SQL injection** (il `tableName` non è validato e l'identificatore tra virgolette può essere forzato). **Azione**: richiedere autenticazione/ruolo ADMIN, validare `tableName` contro una whitelist e vincolare `filePath` a una directory consentita.

### 4.4 MEDIA — Autorizzazione applicata soprattutto lato client

Il `PermissionEngine` che decide chi vede costi, margini e budget è **lato client** (classe statica in Shared, usata dal WPF). Lato server l'autorizzazione fine è quasi assente: solo **6 endpoint** usano `[Authorize(Roles = "ADMIN")]` e solo **4 punti** controllano il ruolo via codice; **26 controller** richiedono soltanto l'autenticazione (un qualsiasi utente loggato). 

Conseguenza pratica: la regola "i dati economici sono visibili solo a PM/ADMIN" è solo un filtro dell'interfaccia. Un tecnico autenticato può chiamare direttamente l'API (es. costing, budget vs actual) e leggere costi e margini. **Azione**: replicare i controlli di livello/ruolo sugli endpoint sensibili lato server.

Da notare anche che `PermissionEngine.CanAccess` è **fail-open**: se le feature non sono caricate o non sono registrate, restituisce `true` (accesso consentito). Comodo in sviluppo, rischioso in produzione.

### 4.5 MEDIA — Segreti non coperti dalla cifratura automatica

All'avvio il server cifra (DPAPI) e ripulisce da `appsettings.json` **solo** `ConnectionStrings:Default` e `Jwt:Key`. Restano in chiaro nel file:

- la connection string **Codex** (`ConnectionStrings:Codex`);
- la password **Firebird/Danea** (`DaneaSync:FbPassword = "AUTOnoa15!"`).

Inoltre la cifratura è DPAPI con scope `CurrentUser`: i segreti sono leggibili solo dall'account Windows che li ha generati, il che è sicuro ma fragile (cambio account/servizio → segreti illeggibili). **Azione**: estendere la cifratura anche a Codex e Danea, o spostarsi su un secret store (variabili d'ambiente, DPAPI LocalMachine, o un vault).

### 4.6 MEDIA — CORS completamente aperto

La policy CORS `"All"` consente `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`. Per un'app desktop in LAN l'impatto è limitato, ma è comunque da restringere alle origini effettivamente necessarie. `AllowedHosts` è anch'esso `*`.

### 4.7 BASSA — Costruzione del JSON di login per concatenazione

`ApiClient.PostLogin` costruisce il body così:

```csharp
$"{{\"username\":\"{user}\",\"password\":\"{pass}\"}}";
```

Username o password contenenti `"` o `\` rompono il JSON: oltre alla fragilità funzionale (password legittime con virgolette falliscono il login), è una cattiva pratica. **Azione**: serializzare con `JsonSerializer`/oggetto tipizzato.

### 4.8 Aspetti positivi confermati

- Password con **bcrypt**, con verifica e migrazione trasparente dei vecchi hash SHA2.
- **Rate limiting** sul login con lockout e cleanup periodico in memoria.
- Query Dapper **parametrizzate**; dove serve un nome di colonna dinamico (`QuotesController`, `DbService.UpdateField`) c'è una **whitelist** che impedisce l'injection.
- `appsettings.json` in `.gitignore` e cifratura a riposo dei segreti principali sul server.

---

## 5. Stato e roadmap

- La roadmap operativa **non è più nei file**: è stata migrata nella tabella MySQL `atec_pm.roadmap_items` (categorie FEATURE/BUG/REFACTOR/NOTE, stati TODO/IN_PROGRESS/DONE, priorità 1–5). Per fotografare lo stato reale serve interrogare il DB; con il file non è ricostruibile.
- `roadmap_danea.md` documenta l'integrazione Danea/Easyfatt.
- Stato git: branch unico `master`, working tree pulito, 456 file tracciati, remoto su GitHub.
- I moduli appaiono nella maggior parte completi e cablati end-to-end (controller + viste + DTO presenti per ciascuno). La presenza di `appsettings.Secrets.json` generato a runtime e dei background service suggerisce che l'app è in uso reale, non un prototipo.

Se vuoi, posso connettermi al database (serve accesso) e produrre il riepilogo aggiornato dei `roadmap_items` per stato/modulo/priorità.

---

## 6. Azioni consigliate (in ordine di priorità)

> Aggiornato al 22/05/2026. ✅ = già affrontato nel WIP (vedi sezione 0). ❌ = ancora aperto.

1. ❌ **Ruotare** la password MySQL root e la chiave JWT; rimuovere il fallback JWT da `Program.cs:61`; far fallire l'avvio senza chiave configurata.
2. ❌ **Ripulire i segreti da git**: aggiungere `.claude/` al `.gitignore`, ripulire `ROADMAP.md`, valutare la pulizia della storia git. Rimuovere i `.csproj.Backup.tmp` tracciati e il nuovo `DbService.cs.bak` untracked.
3. ✅ **Proteggere `ImportController`**: autenticazione + ruolo (`ADMIN,PM`), whitelist su `tableName`, validazione estensione `filePath`. *Fatto nel WIP.*
4. ❌ **Portare l'autorizzazione lato server** sugli endpoint con dati economici (costing, budget, cash flow, revenue), non solo nell'interfaccia.
5. ❌ **Estendere la cifratura** dei segreti a Codex e Danea/Firebird.
6. ❌ **Introdurre i test**: partire da un progetto di unit test sui calcoli economici (totali preventivo, margini, budget vs actual) e sui controller critici. Particolarmente urgente ora che `DbService.cs` ha perso il codice di migrazione idempotente: una regressione non sarebbe più auto-correttiva.
7. ✅ **Bonificare i `catch` vuoti**: convertiti ~80 blocchi a `_logger.LogWarning(...)` / `Trace.WriteLine(...)`; restano ~8 casi residui da rivedere. *Fatto in larga parte nel WIP.*
8. ❌ **Restringere CORS** e `AllowedHosts`.
9. 🟡 **Refactoring incrementale** dei file oltre ~800 righe. `DbService.cs` è sceso a ~740 righe; i giganti del client (CostingTreeControl, CostSectionsTreePage, QuoteCatalogPage, ProjectsPage) restano invariati.

### Azioni nuove emerse dal WIP

10. ❌ **Verificare le migrazioni DB applicate** prima di committare il refactoring di `DbService.cs`: la rimozione del codice di migrazione idempotente significa che un DB non aggiornato non si auto-correggerà più all'avvio. Documentare le modifiche schema richieste e/o tenere uno script `migrate_one_shot.sql`.
11. ❌ **Committare il WIP a piccoli passi** con messaggi descrittivi (non `v1`): le 38 modifiche correnti coprono almeno 4 temi distinti (sicurezza ImportController, logging dei catch, refactor DbService, feature LinkedQuote+CashFlow start_date). Separarli in commit dedicati semplifica revisione e rollback.
