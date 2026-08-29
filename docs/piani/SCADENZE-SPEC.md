# Istruzioni per Antigravity — Pagina PM «Scadenze» (cruscotto unificato master-detail)

> Obiettivo: una pagina PM che raccoglie **TUTTE le scadenze** monitorate dalla campanella
> (SAL, Commesse, Check list, Azioni MoM, DDP articoli), a sinistra l'elenco, e cliccando una voce
> a destra compare il "colpevole" (l'entità che scade) con un pulsante **«Apri»** per andarci.
> Più un pulsante **«Vedi tutte le scadenze»** nel popover della campanella.
> Verifica finale a carico di **Claude**. Rispetta nomi di file/endpoint/feature-key qui indicati.

**Regole non negoziabili** (`HANDOFF.md`): fedeltà blocchi shadcn; un file API per dominio in
`src/lib/api/` con `apiGet/... + unwrapApi`; DTO reali dal controller; `dotnet build` + `tsc -b` +
`eslint` puliti; **niente `window.confirm`** (usa `useConfirm` se servono conferme). Sola lettura: nessuna scrittura.

---

## 0. Contesto — cosa esiste già (riusa, non rifare)

La campanella calcola già queste scadenze nel `NotificationBackgroundService` (`Services/NotificationService.cs`):
`CheckSalDeadlines`, `CheckProjectDeadlines`, `CheckMoMDeadlines`, `CheckChecklistDeadlines`, `CheckOverdueDdp`.
**Usa le STESSE definizioni di "scadenza aperta"** di quei metodi (stessi filtri/esclusioni) — vedi §1.2.

Navigazione al "colpevole": esiste già `src/features/notifications/notification-navigation.ts`
(`getNotificationHref` + `commessaSectionForReference`). La pagina Scadenze DEVE riusarla.
⚠️ **Bug da correggere lì**: `commessaSectionForReference` NON mappa `SAL_ROW` → va aggiunto
`case "SAL_ROW": return "sal"` (oggi le scadenze SAL finiscono sulla sezione "details").

Campanella UI: `src/features/notifications/NotificationsBell.tsx` (popover in header).

---

## 1. Backend — endpoint unificato

### 1.1 DTO — nuovo `ATEC.PM.Shared/DTOs/Deadlines_DTOs.cs`
```csharp
namespace ATEC.PM.Shared.DTOs;

// Una scadenza generica (unione di 5 domini). Type ∈ SAL|PROJECT|CHECKLIST|MOM|DDP.
public class DeadlineDto
{
    public string Type { get; set; } = "";      // SAL|PROJECT|CHECKLIST|MOM|DDP
    public string RefType { get; set; } = "";    // reference_type per la navigazione: SAL_ROW|PROJECT|CHECKLIST|MOM_ACTION|BOM
    public int RefId { get; set; }               // id della riga sorgente
    public int? ProjectId { get; set; }          // commessa (null per check list di gruppo generico)
    public string Code { get; set; } = "";       // codice commessa (o "")
    public string Title { get; set; } = "";      // titolo commessa / contesto
    public string Description { get; set; } = ""; // step SAL / attività / part_number+descr / ...
    public DateTime DueDate { get; set; }
    public int Days { get; set; }                // DATEDIFF(DueDate, oggi): <0 scaduta, 0..N imminente
}
```

### 1.2 Controller — nuovo `Controllers/DeadlinesController.cs` (`[Route("api/deadlines")] [Authorize]`)
`GET /api/deadlines` → `List<DeadlineDto>`. Un **UNION ALL** dei 5 domini, poi `DATEDIFF` e `ORDER BY DueDate`.
Le clausole di "aperta" **devono ricalcare** i check della campanella:

- **SAL** (`SAL_ROW`): `sal_rows sr JOIN projects p ON p.id=sr.project_id` — `sr.data_fatt IS NOT NULL AND sr.stato=''`. Description=`sr.step`.
- **PROJECT** (`PROJECT`): `projects p` — `p.end_date_planned IS NOT NULL AND p.end_date_actual IS NULL AND p.status='ACTIVE'`. Description=`p.title`, DueDate=`p.end_date_planned`.
- **CHECKLIST** (`CHECKLIST`): `checklist_items i LEFT JOIN projects p ON p.id=i.project_id` — `i.due_date IS NOT NULL AND i.status<>'CLOSED'`. Description=`i.description`, ProjectId può essere NULL (gruppo generico).
- **MOM** (`MOM_ACTION`): `mom_action_items a JOIN mom_records m ON m.id=a.mom_id LEFT JOIN projects p ON p.id=m.project_id` — `a.data_check IS NOT NULL AND a.status<>'CLOSED'`. Description=`a.attivita`, DueDate=`a.data_check`, ProjectId=`m.project_id`.
- **DDP** (`BOM`): `bom_items b JOIN projects p ON p.id=b.project_id` — `b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered AND COALESCE(b.item_status,'') NOT IN @Excluded`. `@Delivered = {CON,COS,DISP,ASS,MOD}` (vedi `DdpDeliveredStatuses`); `@Excluded` = set aggregazione **A9** caricato in C# con `DdpAggregationSet.Load(c,"A9")` e passato come parametro (come fa `CheckOverdueDdp`). Description=`CONCAT(b.part_number,' - ',b.description)`, DueDate=`b.date_needed`.

Ogni SELECT proietta le stesse colonne (`Type, RefType, RefId, ProjectId, Code, Title, Description, DueDate`). Outer: `SELECT u.*, DATEDIFF(u.DueDate, CURDATE()) AS Days FROM ( ...union... ) u ORDER BY u.DueDate ASC`.
> Include TUTTE le scadenze aperte (anche future): il filtro per orizzonte/urgenza è lato client.

### 1.3 Feature key nav — migrazione **v18** (`Services/DbService.cs`)
Come il blocco v17: `LatestSchemaVersion` → **18**, blocco `if (currentVersion < 18)` con
`INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES ('nav.scadenze','Scadenze','navigation',2,'HIDDEN')`
e `INSERT IGNORE INTO schema_migrations (version, description) VALUES (18, 'nav.scadenze feature key')`.

---

## 2. Frontend

### 2.1 API + tipi
- `src/lib/api/deadlines.ts` — `export async function fetchDeadlines(): Promise<Deadline[]>` (`apiGet + unwrapApi` su `/api/deadlines`).
- `src/lib/api/types.ts` — `Deadline` (camelCase del DTO: `type, refType, refId, projectId(number|null), code, title, description, dueDate(string), days`).

### 2.2 Navigazione al "colpevole" (riuso + fix)
- In `src/features/notifications/notification-navigation.ts`: aggiungi `case "SAL_ROW": return "sal"` in `commessaSectionForReference`.
- Aggiungi un helper riusabile `deadlineHref(d: Deadline): string | null` (nello stesso file o in `deadlines`), costruito sulla stessa logica di `getNotificationHref` ma partendo dal `Deadline` (usa `d.refType`, `d.projectId`, `d.refId`). Per CHECKLIST/MOM senza `projectId` → `/checklist` / `/mom`.

### 2.3 Pagina master-detail — `src/features/scadenze/ScadenzePage.tsx`
- **Query**: `useQuery(["deadlines"], fetchDeadlines, { refetchInterval: 60_000, refetchOnWindowFocus: true })` (cruscotto sempre fresco senza realtime multi-dominio).
- **Layout 2 colonne** (fedele ai blocchi; niente PmSidebar, è una lista di scadenze non di commesse):
  - **Sinistra** (lista, ~360px, scrollabile): 
    - **Filtri per tipo** = chip/toggle (SAL, Commesse, Check list, MoM, DDP) + ricerca testo + toggle «solo da gestire» (mostra solo `days <= N`, default N=7) vs «tutte».
    - Ordine per urgenza: `days` crescente (prima le scadute). 
    - Ogni voce: **badge stato** (🔴 Scaduta `days<0` · 🟡 Imminente `0..7` · ⚪ In programma `>7`), **tipo**, **commessa** (`code`), **descrizione** troncata, **data** (`formatDateWithWeekday`) + «scaduta da N g / tra N g». Selezione evidenziata.
  - **Destra** (dettaglio del selezionato = "colpevole"): card con Tipo, Commessa (code+title), Descrizione completa, Data, stato; e **pulsante «Apri»** → `navigate(deadlineHref(sel))` (disabilitato se href null). Stato vuoto se nulla selezionato.
- Colori stato coerenti con la tenuità già usata (rosso/giallo/neutro).
- Stati loading/errore/empty.

### 2.4 Pulsante nella campanella
- In `NotificationsBell.tsx` (nel footer del popover, vicino a «segna tutte lette»): pulsante/link **«Vedi tutte le scadenze»** → `navigate("/scadenze")` e chiudi il popover.

### 2.5 Wiring
- `src/app/AppRoutes.tsx`: import `ScadenzePage` + `"scadenze": <ScadenzePage />` in `LIVE_ROUTES`.
- `src/config/navigation.ts`: voce nel gruppo **PM** (dopo Milestone/SAL), `{ id:"scadenze", label:"Scadenze", path:"/scadenze", featureKey:"nav.scadenze", icon: AlarmClock /* da lucide-react */, status:"live", description:"Cruscotto unificato di tutte le scadenze (SAL, commesse, check list, MoM, DDP): elenco a sinistra, dettaglio del 'colpevole' a destra." }`.

Aggiorna `docs/HANDOFF-WEB.md` a fine lavoro.

---

## 3. Checklist di verifica (la userà Claude — non cancellarla)

**Backend**
- [ ] `DeadlineDto` + `DeadlinesController` `GET /api/deadlines` con UNION ALL dei 5 domini; filtri "aperta" identici ai check della campanella (SAL/PROJECT/CHECKLIST/MOM/DDP); DDP usa set A9 via `DdpAggregationSet.Load`; `Days=DATEDIFF`; `ORDER BY DueDate`.
- [ ] Migrazione **v18** (`LatestSchemaVersion=18`, blocco `if(currentVersion<18)`, feature key `nav.scadenze`, `schema_migrations (18,...)`).
- [ ] `dotnet build` → 0 errori. Nessuna scrittura DB (sola lettura).

**Frontend**
- [ ] `fetchDeadlines` in `deadlines.ts` (`apiGet+unwrapApi`); tipo `Deadline` in `types.ts`; nessun `any`.
- [ ] `notification-navigation.ts`: aggiunto `SAL_ROW → "sal"`; helper `deadlineHref` riusabile.
- [ ] `ScadenzePage.tsx`: master-detail (lista sinistra filtrabile per tipo + ricerca + toggle da-gestire; dettaglio destra con «Apri» → `deadlineHref`), stato badge scaduta/imminente/in-programma, refetch 60s.
- [ ] `NotificationsBell.tsx`: pulsante «Vedi tutte le scadenze» → `/scadenze`.
- [ ] Rotta `"scadenze"` in `LIVE_ROUTES` + voce nav PM `status:"live"` (`nav.scadenze`, `/scadenze`).
- [ ] `tsc -b` e `eslint` puliti.

**Regressioni**
- [ ] Nessuna modifica ai check della campanella né alle migrazioni ≤ v17; la modifica a `notification-navigation.ts` è solo additiva (SAL_ROW) e non rompe la navigazione delle notifiche esistenti.
