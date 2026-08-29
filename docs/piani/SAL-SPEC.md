> ⚠️ **SUPERSEDED (09/07/2026)**: il modello dati e le regole di questa spec (prototipo V31) sono stati
> estesi alla parità col prototipo `Gestione_Pagamenti_SAL_v10.html` — vedi **`SAL-V10-PLAN.md`**
> (fonte di verità attuale: campi IVA/saldo/pagamento, anagrafiche SAP/pagamenti, warning incasso,
> Prospetto con controllo periodico, Cash Flow + Analisi, migrazione schema v21).

# Istruzioni per Antigravity — Gestione SAL / Fatturazione a stati d'avanzamento

> Obiettivo: portare in `atec-pm-web` (React) + `ATEC.PM.Server` (ASP.NET) la **Gestione SAL**
> del prototipo `C:\Users\diego\Desktop\GESTIONALE\Gestione_Commesse_V31.html` (pagina «Pagamenti
> SAL» + «Warning Fatturazione» + «Prospetto SAL»). È un modulo **full-stack** (DB + API + web).
> Il controllo/verifica finale è a carico di chi commissiona (**Claude**): rispetta alla lettera
> i nomi di file, tabelle, endpoint ed eventi qui indicati, così la verifica è meccanica.

**Regole non negoziabili** (da `HANDOFF.md`): fedeltà ai blocchi shadcn (`BLOCKS-RULES.md`);
un file API per dominio in `src/lib/api/` con `apiGet/apiPost/... + unwrapApi`; DTO reali dal
controller; `tsc -b` + `eslint` puliti e `dotnet build` a 0 errori prima di chiudere; **conferme
azioni distruttive con `useConfirm`, MAI `window.confirm`**; multi-utente ⇒ realtime + `row_version`.

---

## 0. Cos'è la Gestione SAL (dal prototipo)

Piano di **fatturazione a stati d'avanzamento** per commessa. Ogni commessa ha `cliente` + `valore`
e una lista di **step di pagamento**; ogni step:

| campo | tipo | note |
|-------|------|------|
| `step` | testo | descrizione (es. «1° acconto all'ordine…») |
| `perc` | numero | % sul valore commessa (step 0,5) |
| `cond` | testo | condizione di pagamento (da anagrafica: «A Vista», «30 gg. dffm.»…) |
| `dataFatt` | data | «Ipotesi Fatturazione» (può essere vuota) |
| `stato` | enum | `''` / `emessa` / `pagata` |

- **Importo Fattura** = `valore × perc/100` → **DERIVATO**, non si memorizza.
- **Avanzamento Incasso** = Σ`perc`(righe `pagata`) / Σ`perc`(tutte) → barra %.
- **Warning Fatturazione** (semaforo su `dataFatt`+`stato`, solo righe con `stato=''`):
  - 🔴 **warn**: `dataFatt ≤ oggi` (ipotesi raggiunta/superata, fattura non emessa);
  - 🟡 **pre**: da **lunedì della settimana precedente** l'ipotesi;
  - altrimenti nessuna segnalazione.
- **Template standard** a 6 step (15/15/10/20/20/20) precaricabile.
- **Prospetto SAL**: per ogni commessa le **prime 2** ipotesi aperte (con data, `stato=''`),
  ordinate per data crescente.

### Decisione presa (data model)
Nel prototipo cliente/valore vivono in un registro SAL **indipendente**. Nel web la commessa
esiste già, quindi: **`cliente` e `valore` sono campi propri della SAL** (tabella header per
commessa), **pre-suggeriti** dai dati commessa se disponibili ma **editabili**. Niente
accoppiamento forte col Commerciale (non tutte le commesse hanno un preventivo convertito).
Se il committente preferirà agganciarli al preventivo, sarà una modifica successiva.

---

## 1. Backend — DB + API (`ATEC.PM.Server` / `ATEC.PM.Shared`)

### 1.1 Nuove tabelle (migrazione **schema v16**)

Crea `ATEC_PM\ATEC.PM.Server\Services\SalDbService.cs` sul modello **esatto** di
`Services\MilestonesDbService.cs` (metodo `InitTables(MySqlConnection c)` idempotente +
`SeedConditions(MySqlConnection c)` una-tantum).

```sql
-- Anagrafica condizioni di pagamento (globale, riusabile nei menu a tendina). Mirror di activity_catalog.
CREATE TABLE IF NOT EXISTS sal_conditions (
  id INT AUTO_INCREMENT PRIMARY KEY,
  label VARCHAR(200) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Header SAL per commessa: cliente + valore (1:1 con projects).
CREATE TABLE IF NOT EXISTS project_sal (
  project_id INT NOT NULL PRIMARY KEY,
  cliente VARCHAR(300) NOT NULL DEFAULT '',
  valore DECIMAL(14,2) NULL,
  row_version INT NOT NULL DEFAULT 0,
  updated_at DATETIME NULL,
  CONSTRAINT fk_psal_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Righe/step di pagamento SAL della commessa.
CREATE TABLE IF NOT EXISTS sal_rows (
  id INT AUTO_INCREMENT PRIMARY KEY,
  project_id INT NOT NULL,
  step VARCHAR(1000) NOT NULL DEFAULT '',
  perc DECIMAL(6,3) NULL,
  condizione VARCHAR(200) NOT NULL DEFAULT '',
  data_fatt DATE NULL,
  stato VARCHAR(10) NOT NULL DEFAULT '',   -- '' | emessa | pagata
  sort_order INT NOT NULL DEFAULT 0,
  row_version INT NOT NULL DEFAULT 0,
  created_by INT NULL,
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NULL,
  CONSTRAINT fk_salrow_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
  KEY idx_salrow_project (project_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

`SeedConditions`: se `sal_conditions` è vuota, inserisci in ordine
`['A Vista','30 gg. dffm.','60 gg. dffm.','90 gg. dffm.']`.

**Template step** (`SAL_TEMPLATE`, usato dal precarico opzionale): costante lato server/DTO con i 6
step del prototipo (descrizione, perc, cond) — vedi §0.

### 1.2 Aggancio migrazione (in `Services\DbService.cs`)

Segui **identico** al blocco `v15` (righe ~1428-1449):
1. Nel metodo `InitDatabase` dove sono elencati gli `InitTables` (righe ~964-982), aggiungi
   `new SalDbService(this).InitTables(c);`.
2. Alza `private const int LatestSchemaVersion = 15;` → **`16`** (riga ~995).
3. Aggiungi il blocco migrazione:
```csharp
// v16: modulo "SAL / Fatturazione" (sal_conditions + project_sal + sal_rows) + anagrafica condizioni.
if (currentVersion < 16)
{
    try
    {
        var sal = new SalDbService(this);
        sal.InitTables(c);
        sal.SeedConditions(c);
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.sal_condizioni', 'Condizioni Pagamento SAL', 'navigation', 2, 'HIDDEN')");
        c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (16, 'sal_conditions + project_sal + sal_rows + nav.sal_condizioni')");
        _logger.LogInformation("[Migration v16] Tabelle SAL + seed condizioni + feature key nav.sal_condizioni");
    }
    catch (Exception ex) { _logger.LogWarning("[Migration v16] Errore (non bloccante): {Message}", ex.Message); }
}
```

### 1.3 DTO — `ATEC.PM.Shared\DTOs\Sal_DTOs.cs`

Sul modello di `Milestones_DTOs.cs`. Includi almeno:
- `SalRowDto` { Id, ProjectId, Step, Perc(decimal?), Condizione, DataFatt(DateTime?), Stato, SortOrder, RowVersion }
- `SalHeaderDto` { ProjectId, Cliente, Valore(decimal?), RowVersion }
- `SalBundleDto` { Header: SalHeaderDto, Rows: List<SalRowDto> } (risposta GET singola)
- `SalHeaderSaveRequest` { Cliente, Valore, RowVersion(int?) }
- `SalRowSaveRequest` { Step, Perc, Condizione, DataFatt, Stato, RowVersion(int?) }
- `SalReorderRequest` { Ids: List<int> }
- `SalConditionDto` { Id, Label, SortOrder, IsActive } + `SalConditionSaveRequest` { Label }
- `SalProspettoRowDto` { ProjectId, Code, Cliente, Step, Perc, Condizione, DataFatt, Importo(decimal?), Ord(int), Alert(string:''/warn/pre) } (per la pagina globale Fase 3)

### 1.4 Controller — `Controllers\SalController.cs` (`[Route("api/sal")] [Authorize]`)

Clona la struttura di `MilestonesController` (iniezione `DbService _db` + `IHubContext<ProjectHub> _hub`,
`ApiResponse<T>`, `NotifyChanged`, concorrenza `row_version` con `ConflictMessage`). Endpoint:

| verbo | rotta | scopo |
|-------|-------|-------|
| GET | `/api/sal?projectId=` | `SalBundleDto` (header — creato al volo vuoto se assente — + righe ordinate) |
| PUT | `/api/sal/header?projectId=` | upsert header (cliente/valore), `row_version` |
| POST | `/api/sal/rows?projectId=` | nuova riga (sort_order in coda), ritorna id |
| PUT | `/api/sal/rows/{id}` | update riga con `row_version` (→ `ConflictMessage`) |
| DELETE | `/api/sal/rows/{id}` | elimina riga |
| POST | `/api/sal/rows/reorder?projectId=` | riordino (`SalReorderRequest`) |
| POST | `/api/sal/project/{projectId}/seed-template` | precarica i 6 step standard (solo se non ci sono righe) |
| GET | `/api/sal/conditions` | tutte le condizioni |
| GET | `/api/sal/conditions/active` | solo attive (per i menu) |
| POST/PUT/DELETE | `/api/sal/conditions[/{id}]` | CRUD anagrafica condizioni (add/rinomina/disattiva) |
| GET | `/api/sal/prospetto` | prime 2 ipotesi aperte per commessa (Fase 3) |

- **Realtime**: dopo ogni mutazione su header/righe chiama
  `_hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("SalChanged", new { action, projectId })`.
- **Importo/avanzamento** NON si calcolano qui (derivati client), tranne nel `/prospetto` dove serve `Importo`.
- `/prospetto`: per ogni commessa con righe `data_fatt IS NOT NULL AND stato=''`, prendi le prime 2 per
  `data_fatt` crescente; `Importo = valore*perc/100`; `Alert` calcolato in SQL con `CURDATE()` (warn se
  `data_fatt <= CURDATE()`, altrimenti pre/'' — vedi regola lunedì sett. prec. in §2.3, oppure lascia
  `pre` al client). JOIN `projects` per `code`, `project_sal` per `cliente`/`valore`.

### 1.5 Motore Warning → **campanella esistente** (`Services\NotificationService.cs`)

Aggiungi un metodo `CheckSalDeadlines()` al `NotificationBackgroundService` sul modello **esatto** di
`CheckProjectDeadlines()` / `CheckMoMDeadlines()` (righe ~517-577), e chiamalo nella lista di
`ExecuteAsync` (dopo `CheckProjectDeadlines()`):

- Sorgente: `sal_rows` con `data_fatt IS NOT NULL AND stato = ''`.
- `days = DATEDIFF(data_fatt, CURDATE())`; `ALARM` se `days < 0`, `WARNING` se `0 ≤ days ≤ @Warn`
  (usa `_warningDays`, come gli altri check — **uniformità col resto della campanella**; il semaforo
  giallo «lunedì sett. prec.» del prototipo resta solo nel colore riga del tab, §2.3).
- `notification_type='SAL_DUE'`, `reference_type='SAL_ROW'`, `reference_id=sal_rows.id`, `project_id`.
- Dedup: max un WARNING e un ALARM per riga **al giorno** (severità + `DATE(created_at)=CURDATE()`).
- Pulizia preventiva delle notifiche non più pertinenti (riga eliminata / `stato<>''` / `data_fatt` nulla
  / severità non più coerente) — copia il `DELETE ... JOIN` degli altri check.
- Destinatari: `notif.GetProjectPmIds(projectId)` (PM commessa + ADMIN).
- Titolo: `"Fatturazione SAL scaduta — {code}"` / `"Fatturazione SAL in scadenza — {code}"`;
  messaggio: `"{step} — {DueText(days)} ({data:dd/MM/yyyy}) — {importo €}"`.

---

## 2. Frontend — `atec-pm-web`

### 2.1 API + tipi
- `src/lib/api/sal.ts` — sul modello di `src/lib/api/milestones.ts`: `fetchSal(projectId)`,
  `saveSalHeader`, `createSalRow`, `updateSalRow`, `deleteSalRow`, `reorderSalRows`,
  `seedSalTemplate`, `fetchSalConditions(active?)`, CRUD condizioni, `fetchSalProspetto()`.
- `src/lib/api/types.ts` — aggiungi `SalRow`, `SalHeader`, `SalBundle`, `SalRowSaveRequest`,
  `SalHeaderSaveRequest`, `SalCondition`, `SalProspettoRow` (camelCase, `dataFatt: string|null` → usa
  `.slice(0,10)` come per le milestone; `valore`/`perc: number|null`).

### 2.2 Realtime
- `src/lib/signalr/use-sal-hub.ts` — **clone** di `use-milestones-hub.ts`, evento `"SalChanged"`,
  gruppo `project-{id}`, debounce 400 ms.

### 2.3 Tab nel dettaglio commessa
- `src/features/commesse/commessa-sections.ts` — aggiungi la sezione **dopo** `milestones`:
  `{ key: "sal", label: "SAL / Fatturazione", icon: "💶" }`.
- `src/features/commesse/CommessePage.tsx` — aggiungi `sal: "SAL / Fatturazione"` in `SECTION_TITLES`
  e il `case "sal": return <ProjectSal projectId={projectId} />` (vicino al case `milestones`, righe ~392-396).
- **Nuovo** `src/features/commesse/ProjectSal.tsx` — clona l'impianto di `ProjectMilestones.tsx`
  (React Query + `useSalHub` + autosave con mutation + invalidate). Contenuto:
  - **Testata**: `cliente` (input), `valore` (input € — riusa il pattern euro delle altre pagine),
    **barra «Avanzamento Incasso SAL»** (Σperc pagata / Σperc), pulsante «Precarica modello standard»
    (chiama `seedSalTemplate`, abilitato solo se 0 righe).
  - **Tabella editabile inline** (parti da un blocco tabella shadcn, come `milestone-table.tsx`):
    colonne `Step SAL | % SAL | Condizioni pagamento | Importo Fattura | Ipotesi Fatturazione | Stato`.
    - `Importo` = `valore*perc/100`, sola lettura, formato €.
    - `Condizioni`: `<Select>` con opzioni da `fetchSalConditions(true)` + regola «mantieni il valore
      corrente anche se non più attivo» (vedi `ddp-destination-options.ts` come riferimento) + voce
      «➕ Nuova condizione…» che apre la pagina/anagrafica condizioni.
    - `Ipotesi Fatturazione`: `DateField` standard con giorno-settimana/festivi (come le milestone).
    - `Stato`: `<Select>` `— / Fattura emessa / Fattura pagata`.
    - Riga: **drag&drop** riordino (`reorderSalRows`), «Aggiungi step», elimina riga **con `useConfirm`**
      solo se la riga contiene dati.
    - **Colore riga** (semaforo fedele al prototipo, calcolato client): `emessa`=giallo pastello,
      `pagata`=verde pastello; se `stato=''`: **warn** (rosso) se `dataFatt ≤ oggi`, **pre** (giallo) da
      **lunedì della settimana precedente** `dataFatt`. Metti l'helper `salAlertState(row, today)` +
      `mondayPrevWeek(d)` in `src/features/commesse/sal-utils.ts` (tenuità colori come Check list/Milestone).
  - Concorrenza: invia sempre `rowVersion`; su risposta di conflitto (messaggio server) mostra toast e
    ricarica (come le milestone).

### 2.4 Anagrafica condizioni (Gestione avanzata)
- Pagina `src/features/admin/sal/SalConditionsPage.tsx` — **clone funzionale** della pagina «Anagrafica
  attività» (`activity_catalog`): tabella con add / rinomina inline / disattiva / «Ripristina standard»,
  su `/api/sal/conditions`. Registra rotta in `src/app/AppRoutes.tsx` (`LIVE_ROUTES`) e voce nav
  `status:"live"` in `src/config/navigation.ts` (feature key `nav.sal_condizioni`, sotto Gestione avanzata).

### 2.5 (Fase 3, opzionale) Pagina globale «Prospetto SAL»
- `src/features/sal/SalProspettoPage.tsx` con **`PmSidebar`** (`src/components/shared/pm-sidebar.tsx`,
  vedi come la usa `MilestonesPage.tsx`): contenitori = commesse con righe SAL aperte, pallini di stato
  (rosso=warn, giallo=pre, neutro=in programma), tabella prime-2-ipotesi ordinabile e colorata.
  Rotta `/sal`, voce nav PM, endpoint `/api/sal/prospetto`. La vista «Warning Fatturazione» del prototipo
  NON va rifatta: quegli avvisi sono già nella campanella (§1.5).

---

## 3. Ordine di lavoro consigliato
1. **F1 — Backend**: `SalDbService` + migrazione v16 + `Sal_DTOs` + `SalController` (senza `/prospetto`).
   Verifica: `dotnet build` OK, avvio server → migrazione v16 applicata, endpoint testabili con JWT.
2. **F2 — Web tab**: `sal.ts` + tipi + `use-sal-hub.ts` + sezione + `ProjectSal.tsx` + `sal-utils.ts`.
   Verifica: `tsc -b` + `eslint` puliti; CRUD righe, importo/avanzamento, semaforo, realtime.
3. **F3 — Contorno**: anagrafica condizioni (pagina + nav) + `CheckSalDeadlines` nella campanella +
   (opz.) pagina globale «Prospetto SAL».

Aggiorna `docs/HANDOFF-WEB.md` a fine lavoro.

---

## 4. Checklist di verifica — ✅ VERIFICATA (Claude, 07/07/2026)

> **Esito: implementazione conforme alla spec, completa, che compila pulita.** Verifica **statica**
> (lettura del codice) + **build**: `dotnet build` (server) → exit 0 · `tsc -b` → 0 · `eslint` (file SAL) → 0.
> **Non ancora eseguita** la verifica runtime GUI (accendere server+Vite e pilotare la pagina) — unico tassello residuo.
>
> **Correzioni cosmetiche applicate da Claude in `ProjectSal.tsx`** dopo la verifica:
> 1. Avanzamento incasso ora a **1 decimale, virgola italiana** (helper `fmtPct`, applicato a % e a «(x% di y%)»; barra clampata a `min(100,…)`) — fedele al `fmtProg` del prototipo.
> 2. Importo con `valore` o `perc == 0`: la guardia `header?.valore && row.perc` (che scartava lo 0) → `header && header.valore != null && row.perc != null`, così `0%`/valore 0 mostra `0,00 €` invece di `—`.
>
> **Nota #1 ritirata (non era un difetto):** `sal`, non essendo in `SECTION_TITLES`, attiva `showCommessaHeader`
> e mostra l'**header ricco della commessa** (codice/titolo), esattamente come `milestones`. È voluto. Nessuna modifica.

**DB / migrazione**
- [x] `LatestSchemaVersion == 16`; blocco `if (currentVersion < 16)` + `INSERT ... schema_migrations (16, ...)` + feature key `nav.sal_condizioni`.
- [x] `SalDbService.InitTables` crea `sal_conditions`, `project_sal`, `sal_rows` con FK `ON DELETE CASCADE` su `projects`.
- [x] `SeedConditions` idempotente (`if count>0 return`) con le 4 condizioni standard.
- [x] `new SalDbService(this).InitTables(c)` presente nella lista InitTables di `DbService`.

**API (contratto)**
- [x] `SalController` `[Route("api/sal")] [Authorize]`; tutti gli endpoint di §1.4 (+ extra: conditions `toggle-active`/`reorder`/`reset`), firme coerenti ai DTO.
- [x] `PUT /rows/{id}` e `PUT /header` applicano `row_version` e ritornano `ConflictMessage` sul conflitto.
- [x] Ogni mutazione (header/righe/reorder/seed) emette `SalChanged` sul gruppo `project-{id}`.
- [x] `seed-template` inserisce i 6 step SAL (15/15/10/20/20/20) solo se la commessa non ha righe.
- [x] `importo`/`avanzamento` NON persistiti (derivati); `/prospetto` calcola `importo` + prime-2 per data (`ROW_NUMBER`) + alert warn/pre in SQL.

**Campanella**
- [x] `CheckSalDeadlines()` aggiunto e chiamato in `ExecuteAsync`; `SAL_DUE`/`SAL_ROW`; ALARM/WARNING per `DATEDIFF`; dedup giornaliera; pulizia righe risolte; destinatari `GetProjectPmIds`; importo nel messaggio.

**Web**
- [x] `sal.ts` usa `apiGet/... + unwrapApi`; tipi in `types.ts`; nessun `any`.
- [x] Sezione «SAL / Fatturazione» nell'albero commessa + `case "sal"` in `CommessePage`. (Titolo header = header commessa via `showCommessaHeader`, come `milestones` — vedi nota #1.)
- [x] `ProjectSal.tsx`: tabella inline, importo derivato, barra avanzamento, drag riordino, add/seed, **elimina con `useConfirm`** (mai `window.confirm`), select condizioni con «mantieni valore corrente» + «➕ Nuova condizione…», `DateField` standard.
- [x] Semaforo riga fedele al prototipo (warn `dataFatt≤oggi`, pre da lunedì sett. prec.) in `sal-utils.ts`.
- [x] `use-sal-hub.ts` sottoscrive `SalChanged`; il tab ricarica su evento realtime.
- [x] Anagrafica condizioni: pagina completa + rotta in `LIVE_ROUTES` + nav `status:"live"` (feature key `nav.sal_condizioni`).
- [x] `tsc -b` e `eslint` puliti. *(`npm run build`/vite non eseguito: coperto da tsc+eslint.)*

**Regressioni**
- [x] Migrazioni v1–v15 intatte; v16 solo additiva; nessuna modifica a tabelle esistenti; altre sezioni commessa invariate.

**Residuo**
- [ ] Verifica runtime GUI (server+Vite, CRUD/semaforo/realtime) — da fare su richiesta (poi spegnere i server).
