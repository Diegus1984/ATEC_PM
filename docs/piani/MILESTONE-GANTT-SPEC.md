# Istruzioni per Antigravity — Gantt delle Milestone (Fase 3)

> Obiettivo: aggiungere una **vista Gantt** al tab «Milestone» del dettaglio commessa in
> `atec-pm-web`, **riusando l'infrastruttura Gantt del modulo Risorse**. È un lavoro
> **solo-client, solo-lettura**: nessuna modifica al DB o al server. Il controllo/verifica
> finale è a carico di chi commissiona (Claude).

---

## 0. Contesto — cosa esiste già (NON rifarlo)

Il modulo Milestone (Fasi 0–2) è **già fatto, girante e verificato**:

- **DB**: tabelle `activity_catalog` (catalogo globale) e `project_milestones` (per-commessa), migrazione schema **v15**. NON toccare il DB.
- **Server**: `MilestonesController` (`/api/milestones`) con GET `?projectId`, POST, PUT (`row_version`), DELETE, `/reorder`, `/project/{id}/seed-from-catalog`. NON serve nuovo server per il Gantt.
- **Web (già presente)**:
  - `src/lib/api/milestones.ts` — `fetchMilestones(projectId)`, CRUD, `reorderMilestones`, `seedMilestonesFromCatalog`.
  - `src/lib/api/types.ts` — tipi `Milestone` e `MilestoneSaveRequest`.
  - `src/features/milestones/milestone-utils.ts` — `weekLabel`, `weekTot`, `isoWeek`, `msStatus`, `statusRowClass`, `avgAvanz`, `periodo`, `buildMilestoneSave`.
  - `src/features/milestones/milestone-table.tsx` — la **tabella** editabile (NON modificarla se non per il toggle di vista).
  - `src/features/commesse/ProjectMilestones.tsx` — il **tab** nel dettaglio commessa (testata di sintesi + tabella). Qui va aggiunto il toggle Tabella/Gantt.
  - Realtime già attivo: `src/lib/signalr/use-milestones-hub.ts` (`MilestonesChanged` su `project-{id}`), agganciato in `ProjectMilestones`.

### Modello dati (già definito in `types.ts`)

```ts
interface Milestone {
  id: number
  projectId: number
  descrizione: string
  dataInizio: string | null   // ISO, può arrivare come "2026-07-13T00:00:00" → usare .slice(0,10)
  dataFine: string | null
  avanzamento: number | null  // 0..100 o null
  note: string
  evidenza: boolean           // hl (urgenza)
  spento: boolean             // riga esclusa da tabella/gantt/medio
  sortOrder: number
  rowVersion: number
  sourceCatalogId: number | null
}
```

---

## 1. Cosa costruire

### 1.1 Toggle di vista nel tab (`ProjectMilestones.tsx`)

Aggiungere nella testata del tab un selettore **`Tabella | Gantt`** (usa `Tabs`/`ToggleGroup` shadcn, o lo stesso pattern del toggle **Tabella|Card** del Gestore DDP / MoM). La scelta va **persistita per utente in `localStorage`** (chiave dedicata, es. `milestones:view`), come già fatto per le altre viste.

- `Tabella` → renderizza `<MilestoneTable ... />` (invariato).
- `Gantt` → renderizza il nuovo `<MilestoneGantt projectId items />`.

La query React Query e il realtime restano in `ProjectMilestones` (entrambe le viste consumano lo stesso `items`).

### 1.2 Nuovo componente `src/features/milestones/MilestoneGantt.tsx`

Un Gantt **read-only** con **una barra per milestone** (NON serve lane-packing: ogni milestone è la sua riga).

**Template di riferimento più vicino:** `src/features/risorse/FeriePage.tsx` (Gantt read-only, senza drag). Per header/barre/oggi/zoom vedi anche `src/features/risorse/ResourcePlannerPage.tsx`.

---

## 2. Cosa RIUSARE (per non reinventare)

### 2.1 Helper calendario — `src/features/risorse/planner-logic.ts`

Importa SOLO queste funzioni pure (NON importare `ResAssignmentDto` né la logica allocazioni):

- `parseDate(iso)`, `toIso(date)`, `addDays(date,n)`, `diffDays(a,b)`, `startOfDay(date)`
- `mondayOf(date)` — lunedì ISO della settimana
- `isWeekend(date)`, `isHoliday(date)` — festività nazionali IT + Lunedì dell'Angelo (già incluse)
- `monthName(m)`, `dowLetter(date)`, `dayCount(start,end)`

### 2.2 Utility milestone — `src/features/milestones/milestone-utils.ts`

- `isoWeek(date)` / `weekLabel(iso)` — numero settimana ISO 8601 (per la banda settimane)
- `msStatus(m)` → `"done" | "late" | "current" | "none"` — per il colore barra
- `statusRowClass(s)` — riferimento per le **famiglie colore** (teal/blu/rosso)

### 2.3 CSS — `src/features/risorse/risorse-gantt.css`

**Copiala** in `src/features/milestones/milestones-gantt.css` e adatta (rinomina le classi `g2-*` → `m-*`, cambia solo i colori barra). NON riusare direttamente le classi di Risorse (evita accoppiamento). Mantieni: bande mese/settimana/giorno, shading weekend/festivi, linea oggi.

### 2.4 Zoom / larghezza giorno (da `ResourcePlannerPage.tsx`)

Riusa l'idea `windowDays → dayWidth`: `{ 14: 46, 30: 32, 60: 20 }` px per giorno, con un `Select` di zoom. Persisti lo zoom in `localStorage` (vedi `src/features/risorse/use-planner-settings.ts` come esempio).

---

## 3. Requisiti dettagliati del Gantt

**Righe (pannello sinistro):**
- Una riga per ogni milestone **non spenta** (`!m.spento`), nello stesso ordine di `sortOrder`.
- Mostra: numero (i+1), descrizione (troncata con ellipsis), e opzionale W.In→W.Fine.
- Altezza riga fissa (allineata alla riga della timeline).

**Timeline (pannello destro):**
- **Intervallo (band):** `bandStart = mondayOf(min(dataInizio))` con ~1 settimana di margine prima; `bandEnd = max(dataFine) + ~1 settimana`; **includi SEMPRE `oggi`**. Se nessuna milestone ha date → stato vuoto («Nessuna data pianificata»).
- **Header a bande:** Mese (span sui giorni del mese) · Settimana (`W{isoWeek}`) · Giorno (numero + `dowLetter`, rosso se `isWeekend||isHoliday`).
- **Sfondo track:** shading weekend + festivi (come in Risorse, `repeating-linear-gradient` o celle) + linee giorno.
- **Linea «oggi»:** verticale a `diffDays(today, bandStart) * dayWidth` (solo se oggi è nel range).

**Barre (una per milestone):**
- Solo se ha **entrambe** le date. `left = diffDays(parseDate(inizio), bandStart) * dayW`; `width = dayCount(inizio, fine) * dayW` (min 1 giorno).
- **Riempimento avanzamento:** dentro la barra un riempimento largo `avanzamento%` (0 se null), in tono più pieno; il resto della barra in tono tenue.
- **Colore per stato** (`msStatus`), coerente con la **tenuità del Check list** già usata in tabella:
  - `done` = **teal** · `current` = **blu** · `late` = **rosso** · `none` = neutro/grigio.
  - Barra tenue (famiglia `-100/-200`), riempimento avanzamento più saturo (`-400/-500`). Niente colori sgargianti.
- **Etichetta** dentro/accanto alla barra: descrizione troncata + `avanzamento%`. `title`/tooltip con `inizio → fine` e `%`.
- `evidenza` (hl): bordo/indicatore più marcato (es. bordo ambra) senza cambiare la famiglia colore.

**Interazione (MVP = read-only):**
- Nessun drag/resize (arriverà in una fase successiva).
- Zoom (14/30/60 gg) + **scroll orizzontale**; allo `mount` scrolla a `oggi` (come Risorse: `scrollLeft = max(0,(todayIdx-2)*dayW)`).
- (Opzionale) click su una barra → evidenzia/scrolla la riga corrispondente. Non obbligatorio per l'MVP.

**Realtime:** nessun lavoro extra — il Gantt usa gli stessi `items` di `ProjectMilestones`, già aggiornati da `useMilestonesHub`.

---

## 4. Vincoli di progetto (OBBLIGATORI)

Leggi prima: `atec-pm-web/HANDOFF.md`, `atec-pm-web/BLOCKS-RULES.md`, `atec-pm-web/DESIGN-RULES.md`.

1. **Fedeltà shadcn / token**: usa i primitivi e i token colore/spazio del progetto; niente HTML/colori cablati fuori standard (le tinte Tailwind `teal/blue/red/violet-*` già usate in `milestone-table.tsx` sono ok).
2. **Verifica obbligatoria prima di chiudere**: `tsc -b` e `eslint` **puliti**.
   - ⚠️ In questa shell **Node non è nel PATH**: `$env:Path = "C:\Program Files\nodejs;" + $env:Path`, poi `cd atec-pm-web` e `.\node_modules\.bin\tsc.cmd -b` / `.\node_modules\.bin\eslint.cmd src/features/milestones`.
3. **Niente `window.confirm`** (usa `useConfirm` se servisse una conferma) — ma il Gantt read-only non dovrebbe averne bisogno.
4. **Commenti**: italiano per la logica di business, inglese per il tecnico.
5. **Non persistere valori derivati** (settimane, stato): sempre calcolati dalle date.
6. **Non toccare** `MilestonesController`, il DB, le migrazioni, `milestone-table.tsx` (se non per il toggle), né importare `ResAssignmentDto`.

---

## 5. File da creare / modificare

**Creare:**
- `src/features/milestones/MilestoneGantt.tsx` — il componente Gantt.
- `src/features/milestones/milestones-gantt.css` — copia adattata di `risorse-gantt.css` (classi `m-*`).
- (Opzionale) `src/features/milestones/use-milestone-gantt-settings.ts` — persistenza zoom/vista (sul modello di `use-planner-settings.ts`).

**Modificare:**
- `src/features/commesse/ProjectMilestones.tsx` — aggiungere il toggle `Tabella | Gantt` (persistito) e renderizzare `MilestoneGantt` quando è selezionato «Gantt».

---

## 6. Criteri di accettazione (per la verifica finale)

- [ ] `tsc -b` e `eslint` **puliti**; nessuna regressione alla tabella.
- [ ] Toggle **Tabella | Gantt** nel tab, scelta persistita per utente; la tabella resta identica.
- [ ] Gantt: una barra per milestone con date; **posizione corretta** (es. una milestone 13/07→20/07/2026 copre esattamente quei giorni), **larghezza** = durata.
- [ ] **Header** mese/settimana(ISO)/giorno corretti; **weekend/festivi** ombreggiati (incluso Lunedì dell'Angelo); **linea oggi** presente e allo scroll iniziale centrata su oggi.
- [ ] **Riempimento avanzamento** proporzionale (0/60/100% visibili); **colori** coerenti con la tabella (teal=completata, blu=in corso, rosso=in ritardo, neutro=senza date), tenui.
- [ ] Milestone **spente** escluse dal Gantt; milestone **senza date** non disegnano barra (o stato vuoto se nessuna ha date).
- [ ] **Zoom** (14/30/60 gg) e **scroll orizzontale** funzionanti; realtime ancora ok (modifica da un altro client aggiorna il Gantt).

---

## 7. Note tecniche / gotcha

- Le date arrivano dal server come `DateTime?` → possono essere `"2026-07-13T00:00:00"`. Usa sempre `parseDate(iso)` (fa `.slice(0,10)`) o `.slice(0,10)` prima di confrontare/renderizzare.
- **Una barra per riga**: NON serve `packLanes` (quello di Risorse serve perché più allocazioni stanno sulla stessa riga-risorsa).
- Le stringhe ISO `YYYY-MM-DD` si confrontano lessicograficamente (`a < b` funziona) — comodo per min/max e per «oggi».
- `avgAvanz`/`periodo` (in `milestone-utils.ts`) già escludono le righe spente: riusale se ti servono aggregati in testata.
- La tenuità colore di riferimento è in `statusRowClass` (Check list standard): mantienila per coerenza e minor affaticamento visivo.
- Il Gantt di Risorse è **accoppiato a `ResAssignmentDto`**: prendi solo gli helper puri di `planner-logic.ts` e la struttura di rendering, non i tipi/DTO.
