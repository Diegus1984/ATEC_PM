# HANDOFF — Conversione FEDELE della Gestione Commessa (WPF → web)

> **Leggi questo file per primo.** Obiettivo: riportare sul client web `atec-pm-web`
> il modulo **Gestione Commessa** in modo **fedele al client WPF** (`ATEC.PM.Client`),
> non come reinterpretazione "web". La conversione precedente ha reinterpretato le
> schermate a modo proprio (ordine diverso, layout diverso, griglie ridisegnate) →
> da rifare aderente all'originale.

## 0. REGOLA D'ORO (il motivo di questo handoff)

**Il WPF è la verità. Riproduci la sua struttura, non reinterpretarla.**
- Stesso **ordine** delle sezioni, stessi **header/titoli**, stesse **griglie e colonne**,
  stessi **colori**, stessi **comportamenti** del WPF.
- Prima di costruire una schermata: **apri il file XAML(.cs) WPF corrispondente e mappalo**
  (sezioni, colonne, totali, bottoni, colori, ordine). Solo dopo scrivi il React.
- Niente scelte di layout "perché sul web è più comodo". Se una cosa nel WPF è un albero,
  sul web è un albero. Se sono 4 sezioni collassabili in quell'ordine, sono 4 in quell'ordine.
- Restano valide le regole tecniche del progetto (shadcn, token, `useConfirm`, `DateField`,
  ecc. — vedi `BLOCKS-RULES.md`/`DESIGN-RULES.md`): la fedeltà è alla **struttura/UX** del WPF,
  realizzata con i primitivi shadcn del progetto.

## 1. Documenti da leggere PRIMA (in ordine)

| File | A cosa serve |
|------|--------------|
| **questo** | regola di fedeltà + mappa WPF + stato web + cosa rifare |
| `atec-pm-web/HANDOFF.md` | stato generale moduli web + come avviare |
| `atec-pm-web/BLOCKS-RULES.md` | regole layout pagine (primitivi shadcn) |
| `atec-pm-web/DESIGN-RULES.md` | tema/token/colori |
| `atec-pm-web/WEB-MIGRATION.md` | stato web vs WPF, riepilogo |
| memoria `verifica-runtime-web-harness` | come avviare e **verificare a runtime** (sotto, §7) |

**Sorgente di verità (WPF):** `ATEC.PM.Client/Views/...`
**Contratti API (server):** controller in `ATEC.PM.Server/Controllers/`, DTO in `ATEC.PM.Shared/DTOs/`.
**Regola di progetto:** prima di scrivere un client API, leggi il controller reale e il DTO.

## 2. Cos'è la "Gestione Commessa" nel WPF (la struttura da riprodurre)

File: `ATEC.PM.Client/Views/Commesse/ProjectsPage.xaml(.cs)`.

- **Layout a 3 colonne**: a sinistra un **ALBERO** (ridimensionabile, ~250px), splitter, a destra
  il **contenuto della sezione selezionata** (un `ContentControl` dove iniettano i UserControl).
- **Albero = lista piatta di commesse** (NON raggruppata per cliente/anno/stato): **1 commessa = 1
  nodo radice**, header `"{Code} - {CustomerName}"` (SemiBold). Ogni commessa ha **sotto-nodi fissi
  sempre questi e in quest'ordine**:
  1. **Dettagli** → `ProjectDashboardControl`
  2. **💰 Flusso di Cassa** → `CashFlowControl`
  3. **📊 Preventivo vs Consuntivo** → `BudgetVsActualControl`
  4. **💬 Chat** → `ProjectChatControl`
  5. **📝 Verbali (MoM)** → `ProjectMoMControl`
  6. **📋 DDP Commerciali** → control DDP commerciale
  7. **🔧 DDP Officina** → control DDP officina
  8. **📁 Documenti** → `DocumentManagerControl` (lazy: carica `file-tree` al primo expand,
     sotto-nodi cartelle/file ricorsivi)
- **Ricerca** server-side con **debounce 350ms** (su code/title/cliente/PM). **Paginazione 50** con
  infinite scroll. **Ordinamento fisso** `created_at DESC` (non esposto). **Nessun menu contestuale,
  nessun doppio-click**: navigazione su selezione singola del nodo.
- **Toolbar albero**: `+ Nuova` (apre `ProjectDialog`), `Aggiorna`.
- **Pulsante header destro** (`btnAction`) multifunzione per sezione (es. "Modifica" sui Dettagli).
- **Elimina definitivo** (hard delete): solo **ADMIN**, **doppia conferma**, `DELETE /api/projects/{id}/hard`.

> **✅ DECISIONE PRESA (24/06/2026): ALBERO con le sezioni come nel WPF, NIENTE TABS.** L'utente ha
> bocciato i Tabs ("niente tabs, mi fanno cagare"). Si riproduce il layout a 3 colonne del WPF
> `ProjectsPage`: albero a sinistra (commesse → 8 sotto-nodi) + area contenuto a destra. La pagina a
> Tabs (`ProjectDetailPage.tsx`) va eliminata.

## 3. Stato attuale del web (cosa c'è e cosa DIVERGE)

Cartella: `atec-pm-web/src/features/commesse/`.

> **✅ MODULO COMMESSA COMPLETO (25/06/2026, build+tsc+eslint OK, TUTTE LE 8 SEZIONI VERIFICATE A RUNTIME).**
> Tutte le sezioni dell'albero sono portate sul web e caricano (admin, commessa AT2026001/id17, 0 errori console):
> Dettagli (header scuro + 4+4 KPI + 5 grafici recharts + reparti/personale/attività, fedele a `ProjectDashboardControl`),
> Prev vs Consuntivo (4 sezioni collassabili blu nell'ordine WPF: Impegno Risorse/Materiali/Conto Economico/Scheda Prezzi;
> dentro ogni sezione di costo: RISORSE PIANIFICATE a sx + FASI ASSEGNATE a dx con tecnici/ore pr. inline/aggiungi
> tecnico/fase locale/importa fase — le fasi sono filtrate per `templateId`↔`costSectionTemplateId`, come il WPF, NON in blocco separato),
> Flusso di Cassa (griglia righe×mesi con ricalcolo client-side + grafico, fedele a `CashFlowViewModel`),
> Chat (2 pannelli + nuova chat + polling 5s, no hub), Verbali MoM (card per-commessa → `/mom/:id`),
> DDP Commerciale + Officina (grid `DataTableCardFiltered` con barre di ricerca per colonna + menu «Colonne» +
> righe colorate per stato `ddp_statuses` su tutta la riga; tutte le colonne del WPF — Data/Rich./Produttore/
> Rif.Danea/Note nascoste di default, attivabili dal menu), Documenti (riusato). File: `ProjectDetailsSection`,
> `ProjectBudgetVsActual`, `ProjectCashFlow`, `ProjectChat`, `ProjectMoM`, `ProjectDdpOfficina` + API in `lib/api/`.
> Rifinitura fatta (25/06): stati DDP Officina allineati a `ddp_statuses` (badge colorati + Select + default DO,
> stesso pattern della DDP Commerciale). Rimane solo: allegati Chat non portati.
>
> **✅ SCHELETRO ALBERO FATTO (24/06/2026, build+tsc+eslint OK, VERIFICATO A RUNTIME).** Sostituita lista+Tabs
> col layout a 3 colonne fedele al WPF: `CommessePage.tsx` riscritto come shell (albero + splitter ridimensionabile
> + area sezione con header/titolo + Modifica sui Dettagli + Elimina definitivo ADMIN doppia conferma); nuovo
> `CommessaTree.tsx` (lista piatta commesse, ricerca debounce 350ms, infinite scroll pag.50, 8 sotto-sezioni in
> ordine); `commessa-sections.ts` (le 8 sezioni); `ProjectDetailsSection.tsx` (Dettagli provvisorio, panoramica
> estratta). `ProjectDetailPage.tsx` (Tabs) **eliminato**. Route `/commesse/:projectId/:section?` → stessa pagina.
> Sezioni riusate come pannelli: Dettagli/Documenti/DDP commerciale/Prev-vs-cons; placeholder «in corso di
> portabilità» per Flusso di Cassa, Chat, MoM, DDP Officina. **Restano da fare i contenuti fedeli sotto** (§4).

| File web | Cosa fa | Fedeltà al WPF |
|----------|---------|----------------|
| `CommessePage.tsx` | **shell 3 colonne**: albero + splitter + area sezione (header/Modifica/Elimina), dispatch sezione, deep-link | ✅ layout fedele (albero+sezioni, no Tabs) |
| `CommessaTree.tsx` | pannello albero: commesse → 8 sotto-sezioni, ricerca, infinite scroll, status | ✅ fedele a `BuildTree` |
| `ProjectDetailsSection.tsx` | sezione Dettagli (panoramica ridotta provvisoria) | ⚠️ contenuto da rifare fedele (§4.2, task #2) |
| ~~`ProjectDetailPage.tsx`~~ | ~~Tabs~~ | ❌ ELIMINATO |
| `ProjectDialog.tsx` | crea/modifica commessa | ~OK (vedi §4.1) |
| `ProjectDocuments.tsx` + `ProjectFilePreviewDialog.tsx` | gestore documenti + anteprima | ~OK (vedi §4.3) |
| `ProjectDdpCommercial.tsx` + `DdpRowDialog.tsx` | DDP commerciale | da verificare vs WPF |
| `ProjectBudgetVsActual.tsx` + `ProjectPhaseAssignments.tsx` | Prev vs Consuntivo | ❌ **layout reinterpretato, NON fedele** (vedi §4.4) — **da rifare** |

API web esistenti: `src/lib/api/projects.ts`, `project-documents.ts`, `project-ddp.ts`,
`project-bva.ts`, `phases.ts`. Tipi in `src/lib/api/types.ts`.

**Tab/sezioni mancanti del tutto sul web**: Flusso di Cassa, Chat, MoM-per-commessa, **DDP Officina**.

## 4. Mappa per sezione: WPF (verità) + endpoint + stato + cosa fare

### 4.1 Lista commesse + Crea/Modifica (`ProjectDialog`)
- **WPF**: `ProjectsPage` (albero, §2) + `Views/Commesse/ProjectDialog.xaml(.cs)`.
- **Dialog campi** (in ordine WPF): Codice* (in *nuovo* precompilato da `GET /api/projects/next-code`),
  Titolo*, Cliente* (`GET /api/lookup/customers`), Project Manager* (`GET /api/lookup/employees?role=PM`),
  Data inizio (default oggi), Data fine prevista, Ricavo, Budget, Ore previste, Stato (default `DRAFT`,
  con transizioni vincolate in modifica), Priorità (default `MEDIUM`), Descrizione, Path Server, Note,
  «Crea fasi di default» (checkbox, solo *nuovo*). Se `LinkedQuoteId>0` → Ricavo/Budget/Ore **read-only**
  (banner 🔒). Salvataggio `POST /api/projects` (nuovo) / `PUT /api/projects/{id}` (modifica), payload camelCase.
- **Stato web**: `CommessePage.tsx` + `ProjectDialog.tsx`. Il dialog è ~fedele; **la lista va decisa**
  (albero vs lista, §2).
- **Endpoint**: `GET /api/projects?page&pageSize&search`, `GET /api/projects/{id}`, `POST/PUT /api/projects`,
  `DELETE /api/projects/{id}` (soft→CANCELLED), `DELETE /api/projects/{id}/hard` (ADMIN), `GET /api/projects/next-code`,
  `GET /api/lookup/customers`, `GET /api/lookup/employees?role=PM`.

### 4.2 Dettagli / dashboard commessa (`ProjectDashboardControl`)
- **WPF**: `Views/Commesse/ProjectDashboardControl.xaml.cs` (costruito a runtime). Sezioni **in ordine**:
  1) **Header** scuro `#1A1D26` (codice grande blu, badge Stato, badge Priorità, titolo, chip 🏢Cliente/👤PM/📅Inizio/🏁Fine).
  2) **Riga KPI** (`UniformGrid` 4): AVANZAMENTO (% fasi), ORE TOTALI (vs budget, rosso se >100%), TECNICI, COSTO MAT.
     — **solo PM/ADMIN** aggiunge: COSTO ORE, COSTO TOTALE (vs budget), RICAVO, MARGINE.
  3) **Descrizione**, 4) **Cartella Progetto** (path + apri), 5) **Note**.
  6) **Grafici** (OxyPlot): pie "Ore per Reparto", bar "Preventivato vs Assegnato vs Consuntivo",
     area "Andamento Ore Settimanali", **Gantt** "Timeline Fasi".
  7) **Scadenze Prossime** (semaforo 🔴🟡🔵🟢). 8) **Ore per Reparto** (barre a 3 layer prev/assegn/lav).
  9) **Personale su Commessa** (tabella). 10) **Ultime Attività** (timesheet).
  - Colori reparto: PM `#4F6EF7`, UTM `#059669`, UTE `#2563EB`, MEC `#D97706`, INS `#DC2626`, PLC `#7C3AED`, ROB `#BE185D`, ACQ `#0891B2`, default `#6B7280`.
- **Endpoint UNICO**: `GET /api/projects/{id}/dashboard` → `ProjectDashboardData` (contiene già `DepartmentSummaries`,
  `RecentEntries`, `ActiveTechnicians`, `WeeklyHours`, `PhaseGantt`, `Deadlines`).
- **Stato web**: oggi è il tab "Panoramica" con KPI base + 2 tabelle — **molto ridotto** rispetto al WPF.
  Per fedeltà: riprodurre header scuro, le 4(+4) KPI, i grafici (libreria già usata: `recharts`), scadenze,
  ore per reparto, personale, ultime attività. Economics **solo PM/ADMIN**.

### 4.3 Documenti (`DocumentManagerControl` + `FilePreviewControl`)
- **WPF**: `Views/Commesse/DocumentManagerControl.xaml(.cs)`. **Lista piatta DataGrid** della cartella
  corrente (colonne: icona-tipo, Nome, Dimensione, Modificato), **breadcrumb** cliccabile, toolbar
  («📁 Nuova Cartella», «📤 Carica File», «📂 Apri in Explorer»), **drag&drop** upload, **context menu** riga
  (Scarica/Rinomina/Sposta…/Elimina). Doppio click cartella = entra; file = apre.
- **FilePreview**: PDF e immagini (download bytes), Word/Excel/CSV (`/preview` → HTML), CAD/eDrawings/3D
  (casi pesanti, non portabili 1:1 — sul web: PDF/immagini in iframe/img, Office via `/preview`, il resto «Scarica»).
- **Endpoint**: `/files?subPath=`, `/upload?subPath=` (campo `file`), `/upload-multiple?subPath=` (campo `files`),
  `/create-subfolder` `{subPath,folderName}`, `/rename` `{oldPath,newName}`, `/delete-item` `{itemPath}`,
  `/move-item` `{sourcePath,destinationFolder}`, `/download?path=` (bytes), `/preview?path=` (HTML).
- **Stato web**: `ProjectDocuments.tsx` + `ProjectFilePreviewDialog.tsx` — **già abbastanza fedele**
  (cartella piatta + breadcrumb + upload+drag&drop + rinomina/sposta/elimina + anteprima). Verificare i
  dettagli (colonne, icone) contro il WPF.

### 4.4 ⚠️ Preventivo vs Consuntivo (`BudgetVsActualControl`) — DA RIFARE FEDELE
**Questa è la schermata principale da correggere.** Oggi è reinterpretata e illeggibile.
- **WPF**: `Views/BudgetVsCosting/BudgetVsActualControl.xaml(.cs)` + `ViewModels/BvaCostingVM.cs` +
  `ImportPhasesDialog`. **Unica scrollata verticale, 4 sezioni collassabili, header blu `#2563EB`**, in QUESTO ORDINE:
  1. **IMPEGNO RISORSE** (albero): **GRUPPO** (barra colorata, totali GG/Ore/Netto/Vendita) → **SEZIONE**
     (header con tipo SEDE/CLIENTE + totali + bottone `+` risorsa) → **DataGrid risorse**:
     - sezioni **IN_SEDE**: colonne RISORSA, REP, GG, ORE/G, TOT ORE, €/H, COSTO NETTO, K, VENDITA;
     - sezioni **DA_CLIENTE**: in più VIAGGI, KM, €/KM, VITTO/G, HOTEL/G, GG IND., €/G IND. (campi tariffa con dropdown);
     - **sotto ogni sezione**, la griglia **FASI ASSEGNATE / tecnici**: TECNICO, ORE PR. (editabile inline),
       ORE LAV. (consuntivo), % (= lav/pr); header fase "X h prev. | Y h lav. | Z%"; bottoni `+ Importa fase`,
       `+ Fase locale`, `↑ Salva come template`; rimozione tecnico/fase.
  2. **MATERIALI**: per **prodotto** una card (badge "MAT", nome, totale vendita) con righe DESCRIZIONE/QTA/€UNIT/K/NETTO/VENDITA.
  3. **CONTO ECONOMICO** (4 KPI in alto): **FINAL OFFER PRICE**, **ORDER PRICE** (editabile → `PATCH /api/projects/{id}/revenue`),
     **BUDGET COSTI**, **CONSUNTIVO COSTI**; poi dettaglio **Budget** (Risorse/Acquisti/Trasferta) vs **Consuntivo**
     (Risorse/Acquisti/Trasferta editabile → `PATCH …/budget-vs-actual/actual-travel-cost`); KPI REDDITIVITÀ %,
     AVANZAMENTO %, TECNICI ATTIVI, FASI COMPLETATE n/N.
  4. **SCHEDA PREZZI**: NET COST → +Contingency → OFFER PRICE → +Margine trattativa → FINAL OFFER PRICE.
  - **Colori delta ore**: rosso `#DC2626` se delta>0 (sforamento), verde `#059669` se ≤0. Tipo sezione:
    CLIENTE arancio `#D97706`, SEDE verde `#059669`.
- **Endpoint**: `GET /api/projects/{id}/budget-vs-actual` (gated feature `data.budget` = PM/ADMIN, 403 se no),
  `PATCH /api/projects/{id}/revenue` (decimal grezzo), `PATCH /api/projects/{id}/budget-vs-actual/actual-travel-cost`
  (decimal grezzo). Fasi/assegnazioni: `GET /api/phases/project/{id}`, `GET /api/phases/templates`,
  `GET /api/employees/by-phase/{phaseId}`, `POST /api/phases/{phaseId}/assignments`,
  `PATCH /api/phases/assignments/{id}/hours`, `DELETE /api/phases/assignments/{id}`, `POST /api/phases/bulk`,
  `POST /api/phases/local`, `DELETE /api/phases/{id}`.
- **Stato web**: `ProjectBudgetVsActual.tsx` + `ProjectPhaseAssignments.tsx` esistono e i layer API +
  i tipi (`BudgetVsActualData`, `Bva*`, `PhaseListItem`, …) sono **corretti e riutilizzabili** — ma il
  **layout va rifatto** secondo l'ordine/struttura WPF sopra (oggi: KPI economici in cima, tabelle proprie,
  pannello fasi separato, colori diversi). **Riusa il layer API, riscrivi la vista.**

### 4.5 Flusso di Cassa (`CashFlowControl`) — DA MAPPARE E COSTRUIRE
- **WPF**: `Views/CashFlow/CashFlowControl.xaml(.cs)`. **Da leggere e mappare** (struttura, colonne, totali).
- **Server**: `Controllers/CashFlowController.cs` (+ DTO `CashFlow_DTOs` se presente). **Web: assente.**

### 4.6 Chat commessa (`ProjectChatControl`) — DA MAPPARE E COSTRUIRE
- **WPF**: `Views/Commesse/ProjectChatControl.xaml(.cs)` + `NewChatDialog.xaml`. **Da mappare.**
- **Server**: `Controllers/ChatController.cs` + SignalR (probabile hub). **Web: assente.**

### 4.7 MoM per commessa (`ProjectMoMControl`) — DA MAPPARE E COSTRUIRE
- **WPF**: `Views/Commesse/ProjectMoMControl.xaml(.cs)`. Il MoM **standalone** è già live sul web
  (`features/mom/`); qui serve la variante **filtrata per commessa** (`MoMController` accetta `?projectId`).
- **Server**: `Controllers/MoMController.cs`.

### 4.8 DDP Commerciali / DDP Officina
- **Commerciali**: web `ProjectDdpCommercial.tsx` esiste — **verificarne la fedeltà** al control WPF DDP
  commerciale (colonne, ordine, stati colorati). Endpoint `GET/POST/PUT/DELETE /api/projects/{id}/ddp`.
- **Officina**: **assente sul web.** Endpoint `GET/POST/PUT/DELETE /api/projects/{id}/ddp-officina`
  (DTO `OfficinaItemListItem`/`OfficinaItemSaveRequest`). Mappare il control WPF officina e costruirlo.

## 5. Contratti server — tutti GIÀ esistenti

Nessun lavoro server necessario. Controller chiave:
`ProjectsController` (commessa + documenti + ddp), `BudgetVsActualController`, `PhasesController`,
`CashFlowController`, `ChatController`, `MoMController`, `DdpManagerController`.
DTO in `ATEC.PM.Shared/DTOs/`. **Leggi sempre il controller reale prima di scrivere il client.**
Wrapper risposta: `ApiResponse<T>` (`{success,data,message}`). Helper web: `apiGet/apiPost/apiPut/apiPatch/apiDelete/apiUpload` + `unwrapApi` (`src/lib/api/client.ts`).
Gotcha: alcuni PATCH hanno **body decimal/int grezzo** (revenue, actual-travel-cost, progress); molti errori
applicativi tornano `ApiResponse.Fail` con **HTTP 200**; `data` null è **omesso** dal JSON (serializer
`WhenWritingNull`) → su risposte nullable usa un unwrap tollerante (vedi `updateDdpRow` in `project-ddp.ts`).

## 6. Convenzioni web (obbligatorie)

- Un file `lib/api/<dominio>.ts` per dominio; tipi in `types.ts`. Pagine non chiamano `apiGet` diretto.
- Tabelle: `@/components/ui/table` (o `DataTableCard`); numeri `tabular-nums`; valuta `euro()` (`lib/format`).
- Azioni riga: `RowActionsMenu` (`⋮`). Conferme distruttive: **`useConfirm()`** (mai `window.confirm`).
- `Select` shadcn (sentinella `"__none__"`, mai `value=""`). Date: **`DateField`** + `lib/date-iso`
  (regola coppia inizio/fine: fine≥inizio, fine disabilitata senza inizio).
- Accordion: `<Collapsible>` + token `--accordion-duration/--accordion-ease`.
- Realtime ambiente condiviso: `useProjectHub(projectId, onChange)` (`lib/signalr`).
- Gating economics PM/ADMIN: `const role = getSession()?.user.userRole; const canSeeEconomics = role==="ADMIN"||role==="PM"`.
- Numeri da input: `Number(value.replace(",", "."))`.
- Pattern di riferimento già in repo: `ProjectTemplatesPage`/`CostSectionsTreePanel` (albero+azioni),
  `ClientiPage`+`CustomerDialog` (tabella+dialog), `ProjectDdpCommercial` (tabella+realtime).

## 7. Come avviare e VERIFICARE a runtime (non saltare)

Vedi memoria `verifica-runtime-web-harness`. In sintesi:
1. **Avvio**: in Debug premendo **F5** sul progetto `ATEC.PM.Server` parte il server (5150/5151) e
   `DevSpaLauncher` avvia da solo Vite (5173) + apre il browser. *Se VS desse `E_NOINTERFACE`: è un problema
   di VS, non del codice — chiudi VS, cancella `ATEC_PM\.vs`, riapri.* In alternativa da terminale:
   `dotnet run --project ATEC.PM.Server` (avvia entrambi). Node spesso **non è nel PATH**: per comandi npm
   manuali `$env:Path = "C:\Program Files\nodejs;" + $env:Path`.
2. **Login per la GUI**: l'unico ADMIN (`admin`) ha hash bcrypt ignoto → **conia un JWT** con la chiave
   `Jwt:Key` di `appsettings.json` (iss/aud `ATEC.PM`, claim URI nameidentifier/name/role=ADMIN, exp futuro —
   vedi `AuthController.Login`) e inietta in `localStorage` (`atec_pm_token` + `atec_pm_user={...userRole:'ADMIN'}`),
   poi reload. La UI legge il ruolo da `atec_pm_user`, non dal token.
3. **Guidare la GUI (preview MCP)**: la pagina BVA è **molto pesante** → `preview_screenshot` va in timeout e i
   tab Radix si attivano con **forte ritardo** (le `eval` leggono prima del commit). Verifica via **DOM eval**
   (`document.body.innerText`, conteggi) più che screenshot; per i Radix usa `preview_click` (pointer reale) e
   ricontrolla lo stato dopo. Per gli input React: native value setter + `dispatchEvent('input')`.
4. **Dati di test**: commessa demo `AT2026001` (id 17, `linkedQuoteId 39`) ha costing/fasi reali → ottima per
   la BVA. **Ripulisci sempre** i dati di test creati.

## 8. Metodo consigliato per ogni schermata

1. Concorda con l'utente la **decisione albero vs tabs** (§2) — è la prima divergenza da chiarire.
2. Per ciascuna sezione: **apri il file WPF**, mappa (sezioni/colonne/ordine/colori/totali/bottoni),
   leggi il **controller + DTO** server, poi **costruisci aderente** al WPF coi primitivi shadcn.
3. `tsc -b` + `eslint` puliti; poi **verifica a runtime** (§7) prima di dichiarare fatto.
4. Aggiorna `WEB-MIGRATION.md`.

> **Riusa il più possibile i layer API e i tipi già scritti** (`projects.ts`, `project-documents.ts`,
> `project-ddp.ts`, `project-bva.ts`, `phases.ts`, tipi in `types.ts`): sono allineati ai contratti reali.
> Il lavoro è quasi tutto **UI da rifare fedele**, non API da riscrivere.
