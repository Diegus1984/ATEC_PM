# Piano — sincronizzazione in tempo reale ATEC PM ⇄ ATEC Risorse (VPS)

> Scritto il 02/09/2026 dopo il confronto fra i due programmi e i dati reali dei due server.
> Obiettivo di Diego: «la pagina Risorse aggiornata in modo bidirezionale, così da essere sempre allineati»,
> con «una sorta di servizio che aggiorna in tempo reale le cose».

---

## 1. I due programmi oggi

### ATEC Risorse (`C:\Users\diego\Desktop\ATEC_Risorse`)

Programma **autonomo**, in uso tutti i giorni, che gira sul **VPS Shellrent** (`https://178-32-137-221.sslip.io`,
servizio `atec-risorse`, database SQLite in `/var/lib/atec-risorse/risorse.db`). Non è raggiungibile
dalla LAN e non raggiunge la LAN: il MySQL di ATEC PM per lui non esiste.

| Pezzo | Cosa fa |
|---|---|
| `ATEC.Risorse.Server` | API ASP.NET Core + SQLite + SignalR + hosting del client web |
| `ATEC.Risorse.Web` | Client Blazor WASM: **Planner** (Gantt OP/FLEX/FERIE, drag, conflitti, filtri, reparti, stampa), **Piano ferie**, **Export attività** (stampa/PDF per periodo), **Parco auto** (prenotazioni, gestione veicoli, scadenze, guasti), **Utenti e permessi** (utenti, reparti, permessi calendario, digest) |
| `ATEC.Risorse.Mobile` | App Android (MAUI): attività assegnate (`/api/resource-planner/my-assignments`), prenotazione auto, segnalazione guasti, notifiche push |
| Servizi in sottofondo | **Digest email** ogni mattina (07:30, mittente `BOT_Atec@atec.srl`, 94 giri fatti), **riepilogo push** alle 08:00, promemoria scadenze auto, **feed calendario ICS** per Outlook/Google (in uso), presenza online |

Dati suoi: `employees` (copia di ATEC PM importata il 12/06 con gli stessi id), `departments`,
`projects` (2 commesse **demo**, mai usate), `res_assignments` (le allocazioni vere), `res_services`
e `res_other_activities` (2 demo ciascuna, mai usate), tabelle `veh_*` del parco auto.

L'import da MySQL (`importa-da-mysql.bat`, `MySqlImportService`) è **manuale, una tantum e
distruttivo** (svuota e ricarica) e dal VPS non può girare: è servito solo a costruire il seme iniziale.

### Modulo Risorse di ATEC PM

È il **port del programma sopra** (Blazor → React, 30/06–01/07/2026): pagina `/risorse`, `/risorse/ferie`,
stesso dialogo, drag, conflitti, presenza online, hub `/hubs/resource-planner`, digest email (spento),
tabelle `res_*` **identiche** su MySQL, API `/api/resource-planner` **identica nella forma**.

Mancano in ATEC PM, e restano sul VPS: parco auto, app mobile e push, feed ICS, export attività,
filtro reparti nel planner, permesso `calendar.view_all`.

Aggancio da non dimenticare: il modulo **HR** (`HrAttendanceService.SyncToResourcePlanner`) quando
approva una ferie (`VACATION`) **scrive una riga FERIE in `res_assignments` con SQL diretto**
(senza `updated_at`, senza evento hub) e la cancella se la rifiuta.

## 2. I dati reali (letti in sola lettura il 02/09/2026)

| | ATEC PM (MySQL, LAN) | ATEC Risorse (SQLite, VPS) |
|---|---|---|
| Dipendenti | 38 (id 1–38) | 38 (id 1–38) |
| ↳ differenze | id 36 = Christian Monticone (esterno), id 38 = Alessandra Abatangelo (PM) | id 36 = **Alessandra Abatangelo** (RESP), id 38 = pasquale zamputo (cessato, solo VPS) |
| ↳ ruoli diversi | Maracich TECH, Chiantia TECH, M. Carretta RESP | Maracich RESP, Chiantia RESP, M. Carretta TECH |
| Reparti | 11 (PM…SRV) | 13 (+ MAG, MAN; MEC si chiama «Meccanico») |
| Commesse | 15 ACTIVE, 2 ON_HOLD, 2 COMPLETED (max id 47) | 2 demo (`C-2024-001/002`), nessuna allocazione le usa |
| Allocazioni | **7 di prova** (giugno 2026, tutte di admin, tutte scadute) | **182 vere**: 92 OP, 38 FLEX, 52 FERIE; dal 04/05/2026 al 23/02/2027; ultima modifica 01/09 |
| ↳ chi le scrive | — | Chiantia 52, Zanoni 45, Maracich 7, Diego 5, Abatangelo 5 |
| ↳ commessa agganciata | 0 | **0 su 182**: sono tutte a testo libero in `descrizione` («C260402_202 -OSVA UPGRADE…», «Manutenzione Minebea»…) |
| Service / Altre attività | 0 / 0 | 2 / 2 demo, mai usate |
| Digest email | non configurato, servizio spento | **attivo** (07:30, no weekend) |
| App mobile | — | 2 dispositivi registrati |
| Parco auto | — | 15 veicoli, 4 prenotazioni (ultima 13/07) |
| Ora in `updated_at` | **locale** (`NOW()`, Europe/Rome) | **UTC** (`datetime('now')`) |
| Schema | migrazioni v118 | — |

Conclusione: **il VPS è la verità sulle allocazioni**, ATEC PM lo è sulle anagrafiche (dipendenti,
reparti, commesse). Le 7 righe di prova in PM vanno buttate; i 2+2+2 demo sul VPS pure.

## 3. Perché un ponte e non «un solo database»

L'alternativa sarebbe far usare a tutti (web e app) il solo ATEC PM, esponendolo dal VPS con un
tunnel verso la LAN. Scartata per ora:

- il server LAN **si spegne di notte** per gli stacchi di corrente (28/08, 30/08, 02/09): l'app
  mobile, le push delle 08:00 e il digest delle 07:30 morirebbero con lui;
- parco auto, push, ICS ed export vivono sul VPS e in PM non esistono: sarebbe una riscrittura;
- il VPS non raggiunge la LAN, la LAN raggiunge il VPS in HTTPS: la direzione della rete è già decisa.

Quindi: **ogni programma continua a funzionare da solo**, e un motore in ATEC PM li tiene uguali.

## 4. Architettura del servizio

```
ATEC PM (LAN, servizio Windows)                     VPS (Internet)
┌─────────────────────────────────┐   HTTPS in uscita   ┌──────────────────────────┐
│ RisorseSyncService              │ ───────────────────▶ │ /api/sync/*  (nuovo)     │
│  • client SignalR verso il VPS  │ ◀─── WebSocket ───── │ /hubs/resource-planner   │
│  • innesco da ResourcesController│                     │  AssignmentsChanged      │
│  • timer di sicurezza 60 s      │                     │  EmployeesChanged        │
│  • mappa res_sync_map           │                     │ digest 07:30 · push 08:00│
│  • log res_sync_log             │                     │ app mobile · ICS · auto  │
└─────────────────────────────────┘                     └──────────────────────────┘
```

### 4.1 Il motore (ATEC PM) — `Services/RisorseSyncService.cs`

`BackgroundService` sul modello di `DaneaSyncService`. L'interruttore di hosting `Services:RisorseSync`
è **true** di default ma il motore resta a riposo (nessuna rete, hub chiuso) finché dalla scheda
«Sincronizzazione ATEC Risorse (VPS)» della pagina Digest Email non si accende `sync.enabled` con
indirizzo, utente e password. Le impostazioni vivono in `res_settings` (chiavi `sync.*`, password
**cifrata DPAPI** come quella SMTP) con ripiego sulla sezione `RisorseSync:*` della configurazione:
in produzione la password può quindi stare in `appsettings.Secrets.json` del server, senza passare
per nessuno.

**Tre inneschi, un solo giro alla volta** (le richieste arrivate durante un giro ne fanno partire uno
solo dopo):

1. **evento dal VPS** — il motore è collegato a `/hubs/resource-planner` del VPS come client SignalR
   (`Microsoft.AspNetCore.SignalR.Client` 8.0.11, con riconnessione automatica); a ogni
   `AssignmentsChanged` / `EmployeesChanged` parte un giro → una modifica fatta sul VPS è in PM in
   **1–2 secondi**;
2. **scrittura locale** — `ResourcesController` (POST/PUT/DELETE) chiama `_sync.Trigger()` dopo il
   successo → una modifica fatta in PM è sul VPS in **1–2 secondi**;
3. **timer 60 s** — rete di sicurezza: copre le scritture SQL dirette del modulo HR, i riavvii,
   le sconnessioni dell'hub.

**Un giro** fa, in ordine:

1. login sul VPS (JWT da 8 ore, rinnovato al primo 401);
2. **anagrafiche PM → VPS**: dipendenti, reparti + legami, commesse (upsert, mai cancellazioni);
3. **allocazioni nei due versi**: scarica tutte le righe del VPS, legge tutte quelle di MySQL
   (~200 righe per lato: un confronto completo costa niente e non ha bisogno di cursori né di
   tabelle di «lapidi»), confronta con la mappa e applica le differenze;
4. aggiorna mappa e log.

### 4.2 La mappa — `res_sync_map` (solo in PM, migrazione **M119**)

| colonna | note |
|---|---|
| `kind` | `EMPLOYEE` · `DEPARTMENT` · `PROJECT` · `ASSIGNMENT` |
| `local_id` / `remote_id` | id in PM / id sul VPS |
| `synced_hash` | impronta dei campi significativi all'ultimo allineamento |
| `synced_at` | quando |

L'impronta di un'allocazione = dipendente (**mappato**), tipo, data inizio, data fine, commessa
(**mappata**), descrizione (tagliata a 500 caratteri, il limite della colonna in PM, prima di
calcolarla). `updated_by` e `updated_at` non entrano nell'impronta: sono audit, servono solo a
decidere i conflitti. `updated_by` viaggia mappato attraverso `EMPLOYEE` (VPS 36 → PM 38, ecc.) e
diventa `null` se non abbinabile.

### 4.3 Regole di merge delle allocazioni

| Situazione | Cosa fa il motore |
|---|---|
| riga solo in PM, senza mappa | la crea sul VPS (il `POST /api/sync` ritorna l'id) e mappa |
| riga solo sul VPS, senza mappa | la crea in PM e mappa |
| mappa presente, riga sparita in PM | la cancella sul VPS, toglie la mappa |
| mappa presente, riga sparita sul VPS | la cancella in PM, toglie la mappa |
| impronta ≠ `synced_hash` su **un lato solo** | copia da quel lato all'altro |
| cambiata **su entrambi** | vince l'`updated_at` più recente, **normalizzato in UTC** (PM è ora locale, VPS è UTC); se un lato non ha `updated_at` vince l'altro; riga `CONFLITTO` nel log |
| cancellata da una parte e modificata dall'altra | **la cancellazione vince** (riga nel log) |
| dipendente non mappato (da un lato o dall'altro) | riga **saltata** e segnalata, mai cancellata, mai creata |
| commessa non mappata (riga PM su una commessa che il VPS non ha, o riga VPS su una commessa che PM non conosce) | riga **saltata** e segnalata; riparte da sola quando la Fase 1 mappa la commessa (mai azzerare la commessa in silenzio) |
| dipendente **cancellato** in PM (raro: di norma è TERMINATED) | le sue allocazioni sul VPS **restano** (segnalate come «dipendente non mappato»); le coppie orfane escono dalla mappa |
| scrittura in PM | `UPDATE`/`DELETE` solo se `updated_at` è ancora quello letto a inizio giro; altrimenti la riga si rimanda al giro dopo (l'utente che ha salvato nel frattempo non perde niente). Un conflitto si conta e si racconta («vince …») solo quando la scrittura è avvenuta davvero, su entrambi i versi |
| il VPS risponde **0 allocazioni** con ≥ 10 coppie mappate, oppure più di metà delle coppie sparite (minimo 10) | **freno**: il giro si ferma con errore visibile, niente cancellazioni di massa in PM; solo «Sincronizza adesso» dal pannello procede (l'operatore ha guardato) |
| riga MySQL rifiutata (dato fuori misura, vincolo) | solo quella riga viene saltata e segnalata (SAVEPOINT), il resto del giro passa |

Niente eco: dopo l'applicazione i due lati hanno la stessa impronta, il giro successivo non fa nulla;
l'evento hub che il VPS manda per le scritture del motore innesca solo un giro vuoto.

Le scritture del motore in MySQL passano per lo stesso `NotifyChange` dell'hub PM, così chi ha il
planner aperto vede la barra comparire come se l'avesse messa un collega.

### 4.4 Il VPS — nuovo `Controllers/SyncController.cs` (`/api/sync`, ruolo `SYNC` o ADMIN)

Account dedicato **`sync.pm`**, creato all'avvio dalla sezione `Sync` dell'`appsettings.json` del
server (username + password), con un **ruolo dedicato `SYNC`** (deciso in revisione, 02/09): il suo
token apre **solo** `/api/sync` e l'hub, non utenti, credenziali, SMTP o import. È escluso da lookup,
elenco Utenti, digest e presenza online (nome `[SYNC] ATEC PM`, il prefisso `[` lo nasconde ovunque).

| Endpoint | Note |
|---|---|
| `GET status` | ora UTC del server, versione, conteggi (per la prova di collegamento e il pannello) |
| `GET employees` · `GET projects` · `GET departments` | letture complete (Fase 1): tutti i dipendenti (mai l'hash della password), tutte le commesse, reparti e legami; servono al seme della mappa e alla diagnostica |
| `GET assignments` | tutte le righe grezze, `updated_at` UTC, nessun filtro di visibilità |
| `POST assignments` (lista) | upsert per id VPS: **senza il controllo «niente date nel passato»** del dialogo; scrive `updated_by`/`updated_at` **come ricevuti** (autore vero, mappato); risponde riga per riga `created` / `updated` / `unchanged` (riga identica: niente scritto, niente push) / `skipped` (dipendente, commessa, service o attività inesistenti sul VPS, date invertite); poi `NotifyAssignmentsChanged` + coda push → **le push e il digest partono anche per le modifiche fatte in PM** |
| `POST assignments/delete` (lista di id) | cancellazione in blocco, identica a `DeleteAssignment`: annota in `res_notify_pending`, push, realtime |
| `PUT employees` | upsert per id VPS di nome, cognome, email, `emp_type`, stato. **Non tocca** username, password, ruolo, `calendar_token` di chi c'è già; per i **nuovi** copia username e hash da PM (stesso login) con ruolo mai superiore a PM. Gli account di sistema (`admin`, `[SYNC]`) non si toccano. Mai cancellazioni. `EmployeesChanged` solo se qualcosa è cambiato (altrimenti i due hub si rimbalzerebbero a vicenda) |
| `PUT departments` | upsert **per codice** (gli id divergono: il VPS ha MAG e MAN in più); legami dipendente↔reparto sostituiti solo verso i reparti presenti nel payload (MAG e MAN restano intatti) |
| `PUT projects` | upsert per id con ripiego per codice (codice, titolo, stato) delle commesse ACTIVE; mai cancellazioni |

Le date-solo viaggiano come `DateOnly` (`yyyy-MM-dd`, niente fuso: un `DateTime` locale avrebbe
spostato le date di un giorno sul VPS che gira in UTC); gli istanti come UTC con la `Z`.
Ogni chiamata in una transazione SQLite; realtime e push dopo il commit, e un loro errore non cambia
l'esito (le righe sono già scritte: rispondere «errore» farebbe duplicare al retry).

## 5. Bootstrap (una tantum, prima di accendere)

1. **PM**: cancellare le 7 allocazioni di prova (giugno, scadute) — il VPS è la verità iniziale.
2. **VPS**: cancellare le 2 commesse demo, i 2 service e le 2 altre attività demo (nessuna riga li usa).
3. **Seme della mappa dipendenti**, automatico al primo giro: abbinamento per **nome + cognome**;
   copre 1–35 e 37 (stessi id) e sistema da solo l'inversione **36/38** (Abatangelo).
   Restano fuori e finiscono nel log: VPS 38 «zamputo» (solo VPS, cessato) e PM 36 «Monticone»
   (esterno, senza utente). Le allocazioni di un dipendente non abbinato si saltano, non si cancellano.
4. Reparti: per codice. Commesse: per id (PM comanda).
5. Primo giro reale: 182 allocazioni VPS → PM; 15 commesse ACTIVE, 38 dipendenti, 11 reparti PM → VPS.

## 6. Fasi di lavoro

| Fase | Contenuto | Verifica |
|---|---|---|
| **0 — Preparazione** ✅ **fatta il 02/09/2026** | VPS: `SyncController` + DTO del contratto, ruolo `SYNC`, account `sync.pm` dalla sezione `Sync` dell'appsettings di produzione (password solo lì), regole del planner condivise in `PlannerRules`, guardia sulla chiave JWT di sviluppo; **pubblicato alle 13:24**. PM: migrazione M119, `Services/RisorseSync/` (impostazioni DPAPI, client HTTP, motore con i tre inneschi che in Fase 0 fa solo login + stato), 5 endpoint `sync/*`, scheda «Sincronizzazione ATEC Risorse (VPS)» in Digest Email, 37 test; committato, **non ancora deployato** | VPS: login `sync.pm` dal PC, `GET /api/sync/status` → 39 dipendenti / 182 allocazioni / 2 commesse / 13 reparti, `GET /api/sync/assignments` → 182 righe con date `yyyy-MM-dd` e UTC con la Z, `/api/users` negato al ruolo SYNC (403); prova funzionale locale di tutti gli endpoint (created / unchanged / updated / skipped / deleted, account di sistema protetti); PM: build 0 errori, 530 test verdi, web `tsc -b` + eslint puliti |
| **1 — Anagrafiche PM → VPS + seme mappa** ✅ **fatta il 02/09/2026** | `RisorseSyncMap` + `AnagraficheSync` (logica pura: normalizzazione, abbinamento username → nome+cognome → cognome+token del nome solo se unico, impronte senza credenziali) + `SyncAnagraficheAsync` (seme, invio delle sole righe cambiate, invio completo ogni 24 h, credenziali solo ai nuovi, esterni e wildcard esclusi, righe rifiutate ricordate); VPS: `GET employees/projects/departments` **pubblicati alle 15:19**; PM committato, non deployato | 61 test nel filtro Risorse, suite 556 verdi; **prova end-to-end** su una copia del DB di produzione del VPS con il motore PM in locale: 27 abbinati (PM 38 → VPS 36 Abatangelo per nome, Obreja per token), 0 doppioni, 15 commesse create, MEC rinominato, legami MAG/MAN del VPS intatti, un solo eco dall'hub, timer silenzioso. 🪤 Il DB di **sviluppo** di PM aveva «Larganà»/«Qualità» in mojibake (`├á`): ripulito; la produzione è corretta (verificata in esadecimale) |
| **2 — Allocazioni bidirezionali in tempo reale** ✅ **fatta il 02/09/2026** | `AllocazioniSync` (logica pura: impronta senza audit, `Decidi` = tabella §4.3, abbinamento per contenuto, fusi Europe/Rome) + `SyncAllocazioniAsync` (una POST per creazioni+aggiornamenti, una per autore per le cancellazioni con l'autore da `res_notify_pending`, poi una transazione MySQL con guardia di concorrenza e SAVEPOINT per riga, mappa, hub PM notificato dopo il commit); freni anti-cancellazione di massa; fix HR in sovrapposizione con `updated_at`; 596 test in tutto (93 nel modulo). PM committato, **non deployato** | **Prova end-to-end** (VPS locale su copia del DB di produzione + motore PM locale): 182 righe copiate in PM con dipendente e commessa tradotti e `updated_at` in ora locale (12:11 UTC → 14:11); creazione in PM → sul VPS al timer, creazione sul VPS → in PM in 1 s via hub; conflitto → vince la modifica più recente (1 conflitto nel registro); cancellazione in PM → VPS con `made_by`, cancellazione sul VPS → PM; ferie senza autore come le scrive HR → VPS; alla fine 183 = 183, timer silenzioso, nessun errore |
| **3 — Stato e avvisi** ◐ in parte | endpoint `sync/status` e `run-now`, scheda in Digest Email e `GUIDA-SERVER-LAN.md` §5.1 **fatti**; **resta** l'avviso nel planner se il VPS non risponde da > 10 min | a vista |

Deploy: il VPS con `aggiorna-server-online.bat` (2–3 min, ~5 s di fermo, l'`appsettings.json` di
produzione non si tocca); ATEC PM con il consueto `aggiorna-server.ps1`. Stima: **3 sessioni** più i due deploy.

## 6-bis. Go-live — fatto il 02/09/2026 alle 16:49

1. Cancello dei test verde e registrato (596), copia `.backup` del DB del VPS
   (`backup/risorse-pre-sync-20260902.db`).
2. ATEC PM produzione: dump (nello scratch della sessione) e cancellazione delle 7 allocazioni
   di prova di giugno (id 10-19).
3. Password di `sync.pm` copiata dal VPS ai segreti cifrati del server PM
   (`RisorseSync:Password` in `appsettings.Secrets.json`, DPAPI macchina) e sezione `RisorseSync`
   in `appsettings.json` (`Enabled: true`, indirizzo, utente) con copie `.prima-sync-20260902`.
   Fatto via SSH con un file temporaneo, cancellato subito: la password non è mai passata in chat.
4. Deploy di ATEC PM (build `20260902-1647`, 40 s, migrazione v119 applicata, health 200).
5. **Primo giro alle 16:49:46 (958 ms)**: 27 dipendenti abbinati (PM 38 → VPS 36 Abatangelo per
   nome, Obreja per token), Monticone non abbinato (esterno), zamputo solo VPS; reparti inviati
   (MEC → «Officina Meccanica», MAG e MAN del VPS intatti); **15 commesse create sul VPS**;
   **182 allocazioni copiate in PM** con autore e ora locale; VPS intatto (182 righe, ultimo
   `updated_at` invariato). Un solo eco dall'hub, a vuoto.
6. Tolte dal VPS le 2 commesse demo (nessun riferimento). Service/altre attività demo lasciate:
   il VPS le riseminerebbe a ogni riavvio se la tabella fosse vuota, e nessuno le usa.

Da qui in poi i due programmi si tengono allineati da soli. Istruzioni operative in
`docs/guide/GUIDA-SERVER-LAN.md` §5.1.

## 7. Decisioni da confermare (col default proposto)

1. Verità iniziale sulle allocazioni = **VPS**; le 7 righe di prova in PM si buttano. → **sì**
2. Anagrafiche: **PM comanda**. Sul VPS non si creano più dipendenti, reparti o commesse a mano
   (ruoli, credenziali e permessi calendario restano gestiti lì). → **sì**
3. Credenziali: chi c'è già sul VPS non viene toccato; i nuovi arrivano con lo stesso login di PM. → **sì**
4. Commesse inviate al VPS: **solo ACTIVE** (come la tendina di PM), non ON_HOLD/COMPLETED. → **sì**
5. Conflitti: vince l'ultima modifica; la cancellazione vince sempre. → **sì**
6. Notifiche (digest email e push) restano sul VPS; il digest di PM resta spento (niente mail doppie). → **sì**
7. Le FERIE nate sul VPS entrano nel planner di PM ma **non** in HR (`hr_absences`): nessun
   automatismo verso EcosAgile, per ora. → **sì**

## 8. Trappole e rischi

- **Doppie ferie**: HR controlla solo date *identiche*; una ferie già pianificata sul VPS con date
  diverse genera una seconda riga FERIE → due barre in conflitto su entrambi i lati. Fix in Fase 2:
  `SyncToResourcePlanner` cerca la **sovrapposizione**, non l'uguaglianza.
- **Fusi orari**: VPS UTC, PM locale → normalizzare sempre prima di confrontare `updated_at`.
- **Id dipendenti**: non fidarsi degli id (36/38 già invertiti): passa tutto dalla mappa.
- **Descrizioni a testo libero**: sul VPS il dialogo nasconde la commessa; se in PM si aggancia una
  commessa, sul VPS la barra mostra «codice — titolo» grazie a `PlannerLogic.AssignmentLabel`
  (le commesse arrivano con le anagrafiche). Nessuna conversione automatica del testo in commessa.
- **Account `sync.pm`**: ADMIN sul VPS; se cambia la password o la chiave JWT del VPS il motore va in
  errore **visibile** nel pannello, non in silenzio.
- **VPS da 1 GB di RAM**: chiamate piccole (poche centinaia di righe), nessun carico.
- **Chi legge `res_assignments` in PM oltre al planner**: solo HR (ferie approvate). Nessun altro modulo.
