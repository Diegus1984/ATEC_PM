# ATEC PM — TODO

Lista di lavoro post-migrazione Shadcn UI. Per i bug riproducibili usare `BUGS.md` (formato dedicato), qui sta tutto il resto: hardening, componenti UI, debito tecnico.

Legenda: `[ ]` aperto · `[~]` in corso · `[x]` fatto · `[-]` scartato

---

## 0 · PROMEMORIA CON SCADENZA (guardare qui per primo)

### 🔴 E1 — la misura delle prestazioni è pronta ma NON è ancora partita

Gli strumenti sono in produzione dal **15/08/2026** (deploy di oggi). Quello che manca è la
parte che non si può programmare: **lasciarla accesa e tornare a leggerla**. Finché non ci sono
quei dati, E2 (indici), E3 (N+1) ed E5 (async) si farebbero a naso — che è esattamente ciò che
il piano vieta. Dettagli in [PIANO-MIGLIORIE-TECNICHE.md](PIANO-MIGLIORIE-TECNICHE.md) §E1.

- [x] **① Accendere lo slow query log sul server** — **PROGRAMMATO: lunedì 24/08/2026 alle 08:00**
      (spostato dal 17: era in piena settimana di Ferragosto e si sarebbe misurato il silenzio).
      Attività pianificata `AtecPm-SlowQueryLogOn` sul server, gira come SYSTEM anche a PC spento e
      senza nessuno collegato. Lo script
      [accendi-slow-log.ps1](../deploy/accendi-slow-log.ps1) (copia sul server in
      `C:\ATEC_PM\Updates\`) si legge da solo la password di **root** da
      `C:\ATEC_PM\Config\credenziali.txt` — non c'è nessuna password dentro l'attività pianificata,
      né in uno script, né in una cronologia di terminale. Provato a vuoto il 15/08 (`-SoloProva`):
      password letta, root connesso, niente modificato. A lavoro fatto l'attività **si cancella da
      sola**.

      **Martedì mattina, controllare che sia partito** (una riga):
      ```
      ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150 "type C:\ATEC_PM\Logs\slow-log-accensione.txt"
      ```
      Deve dire «Slow query log ACCESO». Se dice FALLITO, il motivo è sulla stessa riga.

      **Per rimandare ancora o annullare**:
      ```
      ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150 "schtasks /change /tn AtecPm-SlowQueryLogOn /sd 31/08/2026 /st 08:00"
      ```
      (con `/delete /f` al posto di `/change ... /sd ...` per toglierlo del tutto).

      **Poi la settimana di attesa**: leggere le tre liste da lunedì **31/08** in avanti.

- [ ] **② Dopo una settimana di lavoro vero, leggere le tre liste** (sempre sul server):

      ```
      powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione lente
      powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione classifica
      powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione richieste
      ```

      `richieste` non chiede password e funziona **da subito**: legge i log del server.

- [ ] **③ Poi spegnere e svuotare** — `-Azione spegni` e `-Azione svuota`: il registro sta in una
      tabella (`mysql.slow_log`) che cresce e non si pulisce da sola.
      La misura delle richieste HTTP invece può restare accesa: scrive solo sopra i 500 ms.

---

## 1 · Security / hardening (priorità ALTA)

- [x] **BackupController — restringere a ADMIN** *(fatto 2026-06-03)*
  File: `ATEC.PM.Server/Controllers/BackupController.cs:12`
  Era `[Authorize]` senza ruolo → qualunque utente poteva fare `restore` e distruggere il DB. Ora `[Authorize(Roles="ADMIN")]` a livello di classe.

- [x] **ApiClient.PostLogin — JSON costruito a mano** *(fatto 2026-06-03)*
  File: `ATEC.PM.Client/Services/ApiClient.cs:68`
  Sostituito con `JsonSerializer.Serialize(new { username, password })`.

- [x] **ApiClient.HandleResponse — Dispatcher.Invoke a rischio deadlock** *(fatto 2026-06-03)*
  File: `ATEC.PM.Client/Services/ApiClient.cs:42`
  `Invoke`→`BeginInvoke` + guard `Interlocked` contro 401 concorrenti (evita N LoginWindow/MessageBox). La chiusura finestre resta forzata: documentata come limite noto (modali con lavoro non salvato).

- [~] **Allineare matrice ruoli controller** *(audit + enforcement economici fatti 2026-06-03)*
  File: `ATEC.PM.Server/Controllers/*.cs`
  Audit completato. Scelta architetturale: **enforcement a livelli server-side** speculare a `PermissionEngine` (no hardcoding ruoli).
  - **Fatto:** nuovi `Services/FeatureAccessService.cs` + `Authorization/RequireFeatureAttribute.cs` (risolvono ruolo→livello via `auth_levels`/`auth_features`, cache con `Reload()` su modifica feature). Applicato `[RequireFeature(...)]` ai 4 controller economici: `BudgetVsActual`→`data.budget`, `CashFlow`→`data.revenue`, `ProjectCosting`/`PreventiviCosting`→`data.costs`. `EmployeesController` Create/Update/Delete → `[Authorize(Roles="ADMIN")]`.
  - **Da testare:** login come **TECH** (livello 0) → BvA/Costing/CashFlow devono dare 403; PM/ADMIN ok.

- [x] **TimesheetController — IDOR su `employeeId`** *(fatto 2026-06-03)*
  File: `ATEC.PM.Server/Controllers/TimesheetController.cs`
  Aggiunto guard `CanAccessTimesheet(c, employeeId)` su tutti gli endpoint (`week`, `summary`, `projects-for-employee`, `phases-for-employee`, `POST`, `DELETE`): consente **self** OR **livello >= PM** (vede tutti) OR **RESP_REPARTO** sui dipendenti che condividono un suo reparto (`employee_departments`). Su `POST`/`DELETE` il controllo usa il proprietario reale della riga. Usa `FeatureAccessService` per i livelli. **Testato a runtime:** TECH legge/scrive solo sé stesso (altri→403), PM tutti, RESP solo il proprio reparto.

- [x] **DashboardController — KPI economici** *(fatto 2026-06-03)*
  File: `ATEC.PM.Server/Controllers/DashboardController.cs`
  Esponeva `TotalRevenue` (fatturato totale) a chiunque. Non si può bloccare l'intera dashboard (i KPI operativi — commesse, ore — servono a tutti), quindi `TotalRevenue` viene **azzerato** se il chiamante non ha la feature `data.revenue` (livello < 2). Gli altri campi sono operativi. **Testato a runtime:** con fatturato 50.000 → TECH vede 0, PM vede 50.000.

---

## 2 · UX / bug minori (priorità MEDIA)

- [ ] **403 lato client — feedback visibile (opzionale)**
  File: `ATEC.PM.Client/Services/ApiClient.cs:42` (`HandleResponse`)
  Oggi `HandleResponse` mostra un dialog **solo sul 401**. Sul **403** (privilegi insufficienti, introdotto col `RequireFeature` 2026-06-03) ritorna l'envelope `success=false` → i pannelli economici (BvA, CashFlow) restano semplicemente **vuoti**, senza messaggio. Va bene come backstop di sicurezza (la UI nasconde già quei pannelli ai livelli bassi via `PermissionEngine`), ma in caso di **misconfigurazione livelli** l'utente vede vuoto senza capire perché. Eventuale miglioria: gestire il 403 con un messaggio "Non hai i permessi per questi dati" (o un toast, quando ci sarà il componente Toast — vedi §4). Bassa priorità.

- [ ] **App.xaml — merge `ShadcnTheme.xaml` globalmente**
  File: `ATEC.PM.Client/App.xaml`
  Causa primaria del crash ContextMenu in UsersPage (`ShadcnMuted` non trovato nei popup). Aggiungere:
  ```xml
  <ResourceDictionary Source="/Styles/Shadcn/ShadcnTheme.xaml" />
  ```
  In più: rimuovere il merge locale duplicato in `MainWindow.xaml`, `LoginWindow.xaml`, `InputDialog.xaml`, `ProjectTemplatePage.xaml`, `ShadcnMessageBox.xaml`.

- [ ] **Verificare `StaticResource` dentro template di popup → convertire in `DynamicResource`**
  File: `ATEC.PM.Client/Styles/Shadcn/ShadcnNavigation.xaml:375` (e altri da scoprire)
  Pattern problematico: `Style="{StaticResource ShadcnMuted}"` dentro a un `ControlTemplate` di ContextMenu/Menu/ComboBoxItem. Sostituire con `DynamicResource`.

- [ ] **MainWindow — togliere `Width=1920 Height=1080`**
  File: `ATEC.PM.Client/Views/MainWindow.xaml:6-9`
  `WindowState="Maximized"` le sovrascrive ma su monitor piccoli causa un lampeggio iniziale. Lasciare solo `MinWidth/MinHeight`.

- [ ] **MainWindow — Frame.JournalOwnership=OwnsJournal con cache pagine**
  File: `ATEC.PM.Client/Views/MainWindow.xaml:685`
  Con `_pageCache` accumula history. Cambiare in `UsesParentJournal` o resettare manualmente.

- [ ] **MainWindow — VerticalOffset ContextMenu profilo con fallback hardcoded `236`**
  File: `ATEC.PM.Client/Views/MainWindow.xaml.cs:351-354`
  Si rompe su DPI ≠ 100% o se si aggiungono MenuItem. Calcolare dinamicamente o documentare il perché.

- [x] **LoginWindow — URL default mismatch http:5100 vs https:5101** — RISOLTO 10/06/2026
  File: `ATEC.PM.Client/Services/AppSession.cs` vs `LoginWindow.xaml`
  Allineati entrambi a `https://localhost:5151` contestualmente allo spostamento porte server 5100/5101 → 5150/5151 (per non collidere con altri server di test, es. ATEC_Risorse su 5100).

- [ ] **Controller — uniformare ritorno: `BadRequest` / `StatusCode(500)` invece di `Ok(Fail(...))`**
  Files: `TemplateController.cs`, altri post-refactor.
  Oggi il client guarda solo `success` nel payload, ma proxy/cache HTTP non sanno che è errore. Restituire HTTP status appropriato.

### Versamento ore / Timesheet

- [ ] **Avviso sforamento budget ore assegnate (fase + commessa + dipendente)**
  Quando si inserisce o modifica una voce in `TimesheetEntryDialog`, mostrare un **avviso non bloccante** (es. testo warning sotto le ore o toast) se le ore della registrazione — sommate alle ore già versate su quella fase per quel dipendente — superano il **budget assegnato** (`phase_assignments.planned_hours` per `employee_id` + `project_phase_id`).
  - **Dati:** `planned_hours` in BvA/assegnazioni (`PhasesController`, `phase_assignments`); ore già lavorate = `SUM(timesheet_entries.hours)` sulla stessa fase/dipendente (escludere `id` corrente in modifica). Se `planned_hours = 0`, nessun avviso (o messaggio “nessun budget assegnato” solo informativo).
  - **API:** estendere `GET /api/timesheet/phases-for-employee` (o nuovo `GET .../phase-hours-status?employeeId&phaseId&excludeEntryId`) con `PlannedHours`, `HoursWorked`, `RemainingHours`, `WouldExceedAfterSave`.
  - **Client:** `TimesheetEntryDialog.xaml(.cs)` — ricalcolare su cambio fase/ore/data; opzionale conferma “Salva comunque?” se oltre budget (la regola **24h/giorno** resta l’unico blocco hard).
  - **Allineamento:** stessa logica usata in BvA (`BvaCostingVM` / badge % su `PlannedHours` vs `HoursWorked`).

### Gestione Risorse / Piano Ferie

- [x] **🔴 URGENTE — Collaborazione multi-utente sul gantt: real-time + audit + permessi + evidenza propria risorsa** *(richiesto utente 2026-06-06 — implementato 2026-06-06, build soluzione 0 err/0 warn, MANCA verifica runtime GUI con 2 client)*
  **Contesto:** il planner (`Views/Risorse/`) era **single-user di fatto** (nessun SignalR/polling/versioning su `res_assignments`). Tutti e 4 i punti implementati:

  - **1 (real-time SignalR — opzione D) ✅** — Server: `Hubs/ResourcePlannerHub.cs` (`[Authorize]`, solo server→client), `AddSignalR()` + `MapHub<ResourcePlannerHub>("/hubs/resource-planner")` in `Program.cs`; JWT via query `access_token` per il path hub (`JwtBearerEvents.OnMessageReceived`). `ResourcesController` inietta `IHubContext<ResourcePlannerHub>` e fa broadcast `"AssignmentsChanged"` (payload `ResAssignmentChange`) dopo create/update/delete, **escludendo l'autore** (query `?conn={connectionId}` → `Clients.AllExcept`). Client: `ResourcePlannerPage.Realtime.cs` — `HubConnection` (`WithAutomaticReconnect`, token via `AccessTokenProvider`), start in `Page_Loaded` / stop in `Page_Unloaded`; su notifica `OnRealtimeChange` ricarica **solo se idle**, altrimenti rimanda con timer 600ms (`IsRealtimeBusy` = drag/`_barDragPending`/legend-drag/`_busy`/finestra disabilitata da modale). Le 4 CRUD client accodano `WithConn(url)`. Pacchetto `Microsoft.AspNetCore.SignalR.Client` 8.0.11.
  - **2 (audit) ✅** — `res_assignments` + colonne `updated_by`/`updated_at` (migrazione idempotente `AddColumnIfMissing` in `ResourcesDbService.InitTables`). POST/PUT le valorizzano con `CallerId()` + `NOW()`. `ResAssignmentDto`: `UpdatedBy/UpdatedByName/UpdatedAt` (LEFT JOIN employees in GetAssignments). Tooltip barra (`BuildTooltip`): "Modificato da … · gg/MM HH:mm". Concorrenza ottimistica server-side pronta: `ExpectedUpdatedAt` su `ResAssignmentUpdateRequest` → PUT risponde **409**. **NB: il client NON invia ancora `ExpectedUpdatedAt`** (eviterebbe falsi 409 al 2° edit consecutivo dell'autore, che dopo `ApplyLocalUpdate` non riceve il nuovo `updated_at`). Con SignalR il problema lost-update è già coperto in visibilità; per attivare il check serve far ritornare il nuovo `updated_at` dalla PUT e aggiornarlo in `ApplyLocalUpdate`. Lasciato pronto lato server.
  - **3 (sola-lettura) ✅** — feature `resources.edit` (`auth_features`, min_level **1** = RESP_REPARTO↑; seed + migrazione v4). `[RequireFeature("resources.edit")]` su tutti gli endpoint di scrittura (`assignments` POST/PUT/DELETE + anagrafiche `services`/`others`). Client: `_canEdit = PermissionEngine.CanAccess("resources.edit")`; in sola lettura nasconde "+ Allocazione" e le causali trascinabili, disabilita create/drag/resize/doppio-click-modifica/menu contestuale/tasto Canc (selezione barra ancora consentita). Etichette IT in `AuthFeatureLabels`. **Piano ferie resta visibile anche in sola-lettura** (è anche vista di lettura; scritture interne bloccate dal backstop server 403).
  - **4 (evidenza risorsa propria) ✅** — `RenderBody`: la riga del dipendente `== AppSession.UserId` ha nome in **grassetto** + sfondo accent (`SelfRowBg #EFF6FF`).

  **RUNTIME — real-time VERIFICATO 2026-06-06:** create + delete via API (= 2° utente) → comparsa/sparizione live sul client aperto, confermato dall'utente. **Ancora da verificare a vista:** login TECH → planner in sola lettura (no "+ Allocazione", no drag/menu) e POST/PUT/DELETE → 403; riga propria evidenziata (grassetto+accent); tooltip "Modificato da…"; merge che non interrompe un drag/dialog in corso.

  2. **Audit "ultima modifica" in DB + nel popup.** Aggiungere a `res_assignments` (`ResourcesDbService.InitTables`, migrazione `ALTER TABLE ADD COLUMN IF…`/idempotente) le colonne **`updated_by INT NULL`** (FK employees o user id) e **`updated_at DATETIME`**. Server `ResourcesController` POST/PUT le valorizza con l'utente del token (`User`/claims) e `NOW()`. Esporre `UpdatedBy`/`UpdatedByName`/`UpdatedAt` in `ResAssignmentDto` (+ JOIN nome in GetAssignments). Mostrarli nel **tooltip della barra** (`ResourcePlannerHelpers.BuildTooltip`, al passaggio del mouse): es. "Modificato da Mario Rossi · 06/06 14:30". `updated_at` abilita anche la **concorrenza ottimistica** (PUT manda la versione vista → server rifiuta se cambiata → "modificato da un altro utente, ricarica").

  3. **Permesso sola-lettura.** Solo **PM, RESP_REPARTO, ADMIN** possono creare/spostare/modificare/eliminare allocazioni; gli **altri solo visualizzano**. Oggi gli endpoint `/api/resource-planner/assignments` (POST/PUT/DELETE) sono `[Authorize]` aperti a tutti (vedi memoria "editing a tutti"). Server: applicare l'enforcement a livelli speculare al resto dell'app — `FeatureAccessService` + `[RequireFeature("resources.edit")]` (o ruoli) sugli endpoint di scrittura (pattern §1). Client: in modalità sola-lettura **disabilitare** drag/resize/create/"+ Allocazione"/menu Modifica-Elimina e i dialog (gating via `PermissionEngine`/feature). Registrare la nuova feature `resources.edit` in `auth_features`/`auth_levels`.

  4. **Evidenziare la risorsa dell'utente loggato.** Nella colonna nomi del gantt, evidenziare la riga del dipendente **collegato all'utente loggato** (mappa user→employee da `AppSession`/utente corrente), così l'utente vede subito le attività che lo riguardano (es. nome in grassetto + sfondo accent leggero sulla `NameCell` in `RenderBody`).

  **File coinvolti:** `ATEC.PM.Server/Controllers/ResourcesController.cs`, `Services/ResourcesDbService.cs`, `Program.cs` (hub); `ATEC.PM.Shared/DTOs/Resources_DTOs.cs`; `ATEC.PM.Client/Views/Risorse/Pages/ResourcePlannerPage.xaml(.cs)` + `Classes/ResourcePlannerPage.{Data,Render,Drag}.cs` + `ResourcePlannerHelpers.cs`; `FeatureAccessService`/`RequireFeatureAttribute`/`PermissionEngine`; `AppSession` (user→employee).

- [ ] **Conteggio ferie — decidere come trattare le FESTIVITÀ** *(da chiarire con utente, 2026-06-06)*
  File: `ATEC.PM.Client/Views/Risorse/Classes/ResourcePlannerHelpers.cs` (`WorkingDayCount` / `DisplayDayCount`).
  Il conteggio dei giorni di ferie ora esclude **sabato e domenica** (richiesto 2026-06-06), ma **NON** le festività (`IsHoliday`: festività fisse IT + Pasquetta). Da decidere: una ferie a cavallo di una festività infrasettimanale (es. 25/12, 1/5) deve contarla o no? Di norma le festività non si "consumano" come giorno di ferie → andrebbero escluse. Se confermato: aggiungere `&& !IsHoliday(d)` in `WorkingDayCount` (oppure un overload/flag separato per non impattare altri eventuali usi). Vale sia per il **Piano Ferie** sia per le ferie nel **gantt principale** (entrambi usano `DisplayDayCount`).

---

## 3 · Refactor / debito tecnico

- [ ] 🔴 **Il deploy è diventato lento: pubblicare una virgola costa minuti** *(chiesto il 15/08/2026)*
  File: `deploy/_comune.ps1`, `deploy/aggiorna-server.ps1`, `deploy/applica-aggiornamento.ps1`, `ATEC.PM.Tests/Infrastruttura/DatabaseDiProva.cs`

  **Il sintomo, con i numeri di oggi:** l'aggiornamento del 15/08 ha spedito **9 file, 6,5 MB su
  160,8** — e ci ha messo comunque diversi minuti. La sensazione che «prima, quando si spedivano
  160 MB, era più veloce» è fondata: **la rete non è mai stata il collo di bottiglia**. Su questa
  LAN 160 MB viaggiano in ~2 secondi (misurato il 04/08, sta scritto in GUIDA-SERVER-LAN.md §4).
  Il differenziale ha risparmiato quei 2 secondi e ne ha aggiunti parecchi altrove.

  **RISULTATO: da 228,5 s a 47,6 s** (aggiornamento vero, solo client, misurato il 16/08/2026).
  Il salto dei test vale da solo 163 secondi. Ripartizione dei 47,6 s che restano: confronto e
  pacchetto 20,4 · npm build 17,5 · upload 4,8 · publish 2,5 · applica sul server 1,9 · test 0,4.

  **MISURATO PRIMA** dell'intervento, sullo stesso tipo di aggiornamento — **228,5 s in tutto**:

  | fase | secondi | quota |
  |------|--------:|------:|
  | test | **163,6** | **72%** |
  | confronto e pacchetto (impronte SHA256 locale + server) | 33,1 | 14% |
  | npm build (client) | 18,8 | 8% |
  | dotnet publish (server) | 4,6 | 2% |
  | upload | 4,5 | 2% |
  | applica sul server | 3,8 | 2% |
  | copia in wwwroot | 0,0 | 0% |

  Due ipotesi ragionevoli sono state **smentite dai numeri**: `dotnet publish` costa 4,6 s (è
  incrementale, non ricostruisce il runtime) e l'upload 4,5 s. Restano in piedi solo le prime due
  righe. Il **robocopy sul server** è dentro i 3,8 s di «applica»: non è un problema.

  **Dove se ne va il tempo** (analisi di partenza, tenuta per memoria di come ci si è arrivati):
  1. **I test: ~3 minuti e 30.** La guida dice ancora «una cinquantina di secondi», ed era vero
     con 72 test. Oggi sono **130**, e quelli aggiunti da A1/A2/E2/E4/#83 creano **un database
     MySQL nuovo per ogni test**, ci applicano **tutte** le 92 migrazioni e lo buttano.
     Girano prima di ogni deploy, anche cinque minuti dopo averli visti verdi.
  2. **Le impronte SHA256, calcolate due volte su ~160 MB.** Il PC le calcola su tutto il
     pubblicato (`_comune.ps1:192`) e il server su tutto l'installato
     (`applica-aggiornamento.ps1:60`), a ogni deploy, per riscoprire ogni volta che i ~150 MB
     del runtime .NET non sono cambiati.
  3. **La copia completa sul server.** La versione nuova si compone copiando l'attuale con
     `robocopy /MIR` (`applica-aggiornamento.ps1:372`) e applicandoci sopra i file nuovi: 160 MB
     copiati sul disco del server per scriverci sopra 6 MB. Prima si scompattava un tar e basta.
  4. `dotnet publish --self-contained` ricostruisce ogni volta anche il runtime.

  **Da fare, in quest'ordine:**
  - [x] **① Prima misurare, poi tagliare** *(fatto 16/08/2026)*. Lo script cronometra ogni fase e
        alla fine stampa la classifica («DOVE SE N'E' ANDATO IL TEMPO»): la prima riga è sempre il
        prossimo lavoro. `Start-Cronometro` / `Add-Tappa` / `Show-Cronometro` in `_comune.ps1`.
  - [x] **③ Saltare i test se il codice non è cambiato** *(fatto 16/08/2026)*. `Get-ImprontaSorgenti`
        calcola lo SHA256 dei sorgenti **C#** (server + DTO + test; il client web non c'è dentro
        apposta: i test non lo guardano). Se coincide con l'ultima esecuzione verde
        (`deploy/out/.ultimo-test-verde`), i test si saltano. Un fallimento cancella l'impronta,
        così non si eredita mai un verde vecchio. Si forza con `aggiorna-server.bat -ConTest`.
        **È il taglio che risolve il caso di tutti i giorni**: pubblicare una modifica di sola
        grafica non costa più 3m30 di test.
        🪤 **`wwwroot` va tenuto fuori dall'impronta**: è il client *compilato* dentro il progetto
        server e contiene `version.json` con l'identificativo della build, che cambia a ogni
        `npm build`. Includendolo, l'impronta non tornava mai uguale: il salto sembrava attivo e
        non saltava niente. Se ne è accorto solo il cronometro, che continuava a segnare 164 s.
  - [x] **④ Le impronte SHA256 hanno una memoria** *(fatto 16/08/2026)*: l'hash di un file si
        ricalcola solo se sono cambiate dimensione o data di modifica, altrimenti si riusa quello
        già noto (`deploy/out/.impronte-pubblicate.txt` sul PC, `Updates\.impronte-installate.txt`
        sul server — **fuori** dalla cartella del server, che a ogni aggiornamento viene
        ricomposta da capo). I ~150 MB di runtime .NET non vengono più digeriti a ogni giro.
  - [x] **⑥ I test escono dal deploy: `prova-test.bat`** *(fatto 19/08/2026)*. Il cancello del
        14/08 resta, ma non si aspetta più davanti allo schermo: la suite si lancia quando fa
        comodo — anche mentre si continua a lavorare — e se è verde registra l'impronta in
        `deploy/out/.ultimo-test-verde`, la stessa che il deploy consulta. Il deploy successivo
        li salta legittimamente (nessuna rete tolta) e scende a ~50 s. `deploy/prova-test.ps1`
        ricalcola l'impronta **a fine corsa** e non registra niente se il C# è cambiato mentre
        i test giravano: registrarla darebbe un lasciapassare a codice che nessuno ha provato.
        🪤 Gli `.ps1` di questo progetto vanno salvati **con BOM UTF-8**: PowerShell 5.1 senza
        BOM legge il file come ANSI e va in errore di sintassi sulla prima lettera accentata.

  - [x] **⑦ npm build si salta se il client non è cambiato** *(fatto 19/08/2026)*. Erano 30 s —
        il **48%** del deploy misurato — spesi a ricostruire un bundle identico dopo una modifica
        di solo C#. `Get-ImprontaClient` + `deploy/out/.ultima-build-client`, stessa idea dei test.
        Si forza con `aggiorna-server.bat -ConClient`.
        🪤 L'impronta guarda **percorso, dimensione e data**, non il contenuto: con l'hash pieno
        costava 15 s a freddo, cioè metà di quello che doveva far risparmiare.
        📌 Saltando la build, `version.json` e il `<meta app-build>` restano quelli di prima — ed è
        giusto: identificano la versione del **client**, non il deploy. Nessun banner «aggiorna
        adesso» quando il client non è cambiato.

  - [x] **② Test: lo schema non si ricostruisce più a ogni test** *(fatto 19/08/2026)*.
        `ATEC.PM.Tests/Infrastruttura/SchemaCondiviso.cs`: un database solo per tutte le classi
        che non provano le migrazioni, e ogni test riparte da pulito in **~45 ms** invece di ~5 s.
        ⚠️ **Le due strade ovvie erano entrambe sbagliate, misurate il 19/08:**
        `TRUNCATE` di 119 tabelle costa **4,4 s** (ogni TRUNCATE ricrea il tablespace InnoDB) e
        `TRUNCATE`+riseed **6,0 s** — cioè *più* che ricreare il database da zero. Quello che
        funziona è l'opposto: fotografare l'ultimo `id` di ogni tabella a schema appena creato
        (110 su 119 ce l'hanno) e cancellare **solo le righe più recenti**, lasciando stare i dati
        di partenza. 214 ms la fotografia, 43 ms la pulizia.
        **Restano col database usa-e-getta**, e devono restarci: i test delle migrazioni (partono
        da vuoto per definizione), `IndiciEQueryTests` (crea e cancella **indici**: cambia lo
        schema) e `CacheLettureEquivalentiTests` (scrive nelle tabelle di **configurazione** —
        `ddp_aggregation_states`, `ddp_status_transitions` — che la pulizia non tocca).

  - [ ] **② (residuo) i test delle migrazioni** — ⚠️ **la causa NON è dove sembrava**.
        Misurato il 16/08/2026 su un test vero: `CREATE DATABASE` 0,08 s · **`InitDatabase` +
        migrazioni 5,14 s** · `DROP DATABASE` 0,60 s. Ma la somma dei `duration_ms` delle 92
        migrazioni è **1,4 s**: gli altri **3,7 s sono la creazione delle 119 tabelle** dentro
        `InitDatabase`. Ottimizzare le migrazioni non servirebbe quasi a niente.
        Strade, in ordine di rischio: (a) **stampo** dello schema costruito una volta e riusato
        (⚠️ `CREATE TABLE … LIKE` **non copia le foreign key**: servirebbe `SHOW CREATE TABLE`, e
        i test sulle FK sono proprio quelli che si romperebbero in silenzio); (b) fixture
        condivisa xUnit per i ~24 test che **non** provano le migrazioni (i 22 di
        `MotoreMigrazioniTests`/`MigrationRunnerTests` devono continuare a partire da zero);
        (c) lasciar perdere, visto che con ③ i test girano solo quando il C# cambia davvero.
        Guadagno stimato: la suite da 3m30 a ~1m20. **Da fare solo se ② dà fastidio davvero.**
        📈 **Aggiornamento 19/08/2026:** i test sono 146 e **62 di loro si creano un database**
        (5,2 s l'uno): la suite completa è arrivata a **11 minuti**. Con ⑥ non si aspetta più
        durante il deploy, ma questo resta il taglio grosso che manca — è il 90% di quegli
        11 minuti.
  - [ ] **④ Non riesaminare il runtime .NET.** Le sue ~150 MB non cambiano se non si aggiorna
        l'SDK: bastano nome+dimensione+data per accorgersene, e l'hash pieno solo sul resto.
        Alternativa più netta: hash **solo** dei file dell'applicazione, runtime confrontato a parte.
  - [ ] **⑤ Applicare i file nuovi direttamente**, invece di copiare 160 MB per poi sovrascriverne 6
        (la versione precedente è già salvata a parte: `Server.precedente` serve già a quello).

  **Riferimento:** la strada breve per gli aggiornamenti di sola grafica esiste già ed è documentata
  in GUIDA-SERVER-LAN.md §4 («Aggiornamenti di solo client: nessuna interruzione»): lì il servizio
  non si ferma nemmeno. Il problema è tutto in quello che viene **prima** della spedizione.

- [ ] **ShadcnMessageBox — completare migrazione (286 → 2 usi attuali)**
  File: `ATEC.PM.Client/Controls/ShadcnMessageBox.xaml.cs`
  Strategia: regex find&replace `MessageBox.Show(` → `ShadcnMessageBox.Show(` in tutto `ATEC.PM.Client/Views`, `UserControls`. Pulire `using System.Windows;` ridondanti se servisse.

- [ ] **CLAUDE.md — aggiornare al nuovo design system**
  File: `ATEC_PM/CLAUDE.md:19-26`
  Dice "NO CornerRadius, NO shadows, NO gradients" ma il nuovo design Shadcn ne ha eccome. Le skill `.claude/skills/atec-design-system/` e `wpf-xaml-guide/` sono già state cancellate, ma CLAUDE.md no.

- [ ] **TemplateController.CopyFile — già flaggato, rivedere altre operazioni transazionali**
  File: `ATEC.PM.Server/Controllers/*Controller.cs`
  Cercare pattern "scrittura DB + side-effect su disco" e applicare il rollback DB se l'I/O fallisce (come fatto in `CopyFile`). Candidati: `BackupController`, `ImportController`, `DocumentManagerControl` upload.

- [ ] **Audit endpoint server "dead code"**
  Vedi anche `ATEC_PM_ATTIVITA.txt` sez. H. Già rimossi `QuotesController.Duplicate` e `PreventiviController.GetAll`. Cercarne altri non chiamati dal client.

---

## 4 · Componenti Shadcn da aggiungere

Catalogo Shadcn ufficiale = 58 componenti. Stato attuale: 38 presenti, 5 parziali, 15 assenti.

### Da completare (parziali)

- [ ] **Dialog generico riusabile** (oggi solo `ShadcnMessageBox` testo+icone)
  Estendere `ShadcnMessageBox` con costruttore che accetta contenuto XAML custom (es. `FrameworkElement Content`). Sostituisce gradualmente i Window-as-dialog sparsi (`CustomerDialog`, `SupplierDialog`, `ProjectDialog`, ...).

- [ ] **Data Table — toolbar + pagination footer riusabili**
  Oggi `ShadcnDataGrid` è solo lo stile della griglia. Aggiungere uno `ShadcnDataTableShell` UserControl con: tabs di vista, dropdown "Customize Columns", search box, pagination footer (next/prev + page size selector).

- [ ] **Dropdown Menu — wrapper su Button**
  Pattern usato in UsersPage:
  ```csharp
  btn.ContextMenu.PlacementTarget = btn;
  btn.ContextMenu.IsOpen = true;
  ```
  Formalizzare in un attached behavior o UserControl.

### Da creare ex novo (alta priorità)

- [ ] **Toast / Sonner** — feedback non bloccante
  Esempio target: piccola notifica in basso-destra con auto-dismiss 4s, queue stacking, varianti success/error/info/warning. Sostituisce i ~150 `MessageBox.Show` informativi ("Salvato.", "Operazione completata.").
  File da creare: `Controls/ShadcnToast.xaml(.cs)` + `Services/ToastService.cs`.

- [ ] **Skeleton** — placeholder durante load API
  Border + animation `LinearGradientBrush` a shimmer. Usabile come `<shadcn:Skeleton Width="200" Height="20" />`.
  File: `Controls/ShadcnSkeleton.xaml`.

- [ ] **Pagination** — UI esplicita
  UserControl con `«` `‹` `1 2 3` `›` `»` + page-size selector (10/25/50/100). Si aggancia a `PagedApiHelper`. Usabile sotto qualunque lista.
  File: `Controls/ShadcnPagination.xaml(.cs)`.

- [ ] **Sheet (drawer laterale)**
  Window scorrevole da destra/sinistra, larghezza fissa o adattiva. Per dettagli commessa, riga timesheet, edit-inline. Anim su `Width` o `Translation`.
  File: `Controls/ShadcnSheet.xaml(.cs)`.

### Da creare (media priorità)

- [ ] **Field** (Label + Input + Description + Error)
  UserControl che incapsula il pattern ripetuto in ogni Dialog:
  ```
  Label
  [TextBox / ComboBox / DatePicker]
  Helper text (opt)
  Error text rosso (se Validation.HasError)
  ```
  File: `Controls/ShadcnField.xaml(.cs)`.

- [ ] **Empty state** (formalizzazione)
  Hai già overlay con icona+titolo+detail in `ProjectTemplatePage`. Estrarre in `ShadcnEmpty` UserControl: `<shadcn:Empty Icon="📭" Title="..." Detail="..." />`.

- [ ] **Breadcrumb**
  Per pagine a più livelli (Templates con cartelle annidate, Commesse con sezioni). Item separati da `›`, ultimo non cliccabile.
  File: `Controls/ShadcnBreadcrumb.xaml(.cs)`.

- [ ] **Popover** — micro-form ad ancoraggio
  Wrapper su `Popup` con stile Shadcn (radius 6, shadow, padding). Per filtri, mini-editor, info-box ancorati a un Button.
  File: `Controls/ShadcnPopover.xaml(.cs)`.

- [ ] **Hover Card** — anteprima al hover
  Popup con delay (~300ms) + auto-dismiss. Anteprima commessa al hover su un id, scheda utente, ecc.
  File: `Controls/ShadcnHoverCard.xaml(.cs)`.

- [ ] **Toggle / Toggle Group**
  Stile Shadcn per `ToggleButton` singolo e gruppo (es. per i range tab "3 mesi / 30 gg / 7 gg" del chart).
  File: aggiungere style chiavi `ShadcnToggle`, `ShadcnToggleGroup` in `ShadcnInput.xaml`.

### Da creare (bassa priorità / opzionali)

- [ ] **Command palette (⌘K)** — quick-nav "vai a commessa…", "apri preventivo…", "nuova fase…"
  Effort alto: serve fuzzy search + indice ricerca + popup full-screen-ish. Vale solo se gli utenti chiedono accelerazione.

- [ ] **Input OTP** — solo se si introduce 2FA.

- [ ] **Carousel, Aspect Ratio, Direction, Menubar, Native Select, Input Group, Item** — non rilevanti per un gestionale interno.

---

## 5 · Dashboard "dashboard-01" (da Shadcn blocks)

Riferimento: <https://ui.shadcn.com/blocks#dashboard-01>

- [ ] **Stat cards row** — 4 card KPI in alto su `DashboardPage`
  KPI proposti: Fatturato del mese, Commesse aperte, Ore versate questo mese, Margine medio %.
  Pattern: label muted in alto, numero grande, badge delta con freccia ↑/↓, footer 2 righe trend.

- [ ] **Area chart con time-range tabs** — sostituire/affiancare gli OxyPlot esistenti
  Tab "3 mesi / 30 giorni / 7 giorni" sopra a un area-chart stacked (es. ore prodotte vs preventivate, oppure trend margine commesse).

- [ ] **DataTable nella Dashboard** — top-10 commesse a rischio / scadenze prossime
  Con i `ShadcnDataTableShell` (sopra) quando pronto.

---

## 6 · UsersPage (review 2026-05-27)

La pagina è intenzionalmente in tema **dark** (override brush locali + uso di `Shadcn*Dark` styles) mentre il resto dell'app è light. Funziona, ma ha problemi UX e coerenza.

### Visivi / coerenza

- [ ] **Drag handle decorativo invisibile**
  File: `ATEC.PM.Client/Views/Utenti/UsersPage.xaml:124`
  `<TextBlock Text="&#xE700;" Foreground="#52525B" IsHitTestVisible="False" />`
  `#52525B` su sfondo card `#0C0C0E` = quasi invisibile, e `IsHitTestVisible="False"` lo rende decorativo (non fa drag). Decidere: rimuoverlo, oppure implementare drag-reorder vero.

- [ ] **EmployeeDialog è light, UsersPage è dark — stacco visivo**
  File: `ATEC.PM.Client/Views/Utenti/EmployeeDialog.xaml:8`
  Quando da UsersPage si apre il dialog "Modifica" / "+ Nuovo Utente", si passa da nero a bianco. Replicare gli stessi override brush dark dentro `EmployeeDialog.xaml.Resources`, oppure formalizzare convenzione.

- [ ] **⋮ menu poco visibile**
  File: `UsersPage.xaml:213`
  `Foreground="#71717A"` su sfondo nero card → contrasto borderline. Verificare che `ShadcnButtonGhost` schiarisca su hover.

- [ ] **Placeholder search fluttuante (workaround)**
  File: `UsersPage.xaml:91-98` + `UsersPage.xaml.cs:124-128`
  TextBlock posizionato sopra il TextBox + visibility gestita a mano. Soluzione fragile. Incorporare watermark dentro lo stile `ShadcnTextBox` come `VisualBrush` su `Background` (una volta, riusabile in tutta l'app).

### Funzionalità mancanti

- [ ] **Doppio click su riga → Modifica**
  File: `UsersPage.xaml` (DataGrid `dgUsers`)
  Aggiungere `MouseDoubleClick="DgUsers_DoubleClick"` → handler chiama `OpenEdit()`. Standard d'industria.

- [ ] **Sort per colonna**
  File: `UsersPage.xaml` (DataGridTemplateColumn)
  Aggiungere `SortMemberPath` a ogni colonna: `FullName`, `UserRole`, `Username`, `Status`, `DepartmentCodesDisplay`. WPF abilita ordinamento nativo automaticamente.

- [ ] **`_loadedOnce` per evitare reload API ad ogni tab-switch**
  File: `UsersPage.xaml.cs:18`
  La page è cacheata in `MainWindow._pageCache`. `Loaded` scatta ad ogni reentry → `LoadUsers()` rifa la chiamata. Pattern già applicato in `ProjectTemplatePage`. Aggiungere flag + bottone "Aggiorna" esplicito in toolbar.

- [ ] **Debounce su ricerca (opzionale)**
  File: `UsersPage.xaml.cs:124-128`
  `TxtSearch_TextChanged` filtra in-memory ad ogni carattere. Per liste fino a ~200 record va bene; se cresce, aggiungere `DispatcherTimer` 200-300ms.

- [ ] **Soft-delete: chiarire comportamento lato lista**
  File: `UsersPage.xaml.cs:72` + server `EmployeesController`
  Messaggio dice "l'utente verrà disattivato". Verificare che `/api/users` escluda TERMINATED, altrimenti dopo il delete il record resta visibile e confonde.

### Refactor

- [ ] **Estrarre `RowMenuHeaderStyle` in stile condiviso**
  Files: `UsersPage.xaml:33-60` + `MainWindow.xaml` (profilo sidebar) hanno lo stesso pattern avatar+nome+ruolo nel popup. Estrarre in `ShadcnTheme.xaml` come `ShadcnUserPopupHeader`.

- [ ] **ApplyFilter — `StringComparison.OrdinalIgnoreCase` invece di `.ToLower()`**
  File: `UsersPage.xaml.cs:38-45`
  Sostituire `u.FullName.ToLower().Contains(filter)` con `u.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)`. Niente allocazioni intermedie.

- [ ] **TextBox search — `MinWidth`/`MaxWidth` invece di `Width=220` fisso**
  File: `UsersPage.xaml:88`
  Resiliente a finestre strette.

### Decisione di design da formalizzare

- [ ] **Pattern "pagine admin in dark"**
  Se UsersPage rimane dark, decidere quali altre pagine seguono (Permessi, Backup, Configurazione Sezioni?) e spostare gli override brush in `Styles/Shadcn/ShadcnDarkPalette.xaml` riusabile, invece di duplicarli pagina per pagina.

---

## 7 · Note rotazione password MySQL

Già in memoria utente — `Atec2005` ancora valido sul server, rimandato il 22/05/2026. Vedi `~/.claude/projects/.../memory/todo_mysql_root_rotation.md`.

---

## 8 · Gamma Robot (nuovo sottosistema)

Distinta schede/componenti per robot+quadro (Commerciale/Service). Layer dati completo (listino `Gamma Ricambi` 286 prodotti prezzati+descritti; `gamma_robot` 23 → `gamma_quadro` 106 → `gamma_distinta` 3258) + UI con 2 viste (Per Robot / Magazzino per-componente). Build OK, query verificate sul DB. Dettagli in memoria: `memory/gamma_ricambi_distinta_robot.md`.

- [ ] **Aggancio offerte preventivi** *(feature separata, da progettare con l'utente)*
  Mettere un singolo componente **o l'intera distinta di un robot/quadro** dentro un preventivo (riuso sottosistema Preventivi/CMS). Da decidere: flusso UI (pulsante "aggiungi a offerta" dalla distinta?), come mappare le righe gamma_distinta → quote_items, gestione alternative e prezzi VB.

- [x] **Editor Composizione Robot (stile Composizione Codex)** *(implementato 2026-06-04 — verifica runtime GUI ancora da fare)*
  FATTO: 3ª tab "Composizione" (solo ADMIN) con editor drag&drop. **Server**: +colonna `gamma_distinta.is_optional` (migrazione idempotente in `GammaRobotDbService.InitTables`), +9 endpoint CRUD ADMIN-gated su robot/quadro/distinta in `GammaRobotController`. **Client**: `GammaRobotPage.Composizione.cs` (partial nuovo) — albero Robot→Quadro lazy + griglia 379 componenti (drag source, wildcard) + albero distinta editabile (7 sezioni fisse → principali → alternative annidate); **drop su sezione = principale, drop su principale = alternativa** (il ruolo lo decide il punto di rilascio), ▾ menu "segna opzione"/"modifica qtà", ✕ rimuovi; nuovi `GammaRobotDialog`/`GammaQuadroDialog`. Consultazione "Per Robot": badge OPT + **due totali** (base / +opzioni). Build server+client 0 errori; migrazione verificata sul DB. **DA FARE: prova runtime GUI** (drag&drop, render, lazy-load). Decisioni implementate:
  - **Padre = robot → quadro annidati**: albero a più livelli (robot in cima → suoi quadri → sezioni → componenti), come Codex 7→6→5; i componenti restano agganciati al quadro.
  - **Tenere** le viste attuali (Per Robot, Magazzino per componente) e **aggiungere** una tab/pagina "Composizione" editabile.
  - **Mappatura livelli Codex→Gamma**: 101 componente = scheda (`quote_products`); 501 assieme = **Quadro** (schede+azionamenti+tastierino+ventole) e **Manipolatore** (motori+cavi+meccanica); 601 = **robot completo** = Quadro(501)+Manipolatore(501); 701 = futura cella/impianto. NB: l'attuale `gamma_quadro` è di fatto già il **601** (distinta con tutte le sezioni); le **sezioni** sono i due 501 al suo interno → il livello c'è già, è questione di presentazione nell'editor.
  - **Schede opzionali** (deciso, da rivalutare in programmazione): NON un "601+opzioni" come assieme separato (esplosione combinatoria), ma **colonna `is_optional` su `gamma_distinta`** → un componente può essere principale / alternativa (`is_alternate`) / opzione (`is_optional`). Due totali: *base* e *base+opzioni*; le opzioni si scelgono in fase di preventivo. Nessuna tabella nuova, solo la colonna.
  - **Drag&drop**: a sinistra griglia robot/quadri (compositi) + griglia componenti disponibili (i 379 di Gamma Ricambi, con filtro wildcard); a destra TreeView della distinta. Trascini un componente nella sezione del quadro → aggiunge riga `gamma_distinta` (dialog quantità + flag principale/alternativa). ✕ sul nodo per rimuovere.
  - Mappatura: `gamma_quadro`=composito padre, `quote_products`(listino 4)=articoli disponibili, `gamma_distinta`=composizione (qty è già colonna, + sezione + is_alternate → modello più ricco di codex_compositions che usa righe duplicate).
  - Server: estendere `GammaRobotController` con POST/PUT/DELETE su `gamma_distinta` (add/update qty/remove) + validazione (componente↔sezione). Pattern endpoint composizioni di `CodexController`.
  - Riferimenti da seguire: `CodexCompositionPage.xaml(.cs)` (D&D, tree building, QuantityDialog), `CodexController` (endpoint compositions/tree, AddComposition), schema `codex_compositions` in `DbService.cs`.

- [ ] **Verifica runtime UI Gamma Robot** *(serve GUI Windows)*
  Rendering albero/griglia, lazy-load quadri all'espansione, raggruppamento per sezione, switch tab Per Robot/Magazzino. **Ora anche la tab Composizione**: drag&drop componente→sezione (principale) e componente→principale (alternativa), ▾ opzione/qtà, ✕ rimuovi, CRUD robot/quadro, badge OPT + due totali. Build e query già OK; manca la prova visiva.

- [x] **Consolidamento listini → Gamma Ricambi** *(fatto 2026-06-04)*
  Spostate 124 schede-robot curate da Atec Service (S2/S3, S4/S4C/S4C+, IRC5, Motori) dentro Gamma Ricambi; 1 prodotto per codice (30+1 doppioni risolti, distinta+preventivi ripuntati), categorie vecchie svuotate. Gamma ora 379 prodotti. Backup: `db_query/backup_pre_consolidamento_20260604_105449.sql`.

- [ ] **Curare descrizioni 43 board servizio S2/S3/S4** — dopo il consolidamento restano 43 board vecchi (DSQC 115/202/210/253/266x/350/352/377… DSPC 157, ACRB-03, YYT 1020) con descrizione legacy breve (~70 char). Curarli col runbook descrizioni (template 1×2 50/50). Vedi `memory/popolamento_descrizioni_catalogo.md`.

- [ ] **Prezzi VB mancanti** — i prodotti senza prezzo nel listino VB `Confronto_Listini` (gli auto-generati a 0 + i board vecchi col prezzo catalogo, non VB). Da censire/uniformare.

- [ ] **2° giro fogli Excel** — restano `GammaInstallata`, `Kit-Motori`, parco installato `Anagrafica` del file `DB-Master_Manipolatori.xlsm` (per ora gestito solo `Gamma Manipolazione`).

---

## 9 · Porting da ATEC Risorse (standalone)

Modifiche fatte sul programma lite `C:\Users\diego\Desktop\ATEC_Risorse` (2026-06-09/10) da riportare qui. Il modulo Risorse del lite è estratto da `Views/Risorse` di PM, quindi i file omologhi combaciano quasi 1:1 — attenzione solo a SQL (lite=SQLite, PM=MySQL) e namespace (`ATEC.Risorse.*` → `ATEC.PM.*`).

### Gantt / Risorse

- [x] **Regola conflitti: FERIE+FERIE = conflitto** *(richiesta utente 2026-06-10 — PORTATO 2026-06-10, build 0 err)*
  File PM: `ATEC.PM.Client/Views/Risorse/Classes/ResourcePlannerHelpers.cs:235` (`Forbidden`).
  Oggi PM usa il vecchio XOR: due ferie sovrapposte sulla stessa risorsa NON sono segnalate. Sostituire con la versione del lite (`ATEC_Risorse/ATEC.Risorse.Client/Views/Risorse/Classes/ResourcePlannerHelpers.cs`, `Forbidden` riscritta e commentata). Matrice finale: overlap lecito SOLO per OP+FLEX e FLEX+FLEX; le FERIE confliggono con tutto, ferie incluse. Copia-incolla di 1 funzione.

- [x] **Fix sfasamento colonne giorno nel Gantt (glitch grafico)** *(PORTATO 2026-06-10 — da verificare a vista nel Gantt)*
  File PM: `ATEC.PM.Client/Views/Risorse/Classes/ResourcePlannerPage.Render.cs:102` (header) e `:254` (corsie).
  Colonne giorno a larghezza `*` dentro lo ScrollViewer orizzontale (`bodyHScroll` Auto) → misura con larghezza infinita → le colonne si dilatano sul contenuto e la griglia si sfasa nelle righe con barre. Fix (come nel lite, `ResourcePlannerPage.Render.cs`): larghezza fissa `dayWidth = timelineWidth / _windowDays` in `RenderHeader(winEnd, dayWidth)` e nelle lane di `RenderBody`. **NB:** `FerieDashboardWindow` ha lo stesso pattern star MA niente scroller orizzontale (misura finita) → NON serve toccarla.

- [x] **Selettore risorse visibili nel Gantt («Risorse: tutte ▾»)** *(PORTATO 2026-06-10 con PlannerUiSettingsStore completo: persiste finestra/filtri/scroll/selezione in %AppData%\ATEC_PM\planner-ui.json — da verificare a vista)*
  Sorgente lite: `ResourcePlannerPage.ResourceFilter.cs` (partial nuovo), popup in `ResourcePlannerPage.xaml` (2 colonne, slide toggle `ToggleSwitchStyle`, pulsanti Tutte/Nessuna), filtro in `GetFilteredResources` (Render.cs), etichetta col conteggio robusto alle cessazioni (id orfani non contati ma mantenuti → la riattivazione fa ricomparire la risorsa).
  ⚠️ Dipendenza: il lite persiste la selezione in `Services/PlannerUiSettingsStore.cs` (`%AppData%\ATEC_Risorse\planner-ui.json`, salva TUTTI i filtri del planner con debounce 400ms + restore in `ResourcePlannerPage.Settings.cs`) — PM non ha nulla di simile: portare anche lo store (rinominando cartella AppData) o agganciare a `UserPreferences`.

### Utenti

- [x] **Riattivazione utenti cessati (TERMINATED)** *(PORTATO 2026-06-10 — testato runtime: ?includeTerminated 25→26 utenti; nuovo PUT /api/users/status; UI: spunta «Mostra cessati» + «Riattiva» nel menu ⋮)*
  File PM: `ATEC.PM.Server/Controllers/UsersController.cs:30` (GetAll esclude TERMINATED senza alternativa) + `ATEC.PM.Client/Views/Utenti/UsersPage.xaml(.cs)`.
  Oggi un cessato sparisce per sempre dalla UI. Portare dal lite: parametro `GET /api/users?includeTerminated=true`, spunta «Mostra cessati», colonna Stato, azione «Riattiva» nel menu riga (riporta status ACTIVE — in PM può riusare `PUT /api/employees/{id}`), testo conferma eliminazione che spiega la reversibilità. Sorgente lite: `UsersController.cs` + `Views/Admin/AdminWindow.xaml(.cs)`.

- [ ] **Export/Import dipendenti JSON** *(da valutare: caso d'uso primario era la migrazione del lite tra PC)*
  Sorgente lite: `EmployeesController.cs` (`GET /api/employees/export`, `POST /api/employees/import` — ADMIN, import = replace completo con id preservati, FK off fuori transazione, bootstrap admin rigarantito, rifiuto file vuoto) + `User_DTOs.cs` (`EmployeeExportDto` con `PasswordHash`, `EmployeesBackupDto`) + pulsanti in `AdminWindow`.
  Per PM: utile come backup/ripristino utenti. Adattare SQL (`LAST_INSERT_ID()`, niente `sqlite_sequence`) e includere le colonne extra di PM (`badge_number`, `supplier_id`, `hourly_cost`, …).

### Login (assenti in PM — verificato 2026-06-10, PORTATI 2026-06-10)

- [x] **Password iniziale «n.cognome» + cambio forzato al primo accesso** *(PORTATO — testato runtime: login restituisce mustChangePassword)*
  Aggiunti: `ATEC.PM.Shared/InitialPasswordHelper.cs`, `MustChangePassword` in `LoginResponse` + calcolo nel login, flusso forzato in `LoginWindow`, `POST /api/users/reset-password` + voce «Reset password» nel menu ⋮ di UsersPage.

- [x] **Cambio password dalla schermata di login** *(PORTATO — testato runtime: pwd errata → 400 con messaggio corretto)*
  Aggiunti: `ChangePasswordDialog.xaml(.cs)`, `POST /api/auth/change-password-login` (anonimo), `ChangePasswordRequest` esteso (era OldPassword/NewPassword senza consumatori → sostituito), validazioni complete in `ApplyPasswordChange`, pulsante «Cambia password» in LoginWindow, `ApiClient.PostAnonymousAsync`.

- [x] **SessionGuard — logout se l'utente viene disattivato** *(PORTATO — testato runtime: GET /api/auth/session → {employeeId, isActive})*
  Aggiunti: `GET /api/auth/session`, `SessionStatusDto`, `Services/SessionGuard.cs`; hook nel reload realtime del planner. NB: il check scatta sui reload del planner — per una copertura app-wide valutare un hook anche su MainWindow (timer o cambio pagina).

- [x] **Ricordare ultimo username al login** *(PORTATO)*
  Aggiunto `Services/LoginSettingsStore.cs` (`%AppData%\ATEC_PM\login-settings.json`) + aggancio in `LoginWindow` (precompila username, focus su password).

### Già allineati (verificato, NON servono)
Lockout 5 tentativi/5min, bcrypt + migrazione SHA256→bcrypt, gestione 401→re-login: identici nei due programmi. La gestione utenti del lite (ricerca/CRUD/EmployeeDialog) era a sua volta un port DA PM.

---

## 10 · Permessi — generalizzare le concessioni per reparto *(da discutere con l'utente, proposto 2026-08-04)*

Oggi (04/08/2026) esistono **tre** criteri di accesso: livelli (`auth_features.min_level`),
concessioni per ruolo (`auth_role_features`, ruoli con `access_mode='GRANTS'`) e — dal fix
del reparto Contabilità — una **lista bianca esclusiva legata al reparto**, cablata in
`FeatureAccessService.ContabilitaFeatures` (`nav.sal`, `sal.economics`, `nav.bug_reports`,
`nav.clienti`; responsabile FULL, tecnico READ, Clienti sempre READ, ADMIN esenti).

**Proposta**: portare il terzo criterio in DB e renderlo governabile dall'ADMIN.

- Nuova `auth_department_features` (`department_id` + `feature_key` + `access` READ/FULL),
  gemella di `auth_role_features`.
- Flag su `departments` per distinguere i reparti **esclusivi** (Contabilità: vede solo la
  sua lista) dai reparti **additivi** (Officina, Acquisti…: la lista si somma al livello).
  Senza il flag, estendere la regola a tutti i reparti toglierebbe Dashboard/Timesheet ai
  tecnici che oggi li vedono.
- Editor nella pagina «Permessi» (matrice reparto × funzione) accanto a quella dei livelli.
- Mappatura da concordare (bozza utente del 04/08): UTM/UTE → `nav.codex` + `nav.commesse`;
  MEC/INS/PLC/ROB → `nav.gestore_ddp` (+ `nav.officina_inbox`?) + `nav.risorse`;
  ACQ → `nav.acquisti_inbox` + `nav.fornitori`.

**Scartate** (dalla stessa bozza, motivi già discussi):
- «accesso = reparto abilitato **OPPURE** livello ≥ 2»: scavalca `min_level` e la pagina
  «Permessi», e toglie Dashboard/Timesheet/Commesse (livello 0) a chi non ha un reparto
  abilitato;
- «scrittura = livello ≥ 1 ovunque ci sia accesso»: cancella ogni concessione READ, a
  partire da Clienti in sola lettura per la contabilità.

Client: **niente mappatura duplicata** in `permissions.ts` — i grant continuano ad arrivare
già calcolati da `GET /api/auth-levels/features/my`.

---

*Ultimo aggiornamento: 2026-06-10 — sez. 9 PORTATA (tranne export/import JSON, da valutare): conflitti ferie+ferie, fix colonne Gantt, selettore risorse+persistenza, riattivazione cessati, blocco login completo. Build 0 err/0 warn; endpoint testati runtime read-only (login/mustChangePassword, session, includeTerminated, change-password-login). DA VERIFICARE A VISTA: Gantt allineato, selettore risorse, flusso cambio password forzato, reset password, riattiva cessato.*
