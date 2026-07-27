# SAL-V10-PLAN — Estensione modulo SAL / Fatturazione a parità col prototipo `Gestione_Pagamenti_SAL_v10.html`

> **Fonte requisiti**: prototipo `C:\Users\diego\Desktop\GESTIONALE\Gestione_Pagamenti_SAL_v10.html` (analizzato
> integralmente il 09/07/2026) + vocale di Diego del 09/07/2026. Il modulo SAL esistente (SAL-SPEC.md /
> SAL-PAGE-SPEC.md, migrazioni v16–v18) è stato costruito dal prototipo **V31**, che è un sottoinsieme:
> questo piano lo porta alla parità **v10**. Requisito chiave dal vocale: *«deve lavorare in rete»* →
> tutto su DB condiviso + realtime, che è esattamente l'architettura già in piedi.
>
> **Contesto d'uso** (dal vocale): Diego compila il piano (step, %, condizioni, ipotesi fatturazione,
> gg saldo); Marco Carretta fa la gestione quotidiana (stato fattura, pagamento, data incasso, note).
> La tabella replica l'**Excel che Marco usa da 5 anni: la struttura NON si cambia**.

---

## 0. Stato attuale vs target (gap analysis)

### Già pronto (da NON rifare)
| Cosa | Dove |
|---|---|
| Tabelle `sal_conditions` / `project_sal` (cliente, valore) / `sal_rows` (step, perc, condizione, data_fatt, stato, sort_order, row_version, paid_by/paid_at) | `SalDbService.cs`, migrazioni v16–v18 |
| `SalController` completo: bundle, header, rows CRUD+reorder, seed-template 6 step, conditions CRUD, `/prospetto`, `/summary` | `Controllers/SalController.cs` |
| Realtime `SalChanged` (gruppo `project-{id}`) + `GlobalSalChanged` + concorrenza `row_version` | `SalController.cs`, `use-sal-hub.ts` |
| Tab commessa `ProjectSal` (step editabili, % con banner ≠100, DateField ipotesi, stato 3 valori, drag&drop, avanzamento incasso, lock pagata/ADMIN, seed template) | `features/commesse/ProjectSal.tsx` |
| Pagina `/sal` con `PmSidebar` (dots warn/pre) + `SalProspettoView` (badge, CSV `;`+BOM, stampa) + `/admin/sal-conditions` | `features/sal/*`, `features/admin/sal/*` |
| Warning campanella `SAL_DUE` (`CheckSalDeadlines`) + sorgente SAL in `/api/deadlines` + pagina `/scadenze` | `NotificationService.cs` r.582+, `DeadlinesController.cs` |
| Pattern riusabili: foglio MoM (autosave/debounce/flush/drag/focus), `DateField`, `useConfirm`, `euro()`, recharts `ComposedChart` (ProjectCashFlow), export/print client-side (mom-export, SalProspettoView) | vedi §6 |

### Mancante (tutto il delta v10 — da costruire)
1. Colonne riga: **%IVA, IVA, Tot+IVA, GG saldo, Data prevista saldo, N° fattura, Conto SAP, Pagamento, Data incasso, Note**
2. Separazione **Stato fatturazione** (`''`/`daEmettere`/`emessa`) ↔ **Pagamento/incasso** (oggi fusi in un solo `stato` con `'pagata'`)
3. Anagrafiche **Causali Conto SAP** (seed: Acconto, Ricavo) e **Stati Pagamento** (seed: Pagata, Parzialmente Pagata)
4. Header: **PO - Ordine cliente** e **Riferimento Offerta ATEC**
5. **Warning incasso fattura** (oltre data prevista saldo) — campanella + scadenze
6. **Prospetto v10**: tutte le righe aperte + emesse non incassate (oggi max 2 per commessa), colonna Data prevista saldo, pill «Emessa – attesa incasso», **controllo periodico 15 giorni** con banner e «Conferma controllo»
7. **Cash Flow SAL**: 5 totali (Ordini / Incassate / Emesse / da Fatturare / Avere, netto e con IVA) + **Analisi**: grafico mensile a barre impilate + linea «Incasso previsto» + **drill-down** cliccabile + stampa

### Fuori scope (deliberato)
- Trasferte / Bilancio / calcolatori del prototipo v10 → coperti da moduli ATEC PM esistenti (Prev vs Consuntivo, Timesheet, Risorse).
- Planner milestone/Gantt del prototipo → già modulo Milestone.
- Import CSV/XLSX e backup localStorage → non hanno senso su DB condiviso.
- `ProjectCashFlow` (tab «Flusso di Cassa» per commessa) resta com'è: è un modulo diverso, non SAL-based.

---

## 1. Decisioni di design (con raccomandazione)

| # | Decisione | Scelta raccomandata | Motivo |
|---|---|---|---|
| D1 | Ordine colonne tabella step | **Esatto come v10/Excel di Marco**: IVA · %IVA · Tot+IVA · Data prev. saldo · GG saldo · Step · N° Fattura · Conto SAP · %SAL · Cond. pagamento · Importo · Ipotesi Fatt. · Stato · Pagamento · Data incasso · Note | Vocale: «la struttura resta quella» |
| D2 | Terzo stato fatturazione `daEmettere` | **Sì**, enum `''`/`daEmettere`/`emessa` (VARCHAR(10) esistente lo contiene) | Parità v10; il warning scatta per tutto ciò che NON è `emessa` |
| D3 | Campo Pagamento | **Testo da anagrafica** `sal_payment_states` (semantica cablata su `Pagata`/`Parzialmente Pagata` per colori/regole, altri valori neutri) | Parità v10 + flessibilità richiesta nel vocale |
| D4 | Data prevista saldo | **Derivata, mai persistita**: `data_fatt + gg_saldo` (client) / `DATE_ADD(data_fatt, INTERVAL gg_saldo DAY)` (SQL per warning/prospetto) | Regola repo «non persistere derivati» |
| D5 | Migrazione righe `stato='pagata'` esistenti | `stato='emessa'` + `pagamento='Pagata'` (preservando `paid_by`/`paid_at`) | Stessa migrazione che fa il prototipo (v3 interna) |
| D6 | Lock riga non-ADMIN | Passa da `stato='pagata'` a **`pagamento='Pagata'`**; transizione a Pagata setta `paid_by`/`paid_at` | Coerente col nuovo modello |
| D7 | Anagrafiche admin | **Una pagina a 3 tab** (`/admin/sal-conditions`: Condizioni pagamento · Causali SAP · Stati Pagamento), stessa feature key `nav.sal_condizioni` | Meno rotte, pattern identico |
| D8 | Cash Flow SAL | **Quick view dentro `/sal`** (`Cash Flow` + `Analisi`) + rotta figlia `/sal/cashflow`, niente nuova voce nav top-level | La pagina SAL è già il contenitore; evita confusione con «Flusso di Cassa» commessa |
| D9 | Controllo periodico prospetto (15 gg) | Nuova tabella `sal_prospetto_checks` (storico: chi+quando), stato = ultima riga | Multi-utente: il singolo ymd del prototipo non basta, serve audit |
| D10 | Prospetto: quante righe per commessa | **Tutte** (parità v10), non più «prime 2» | Il prospetto è la vista di controllo completa |
| D11 | Warning incasso | Solo **ALARM** dal giorno dopo la data prevista saldo (nessun pre-warning), `pagamento <> 'Pagata'`, **indipendente dallo stato fatturazione** | Regola esatta v10 (`salIncassoState`) |
| D12 | Formattazioni | Uniformare il modulo a `euro()` (`lib/format.ts`) e `formatDateShort` (`lib/date-iso.ts`) | Regole repo (oggi ProjectSal usa `toLocaleString currency`) |

---

## 2. FASE 1 — Server: schema v21 + API estese

**Obiettivo**: DB e contratti pronti per tutti i campi v10. Nessun cambio visibile lato web (il client vecchio continua a funzionare: campi nuovi opzionali).

### 2.1 Migrazione schema **v21** (`DbService.cs`, blocco dopo v20 ~r.1607, bump `LatestSchemaVersion = 21` r.1002)
Idempotente (check `information_schema` prima di ogni ALTER), try/catch non bloccante, `INSERT IGNORE` finale in `schema_migrations`:

```sql
-- sal_rows: nuovi campi v10
ALTER TABLE sal_rows
  ADD COLUMN iva_perc INT NULL,                      -- %IVA intera (default UI 22 su riga nuova)
  ADD COLUMN gg_saldo INT NULL,                      -- GG saldo fattura
  ADD COLUMN n_fatt VARCHAR(50) NOT NULL DEFAULT '', -- N° fattura (solo cifre, stringa per zeri iniziali)
  ADD COLUMN conto_sap VARCHAR(200) NOT NULL DEFAULT '',
  ADD COLUMN pagamento VARCHAR(100) NOT NULL DEFAULT '',
  ADD COLUMN data_pagamento DATE NULL,               -- Data incasso
  ADD COLUMN note VARCHAR(2000) NOT NULL DEFAULT '';
ALTER TABLE sal_rows ADD INDEX idx_salrow_pag_saldo (pagamento, data_fatt);

-- project_sal: header completo
ALTER TABLE project_sal
  ADD COLUMN po VARCHAR(150) NOT NULL DEFAULT '',
  ADD COLUMN rif_offerta VARCHAR(200) NOT NULL DEFAULT '';

-- nuove anagrafiche (clone sal_conditions)
CREATE TABLE IF NOT EXISTS sal_sap_causali (
  id INT AUTO_INCREMENT PRIMARY KEY, label VARCHAR(200) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0, is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- seed se vuota: 'Acconto', 'Ricavo'
CREATE TABLE IF NOT EXISTS sal_payment_states (…identica…);
-- seed se vuota: 'Pagata', 'Parzialmente Pagata'

-- controllo periodico prospetto
CREATE TABLE IF NOT EXISTS sal_prospetto_checks (
  id INT AUTO_INCREMENT PRIMARY KEY,
  checked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  checked_by INT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- migrazione dati: stato legacy 'pagata' → emessa + Pagata (paid_by/paid_at restano)
UPDATE sal_rows SET pagamento='Pagata', stato='emessa' WHERE stato='pagata';
```
Aggiornare **anche** `SalDbService.InitTables` + `SeedConditions` (percorso DEVELOPMENT) con le stesse strutture/seed.

### 2.2 DTO (`Sal_DTOs.cs` + mirror `types.ts`)
- `SalRowDto` / `SalRowSaveRequest` += `IvaPerc(int?), GgSaldo(int?), NFatt(string), ContoSap(string), Pagamento(string), DataPagamento(DateTime?), Note(string)`
- `SalHeaderDto` / `SalHeaderSaveRequest` += `Po(string), RifOfferta(string)`
- Nuovi: `SalSapCausaleDto`/`SalPaymentStateDto` (= shape `SalConditionDto`), `SalProspettoCheckDto { CheckedAt, CheckedByName, Days, Due, NextDue }`
- `SalProspettoRowDto` += `GgSaldo, DataSaldo(DateTime?), Stato, Pagamento, Alert` esteso (`warn|pre|incasso|attesa|plan`)
- `SalSummaryDto` += `Incasso(int)` (conteggio fatture con incasso scaduto)
- Nuovo `SalEconomicsRowDto { ProjectId, Code, Cliente, Step, Perc, Importo, IvaPerc, Iva, TotIva, Condizione, DataFatt, GgSaldo, DataSaldo, Stato, Pagamento }` (base per Cash Flow/Analisi/drill-down, calcolo in SQL)

### 2.3 `SalController` — modifiche/aggiunte
| Endpoint | Cosa |
|---|---|
| `PUT header` / `POST rows` / `PUT rows/{id}` | Accettano i nuovi campi; `n_fatt` sanificato server-side (`\D`→''); regola lock: se riga corrente ha `pagamento='Pagata'` e ruolo ≠ ADMIN → Fail; transizione a `Pagata` → set `paid_by/paid_at`, uscita da `Pagata` (solo ADMIN) → azzera |
| `DELETE rows/{id}` | **Fix**: aggiungere check `row_version` (oggi assente) + stessa regola Pagata/ADMIN |
| `GET/POST/PUT/DELETE /sap-causali[...]` e `/payment-states[...]` | CRUD + `toggle-active` + `reorder` + `reset` — clone esatto di `/conditions` |
| `GET /prospetto` | Regola inclusione v10: `data_fatt IS NOT NULL AND (stato <> 'emessa' OR (pagamento <> 'Pagata' AND gg_saldo IS NOT NULL))`; `DataSaldo = DATE_ADD(data_fatt, INTERVAL gg_saldo DAY)`; alert calcolato in SQL: `incasso` (saldo scaduto) > `warn` > `pre` > `attesa` (emessa in attesa) > `plan`; **tutte le righe** (via da LIMIT 2) |
| `GET /prospetto/check` + `POST /prospetto/check` | Stato controllo periodico (ultima riga di `sal_prospetto_checks`, `due = days >= 15`) + conferma (insert con employeeId) |
| `GET /economics` | Tutte le righe SAL con campi calcolati (per Cash Flow + Analisi + drill-down); **solo PM/ADMIN** (403 altrimenti) |
| `GET /summary` | += conteggio `incasso` per i dots sidebar |

Realtime: invariato (`SalChanged`/`GlobalSalChanged` dopo ogni mutazione; aggiungere `Notify` anche alle nuove anagrafiche è facoltativo, le esistenti non lo fanno).

**Verifica Fase 1**: `dotnet build` 0 errori; avvio server → v21 applicata su DB dev; smoke test API via curl/JWT (bundle con campi nuovi, migrazione `pagata` verificata con una riga di test).

---

## 3. FASE 2 — Notifiche + Scadenze (warning incasso)

1. **`CheckSalDeadlines`** (fatturazione): filtro da `sr.stato = ''` a **`sr.stato <> 'emessa'`** (il `daEmettere` deve continuare ad allertare — parità v10); pulizia coerente.
2. **Nuovo `CheckSalIncassoDeadlines`** (template = CheckSalDeadlines):
   - Selezione: `data_fatt IS NOT NULL AND gg_saldo IS NOT NULL AND pagamento <> 'Pagata' AND DATE_ADD(data_fatt, INTERVAL gg_saldo DAY) < CURDATE()` → sempre **ALARM** (v10: scatta dal giorno dopo, nessuna fascia warning).
   - `notif.Create("SAL_INCASSO_DUE", 'ALARM', "Incasso fattura scaduto — {code}", "{step} — saldo previsto {dataSaldo dd/MM/yyyy}[ — importo €]", "SAL_ROW", id, projectId, null, GetProjectPmIds(projectId))`.
   - Pulizia: rimuovi notifiche se riga pagata/eliminata/senza date.
   - Registrare nel loop `ExecuteAsync` dopo `CheckSalDeadlines`.
3. **`DeadlinesController`**: 6ª sorgente UNION ALL `Type='SAL_INCASSO'`, `RefType='SAL_ROW'`, `DueDate = DATE_ADD(data_fatt, INTERVAL gg_saldo DAY)`, stesso filtro del check.
4. **Web**: union `Deadline.type` in `types.ts`; `ScadenzePage` filtro+etichetta «Incasso SAL»; campanella: nessun cambio (`*_DUE` → icona Clock automatica); `notification-navigation`: nessun cambio (`SAL_ROW` → sezione `sal` già mappato).

**Verifica**: build + tsc/eslint; query di selezione provata a mano su dati di test.

---

## 4. FASE 3 — Web: tab `ProjectSal` a parità Excel v10

**File principali**: `features/commesse/ProjectSal.tsx` (riscrittura sostanziale), `features/commesse/sal-utils.ts`, `lib/api/sal.ts`, `lib/api/types.ts`, `features/admin/sal/SalConditionsPage.tsx` (→ 3 tab).

1. **Header card**: Cliente · N° Commessa (code, read-only) · Descrizione (title, read-only) · Importo Ordine (`valore`) · **PO - Ordine** · **Riferimento Offerta** (editabili, autosave header con rowVersion). Barra «Avanzamento Incasso SAL» = `Σ perc(pagamento='Pagata') / Σ perc(tutte) × 100` (non più su `stato`).
2. **Tabella 16 colonne nell'ordine v10 (D1)**. Derivate client: `importo = valore×perc/100`, `iva = importo×ivaPerc/100`, `totIva`, `dataSaldo = data_fatt+ggSaldo` (mostrata `formatDateShort` + dow). Editabili: %IVA (int, placeholder 22, default 22 su riga nuova), GG saldo (int ≥0), Step, N° Fattura (input filtrato solo cifre), Conto SAP (Select da anagrafica + «➕ Gestisci»), %SAL, Condizione (Select esistente), Ipotesi Fatturazione (`DateField`), Stato (Select: — / Da emettere / Emessa), Pagamento (Select da anagrafica: Nessuna / Pagata / Parz. Pagata / …), Data incasso (`DateField`), Note (GrowTextarea).
3. **Colori riga** (`sal-utils.ts`): priorità **Pagamento > Stato**: `Pagata`→verde, `Parzialmente Pagata`→rosso, altrimenti `emessa`→giallo; semaforo warn/pre resta sulla cella/pill segnalazione (regola v10: alert esclusa solo se `emessa`).
4. **Footer totali**: Σ% (verde solo se = 100,00 esatto, altrimenti ambra — già c'è il banner), Σ Importo, Σ IVA, Σ Tot+IVA (`euro()`; visibili solo `canSeeEconomics`).
5. **Select con valori storici**: se il valore corrente non è più in anagrafica → option extra «(non in anagrafica)» preservata (pattern v10/`personaOptionsHtml`).
6. **Lock**: riga con `pagamento='Pagata'` bloccata per non-ADMIN (icona Lock, come oggi con `pagata`).
7. **Anagrafiche admin**: `SalConditionsPage` → **Tabs** «Condizioni pagamento · Causali SAP · Stati Pagamento» (stesso CRUD/reorder/reset per le 3 liste, API nuove); i Select del foglio linkano al tab giusto.
8. **Uniformare** `euro()` + `formatDateShort` in tutto il modulo (anche `SalProspettoView`).
9. Autosave/rowVersion/realtime/drag&drop: pattern esistente invariato (payload esteso).

**Verifica**: tsc/eslint puliti + `dotnet build`.

---

## 5. FASE 4 — Prospetto v10 + controllo periodico 15 giorni

**File**: `features/sal/SalProspettoView.tsx`, `SalPage.tsx`, `lib/api/sal.ts`.

1. Colonne: Segnalazione · Commessa · Cliente · Step SAL · % · Condizioni · Importo · Ipotesi Fatturazione · **Data Prevista Saldo**. Pill estese: 🔴 Scaduto / 🔴 Fattura no incasso / 🟡 Pre-warning / 🔵 Emessa – attesa incasso / ⚪ In programma. Righe colorate coerenti (rosa/giallo).
2. Dati dal `/prospetto` esteso (tutte le righe, regola inclusione v10); ordinamento client su ogni colonna (già c'è, aggiungere `dataSaldo`); default = Ipotesi Fatturazione crescente.
3. **Banner controllo periodico**: da `GET /api/sal/prospetto/check` — rosso se `due` («Warning — Allinea Gestione Commesse. Sono trascorsi N giorni…», bottone primario «Conferma controllo» → `POST` con `useConfirm`? no, azione non distruttiva → diretto) / verde se ok («Ultimo controllo il … di <nome>. Prossimo avviso il …»).
4. Sommario contatori: monitorate / scadute fatturazione / pre-warning / non incassate / emesse in attesa.
5. CSV + stampa aggiornati alle nuove colonne/pill (pattern esistente).
6. **Sidebar `/sal`**: `salSummaryDots` += pallino «incasso scaduto»; count quick view Prospetto += incasso.

**Verifica**: tsc/eslint + build.

---

## 6. FASE 5 — Cash Flow SAL + Analisi (grafico drill-down)

**File nuovi**: `features/sal/SalCashFlowView.tsx`, `features/sal/SalAnalisiView.tsx` (+ eventuale `sal-economics.ts` helper puro con i bucket — testabile). Dati: `GET /api/sal/economics` (righe già calcolate), aggregazioni client.

1. **Classificazione a 3 stati esclusivi** (regola v10): `Pagata` → Incassate; `emessa` e non pagata → Emesse; nessuno stato e nessun pagamento → da Fatturare (`daEmettere` non conta in nessun bucket — parità v10).
2. **Cash Flow** (quick view in `/sal`): 5 card — Totale Ordini commesse (Σ `valore`), Incassate, Emesse, da Fatturare, **Avere = Emesse + da Fatturare** — ciascuna **Netto** e **Con IVA** (`euro()`). Ordini con IVA = Σ valore + Σ IVA di tutte le righe. Stampa = `window.print()` con CSS print.
3. **Analisi** (seconda quick view o toggle dentro Cash Flow): serie mensile continua per **mese di Ipotesi Fatturazione** → `ComposedChart` recharts con **`Bar stackId`**: Incassate `#3FA45E` / Emesse ambra / da Fatturare grigio, etichetta totale sopra la barra + **`Line` blu «Incasso previsto»** per **mese di Data Prevista Saldo** (tutte le righe non pagate). Pattern base già in `ProjectCashFlow.tsx` r.418-428 (manca solo `stackId` + onClick).
4. **Drill-down**: `onClick` su segmento Bar (`data-kind` inc/em/daf), sull'etichetta totale (kind=bar) e sui punti Line (kind=prev) → **Dialog** con tabella righe filtrate per mese/categoria (colonne come Prospetto + pill stato, totale imponibile, nota «Collocazione per Ipotesi Fatturazione / Data Prevista Saldo»). Ordinamento: prev→dataSaldo, altrimenti dataFatt.
5. Stampa Analisi (finestra dedicata, pattern mom-export/printGantt).
6. Tutto gated `canSeeEconomics` (PM/ADMIN); l'endpoint già fa 403.

**Verifica**: tsc/eslint + build; unit-check dei bucket con dataset sintetico (vitest se presente, altrimenti verifica manuale della funzione pura via node).

---

## 7. FASE 6 — Chiusura

1. Aggiornare **`WEB-MIGRATION.md`** (riga SAL) + **`HANDOFF.md`** (blocco SAL) + questo file (stato fasi).
2. `SAL-SPEC.md`: nota in testa «superseded da SAL-V10-PLAN.md per il modello esteso».
3. Verifica finale: `dotnet build` + `tsc -b` + `eslint` puliti. **Runtime GUI solo su richiesta esplicita** (regola di lavoro).
4. Comunicare all'utente le decisioni D1–D12 applicate e i punti aperti.

---

## 8. Esecuzione multi-agente (come procediamo)

Una fase alla volta, con questo giro per ciascuna fase:
1. **Implementazione**: 1–3 agenti in parallelo su file disgiunti (es. Fase 1: agente A = migrazione+SalDbService, agente B = DTO+Controller; Fase 3: A = ProjectSal, B = anagrafiche admin+API client).
2. **Verifica meccanica**: `dotnet build` + `tsc -b` + `eslint` (agente o main loop).
3. **Code review avversariale**: agenti verificatori sul diff (correttezza SQL migrazione, regole v10, row_version, ruoli) prima di dichiarare la fase chiusa.
4. Aggiornamento di questo file (checkbox stato) e go dell'utente per la fase successiva.

### Stato fasi
- [x] **Fase 1** — Server: schema v21 + API estese — **FATTA 09/07/2026** (build 0 errori; review avversariale a 3 lenti + giro di fix). Note di contratto emerse, vincolanti per le fasi successive:
  - **Null-preserve**: in `SalRowSaveRequest` i campi `nFatt/contoSap/pagamento/note` e in `SalHeaderSaveRequest` `po/rifOfferta` sono nullable con semantica *null = non modificare, "" = svuota* (protegge i client pre-Fase 3). **Il client Fase 3 deve SEMPRE inviarli esplicitamente** (stringa, anche vuota).
  - **`GET /api/sal/economics`** ritorna `SalEconomicsDto { headers[], rows[] }` (headers = tutti i `project_sal` di commesse ACTIVE anche senza righe, per il totale Ordini; rows filtrate su ACTIVE, coerenza col Prospetto); non-PM/ADMIN → **HTTP 403**.
  - `stato` legacy `'pagata'` in ingresso (POST e PUT) viene mappato server-side in `emessa` + `pagamento='Pagata'`; confronti su 'Pagata' case-insensitive; voci `Pagata`/`Parzialmente Pagata` di `sal_payment_states` sono **di sistema** (no rename/delete).
  - `DELETE rows/{id}` accetta `?rowVersion=` (il client Fase 3 lo deve passare). Campi testo troncati server-side ai limiti colonna.
  - ⚠️ Fino a fine Fase 3 **non usare il tab SAL dal web** su dati reali: il client attuale non conosce i campi nuovi (le stringhe sono protette dal null-preserve, ma la UI mostra lo stato migrato in modo incompleto). La migrazione v21 si applica al primo avvio del server.
- [x] **Fase 2** — Warning incasso (campanella + scadenze) — **FATTA 09/07/2026** (build verde, review + fix). `CheckSalIncassoDeadlines` (`SAL_INCASSO_DUE`, sempre ALARM dal giorno dopo il saldo, dedupe giornaliero), filtro fatturazione aggiornato a `stato <> 'emessa'` (campanella + deadlines), 6ª sorgente `SAL_INCASSO` in `/api/deadlines` (tutti gli incassi aperti anche futuri, guardia anti-overflow DATE_ADD), filtro «Incasso SAL» in `/scadenze` (chiave item ora include `type`: SAL e SAL_INCASSO condividono refType/refId), seed-template con `iva_perc=22`.
- [x] **Fase 3** — Tab ProjectSal a parità Excel v10 + anagrafiche a 3 tab — **FATTA 09/07/2026** (dotnet+tsc+eslint verdi, review 3 lenti + 6 fix). Foglio a 16 colonne nell'ordine v10, derivate client (IVA/Tot+IVA/Data prev. saldo), colori riga Pagamento>Stato, lock e barra incasso su `pagamento='Pagata'`, Select con valori storici + «➕ Gestisci» (deep-link `state.tab`), riga nuova con ivaPerc=22, payload sempre esplicito (null-preserve) + DELETE con rowVersion, footer Σ%/Σimporti, euro()/formatDateShort, header con PO/Rif. Offerta (effect su primitive: niente reset in digitazione). `SalConditionsPage` → «Anagrafiche SAL» a 3 tab (pannello riusabile `SalOptionListPanel`, invalidation ampia `["sal"]`, voci di sistema Pagata/Parz. protette in UI). `SalProspettoView`: allineamento interinale (badge/rank/CSV per `incasso`/`attesa`, euro()) in attesa della riscrittura Fase 4. Clamp server gg_saldo [0,3650] e iva_perc [0,100]. **Runtime GUI non verificato** (regola: solo su richiesta).
- [x] **Fase 4** — Prospetto v10 + controllo periodico 15 gg — **FATTA 09/07/2026** (tsc/eslint verdi, review + fix). Colonna «Data Prevista Saldo» ordinabile (null in coda), banner controllo 15 gg (rosso/verde, «Conferma controllo» con `setQueryData` + broadcast `GlobalSalChanged` dal server), sommario contatori (monitorate/scadute/pre/non incassate/attesa), CSV+stampa aggiornati, **ordinamento default = Ipotesi Fatturazione crescente** (parità v10), testi «fino a 2» rimossi ovunque, `/summary` con conteggi mutuamente esclusivi (incasso>warn>pre) e filtro ACTIVE.
- [x] **Fase 5** — Cash Flow SAL + Analisi con drill-down — **FATTA 09/07/2026** (tsc/eslint verdi, 35 check unitari sulle formule, review + fix). `sal-economics.ts` (logica pura v10: bucket esclusivi, totali netto/conIVA, serie mensile continua con finestra 600 mesi più recenti + flag truncated, drill, pill con priorità allineata al Prospetto), `SalCashFlowView` (5 card netto/con IVA + stampa), `SalAnalisiView` (ComposedChart barre impilate + linea «Incasso previsto», drill-down su segmenti/etichette/punti → Dialog, stampa con SVG reale + tabella, nota righe senza data), quick view in `/sal` gated PM/ADMIN con deep-link `?view=` (sostituisce la rotta figlia D8), toast su popup bloccati.
- [x] **Fase 6** — Documentazione + verifica finale — **FATTA 09/07/2026**: HANDOFF.md + WEB-MIGRATION.md aggiornati, nota superseded su SAL-SPEC.md. Verifica finale: `dotnet build` 0 errori, `tsc -b` 0 errori, eslint pulito sui file del modulo. **Runtime GUI non verificato** (su richiesta). La migrazione v21 si applica al primo avvio del server.

### Post-collaudo (10/07/2026, richieste di Diego durante il runtime)
- **Analisi sotto le card Cash Flow** (unica vista `?view=cashflow`, voce «Analisi» rimossa dalla sidebar; alias `?view=analisi` → cashflow).
- **`euro()` con punto migliaia sempre** (formattazione manuale anti-quirk CLDR it: «4.000,00 €») — vale in tutto il gestionale.
- **IVA 22% di default alla creazione riga** anche server-side + **migrazione v22** (righe legacy con %IVA NULL → 22).
- **Aggiunta rapida nelle tendine anagrafica** (`AnagraficaSelect` custom su Popover): testo + Invio → crea, seleziona e propaga (fix: `useQueryClient` nello scope riga; duplicato → seleziona l'esistente; errori a toast; invalidation estesa alle chiavi della pagina Anagrafiche; rimossi gli `stopPropagation` sul click che tenevano aperti più popover). Niente «Gestisci»/«+» nella tendina (scelta utente); la pagina Anagrafiche resta dal menu.
- **Colori configurabili Stati Pagamento** (pattern `ddp_statuses`, **migrazione v23**): `color_bg`/`color_fg` NULL su `sal_payment_states` (NULL = neutro), seed pastello su Pagata/Parz. Pagata, editor colori nel tab admin (preset + Bianco/Nero + anteprima + «Nessun colore»), tinta riga inline nel foglio con priorità colore>emessa e fallback cablato; semantica (lock/incasso/bucket) SEMPRE cablata sulle etichette di sistema. PUT payment-states con label vuota = solo colori (anti lost-update); **realtime `GlobalSalChanged action='lookup'`** su tutte le mutazioni anagrafiche + listener in ProjectSal.

### Ordine e dipendenze
`F1 → F2` (serve gg_saldo/pagamento) · `F1 → F3` (serve contratto esteso) · `F3 → F4 → F5` (riusano pill/derivate; F4 e F5 parallelizzabili dopo F3). Le fasi 2 e 3 sono parallelizzabili tra loro dopo la 1.

---

## 9. Riferimenti rapidi (dalla ricognizione 09/07/2026)

- Migrazioni: `ATEC.PM.Server/Services/DbService.cs` — `LatestSchemaVersion` r.1002, blocchi versionati in `ApplyVersionedMigrations` (v20 finisce ~r.1607); pattern idempotente try/catch + `INSERT IGNORE`.
- SAL server: `Services/SalDbService.cs` (InitTables r.29-65, seed r.73-86), `Controllers/SalController.cs` (lock pagata r.137+, ConflictMessage r.32, Notify SignalR).
- Notifiche: `Services/NotificationService.cs` — CheckSalDeadlines r.582-652, regola comune r.339-351, destinatari `GetProjectPmIds` r.56-65; `Controllers/DeadlinesController.cs` (UNION r.21-121).
- Web SAL: `features/commesse/ProjectSal.tsx` (807 r.), `features/commesse/sal-utils.ts`, `features/sal/{SalPage,SalProspettoView,SalProspettoPage}.tsx`, `features/admin/sal/SalConditionsPage.tsx`, `lib/api/sal.ts`, `lib/signalr/use-sal-hub.ts`, tipi in `lib/api/types.ts` r.1148-1223.
- Pattern: foglio MoM `features/mom/mom-sheet.tsx` + `MoMDetailPage.tsx` (debounce 600ms/riga, coda seriale, conflitto→reload); `ComposedChart` in `features/commesse/ProjectCashFlow.tsx` r.418-428; export in `features/mom/mom-export.ts`; `DateField` `components/shared/date-field.tsx`; `euro()` `lib/format.ts` r.9; `PmSidebar` `components/shared/pm-sidebar.tsx`.
- Prototipo e analisi: mappa completa in `scratchpad\salmap\` (sessione Claude 641e3220) + memoria `pagamenti_sal.md`.
