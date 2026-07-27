# Piano — Preventivo editabile INLINE nella pagina «Preventivo vs Consuntivo»

> Obiettivo: NON una tab separata. Rendere **editabile la colonna sinistra
> ("Risorse pianificate")** della pagina già esistente `ProjectBudgetVsActual.tsx`,
> riusando la struttura a 2 colonne (sinistra = preventivo, destra = fasi, già
> con "Importa fase" / "Fase locale"). Così una commessa **creata da zero** si
> compila a mano qui.
>
> ## DECISIONI PRESE (08/07/2026) — vincolanti
> 1. **Commessa CONVERTITA da preventivo (`linkedQuoteId > 0`) = SOLA LETTURA**:
>    la colonna sinistra è specchio del preventivo di origine, NON editabile
>    (bottoni "+ Risorsa", "+ Sezione", azioni riga, "+ Materiale", edit pricing
>    nascosti/disabilitati). L'edit a mano è consentito **solo sulle commesse
>    create da zero** (`linkedQuoteId === 0`).
> 2. **Ambito editabile = Risorse + Sezioni + Materiali + Scheda prezzi** (tutto,
>    non solo risorse), sempre soggetto al gate del punto 1.

## Contesto architetturale (già verificato a runtime 08/07/2026)

Il "preventivo" di una commessa È il suo albero di costing proprio:
`project_cost_sections` → `project_cost_resources` (+ `project_material_*`,
`project_pricing`). Endpoint CRUD completi già esistenti in
`ATEC.PM.Server/Controllers/ProjectCostingController.cs` (`/api/projects/{id}/costing/*`),
inclusa `POST /costing/init` che semina dai template `is_default_project=1`.
Il confronto (`BudgetVsActualController`) aggancia consuntivo↔preventivo via
`cost_section_template_id` (fase → sezione).

## Cosa è GIÀ pronto in questa sessione (da riusare, non riscrivere)

- **`src/lib/api/project-costing.ts`** — layer API completo verso
  `/api/projects/{id}/costing/*` (init, sezioni CRUD, risorse CRUD, materiali
  CRUD, pricing, available-templates, section-employees). **SI RIUSA COM'È.**
- **`src/features/commesse/ProjectPreventivo.tsx`** — contiene i componenti
  `ResourceDialog`, `AddSectionDialog`, `MaterialItemDialog`, `NumField` già
  funzionanti. **Da qui si estraggono i dialoghi**; il resto della tab (il
  wrapper `ProjectPreventivo`, `CostSectionsBlock`, `GroupBlock`, `SectionBlock`,
  `MaterialsBlock`, `PricingBlock`) **si elimina** (vedi step 5).

## Modifiche

### STEP 1 — Server: arricchire i DTO del confronto con gli ID editabili

La pagina BvA oggi riceve `BvaSectionDto` (ha `TemplateId` ma NON l'id della
`project_cost_sections`) e `BvaBudgetResourceDto` (senza id). Per editare/eliminare
in place servono gli id reali.

File: `ATEC.PM.Shared/DTOs/BudgetVsActual_DTOs.cs`
- In `BvaSectionDto` aggiungere: `public int SectionId { get; set; }`
  (= `project_cost_sections.id`).
- In `BvaBudgetResourceDto` aggiungere: `public int ResourceId { get; set; }`
  (= `project_cost_resources.id`) e `public int? EmployeeId { get; set; }`
  (per preservare il riaggancio dipendente in edit).
- In `BudgetVsActualData` aggiungere: `public int LinkedQuoteId { get; set; }`
  (0 = da zero → editabile; >0 = convertita → sola lettura). Vale per l'intera
  pagina (gate globale dei bottoni di edit).

File: `ATEC.PM.Server/Controllers/BudgetVsActualController.cs`
- Nella query `resources` (righe ~36-56) aggiungere `r.id AS ResourceId,
  r.employee_id AS EmployeeId`.
- Nel build di `BvaSectionDto` (riga ~133) valorizzare `SectionId = secId`.
- Nel loop che popola `BudgetResources` (riga ~145) valorizzare
  `ResourceId = (int)r.ResourceId` e `EmployeeId = (int?)r.EmployeeId`.
- La query finale su `projects` (riga ~302, quella di `revenue`/`actual_travel_cost`)
  aggiungere `linked_quote_id` e valorizzare `result.LinkedQuoteId`.

File client: `src/lib/api/types.ts`
- `BvaSectionDto`: `sectionId: number`. `BvaBudgetResourceDto`: `resourceId: number`,
  `employeeId: number | null`. `BudgetVsActualData`: `linkedQuoteId: number`.

> Nel client calcolare `const canEditBudget = data.linkedQuoteId === 0` e usarlo
> come gate per TUTTI i controlli di edit (risorse, sezioni, materiali, pricing).
> Se convertita: mostrare un badge "Preventivo da offerta #{linkedQuoteId} — sola
> lettura" in cima alla pagina.

> NB: sezioni preventivo con `is_enabled=0` NON compaiono nel BvA (query filtra
> `is_enabled=1`). Enable/disable e "aggiungi sezione" richiedono l'elenco
> completo → in STEP 4 caricare anche `fetchProjectCosting` per conoscere i
> template già presenti/disattivati (serve solo se `canEditBudget`).

### STEP 2 — Estrarre i dialoghi in un file condiviso

Creare `src/features/commesse/preventivo-dialogs.tsx` spostando da
`ProjectPreventivo.tsx`: `ResourceDialog`, `AddSectionDialog`, `MaterialItemDialog`,
`NumField`, e l'helper `num()`. Esportarli. (Sono già scritti e tipizzati.)

`ResourceDialog` accetta già `section: ProjectCostSectionDto` — ma nella pagina BvA
abbiamo un `BvaSectionDto`. Due opzioni:
- (a) **Consigliata**: adattare `ResourceDialog` ad accettare solo i campi che usa
  → `{ sectionId: number; sectionName: string; sectionType: string; resourcesCount: number }`
  invece dell'intero `ProjectCostSectionDto`. Così è usabile sia dalla vecchia tab
  (che verrà rimossa) sia dalla pagina BvA senza avere il DTO costing completo.
- (b) In alternativa, nella pagina BvA caricare `fetchProjectCosting` e mappare la
  sezione BvA → sezione costing per `templateId`/`sectionId`, passando il
  `ProjectCostSectionDto` vero al dialog.

Scegliere (a): meno accoppiamento. `ResourceDialog` internamente usa solo
`section.id`, `section.name`, `section.sectionType`, `section.resources.length`.

### STEP 3 — Rendere editabile la colonna sinistra in `ProjectBudgetVsActual.tsx`

In `SectionBlock` (righe ~249-379), colonna sinistra "Risorse pianificate":
1. Header colonna: accanto a "Risorse pianificate" aggiungere un bottone
   **"+ Risorsa"** (mostra `ResourceDialog` in modalità new, passando
   `{ sectionId: section.sectionId, sectionName: section.sectionName,
   sectionType: section.sectionType, resourcesCount: section.budgetResources.length }`).
2. Tabella risorse: aggiungere una colonna azioni con `RowActionsMenu`
   (Modifica → apre `ResourceDialog` con la riga; Elimina → `deleteProjectResource`).
   Per "Modifica" servono i valori pieni della risorsa: `BvaBudgetResourceDto` ha
   già `workDays, hoursPerDay, hourlyCost, markupValue` + trasferta + ora
   `resourceId`. Mappare `BvaBudgetResourceDto` → payload
   `ProjectCostResourceSaveRequest` (manca solo `employeeId`: passarlo `null`,
   il nome resta libero — il BvA non porta l'employeeId; è un limite accettabile,
   oppure aggiungere anche `EmployeeId` al DTO nello STEP 1 se si vuole preservarlo).
   → **Consiglio: aggiungere anche `EmployeeId` a `BvaBudgetResourceDto`** così il
   riaggancio dipendente si preserva in edit.
3. `onSaved`/`onDeleted` dei dialoghi → invalidare le query
   `["project-bva", projectId]` e `["project-phases", projectId]` (già presente
   `invalidate` nel componente `ProjectBudgetVsActual`; passarlo giù a `SectionBlock`).

### STEP 4 — Header sezione: aggiungi/gestisci sezione + empty state

1. Sotto l'header "Impegno Risorse" (o vicino al bottone "Aggiorna", riga ~646),
   aggiungere **"+ Aggiungi sezione"** → `AddSectionDialog` (già pronto: usa
   `fetchProjectAvailableTemplates` + `addProjectCostSection`).
2. Empty state (`data.groups.length === 0`, righe ~685-694): sostituire il testo
   statico con un blocco che offre **"Inizializza preventivo"**
   (`initProjectCosting(projectId)`) + "Aggiungi sezione". Riusare l'empty state
   già scritto in `ProjectPreventivo` (blocco `Empty` + bottone `Sparkles`).
   Dopo l'init → `invalidate()`.

### STEP 5 — Rimuovere la tab «Preventivo» separata

- `src/features/commesse/commessa-sections.ts`: rimuovere la voce
  `{ key: "preventivo", ... }` aggiunta in questa sessione.
- `src/features/commesse/CommessePage.tsx`: rimuovere `import ProjectPreventivo`,
  la voce `preventivo:` in `SECTION_TITLES`, e il `case "preventivo"` in
  `SectionContent`.
- Eliminare `src/features/commesse/ProjectPreventivo.tsx` DOPO aver estratto i
  dialoghi (STEP 2). Mantenere `project-costing.ts`.
- (Opzionale) rimuovere l'hint aggiunto nell'empty-state BvA (ora l'empty state
  fa direttamente init, non serve più rimandare a un'altra sezione).

### STEP 5b — Materiali + Scheda prezzi editabili (nel BvA)

Blocco "Materiali" del BvA (righe ~698+) e riepilogo pricing:
1. Materiali: per ogni `data.materialSections`, aggiungere "+ Materiale" e menu
   Modifica/Elimina per riga → riuso `MaterialItemDialog` (estratto in STEP 2) +
   `addProjectMaterialItem`/`updateProjectMaterialItem`/`deleteProjectMaterialItem`.
   Serve `id` della `project_material_sections` e degli item: il BvA
   `BvaMaterialItemDto` ha già `Id`; aggiungere `SectionId` a
   `BvaMaterialSectionDto` (STEP 1 bis) per sapere dove inserire nuovi item.
2. Scheda prezzi: nel blocco riepilogo economico rendere editabili
   `contingency %` e `negotiation %` (riuso logica `PricingBlock` di
   `ProjectPreventivo.tsx`) → `updateProjectPricing`. Il BvA non porta i pricing
   grezzi in forma editabile: leggerli da `fetchProjectCosting` (già caricato in
   STEP 4) oppure aggiungere `contingencyPct`/`negotiationMarginPct` a
   `BvaPricingDto`.
3. Tutto gated da `canEditBudget` (STEP 1).

### STEP 6 — Gate convertito (SOLA LETTURA se `linkedQuoteId > 0`)

- `const canEditBudget = data.linkedQuoteId === 0`.
- Se `!canEditBudget`: NON renderizzare i bottoni "+ Risorsa", "+ Sezione",
  "+ Materiale", i `RowActionsMenu` di modifica/elimina, gli input pricing e
  l'empty-state "Inizializza". Mostrare in cima un badge
  "Preventivo da offerta #{linkedQuoteId} — sola lettura".
- Se `canEditBudget`: tutti i controlli attivi (STEP 3/4/5b).

## Ordine di esecuzione consigliato (Antigravity)

1. STEP 1 (server DTO: SectionId, ResourceId, EmployeeId, LinkedQuoteId,
   material SectionId; query) → `dotnet build`.
2. STEP 1 client types.
3. STEP 6 gate `canEditBudget` (definire subito, così STEP 3/4/5b lo usano).
4. STEP 2 (estrai dialoghi) → adatta `ResourceDialog` a props leggere.
5. STEP 3 (colonna sinistra risorse editabile) → `npx tsc -b --noEmit`.
6. STEP 4 (aggiungi sezione + empty state init).
7. STEP 5b (materiali + pricing editabili).
8. STEP 5 (rimuovi tab separata) → tsc + eslint + `npm run build`.

## Verifica

- Build server (`dotnet build`) + web (`tsc`/`eslint`/`npm run build`) verdi.
- Runtime API (harness `verifica_runtime_web_harness`): su commessa da zero →
  BvA empty → init → "+ Risorsa" in una sezione → la riga compare a sinistra e i
  totali "Prev" della sezione/gruppo si aggiornano; "Importa fase" a destra →
  "Assegn"/"Cons" restano coerenti. Pulire con hard-delete.

## Limiti noti / debito

- Editor collaborativo senza real-time/concurrency (come il costing preventivi).
  Da valutare per la regola `realtime_ambiente_condiviso` in un secondo momento.
- Sui convertiti l'edit è disabilitato by-design (decisione 1): se in futuro serve
  "sganciare" una commessa dal preventivo per editarla, servirà un'azione esplicita
  (es. azzerare `linked_quote_id`) — fuori scope ora.
