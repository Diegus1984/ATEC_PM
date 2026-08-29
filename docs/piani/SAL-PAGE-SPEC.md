# Istruzioni per Antigravity — Pagina PM «SAL» globale + Prospetto SAL

> Obiettivo: aggiungere in `atec-pm-web` una **pagina nella sezione PM** che raccoglie i SAL di
> **tutte le commesse** (sul modello della pagina **Milestones** / **Check list**: `PmSidebar` +
> area principale), con dentro anche la vista aggregata **«Prospetto SAL»** (le prime 2 ipotesi di
> fatturazione aperte per commessa, colorate). Il controllo/verifica finale è di **Claude**.
> Rispetta alla lettera nomi di file/endpoint/feature-key qui indicati: la verifica è meccanica.

**Regole non negoziabili** (da `HANDOFF.md`): fedeltà ai blocchi shadcn; un file API per dominio in
`src/lib/api/` con `apiGet/... + unwrapApi`; DTO reali dal controller; `dotnet build` + `tsc -b` +
`eslint` puliti prima di chiudere; conferme distruttive con `useConfirm` (mai `window.confirm`);
multi-utente ⇒ realtime + `row_version`.

---

## 0. Cosa esiste GIÀ (NON rifarlo)

Il modulo SAL (backend + tab commessa) è **fatto, verificato e girante** (vedi `SAL-SPEC.md`):
- **Server** `SalController` (`/api/sal`): bundle, header, righe CRUD/reorder, seed-template, conditions,
  e **`GET /api/sal/prospetto`** → `List<SalProspettoRowDto>` (prime 2 ipotesi aperte per commessa,
  con `Importo` e `Alert` ∈ `''`/`warn`/`pre` già calcolati in SQL). Realtime evento `SalChanged`.
- **Web**: `src/lib/api/sal.ts` (con `fetchSalProspetto()` già presente), tipi in `types.ts`
  (`SalProspettoRow`, `SalRow`, `SalBundle`…), `src/features/commesse/ProjectSal.tsx` (il tab SAL
  completo, autoconsistente: prende `projectId`), `src/lib/signalr/use-sal-hub.ts`,
  `src/features/commesse/sal-utils.ts` (`salAlertState`, `salRowClass`).

**Template più vicino da clonare:** `src/features/milestones/MilestonesPage.tsx` (pagina PM globale con
`PmSidebar` + summary endpoint + card per-commessa lazy). Segui la sua struttura quasi 1:1.

---

## 1. Backend — endpoint summary per la sidebar + feature key nav

### 1.1 DTO — in `ATEC.PM.Shared/DTOs/Sal_DTOs.cs` (aggiungi in coda)
```csharp
// Riepilogo per-commessa dei SAL, per la sidebar PM globale (mirror di MilestoneSummaryDto).
public class SalSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Total { get; set; }   // righe SAL totali (conteggio del contenitore in sidebar)
    public int Open { get; set; }     // righe con data_fatt e stato='' (ipotesi aperte)
    public int Warn { get; set; }     // aperte e scadute (data_fatt <= oggi)
    public int Pre { get; set; }      // aperte e imminenti (da lunedì sett. precedente)
}
```

### 1.2 Endpoint — in `SalController` (aggiungi accanto a `GetProspetto`)
Mirror **esatto** di `MilestonesController.GetSummary` (`GET /api/milestones/summary`).
```csharp
[HttpGet("summary")]
public IActionResult GetSummary()
{
    using var c = _db.Open();
    var rows = c.Query<SalSummaryDto>(@"
        SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
               COUNT(*) AS Total,
               COALESCE(SUM(sr.data_fatt IS NOT NULL AND sr.stato = ''), 0) AS Open,
               COALESCE(SUM(sr.stato = '' AND sr.data_fatt IS NOT NULL
                            AND sr.data_fatt <= CURDATE()), 0) AS Warn,
               COALESCE(SUM(sr.stato = '' AND sr.data_fatt IS NOT NULL AND sr.data_fatt > CURDATE()
                            AND CURDATE() >= DATE_SUB(DATE_SUB(sr.data_fatt,
                                 INTERVAL WEEKDAY(sr.data_fatt) DAY), INTERVAL 7 DAY)), 0) AS Pre
        FROM sal_rows sr
        JOIN projects p ON p.id = sr.project_id
        GROUP BY p.id, p.code, p.title
        HAVING COUNT(*) > 0
        ORDER BY p.code").ToList();
    return Ok(ApiResponse<List<SalSummaryDto>>.Ok(rows));
}
```

### 1.3 Feature key nav — migrazione **v17** (`Services/DbService.cs`)
Come il blocco v16: alza `LatestSchemaVersion` a **17**, aggiungi il blocco:
```csharp
if (currentVersion < 17)
{
    try
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.sal', 'SAL / Fatturazione', 'navigation', 2, 'HIDDEN')");
        c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (17, 'nav.sal feature key')");
        _logger.LogInformation("[Migration v17] feature key nav.sal");
    }
    catch (Exception ex) { _logger.LogWarning("[Migration v17] Errore (non bloccante): {Message}", ex.Message); }
}
```

---

## 2. Frontend

### 2.1 API + tipi
- `src/lib/api/types.ts` — aggiungi `SalSummary` (camelCase di `SalSummaryDto`: `projectId, code, title, total, open, warn, pre`).
- `src/lib/api/sal.ts` — aggiungi:
  ```ts
  export async function fetchSalSummary(): Promise<SalSummary[]> {
    const response = await apiGet<ApiResponse<SalSummary[]>>("/api/sal/summary")
    return unwrapApi(response)
  }
  ```

### 2.2 Helper dots — in `src/features/commesse/sal-utils.ts`
```ts
import type { PmSidebarDot } from "@/components/shared/pm-sidebar"
/** Pallini di stato per la sidebar PM a partire dal riepilogo SAL di una commessa. */
export function salSummaryDots(s: { warn: number; pre: number; open: number }): PmSidebarDot[] {
  const dots: PmSidebarDot[] = []
  if (s.warn > 0) dots.push({ dotClass: "bg-red-500", label: `${s.warn} scadute` })
  if (s.pre > 0) dots.push({ dotClass: "bg-yellow-500", label: `${s.pre} imminenti` })
  if (dots.length === 0 && s.open > 0) dots.push({ dotClass: "bg-emerald-500", label: "in programma" })
  return dots
}
```

### 2.3 Pagina globale — `src/features/sal/SalPage.tsx`
**Clona `MilestonesPage.tsx`** adattando:
- **Titolo**: «SAL / Fatturazione Commesse»; sottotitolo «Piani di fatturazione a stati d'avanzamento di tutte le commesse.».
- **Query**: `projectsQuery` = `fetchProjects({page:1,pageSize:250})`; `summaryQuery` = `fetchSalSummary` (queryKey `["sal-summary"]`).
- **`PmSidebar`** (`storageKey="sal"`, `containersLabel="Commesse"`, `emptyLabel="Nessuna commessa con SAL"`):
  - **quickViews** = DUE voci:
    1. `{ key:"all", label:"Tutte le commesse", icon:<Euro/> (o <ReceiptText/>), count: activeProjects.length, selected: view==="all", onClick → view="all" }`
    2. `{ key:"prospetto", label:"Prospetto SAL", icon:<CalendarClock/>, count: (somma warn+pre dal summary), selected: view==="prospetto", onClick → view="prospetto" }`
  - **containers** = dal `summaryQuery`: `{ key:'p'+s.projectId, label: s.title?`${s.code} — ${s.title}`:s.code, count: s.open, dots: salSummaryDots(s), selected, onClick → seleziona commessa + view="perProject" }`.
- **Stato di vista**: `view ∈ "all" | "perProject" | "prospetto"` + `selectedProjectId`. Selezionando un contenitore → `view="perProject"` e mostra SOLO quella commessa; «Tutte» → tutte le attive; «Prospetto SAL» → tabella aggregata (§2.4).
- **Area principale**:
  - view `all`/`perProject` → per ogni commessa visibile una **card espandibile** (come `ProjectMilestoneCard`) con header `code — title` + pulsante «Apri» → `Link to={`/commesse/${p.id}/sal`}`; contenuto espanso = **`<ProjectSal projectId={p.id} />`** (riuso diretto, è autoconsistente). Nessuna toggle Tabella/Gantt qui.
  - view `prospetto` → `<SalProspettoView />` (§2.4).
- **Aggiorna**: pulsante che rifà `projectsQuery`/`summaryQuery` + `invalidateQueries(["sal"])`.

### 2.4 Vista Prospetto — `src/features/sal/SalProspettoView.tsx`
- `useQuery(["sal-prospetto"], fetchSalProspetto)`.
- Tabella shadcn con colonne: **Segnalazione · Commessa · Cliente · Scad.(ord) · Step SAL · % · Condizione · Importo · Ipotesi Fatturazione**.
- Colore riga da `row.alert` (`warn`/`pre`/`''`) riusando la logica colori di `salRowClass` (rosso/giallo/neutro) — oppure una pill «Scaduto»/«Pre-warning»/«In programma».
- Importo formattato €, data `formatDateWithWeekday`. Ordinata per commessa poi data (già così dal server). Vuoto → messaggio «Nessuna ipotesi di fatturazione aperta».

### 2.5 Wiring
- `src/app/AppRoutes.tsx` — import `SalPage`; aggiungi a `LIVE_ROUTES`: `"sal": <SalPage />`.
- `src/config/navigation.ts` — nel gruppo PM, **dopo** `milestones-summary`, aggiungi:
  ```ts
  { id: "sal", label: "SAL / Fatturazione", path: "/sal", featureKey: "nav.sal",
    icon: ReceiptText /* o Euro, da lucide-react */, status: "live",
    description: "Piani di fatturazione SAL di tutte le commesse + prospetto delle ipotesi di fatturazione aperte." },
  ```

Aggiorna `docs/HANDOFF-WEB.md` a fine lavoro (voce «Pagina PM SAL globale + Prospetto»).

---

## 3. Checklist di verifica — ✅ VERIFICATA (Claude, 07/07/2026)

> **Esito: conforme alla spec, completa, compila pulita.** `dotnet build` → 0 · `tsc -b` → 0 · `eslint` (SalPage+SalProspettoView) → 0. Verifica statica + build; **runtime GUI non ancora pilotato** (unico residuo).
> Note minori (non bloccanti): (a) in `SalProspettoView` il cast `row.alert as "warn"|"pre"|"none"` non copre il valore `''` (In programma), ma `salRowClass` lo gestisce col default (nessuno sfondo) → innocuo; (b) la sidebar/prospetto non hanno realtime cross-commessa (c'è «Aggiorna»), coerente con `MilestonesPage`.

**Backend**
- [x] `SalSummaryDto` aggiunto; `GET /api/sal/summary` con `HAVING COUNT(*)>0`, Warn/Pre in SQL con `CURDATE()` (regola lunedì sett. prec. per Pre).
- [x] Migrazione **v17** (`LatestSchemaVersion=17`, blocco `if (currentVersion<17)`, `INSERT ... schema_migrations (17,...)`, feature key `nav.sal`).
- [x] `dotnet build` → 0 errori.

**Frontend**
- [x] `fetchSalSummary` in `sal.ts` (`apiGet + unwrapApi`); tipo `SalSummary` in `types.ts`; `salSummaryDots`→`PmSidebarDot[]` in `sal-utils.ts`. Nessun `any`.
- [x] `SalPage.tsx` clona `MilestonesPage`: `PmSidebar` (storageKey `"sal"`), 2 viste rapide (**Tutte** + **Prospetto SAL**, count = Σ warn+pre), contenitori dal summary con `count=open` e dots warn/pre.
- [x] view `all`/`perProject`: card per commessa con contenuto = **`<ProjectSal projectId=… />`** (riuso), pulsante «Apri» → `/commesse/{id}/sal`.
- [x] view `prospetto`: `SalProspettoView` con tabella da `/api/sal/prospetto`, badge Scaduto/Pre-warning/In programma + righe colorate, importo € + data con giorno settimana.
- [x] Rotta `"sal"` in `LIVE_ROUTES` + voce nav PM `status:"live"` (`featureKey:"nav.sal"`, path `/sal`) dopo Milestones.
- [x] `tsc -b` e `eslint` puliti.

**Regressioni**
- [x] `ProjectSal.tsx` non modificato (solo riuso); migrazioni ≤ v16 intatte; il tab SAL nel dettaglio commessa resta invariato.

**Residuo**
- [ ] Verifica runtime GUI (server+Vite: sidebar/dots, card→ProjectSal, Prospetto colorato) — da fare su richiesta.
