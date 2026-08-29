# Regole blocchi UI — ATEC PM Web

> **Scopo:** ogni nuova pagina deve essere **fedele ai blocchi ufficiali** di
> [ui.shadcn.com/blocks](https://ui.shadcn.com/blocks). Non si inventano layout:
> si **compone** dai blocchi/primitivi esistenti, riusando le stesse strutture,
> classi e spaziature. Questo file è la fonte di verità per il layout delle pagine;
> [DESIGN-RULES.md](DESIGN-RULES.md) resta la fonte per preset/tema/token.

## Regola d'oro

1. **Parti sempre da un blocco**, non da un `<div>` vuoto. Trova il blocco shadcn
   che corrisponde al tipo di pagina (tabella sotto) e replicane la struttura.
2. **Mai HTML raw** se esiste il primitivo: `Card`, `Button`, `Input`, `Table`,
   `Tabs`, `Dialog`, `Badge`, `DropdownMenu`, `Sidebar`, `Breadcrumb`, `Skeleton`,
   `Alert`, `Empty`, `Switch`, `Pagination`.
3. **Mai colori/spaziature ad-hoc.** Solo token (`bg-muted`, `text-muted-foreground`,
   `border`, `gap-4`…) e le stringhe di classe canoniche qui sotto.
4. **Copia le classi canoniche alla lettera** — sono prese dai blocchi ufficiali e
   dal codice già allineato (`AppShell`, `DashboardSectionCards`). Non riscriverle.
5. **Conferma obbligatoria su OGNI azione distruttiva (NON negoziabile).** Qualunque
   pulsante o voce di menu che **elimina, cancella, disattiva/cessa, rimuove una riga o
   un collegamento, ripristina un backup (sovrascrive) o resetta credenziali** DEVE
   chiedere conferma con `useConfirm()` (`@/components/shared/confirm`) **prima** di
   eseguire. **Mai** `window.confirm`/`window.prompt`, **mai** eseguire diretto. Non
   sono distruttive (niente conferma): svuotare il valore di un campo, annullare una
   bozza non salvata, chiudere un dialog, "Annulla", logout.

## Mappa pagina ATEC PM → blocco shadcn

| Pagina ATEC PM | Blocco di riferimento | Pattern |
|----------------|-----------------------|---------|
| App shell (sidebar+header+content) | `dashboard-01` + `sidebar-07` | `SidebarProvider` > `Sidebar variant="inset" collapsible="icon"` + `SidebarInset` |
| Dashboard | `dashboard-01` | SectionCards (KPI) → chart → DataTable |
| Liste (Commesse, Clienti, Fornitori, Utenti, MoM, Backup…) | `dashboard-01` (DataTable) | Header pagina + `Card` con toolbar + `Table` |
| Dettaglio (Commessa) | `sidebar-03`/`dashboard-01` | `Tabs` per sotto-moduli + card/grid |
| Config con albero (Config Sezioni, Template) | `sidebar-09`/`sidebar-11` | pannello + tree, `Card` contenitore |
| Login | `login-03` | card centrata `max-w-sm` su `bg-muted` |
| Cambio password / dialog | `Dialog` | form in `DialogContent` |
| Date (Timesheet, Ferie, DDP) | `calendar-*` | `Popover` + `Button` + `Calendar` |

## 0. App shell — CONGELATO

`AppShell.tsx` è già l'implementazione fedele di `dashboard-01`. **Non duplicarlo
nelle pagine.** Una pagina è solo ciò che sta dentro `<Outlet/>`: il guscio
fornisce già sidebar, header e il **padding di contenuto**:

```
@container/main flex flex-1 flex-col gap-4 px-4 py-4 md:gap-6 md:py-6 lg:px-6
```

→ Una pagina **non aggiunge padding esterno**. Restituisce sezioni in pila con
`space-y-4` (o `space-y-6`), oppure una griglia. Il container query `@container/main`
è già attivo: usa i breakpoint `@xl/main:`, `@5xl/main:` nelle griglie.

## 1. Pagina dashboard / overview (da `dashboard-01`)

Ordine canonico: **SectionCards → grafico → tabella**.

```tsx
<>
  <SectionCards data={...} />            {/* griglia KPI, §3 */}
  <div className="px-4 lg:px-6">         {/* il grafico ha padding suo */}
    <ChartCard ... />
  </div>
  <DataTable ... />                       {/* §2 */}
</>
```

## 2. Pagina lista / indice (pattern modulo ATEC PM)

Struttura standard già usata in tutte le pagine admin — **replicala identica**:

```tsx
<div className="space-y-4">
  <Card>
    <CardHeader>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <CardTitle>Titolo modulo</CardTitle>
          <CardDescription>Sottotitolo / contesto.</CardDescription>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm"><RefreshCw />Aggiorna</Button>
          <Button size="sm"><Plus />Nuovo</Button>
        </div>
      </div>
    </CardHeader>
    <CardContent>
      {/* filtri opzionali sopra la tabella, poi <Table> */}
      <Table> … </Table>
    </CardContent>
  </Card>
</div>
```

Regole tabella (da DataTable di `dashboard-01`):
- Usa **sempre** `@/components/ui/table` (`TableHeader/Row/Head/Body/Cell`), mai `<table>`.
- Azioni di riga: **mai** icone sparse in-linea → un solo menu `⋮` a fine riga con `RowActionsMenu` (modifica, elimina, ecc.); le distruttive con `destructive: true` + `useConfirm()`.
- Doppio click sulla riga per aprire il dettaglio/modifica (convenzione già in uso).
- Liste grandi (molte colonne, sort, visibilità colonne) → **`DataTableCard`** (`@/components/shared/data-table-card`), lo standard del progetto: incapsula header `bg-muted/50`, ricerca globale, menu **«Colonne»**, selezione righe, stati loading/vuoto/errore e footer. La pagina fornisce **solo** `columns` (con header ordinabili + azioni `⋮`) + `data` + `toolbarActions` (es. «Aggiungi») + il dialog. Esempi: `features/clienti/ClientiPage.tsx`, `features/fornitori/FornitoriPage.tsx`. Non reimplementare la tabella a mano.
- Filtri/ricerca → riga toolbar sopra la tabella (`Input` + `Select`/`DropdownMenu`).
- Stato vuoto → riga unica con `colSpan` e `text-center text-muted-foreground` (§6).

## 3. Section cards / KPI (blocco ufficiale `SectionCards`)

`DashboardSectionCards.tsx` è il blocco ufficiale. Per nuovi KPI **clona questa anatomia**:

```tsx
<div className="grid grid-cols-1 gap-4 *:data-[slot=card]:bg-gradient-to-t *:data-[slot=card]:from-primary/5 *:data-[slot=card]:to-card *:data-[slot=card]:shadow-xs @xl/main:grid-cols-2 @5xl/main:grid-cols-4 dark:*:data-[slot=card]:bg-card">
  <Card className="@container/card">
    <CardHeader>
      <CardDescription>Etichetta</CardDescription>
      <CardTitle className="text-2xl font-semibold tabular-nums @[250px]/card:text-3xl">
        {valore}
      </CardTitle>
      <CardAction>
        <Badge variant="outline"><TrendingUp />delta</Badge>
      </CardAction>
    </CardHeader>
    <CardFooter className="flex-col items-start gap-1.5 text-sm">
      <div className="line-clamp-1 flex gap-2 font-medium">riga 1 <TrendingUp className="size-4" /></div>
      <div className="text-muted-foreground">riga 2</div>
    </CardFooter>
  </Card>
  {/* ...altre card. Numeri sempre tabular-nums. */}
</div>
```

- Valori numerici: **`tabular-nums`** sempre.
- Trend: icona `TrendingUp`/`TrendingDown` in `Badge variant="outline"`.
- Mai più di 4 card per riga (`@5xl/main:grid-cols-4`).

## 4. Form e dialog (da `Dialog` + login form)

- Form: `<form className="flex flex-col gap-4">`, ogni campo `<div className="grid gap-2"><Label/><Input/></div>`.
- Dialog: `Dialog > DialogContent > DialogHeader(DialogTitle) > {corpo space-y-4} > DialogFooter`.
- Footer: `Button variant="outline"` Annulla a sinistra, primario a destra.
- Errori: `<p className="text-sm text-destructive">`.
- Checkbox/flag: primitivo `Checkbox` con label `flex items-center gap-2 text-sm`.
- **Sempre il `Select` shadcn** (`@/components/ui/select`), mai `<select>` nativo —
  per coerenza visiva. Pattern: `Select value onValueChange` > `SelectTrigger className="w-full"` (`size="sm"` se inline in tabella) > `SelectValue` > `SelectContent` con `SelectItem`. ⚠️ `SelectItem` **non** accetta `value=""`: per l'opzione «(nessuno)/(seleziona)» usa un sentinella (`const NONE = "__none__"`) e converti in `null` nell'`onValueChange`.

## 5. Login (`login-03`) — CONGELATO

`LoginPage.tsx` è già `login-03`: card `max-w-sm`, `min-h-svh`, `bg-muted/40`,
`p-6 md:p-10`. Non cambiare il pattern; per aggiungere campi resta dentro la `Card`.

## 6. Stati: loading / vuoto / errore

Coerenza obbligatoria su ogni pagina che carica dati:

```tsx
{isLoading ? <p className="text-sm text-muted-foreground">Caricamento…</p> : null}
{error ? <p className="text-sm text-destructive">{(error as Error).message}</p> : null}
{/* vuoto (fuori tabella / pannelli): */}
<Empty>
  <EmptyHeader>
    <EmptyTitle>Nessun elemento</EmptyTitle>
    <EmptyDescription>Suggerimento per iniziare.</EmptyDescription>
  </EmptyHeader>
</Empty>
{/* errore persistente pagina/sezione: */}
<PageErrorAlert message="…" />
```

- Skeleton di caricamento per tabelle/card pesanti → primitivo `Skeleton`.

## 7. Date picker (pattern `calendar-*`)

Per le tante date di ATEC PM (timesheet, ferie, DDP) usa **sempre** la composizione canonica:
`Popover` + `Button variant="outline"` (con icona `CalendarIcon` e data formattata `it-IT`)
+ `Calendar` nel `PopoverContent`. Range → `Calendar mode="range"`. Mai `<input type="date">`.

## Componenti condivisi (master già pronti) — riusa, NON rifare

> **Prima di creare un componente UI, controlla qui: probabilmente esiste già.**
> Questi sono i "master" del progetto (come `DataTableCard` per le tabelle): hanno
> già stile, token, comportamento e accessibilità corretti. Le pagine li **usano**,
> non li riscrivono. Se ne crei uno nuovo riusabile, aggiungilo a questa tabella.

| Componente | Import | A cosa serve |
|------------|--------|--------------|
| `AppShell` | `@/app/AppShell` | Guscio sidebar+header+content (`dashboard-01`). CONGELATO, non duplicare. |
| `DataTableCard` | `@/components/shared/data-table-card` | Tabella standard client-side: header, ricerca globale, «Colonne», selezione, sort, stati loading/vuoto/errore. La pagina passa solo `columns`+`data`+`toolbarActions`+dialog. |
| `DataTableCardFiltered` | `@/components/shared/data-table-card-filtered` | Variante di `DataTableCard` con **ricerca per colonna** (una casella sotto ogni intestazione filtrabile) + ricerca globale (disattivabile con `enableGlobalSearch={false}`) + pulsante «Pulisci filtri». **Drop-in**: stesse props, basta cambiare l'import. ⚠️ I filtri di colonna si combinano in **AND** — affinano la ricerca, vedi regola sotto. |
| `GridScroller` | `@/components/shared/grid-scroller` | Scroller standard attorno a una `<Table>` (o `<table>` nativa) nelle griglie scritte a mano: righe che scorrono dentro la griglia fino a `--grid-max-h`, **intestazione fissa**, **barra orizzontale in alto**. `DataTableCard`/`Filtered` lo usano già dentro. Vedi regola sotto. |
| `ColumnsMenu` | `@/components/shared/columns-menu` | Menu **«Colonne»** (visibilità colonne). Master unico: si adatta alla voce più larga (`w-auto`), spazio per la spunta, niente a-capo. La pagina passa `columns: { id, label, checked, onToggle }[]`. `DataTableCard` lo usa internamente; per liste/tabelle custom (es. Codex, dashboard) usalo direttamente. |
| `SortableHeader` | `@/components/shared/sortable-header` | Intestazione colonna **ordinabile server-side** (icona neutro/asc/desc). Per liste paginate (Catalogo, Codex): la pagina tiene `SortState { by, dir }` e lo passa alla query API (`sortBy`/`sortDir`). |
| `RowActionsMenu` | `@/components/shared/row-actions` | Menu **`⋮`** azioni di riga (modifica/elimina/…). Passa `actions: RowAction[]`. |
| `StatusDot` / `ActiveStatus` | `@/components/shared/status-dot` | Indicatore di stato pallino+etichetta (mai `Badge` per attivo/disattivo). |
| `useConfirm()` | `@/components/shared/confirm` | Conferma HMI OK/Annulla per azioni distruttive (mai `window.confirm`). |
| `notifyError` / `notifySuccess` | `@/lib/toast` | Feedback errori/successi non bloccante (mai `window.alert`). `Toaster` montato in `App.tsx`. |
| `PageErrorAlert` | `@/components/shared/page-error-alert` | Errore persistente a livello pagina/sezione (`Alert` destructive). Non per dialog/form inline. |
| `ServerPagination` | `@/components/shared/server-pagination` | Footer paginazione **server-side** (Codex, Catalogo). |
| `TablePagination` | `@/components/shared/table-pagination` | Footer paginazione **client-side** TanStack Table. |
| `ModulePlaceholder` | `@/components/shared/ModulePlaceholder` | Placeholder per moduli non ancora `live`. |
| `LookupCombobox` | `@/components/shared/lookup-combobox` | **Tendina lunga con ricerca integrata** (clienti, commesse, dipendenti, sezioni…): pulsante come una `Select` + popover con casella «Cerca…». Props: `options: {id,name,group?,hint?}[]`, `value`, `onValueChange`, `noneLabel` (voce «— nessuna —»), `action` (voce finale tipo «Nuovo…»). |
| `useDebounced(value, ms)` | `@/lib/use-debounced` | Debounce per ricerche/typeahead. |

### Regola — le tendine lunghe sono combo con ricerca (standard, 03/08/2026)

Una `Select` va bene **solo** per elenchi fissi e corti (stati, priorità, tipi, UM).
Appena le voci arrivano da un'anagrafica (clienti, commesse, dipendenti, verbali,
sezioni di costo, categorie…) si usa **`LookupCombobox`**: la ricerca sta dentro la
tendina, si digitano due lettere invece di scorrere centinaia di righe, e Radix non
monta tutte le voci a menu chiuso (una `Select` sì: rallenta l'intero dialog).

Senza testo digitato mostra le prime 100 voci con l'avviso «+N altri — digita per
cercare». Per fornitori e clienti in contesti inline restano validi
`SupplierSearchCombobox` / `CustomerSearchCombobox` (input type-ahead, non a tendina).

### Regola — ogni griglia ha il menu «Colonne» (standard, 31/07/2026)

**Ogni griglia dati ha SEMPRE il menu «Colonne»** per scegliere cosa vedere: è uno
standard di prodotto, non una decisione per pagina. Griglia dati = tabella che elenca
record che l'utente sfoglia (righe DDP, attività, milestone, articoli, prodotti…).

- Il menu è **`ColumnsMenu`** (`@/components/shared/columns-menu`), mai fatto a mano.
  Con `DataTableCard` / `DataTableCardFiltered` arriva già incluso: non aggiungerne un
  secondo.
- La visibilità si **persiste in localStorage** con
  **`usePersistedColumnVisibility(chiave, defaults)`** (`@/lib/use-persisted-column-visibility`).
  **Chiave versionata** (`…-columns-v1`): cambiando gli id delle colonne si **alza la
  versione**, altrimenti il salvato di ieri nasconde le colonne di oggi.
- Le colonne opzionali si dichiarano una volta in cima al file
  (`const X_COLUMNS = [{ id, label }]` + `X_COLUMNS_DEFAULT`), e header **e** celle si
  rendono con lo stesso `show(id)`. `colSpan` delle righe «vuoto / aggiungi / totale»
  va **calcolato** dalle colonne accese, mai cablato a un numero.
- **Restano fisse** (fuori dal menu) solo le colonne strutturali: spunta di selezione,
  espansore, la colonna identità della riga (Descrizione/Attività/Nome/Codice) e la
  colonna azioni `⋮`.
- Più tabelle sulla stessa pagina che mostrano gli stessi record (una card per
  commessa/gruppo) → **un solo menu** in toolbar e stato condiviso via context
  (`ChecklistColumnsProvider`), non un menu per card.
- Se la vista ha una stampa/export «quello che vedo» (Report di Controllo), l'export
  segue le colonne accese.

**Fuori standard** (niente menu): dialoghi e picker, pagine di configurazione admin,
matrice permessi, fogli a struttura fissa (verbale MoM, foglio SAL, Flusso di Cassa,
Preventivo vs Consuntivo, costing preventivi) e tabelle con 2-3 colonne tutte
indispensabili (Codex Ricodifica, MoM Note, Composizione Gamma).

### Regola — intestazione fissa + barra orizzontale in alto (standard, 03/08/2026)

**In ogni griglia le righe scorrono DENTRO la griglia, l'intestazione resta ferma in
alto e la barra di scorrimento orizzontale sta SOPRA la griglia** (non in fondo, dove
per vederla bisognerebbe scorrere fino all'ultima riga). Standard di prodotto, non una
decisione per pagina.

- Si ottiene **solo** con **`GridScroller`** (`@/components/shared/grid-scroller`), mai
  a mano: sostituisce il vecchio `<div className="overflow-x-auto rounded-lg border">`
  attorno alla `<Table>`. Il bordo/raggio va nel suo `className`.
- `DataTableCard` / `DataTableCardFiltered` lo usano **già dentro**: le pagine che
  passano da lì non devono fare nulla (`stickyHeader` e `topScrollbar` sono `true` di
  default, si disattivano solo in casi motivati).
- L'altezza massima dello scroller è il token **`--grid-max-h`** (`index.css`, 65vh):
  si cambia **lì per tutte** le griglie. Per una singola griglia:
  `scrollerClassName="max-h-[40vh]"`.
- Griglia dentro un pannello che ne stabilisce già l'altezza (colonne a tutta pagina,
  dialoghi in flex) → **`<GridScroller fill>`**: riempie il genitore invece di usare
  `--grid-max-h`.
- **Niente `sticky top-0` sul `TableHeader`** e niente `[&>div]:overflow-visible` nelle
  pagine: le regole (a specificità zero, `:where(.grid-sticky-head …)` in `index.css`)
  ci pensano da sole e restano sovrascrivibili con una classe Tailwind sul `<th>`.
- Vale anche per le tabelle HTML native (matrice transizioni DDP, Flusso di Cassa,
  distribuzione prezzo, anteprime di import): `GridScroller` lavora su qualsiasi
  `<thead>`.
- Vale **anche in dialoghi e picker**: lì la griglia sta in un `DialogContent`
  `flex flex-col` → `<GridScroller fill>`. Se il picker ha lo **scroll infinito**,
  l'handler va passato con la prop **`onScroll`** (il componente lo richiama dopo aver
  sincronizzato la barra in alto), mai rimesso su un `div` esterno.

### Regola — filtri per colonna in AND (mai OR)

Nella grid con ricerca per colonna (`DataTableCardFiltered`) i filtri delle varie
colonne si combinano **sempre in AND**: una riga compare solo se **supera tutti** i
filtri attivi. Riempire più colonne **affina** (restringe) la ricerca, non la allarga.
Anche la ricerca globale si combina in AND con i filtri di colonna.

- È il comportamento **di default** di TanStack Table (`getFilteredRowModel` + stato
  `columnFilters`): non c'è e **non va aggiunto** nulla per ottenerlo.
- **Non introdurre `filterFn` custom che cambino la combinazione** tra colonne (es. che
  trasformino l'AND in OR). Un `filterFn` per colonna è ammesso solo per cambiare il
  *matching su quella singola colonna* (es. date/valuta/badge che non filtrano col
  "contiene" di default), mai per alterare la logica AND fra colonne.

### Regola — la riga si adatta al testo, mai testo tagliato

In **tutte le tabelle** con celle di testo su più righe, la riga deve **crescere in
altezza** fino a mostrare tutto il contenuto: se il testo va a capo e la cella resta
alta com'era, quello che sta sotto non si legge più.

- Celle di testo lungo/multi-riga → `<Textarea rows={1}>` con
  **`field-sizing-content min-h-8 resize-none`**. Fa tutto il browser, niente JS.
- **Mai** `h-8`/altezza fissa + `overflow-hidden` su una cella editabile a riposo, e mai
  altezze calcolate solo quando il campo ha il focus: fuori dal focus il testo sparirebbe.
- Non mettere altezze fisse sulla `TableRow`: deve seguire la cella più alta.
- Celle di sola lettura con testo lungo (Descrizione, Note) → `max-w-[Npx]` +
  **`whitespace-normal break-words`**. Nota: `TableCell` di shadcn ha `whitespace-nowrap`
  di default, quindi `whitespace-normal` va messo esplicitamente o il testo non va a capo.
- `truncate` (una riga + puntini) va bene solo dove la riga **deve** restare compatta
  (alberi, sidebar, chip, legende dei grafici) e il testo intero è comunque nel `title`.
- Campi corti per natura (codice, Rif. Danea, N° ordine, quantità) restano `<Input>` a
  riga singola: `DdpInlineTextCell` diventa textarea solo con `multiline`.

Conformi: Check list (`AutoTextarea`), Milestone (`GrowTextarea`), MoM (`mom-sheet`),
SAL (`sal-sheet-fields`), Lavorazioni (`inline-fields`), DDP Commerciale/Officina,
Acquisti, Gestore DDP (Descrizione e Note, decisione utente del 31/07/2026).

### Regola — importi sempre con € e due decimali

Ogni importo mostrato all'utente, **in tutto ATEC PM**, si formatta con **`euro()`**
(`@/lib/format`): due decimali, virgola decimale, punto delle migliaia e simbolo € in
coda (`4.000,00 €`). Vale per celle di tabella, totali, KPI, etichette dei grafici,
stampe e dialog.

- **Mai** formattare a mano: niente `` `${fmt2(x)} €` ``, `x.toFixed(2)`,
  `toLocaleString` con currency. Producono varianti incoerenti (`4000,00€`) e prima o
  poi divergono.
- `fmt2()` resta solo per numeri **non monetari** (ore, quantità, percentuali).
- **Campi di importo → `MoneyInput`** (`@/components/shared/money-input`), **mai un
  `<Input>` nudo**: mostra `100.000,00 €` a riposo e il numero grezzo appena ci clicchi
  dentro, normalizza il valore al blur (così chi salva con `parseDecimal` non inciampa su
  «1.234,50») e allinea a destra. Firma: `value` stringa + `onChange(value)`, opzionale
  `onCommit` (blur/Invio).
- Le tacche dell'asse Y dei grafici possono restare compatte (`4.000`): l'unità sta nel
  titolo/tooltip, altrimenti l'asse diventa illeggibile.
- **Griglie a molte colonne** (Cash Flow commessa: 13 mesi): il `€` completo sta sulla
  colonna «Totale» e sulle righe calcolate; le celle ripetute possono restare compatte
  passando `format` a `MoneyInput` (es. `format={n0}`) e dichiarando «Importi in €»
  nell'intestazione. Il valore raccolto e la normalizzazione al blur non cambiano.

### Regola — liste di card in uno scroller: contenitore a BLOCCO, mai flex (04/08/2026)

Una lista di `Card` dentro un'area che scorre (`overflow-y-auto`) usa **`space-y-*`**, non
`flex flex-col gap-*`. Motivo tecnico, non estetico: `Card` ha `overflow-hidden`, e per le
regole del CSS un flex item con `overflow` diverso da `visible` ha **min-height automatica 0**.
Quindi in un contenitore flex, appena le card superano l'altezza disponibile, il browser **le
comprime tutte** e `overflow-hidden` taglia il contenuto — nel caso reale (18 commesse in `/sal`)
le card sono finite **a 0px di altezza** con la riga «Cliente / PM» tagliata a metà. Misurato,
non ipotizzato: stesso DOM, contenitore flex → 18 card tagliate; contenitore a blocco → 0.

Non si vede con poche card (in sviluppo ce n'erano due e sembrava tutto a posto): **si vede solo
con la lista piena**. Se una lista di card sta in uno scroller, `space-y-*` e via.

Se per qualche motivo il contenitore deve restare flex, alle card serve **`shrink-0`**.

### Regola — il `!` di Tailwind v4 va in CODA

`pb-3!`, non `!pb-3`. La forma con il punto esclamativo davanti è quella di Tailwind 3: in v4
**non genera CSS**, quindi la regola che volevi forzare semplicemente non esiste e te ne accorgi
solo guardando il risultato. Capitato il 04/08/2026 su `[.border-b]:pb-3` nelle card di SAL e
Bilancio: serviva a battere la variante di serie di `CardHeader`
(`[.border-b]:pb-(--card-spacing)`, che a card espansa mette 24px sotto contro i 12 di sopra) e
non faceva niente. Nel dubbio: `grep` della classe nel CSS generato in `dist/assets/*.css` —
se non c'è, non esiste.

### Regola — barra laterale PM: «Commesse» e «Altre commesse» separate (standard, 04/08/2026)

Ogni pagina con `PmSidebar` e un elenco di commesse mostra **due sezioni**, mai un elenco
unico: **«Commesse»** con i codici che cominciano con la data (`C{aaaammgg}…`), ordinati
**dalla meno recente alla più recente** (`compareProjectCodes`), e **«Altre commesse»** con
le commesse di servizio a codice libero (INTERNA, SERVICE _ SANGRATO…), in ordine alfabetico
italiano e con quelle di sistema in testa.

- Si costruiscono con **`buildPmProjectSections()`** (`@/lib/pm-project-sections`): si passa
  `{ code, container }` per ogni commessa e la funzione fa filtro, ordinamento e sezioni.
  **Mai** la prop `containers` piatta per un elenco di commesse — è così che Trasferta e
  Inbox Officina si erano perse la regola (corrette il 04/08/2026 su segnalazione dell'utente).
- La prop `containers` piatta resta legittima per elenchi che **non** sono commesse
  (es. i report del Gestore DDP).
- Chi ha bisogno di un ordinamento diverso dentro «Altre commesse» passa le opzioni
  dell'helper, non riscrive il blocco.

Conformi: Milestones, SAL, Bilancio, Lavorazioni, MoM, Check list, Trasferta, Inbox Officina.
(Le prime sei hanno ancora il blocco in linea, equivalente: si passeranno all'helper alla
prossima occasione in cui si toccano.)

## Sostituzioni obbligatorie

| Mai questo | Usa questo |
|------------|------------|
| stato attivo/disattivo (o stati simili) con `Badge` | pallino colorato + etichetta: `ActiveStatus` / `StatusDot` (`@/components/shared/status-dot`) |
| cluster di icone azione **in-linea** (modifica/elimina/ecc.) | un solo menu **`⋮`** a fine riga via `RowActionsMenu` (`@/components/shared/row-actions`) |
| menu **«Colonne»** (visibilità colonne) fatto a mano | `ColumnsMenu` (`@/components/shared/columns-menu`) — master unico; si adatta alla voce più larga, spazio per la spunta. `DataTableCard` lo usa già internamente |
| `window.confirm` / `window.prompt` per azioni distruttive | hook `useConfirm()` → dialogo HMI OK/Annulla (`@/components/shared/confirm`) |
| `window.alert` per errori API, validazione, successi | `notifyError()` / `notifySuccess()` da `@/lib/toast` |
| stato vuoto con `border-dashed` fatto a mano | primitivo `Empty` (`@/components/ui/empty`) |
| `input type="checkbox"` per toggle on/off (es. «attivo») | `Switch` (`@/components/ui/switch`) |
| paginazione tabella fatta a mano | `Pagination` (`@/components/ui/pagination`) |
| breadcrumb con `<button>` + `/` manuali | `Breadcrumb` (`@/components/ui/breadcrumb`) |
| messaggio errore persistente in pagina | `Alert` (`@/components/ui/alert`) |
| menu col tasto **destro** (alberi, righe) | `ContextMenu` (apre sul right-click) |
| menu da pulsante **`⋮`** esplicito | `DropdownMenu` (apre col click) |
| `<table>` | `Table` (`@/components/ui/table`) |
| `<button>` | `Button` |
| `<input>` / `<input type="date">` | `Input` / `Popover+Calendar` |
| `<select>` con ricerca | `Select` shadcn |
| `bg-gray-100`, `#hex` | `bg-muted`, token |
| `rounded-none` / radius custom | radius del preset (0.625rem) |
| spinner/badge fatti a mano | `Skeleton`, `Badge` |
| layout sidebar fatto a mano | `AppShell` (già esiste) |

## Aggiungere un blocco/primitivo dal registry

```powershell
# primitivi mancanti (sempre radix-vega) — su Windows usa -p src/components/ui
npx shadcn@latest add chart command combobox -y -o -p src/components/ui

# un blocco intero come base di partenza
npx shadcn@latest add dashboard-01    # poi adatti dati e label
npx shadcn@latest add sidebar-07
```

Dopo `add`, **adatta i dati** (label IT, API ATEC PM) ma **non toccare struttura,
classi e spaziature** del blocco.

## Checklist prima di considerare una pagina "fatta"

- [ ] Parte da un blocco della mappa, non da layout custom.
- [ ] Nessun padding esterno aggiunto (il guscio lo fornisce); sezioni in `space-y-4/6`.
- [ ] Solo primitivi shadcn, zero HTML raw dove esiste il primitivo.
- [ ] Solo token di colore/spazio, classi canoniche copiate alla lettera.
- [ ] KPI = anatomia §3; tabelle = §2; form/dialog = §4; date = §7.
- [ ] Stati loading/vuoto/errore presenti (§6).
- [ ] `tsc -b` e `eslint` puliti.
