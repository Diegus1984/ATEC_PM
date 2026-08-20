# PIANO DI LAVORO — Port del prototipo `Gestione_DDP_New_V41.html` dentro ATEC PM

> ## ✅ ESEGUITO il 06/08/2026 — blocchi 0-7 completati
> `npx tsc -b`, `npm run build`, `npx eslint` e `dotnet build ATEC.PM.sln` puliti.
> **Verificato a runtime in locale** il 06/08/2026 su una commessa di prova (23 righe commerciali + 6 officina
> con composizione), poi cancellata: KPI, sezioni, righe spente, Stampa Aggregato e realtime tutti corretti.
> Resta il **deploy della migrazione v67** sul server aziendale.
> Decisioni prese: **D1 = righe spente condivise su DB** (tabella `ddp_row_off`, `DdpRowOffController`,
> SignalR) · **D2 = `< oggi` ovunque** · **D3 = card «Mat. a Magazzino» A2 ∪ A3, tabella solo A2** ·
> **D4 = DC e MIT sempre sulle Officine, sulle Commerciali solo se hanno righe.**
> Blocco 8 (test) **non eseguito**: il progetto non ha un runner di test configurato.
> Dopo l'implementazione una revisione avversariale ha prodotto 32 rilievi, 20 confermati e **tutti corretti**
> (i più seri: Top 10 che non escludeva le righe A9, collasso dei padri che nascondeva righe alle sezioni,
> ritardi non ordinati per data, righe spente che non arrivavano in tempo reale).

> **Scopo**: portare nel gestionale definitivo le **5 pagine** dell'analizzatore DDP V41:
> *Stato DDP*, *Avanzamento* (+ *Stampa Aggregato*), *Top 10 Costi*, *Destinazioni*, *Dati Mancanti*.
> **Fuori scope, da NON portare**: «Dati Distinta» del prototipo, «Report», report di controllo/ritardi,
> analisi consegne, feedback magazzino, upload Excel, registry `localStorage`, backup.
> (Le sezioni web *Dati Distinta*, *Feedback Acquisti*, *Feedback Magazzino* già esistenti **restano dove sono**:
> non si toccano, non si cancellano.)
>
> **Questo documento è autosufficiente**: non serve riaprire il prototipo HTML.
> Tutte le regole del prototipo che contano sono trascritte qui, tradotte sul modello dati del gestionale.

**Casa del codice** (percorsi reali, verificati):

| Cosa | Percorso |
|---|---|
| Pagina Sintesi DDP (contenitore delle 5 pagine) | `ATEC_PM/atec-pm-web/src/features/gestore-ddp/DdpSintesiPage.tsx` |
| Motore di calcolo puro | `ATEC_PM/atec-pm-web/src/features/gestore-ddp/ddp-sintesi-logic.ts` |
| Intestazioni + mapping riga→celle | `ATEC_PM/atec-pm-web/src/features/gestore-ddp/ddp-sintesi-table.ts` |
| Stampa/Excel | `ATEC_PM/atec-pm-web/src/features/gestore-ddp/ddp-export.ts` |
| Testata PDF ATEC | `ATEC_PM/atec-pm-web/src/lib/print-template.ts` |
| Righe DDP (client) | `ATEC_PM/atec-pm-web/src/lib/api/project-ddp.ts` |
| Config DDP (stati/aggregazioni) | `ATEC_PM/atec-pm-web/src/lib/api/ddp-config.ts` |
| Tipo riga | `ATEC_PM/atec-pm-web/src/lib/api/types/ddp.ts` (`DdpRowItem`, righe 129-163) |
| Endpoint righe (server) | `ATEC_PM/ATEC.PM.Server/Controllers/ProjectsController.cs` (`{id}/ddp` r.1051, `{id}/ddp-officina` r.1278) |
| Aggregati DDP (server) | `ATEC_PM/ATEC.PM.Server/Controllers/DdpManagerController.cs` |
| Migrazioni | `ATEC_PM/ATEC.PM.Server/Services/DbService.cs` (**ultima applicata: v66**, riga 3572) |

**Regole di progetto vincolanti per tutto il piano**
importi con `euro()` di `@/lib/format` (mai `toFixed(2)` a mano) · date `gg/mm/aa` con `formatDateShort` di `@/lib/date-iso`
(gli **export/PDF** usano `formatDateFull`, 4 cifre) · ogni griglia dentro `GridScroller` (intestazione fissa + barra
orizzontale in alto) · menu «Colonne» con chiave localStorage **versionata** · accordion con `<Collapsible>` + token
`--accordion-duration` / `--accordion-ease` (mai `{open ? … : null}`) · **niente `flexRender`**, celle via `renderColumnDef`
o JSX diretto stabile · `staleTime: 0` su tutte le query + `useProjectHub` per il real-time · ogni azione distruttiva
(reset righe spente) passa da `useConfirm` di `@/components/shared/confirm`.

---

## 1) Cosa c'è già e cosa cambia

### 1.1 Quadro per pagina

| Pagina | Logica del prototipo (sintesi operativa) | Stato attuale nel web | Verdetto |
|---|---|---|---|
| **Stato DDP** | 7 card (8 con Officina): Totale Acquisti (Σ cu·qtà + riconciliazione con la testata del file), Numero Inserimenti (N + finestra `dal…al…· N gg` sulla data d'inserimento), Finestra Consegne (min/max/ampiezza su righe datate non consegnate/non chiuse), Mat. Parzialmente Consegnato (PAR), Mat. Consegnato, In Ordine/Consegna, [Officina] Mat. in Trattamento (MIT), Mat. in Ritardo (`dataprev < oggi`). Banner refusi date (anno <2015 o >2100). Poi 9/10 card «Stati Avanzamento» + card «Ripartizione per stato» con tabella a scomparsa (Codice/Descrizione/N/%) + riga TOTALE + Stampa PDF. Ogni card cliccabile apre un overlay a piena pagina con la distinta filtrata. | `DdpSintesiPage.tsx` righe 645-740: 7 KPI (Tot. acquisti, Inserimenti, Finestra consegne, Mat. in consegna, Mat. in ritardo, Mat. consegnato, Mat. parziali), alert igiene (refusi + costo zero), torta `DdpOverviewPie`, blocco «Stati Avanzamento» a **8 card cablate** (`ddp-sintesi-logic.ts:487-503`). Ripartizione = `DdpStatusBreakdown` (barra + donut + legenda), **senza tabella e senza riga TOTALE**. Drill-down = apre e scrolla una sezione dell'accordion, **non un overlay**. | **DA ADATTARE** (medio) |
| **Avanzamento** (+ **Stampa Aggregato**) | 6/7 card KPI + **accordion di 8 sezioni (Commerciali) o 10 (Officina)**, una per stato/insieme: `ver, ro, do, [dc], tab, [par↔rit invertiti per tipo], [mit], del, ass`. Ogni sezione: titolo + contatore + «Tutte accese»/«Tutte spente» + «Stampa PDF» + tabella completa della distinta con **checkbox di riga** (riga spenta = esclusa dalle stampe) + nota descrittiva. In testata «Stampa Aggregato»: dialogo con checkbox per tabella (contatore delle sole righe accese) → un unico PDF a sezioni nell'ordine canonico. | Esistono **solo** le sezioni «Materiale in Consegna» e «Materiale Consegnato» (`DdpSintesiPage.tsx:767-800`). Mancano VER, RO, DO, DC, PAR, RITARDO, MIT, ASS. **Nessun** meccanismo di riga spenta. `printReport()` (riga 568) stampa un elenco **fisso** di 6 tabelle senza selezione. | **DA FARE** (grande) |
| **Top 10 Costi** | Le 10 righe a maggior importo, rank 1..10, colonne compatte `# / Descrizione / Qtà / Fornitore / Importo (€) / % sul tot.`, **+ riga «Subtotale Top 10» + riga «Totale commerciali commessa» (100,0%)**. Nessun filtro per stato. | `ddp-sintesi-logic.ts:412-420` + `DdpSintesiPage.tsx:802-854`: ordinamento su `quantity*unitCost` DESC, slice 10, rank e `% tot.` già presenti. Ma la tabella mostra **tutte e 17 le colonne** della distinta e **mancano subtotale e totale**. | **DA ADATTARE** (piccolo) |
| **Destinazioni** | Conteggio righe per destinazione normalizzata (maiuscolo, spazi compattati, vuota → `NON DEFINITA`), ordine conteggio DESC con `NON DEFINITA` **sempre in fondo**, colonne `Destinazione / N. Righe / % sul totale` con barra, **riga TOTALE (N, 100,0%)**, riga «non definita» evidenziata in rosso (regex `/^NON\s*(DEF|DEST)/i`). | `ddp-sintesi-logic.ts:422-443` + `BarList` (`DdpSintesiPage.tsx:106-142, 856-865`): raggruppamento e ordinamento ok, ma ordina `count DESC, nome ASC` **senza forzare NON DEFINITA in fondo**, è una lista di barre e non una tabella, e **manca la riga TOTALE**. | **DA ADATTARE** (piccolo) |
| **Dati Mancanti** | Righe con almeno uno di 5 campi vuoti (Stato, Rif. Danea, Data Prev. Cons., Destinazione, Costo Unitario), escluse solo le righe a **stato chiuso**; tabella a **doppia intestazione** (3 colonne fisse + gruppo «Dati mancanti» a 5 sottocolonne), cella rossa col nome del campo se manca, `–` grigio altrimenti; **pulsante per spegnere la singola riga** (persistito) + «Reset righe»; piede con `X visualizzate · Y spente · Z con almeno un dato mancante su W analizzate (V escluse per stato)`; Stampa PDF. | `ddp-sintesi-logic.ts:445-484` + `DdpSintesiPage.tsx:867-927`: 5 campi identici, esclusione via **A8**, colori flag con contrasto WCAG, tabella a **intestazione singola**, sottotitolo già quasi identico. **Manca** lo spegnimento riga, il «Reset righe» e la doppia intestazione. | **DA ADATTARE** (medio) |

### 1.2 Come si riorganizza la pagina

Oggi `DdpSintesiPage` è **una schermata unica** con 9 sezioni accordion. Il prototipo ha **5 schede**.
**Decisione di struttura (non da chiedere, è meccanica)**: `DdpSintesiPage` diventa un **contenitore a schede** —
barra segmentata in testata, scheda nell'URL (`?tab=stato|avanz|top10|dest|mancanti|distinta`) così i deep-link e il
back del browser funzionano come già fa `DdpControlloPage`.

Schede finali: **Stato DDP** · **Avanzamento** · **Top 10 Costi** · **Destinazioni** · **Dati Mancanti** ·
**Dati Distinta** (esistente, invariata). Le sezioni **Feedback Acquisti / Feedback Magazzino** restano dentro
«Dati Distinta» oppure in coda alla scheda «Stato DDP» — non sono in scope, basta che non spariscano.

La pagina resta raggiungibile da `/gestore-ddp/:projectId?type=COMMERCIAL|OFFICINA`
(`AppRoutes.tsx:171-173`, permesso `nav.gestore_ddp` da `route-features.ts:20`). **Nessuna nuova rotta.**

---

## 2) Adattamenti obbligatori

> Il prototipo ragiona su un file Excel; il gestionale ragiona su DB. Qui c'è la traduzione punto per punto.
> **Regola generale: nessun elenco di stati va scritto a mano nel codice nuovo.** Si legge dalle aggregazioni
> A1..A9 (`GET /api/ddp-aggregations`) con il fallback già presente in `ddp-sintesi-logic.ts:9-16`.

### 2.1 Matrice stati v7 — CON / COS / SPED / MOD non esistono più

Migrazione **v39** (`DbService.cs:2561-2566`) ha rimappato e **disattivato** i vecchi codici:
`CON → DISP`, `COS → DISP`, `SPED → DISP`, `MOD → RAM`. Inoltre `CHECK` del prototipo qui si chiama **`CHEK`**,
e `ND` **non esiste** come causale in `ddp_statuses` (le righe senza stato hanno `item_status = ''`).

Stati vivi (seed `DbService.cs:275-288`): `ANN, SOSP, RAM, SOST, DISP, DC, DO, ASS, CHEK, IO, PAR, RO, VER, MIT`.

| Insieme del prototipo | Traduzione nel gestionale | Se il dato non esiste |
|---|---|---|
| `DELIVERED = CON,COS,DISP,ASS,MOD` (card «Materiale Consegnato») | **A2** (`aggSets.get("A2")`), oggi `{DISP, ASS}` | fallback `DEFAULT_DELIVERED` |
| `CLOSED = ANN,SOSP,RAM,SOST` (righe escluse da consegne) | **A9** «Escluso da totale/conteggi» | fallback `["ANN","SOSP","RAM","SOST"]` |
| `EXCL_MISS = ANN,SOSP,RAM,SOST` (esclusi da Dati Mancanti) | **A8** «Esclusione Dati Mancanti» (⚠ oggi il seed A8 contiene **anche DO, CHEK, IO, RO**, `DbService.cs:1544`) | fallback `DEFAULT_EXCL_MISSING` |
| `INCONS_NODATE = IO,PAR,MIT` (righe senza data ma «in transito») | costante `DEFAULT_IN_TRANSIT_NODATE` (già in `ddp-sintesi-logic.ts:13`) | resta costante: non c'è aggregazione dedicata |
| `magazzN = CON,COS,DISP,PAR,MOD` (card «Mat. a Magazzino») | **A2 ∪ A3** = `{DISP, ASS, PAR}` | fallback A2 ∪ `{PAR}` |
| `DELIVERED_SET = CON,COS,DISP,MOD` (tabella «Materiale a Magazzino») | **A2** (vedi decisione D3) | — |
| `spedmodN = SPED+MOD` (card «Sped-Mod») | **non esiste più**: gli stati sono spariti. **Card da eliminare.** | non ricreare |
| `stopN = ANN+SOSP+RAM+SOST` (card «DDP Stop») | **A9** (già così, `ddp-sintesi-logic.ts:494-499`) | fallback storico |
| Descrizioni/etichette stato del prototipo (`CONFIG.stati.canonical`) | **mai cablate**: `statusDefs.get(key)?.label ?? key` | se lo stato non è in anagrafica, si mostra la chiave |
| Ordine dei canonici (array `CONFIG`) | ordine = `ddp_statuses.sort_order` per le liste «per stato»; per la Ripartizione resta `count DESC, key ASC` (già così) | — |
| Stato `ND` (vuoto) | `item_status === "" ` → etichetta a video **`ND — Stato non valorizzato`**, colore grigio `#CCCCCC` | non creare la causale a DB |

**Card «Stati Avanzamento» — cosa cambia rispetto alle 8 attuali.** Il prototipo ne ha 9 (Commerciali) / 10 (Officina):
`Verificare, Check, Rich. Off., Da Ordinare, [Da Costruire], [Mat. In Tratt.], DDP Stop, Sped-Mod, Mat. a Magazzino, Assegnato`.
Nel gestionale: **si toglie «Sped-Mod»** (stati morti) e **si aggiungono «Da Costruire» (DC) e «Mat. a Magazzino»** ⇒ **9 card**:
`VERIFICARE(VER) · CHECK(CHEK) · RICH. OFF.(RO) · DA ORDINARE(DO) · DA COSTRUIRE(DC) · IN ORDINE(IO) · TRATTAMENTO(MIT) · DDP STOP(A9) · MAT. A MAGAZZINO(A2∪A3) · ASSEGNATO(ASS)`
— *l'ordine è quello del prototipo, con «In Ordine» al posto di «Sped-Mod»*. `DC` e `MIT` restano visibili anche sulle
Commerciali se hanno almeno una riga (vedi decisione D4).

### 2.2 Aggregazioni configurabili al posto degli array

Punto unico di lettura: `aggSets: Map<string, Set<string>>` costruita in `DdpSintesiPage.tsx:414-419` da
`fetchDdpAggregations()`. **Tutte le funzioni nuove devono ricevere `aggSets` come parametro**, mai importare
elenchi di stati da un altro modulo. Regola pratica:

```ts
const A = (code: string, fallback: string[]) => {
  const set = aggSets.get(code)
  return set && set.size ? set : new Set(fallback)
}
```

Se un'aggregazione è vuota a DB **non si deve svuotare la pagina**: si usa il fallback e si logga nulla
(comportamento già adottato per A2; **attenzione**: per A8 e A9 il codice attuale usa `?? new Set(...)`, che NON
scatta se l'aggregazione esiste ma è vuota → uniformare all'helper qui sopra).

### 2.3 Composizione padre/figlio delle DDP Officina

Regola di dominio: **un padre che ha almeno un figlio non si conta** (è un contenitore).
Già applicata sia lato server (tutte le query di `DdpManagerController` con
`o.id NOT IN (SELECT DISTINCT parent_officina_item_id …)`) sia lato client (`ddp-sintesi-logic.ts:213-224`).

**⚠ Trappola**: `GET /api/projects/{id}/ddp-officina` restituisce **tutte** le righe, padri inclusi.
Ogni nuova vista deve partire da `model.rows` (già filtrate) e **mai** da `rowsQuery.data`.
→ **Intervento obbligatorio**: esporre le righe filtrate nel modello.

```ts
// ddp-sintesi-logic.ts — aggiungere a DdpSintesiModel
rows: DdpRowItem[]              // righe "contabili": padri-con-figli già esclusi
parentIdsWithChildren: Set<number>
```

Il ricalcolo del costo del padre (`unitCost = Σ figlio.unitCost × compositionQty`) resta **solo** dentro
`distinta` (fuori scope): le 5 pagine nuove usano `rows`.

Numerazione riga: `rowNumber` è **posizionale** (`project-ddp.ts:33`, `index + 1` sull'ordine per `id`).
Nella distinta officina i figli prendono `"•"`. **Non è un identificatore stabile**: come chiave di riga usare
sempre `row.id`, mai `rowNumber`.

### 2.4 Campi del prototipo che nel gestionale non ci sono

| Campo prototipo | Situazione reale | Cosa fare |
|---|---|---|
| **«Gruppo»** (colonna DDP Commerciali) | Nessuna colonna in `bom_items`, nessuna tabella collegata | **Non portare.** Non aggiungere colonne alla tabella: le intestazioni restano quelle di `ddp-sintesi-table.ts` |
| **«Data»** = data ordine/inserimento | Officina: `order_date` esiste **ed è esposto** nella SELECT (`ProjectsController.cs:1296`) ma **non è nel tipo `DdpRowItem`**. Commerciale: `bom_items.date_ordered` esiste a DB ma **non è nella SELECT** (verificato righe 1058-1077) | **Client-only**: la colonna «Data» resta `createdAt` (come oggi, `ddp-sintesi-table.ts:83,104`). La **«Finestra Inserimenti»** si calcola su `createdAt`. Nessuna modifica al backend |
| **«Totale costi commessa» di testata** (riconciliazione ±0,01) | Non esiste nessun totale di testata: il totale è **sempre** ricalcolato | **Non portare la riconciliazione.** Al suo posto, sotto «Tot. acquisti» si mostra `N righe · M escluse (A9)` |
| **«Totale Riga» letto dal file** (base della Top 10) | Non memorizzato: `totalCost` è calcolato `quantity × unitCost` (`Bom_DTOs.cs:29`) | Top 10 ordina e somma su `quantity × unitCost` (già così). **Da dire a Diego**: su distinte con totali-riga forzati a mano l'ordine può differire dal prototipo |
| **«Costo unitario vuoto» vs «zero»** | `unit_cost DECIMAL(10,2) DEFAULT 0`: indistinguibili | Regola unica: **`unitCost <= 0` = mancante** (oggi il web usa `=== 0`, allineare) |
| **Destinazione «NON DEFINITA»** | Non è una riga di `ddp_destinations`: è una normalizzazione | Generarla in client: `destination.trim().toUpperCase().replace(/\s+/g," ")`, vuota → `"NON DEFINITA"` |
| **`stateNoCost`, `datedAllCount`, `insCount`** | Codice morto nel prototipo (calcolati, mai renderizzati) | **Non portare.** L'equivalente utile (`costoZero`) esiste già nell'alert igiene |
| **Flag «riga spenta» sulle righe di distinta** | Nessuna colonna `hidden`/`excluded` su `bom_items`/`ddp_officina_items` | Vedi §4 (nuova tabella) oppure `localStorage` — decisione **D1** |

### 2.5 Data di riferimento e soglia «in ritardo»

Il prototipo usa `_todayRef()` (mezzanotte locale di oggi) e ha **due soglie incoerenti**:
`overdue` conta `dataprev < oggi`, mentre la cella rossa usa `dataprev <= oggi`.
Il gestionale usa **`< oggi`** in entrambi i punti (`ddp-sintesi-logic.ts:325` e `ddp-sintesi-table.ts:65`).
**Si tiene `< oggi` ovunque** (decisione D2): una consegna prevista oggi non è in ritardo.
La data di riferimento resta l'orologio del client, calcolata **una volta sola** e passata alle funzioni pure
(`today?: string` con default `todayIso()`), così le funzioni restano testabili.

---

## 3) Piano per pagina

> Nota trasversale: `buildSintesiModel()` è già **puro** e va esteso lì, mai in pagina.
> Ogni pagina nuova è un componente in `src/features/gestore-ddp/`, la pagina contenitore passa solo il modello.

### 3.0 Preparazione condivisa (prerequisito di tutte le pagine)

1. **`ddp-sintesi-logic.ts`** — estendere `DdpSintesiModel` con quello che oggi resta interno:
   ```ts
   rows: DdpRowItem[]                 // padri-con-figli esclusi
   dated: DdpRowItem[]                // datate, non A2, non A9, ordinate per dateNeeded ASC
   noDateInTransitRows: DdpRowItem[]  // senza data, stato ∈ IO/PAR/MIT, non A9
   overdueRows: DdpRowItem[]          // dated con dateNeeded < today
   insFinestra: string                // "dal gg/mm/aa al gg/mm/aa · N gg" su createdAt, "—" se vuoto
   statiTable: StatoRow[]             // ripartizione in forma tabellare (vedi 3.1)
   sezioni: DdpAvanzSection[]         // vedi 3.2
   top10Totals: { subtotal: number; total: number }
   destinazioniTable: DestRow[]       // vedi 3.4
   mancantiCounts: { withMissing: number; analyzed: number; excluded: number }
   ```
2. **`buildSintesiModel(input)`** — aggiungere il parametro opzionale `today?: string` (default `todayIso()`),
   e sostituire i due `?? new Set(...)` di A8/A9 con l'helper «vuoto = fallback» di §2.2.
3. **`DdpSintesiPage.tsx`** — aggiungere `staleTime: 0` alle 4 query (righe 364-400): oggi la Sintesi è
   **l'unica** pagina DDP senza (`GestoreDdpPage.tsx:232` e `DdpControlloPage.tsx:45/50/55` ce l'hanno).
4. **`DdpSintesiPage.tsx`** — barra segmentata delle 6 schede + stato in `useSearchParams()` (`?tab=`).
   Estrarre i pezzi esistenti in componenti: `DdpStatoView`, `DdpAvanzamentoView`, `DdpTop10View`,
   `DdpDestinazioniView`, `DdpMancantiView`, `DdpDistintaView` (quest'ultima = taglia-e-incolla di quanto c'è oggi).
5. **Menu Colonne** — le intestazioni cambiano (Top 10 compatta, colonne nuove): **versionare la chiave**
   `ddp-sintesi-columns-v1` → **`ddp-sintesi-columns-v2`**, e sdoppiarla per tipo distinta
   (`ddp-sintesi-columns-COMMERCIAL-v2` / `-OFFICINA-v2`): oggi è una sola chiave per entrambi
   (commento onesto in `DdpSintesiPage.tsx:338-340`).
6. **Test** — creare `ddp-sintesi-logic.test.ts` (oggi **non esiste alcun test** su questo file):
   almeno un caso per insieme di stato, uno per la finestra date, uno per l'ordinamento Destinazioni,
   uno per la composizione officina.

---

### 3.1 Pagina «Stato DDP»

File: **nuovo** `src/features/gestore-ddp/DdpStatoView.tsx` · logica in `ddp-sintesi-logic.ts`.

1. **KPI (8/9 card).** Riusare `KpiCard` (oggi interno a `DdpSintesiPage.tsx:57-104`, **estrarlo** in
   `src/features/gestore-ddp/DdpKpiCard.tsx`). Ordine e contenuto:
   `Tot. acquisti` (`euro(kpi.totValue)`, rosso, hint `N righe · M escluse (A9)`) ·
   `Numero inserimenti` (valore `kpi.count`, hint `model.insFinestra`) ·
   `Finestra consegne` (small, `kpi.finestra`) ·
   `Mat. parzialmente consegnato` (A3) · `Mat. consegnato` (A2) · `In ordine / consegna` (`kpi.inConsegna`) ·
   `[se MIT>0] Mat. in trattamento` · `Mat. in ritardo` (`kpi.overdue`, ambra se >0).
   Le card cliccabili puntano alle **sezioni della scheda Avanzamento** (`?tab=avanz#ddp-section-par` ecc.):
   niente overlay a piena pagina, il drill-down del gestionale è già a sezioni.
2. **Banner igiene dati.** Resta com'è (`DdpSintesiPage.tsx:682-698`), aggiungendo il caso «anno implausibile»
   già calcolato (`refusiDate`, soglie **2015 / 2100**, `ddp-sintesi-logic.ts:337-340`).
3. **Torta panoramica.** `DdpOverviewPie` resta, ma va corretta:
   - `computeDdpHealthBuckets` (`DdpOverviewPie.tsx:45-69`) deve ricevere **A2 e A9** invece delle costanti
     `DEFAULT_DELIVERED` / `DDP_STOP_STATES`. Nuova firma:
     ```ts
     export function computeDdpHealthBuckets(
       bars: BarRow[], total: number,
       sets: { delivered: Set<string>; stop: Set<string> }
     ): DdpHealthBuckets
     ```
   - i testi `hint` delle 3 pillole citano ancora stati morti (`"CON, COS, DISP, ASS, MOD"`,
     `DdpOverviewPie.tsx:219`): sostituirli con `[...delivered].sort().join(", ")` calcolato.
4. **Blocco «Stati Avanzamento» a 9 card** (vedi §2.1). In `ddp-sintesi-logic.ts` sostituire l'array `buckets`
   (righe 487-503) con:
   ```ts
   function avanzBuckets(aggSets: Map<string, Set<string>>, officina: boolean):
     { label: string; states: string[]; officinaOnly?: boolean }[]
   ```
   percentuale sempre `pctLabel(count/total)` con **guardia `total > 0`** (già presente in `pctLabel`, riga 122).
5. **Card «Ripartizione per stato» + tabella «Dettaglio».** Sotto il `DdpStatusBreakdown` attuale aggiungere una
   `<Collapsible>` con la tabella del prototipo:
   ```ts
   export interface StatoRow { code: string; descr: string; n: number; pct: number }
   export function buildStatiTable(rows: DdpRowItem[], statusDefs: Map<string, DdpStatusItem>): StatoRow[]
   ```
   Colonne `Codice` (badge colorato con `colorBg`/`colorFg`) · `Descrizione stato` · `N. Righe` (num) ·
   `% sul totale` (num + barra). **Riga TOTALE** in coda (`N`, `100,0%`, senza badge e senza barra).
   Sottotitolo: `` `${total} righe d'ordine · ${statiTable.filter(s=>s.n>0).length} stati presenti` ``.
   Griglia dentro `GridScroller`, `<Collapsible open={detailOpen}>` col chevron che ruota
   (`transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]`).
6. **Stampa.** Aggiungere le chiavi a `sectionTable` (`DdpSintesiPage.tsx:508-562`) — è **il** punto di estensione:
   `stato` (KPI in forma tabellare) e `rip` (già esistente, da arricchire con la riga TOTALE).

---

### 3.2 Pagina «Avanzamento» + «Stampa Aggregato»  ← il grosso del lavoro

File nuovi: `src/features/gestore-ddp/DdpAvanzamentoView.tsx`,
`src/features/gestore-ddp/DdpStampaAggregatoDialog.tsx`,
`src/features/gestore-ddp/ddp-row-off.ts` (stato «righe spente»).

1. **Definizione delle sezioni — una sola costante, mai duplicata.**
   Nel prototipo lo stesso ordine è ripetuto in 3 punti e differisce fra i due tipi: **nelle Commerciali `rit`
   precede `par`, nelle Officine `par` precede `rit`.** Da tradurre così, in `ddp-sintesi-logic.ts`:
   ```ts
   export type DdpSectionKey =
     | "ver" | "ro" | "do" | "dc" | "tab" | "par" | "rit" | "mit" | "del" | "ass"

   export const DDP_SECTION_ORDER: Record<"COMMERCIAL" | "OFFICINA", DdpSectionKey[]> = {
     OFFICINA:   ["ver","ro","do","dc","tab","par","rit","mit","del","ass"],
     COMMERCIAL: ["ver","ro","do","tab","rit","par","del","ass"],
   }

   export interface DdpAvanzSection {
     key: DdpSectionKey
     title: string
     rows: DdpRowItem[]
     /** Nota descrittiva sotto la tabella. */
     note: string
     /** Evidenzia in rosso la colonna Data prev. cons. scaduta. */
     dueRed: boolean
     emptyText: string
   }

   export function buildAvanzSections(input: {
     rows: DdpRowItem[]
     officina: boolean
     aggSets: Map<string, Set<string>>
     inConsegna: DdpRowItem[]
     dated: DdpRowItem[]
     today?: string
   }): DdpAvanzSection[]
   ```
2. **Contenuto esatto di ogni sezione** (già tradotto sugli stati vivi):

   | key | Titolo | Righe | Ordine | Rosso data | Nota / vuoto |
   |---|---|---|---|---|---|
   | `ver` | da Verificare | `itemStatus === "VER"` | naturale | no | «N righe in stato VER (verificare se disponibile a magazzino).» / «Nessun materiale da verificare.» |
   | `ro` | Richieste di Offerta | `RO` | naturale | no | «N righe in stato RO, da chiudere a livello di quotazione.» / «Nessuna richiesta di offerta.» |
   | `do` | Da Ordinare | `DO` | naturale | no | «N righe in stato DO (da ordinare).» / «Nessun materiale da ordinare.» |
   | `dc` | Da Costruire | `DC` | naturale | no | «N righe in stato DC (da costruire).» / «Nessun materiale da costruire.» |
   | `tab` | In Ordine / Consegna — IO / PAR | `model.consegne` (= `dated` + `noDateInTransitRows`) | date ASC, poi le senza-data | **sì** | «Materiale in ordine e in consegna. Escluse le righe già consegnate o gestite (A2) e quelle a stato chiuso (A9).» / «Nessuna riga in ordine o in consegna.» |
   | `par` | Materiale Parzialmente Consegnato | A3 (`PAR`) | `dateNeeded` ASC, **senza data in coda** | **sì** | «N righe in stato PAR (parzialmente consegnato o costruito).» / «Nessun materiale parzialmente consegnato.» |
   | `rit` | Materiale in Ritardo | `model.overdueRows` | data ASC | **sì** | «N righe con data consegna prevista anteriore a oggi.» / «Nessuna riga in ritardo di consegna.» |
   | `mit` | Materiale in Trattamento | `MIT` | `dateNeeded` ASC, senza data in coda | **sì** | «N righe in stato MIT (materiale in trattamento).» / «Nessun materiale in trattamento.» |
   | `del` | Materiale a Magazzino | **A2** | `rowNumber` naturale | no | «N righe di materiale a magazzino (A2: DISP, ASS).» / «Nessun materiale consegnato o gestito.» |
   | `ass` | Materiale Assegnato | `ASS` | naturale | no | «N righe in stato ASS (assegnato al montatore).» / «Nessun materiale assegnato.» |

   ⚠ **`tab` NON è «solo IO e PAR»** nonostante il titolo: contiene **tutte** le righe con data prevista non
   consegnate e non chiuse (quindi anche VER/RO/DO/DC/CHEK datate) **più** le righe IO/PAR/MIT senza data.
   È il valore più frainteso del prototipo: il titolo si mantiene, la nota lo spiega.
   ⚠ `ass` è **sempre l'ultima**; `del` la penultima.
3. **KPI della scheda (6/7 card).**
   `Materiale in consegna` (`kpi.inConsegna`) · `Mat. par. cons.` (`kpi.parziali`) ·
   `Attesa consegna` (card a 3 righe: `DAL` / `AL` / `GG.` da `kpi.finestra`, che va **sdoppiata** in
   `finestraDal / finestraAl / finestraGg` nel modello per non fare parsing di stringa) ·
   `[Officina] Mat. in trattamento` · `Materiale consegnato` (A2) · `Mat. a magazzino` (A2∪A3) · `Assegnato` (ASS).
4. **Accordion.** Una `<Section>` per chiave — il componente esiste già (`DdpSintesiPage.tsx:284-326`,
   `<Collapsible>` + chevron con i token corretti): **estrarlo** in `src/features/gestore-ddp/DdpSection.tsx`
   e aggiungergli le due proprietà nuove:
   ```tsx
   <DdpSection
     id={s.key} title={s.title} count={s.rows.length}
     open={…} onToggle={…} onPrint={() => printSection(s.key)}
     actions={<RowOffButtons sectionKey={s.key} rows={s.rows} />}
   />
   ```
   Tutte le sezioni nascono **chiuse** (come il prototipo). La casella «Più sezioni aperte» esistente resta.
5. **Righe spente (per la stampa).**
   ```ts
   // ddp-row-off.ts
   export type RowOffMap = Record<string, true>            // chiave: `${sectionKey}|${rowId}`
   export function useDdpRowOff(projectId: number, ddpType: string): {
     isOff: (section: string, rowId: number) => boolean
     toggle: (section: string, rowId: number, off: boolean) => void
     setAll: (section: string, rowIds: number[], off: boolean) => void
     reset: (section?: string) => Promise<void>            // con useConfirm
     offCount: (section: string) => number
   }
   ```
   **Chiave = `row.id`, mai `rowNumber`** (nel prototipo la chiave era il numero di riga e righe duplicate/vuote
   si spegnevano insieme). Colonna checkbox in testa alla tabella, riga spenta con `opacity-40` (tranne la
   checkbox), bottoni «Tutte accese» / «Tutte spente» nell'header di sezione con `stopPropagation` per non
   chiudere l'accordion. **Le righe spente NON riducono il contatore dell'accordion né i KPI** (comportamento
   voluto del prototipo): riducono solo il PDF e il contatore del dialogo aggregato.
   Persistenza → **decisione D1** (`localStorage` per utente vs tabella condivisa: vedi §4).
6. **Tabella di sezione.** `GridScroller` + `RowsTable` esistente (`DdpSintesiPage.tsx:144-282`), esteso con:
   ```tsx
   rowOff?: { isOff(id: number): boolean; toggle(id: number, off: boolean): void }
   dueRed?: boolean   // cella `Data prev.` in rosso quando dateNeeded < today
   ```
   ⚠ Oggi il ritardo è segnalato con il **prefisso testuale `⚠ `** dentro la stringa di cella
   (`ddp-sintesi-table.ts:76-78`) — e finisce anche negli export. Da sostituire con una **classe sulla cella**
   (`text-red-700 font-semibold`) e mantenere il testo pulito: `ddpRowToSintesiCells` guadagna un ritorno
   parallelo `overdueIdx: number` (indice della colonna data) invece dell'opzione `markOverdue`.
7. **«Stampa Aggregato».**
   - Bottone in testata della scheda Avanzamento → `DdpStampaAggregatoDialog`.
   - Dialogo: checkbox master «Seleziona tutte le tabelle» + una riga per sezione in
     `DDP_SECTION_ORDER[tipo]`, con titolo e **contatore delle sole righe accese**
     (`s.rows.filter(r => !isOff(s.key, r.id)).length`). Tutte selezionate all'apertura; voce deselezionata
     con `opacity-55`. Se non è selezionato nulla il bottone «Stampa PDF» è **disabilitato** (niente `alert()`).
   - Stampa:
     ```ts
     function printAggregato(keys: DdpSectionKey[]): void
     ```
     Le sezioni escono **sempre nell'ordine canonico**, non nell'ordine di selezione:
     `DDP_SECTION_ORDER[tipo].filter(k => keys.includes(k))`.
     Riuso diretto del motore esistente, **nessun nuovo template**:
     ```ts
     printDdpTables(
       `Report Aggregato Avanzamento — ${code}${customer ? " — " + customer : ""}`,
       `${sel.length} tabell${sel.length === 1 ? "a" : "e"} · ${totRows} righe complessive · data di riferimento ${formatDateFull(new Date())}`,
       sel.map(k => sectionExportTable(k))
     )
     ```
     Le sezioni selezionate ma vuote si stampano comunque con la riga «Nessuna riga.».

---

### 3.3 Pagina «Top 10 Costi»

File: **nuovo** `src/features/gestore-ddp/DdpTop10View.tsx`.

1. **Colonne compatte** come il prototipo: `# (badge rank) · Descrizione · Qtà · Fornitore · Importo (€) · % sul tot.`
   Il fornitore vuoto diventa `—`. `Qtà` con `fmtQty` (`ddp-sintesi-table.ts:54`), importo con `euro()`,
   percentuale con `pctLabel` (1 decimale, it-IT).
   Le 17 colonne complete restano **opzionali**: interruttore «Colonne complete» che riusa `RowsTable`
   (le colonne complete sono già filtrate dal menu Colonne).
2. **Riga Subtotale e riga Totale.** In `ddp-sintesi-logic.ts`:
   ```ts
   top10Totals: { subtotal: number; total: number }   // subtotal = Σ amount(top10), total = kpi.totValue
   ```
   A video due righe non ordinabili in coda alla tabella (in TanStack sarebbero **footer/pinned rows**, non righe
   dati): `Subtotale Top 10` con `euro(subtotal)` e `pctLabel(subtotal/total)`, `Totale commessa` con
   `euro(total)` e `100,0%`. Celle `#`, `Qtà`, `Fornitore` vuote.
3. **Badge rank**: quadrato 24×24, `rounded-lg`, testo bianco, sfondo `bg-primary` (niente gradiente cablato:
   si usa il token del tema).
4. **Base economica**: `quantity × unitCost`, **escludendo A9 dal totale** (come già fa `totValue`).
   Il prototipo non escludeva nulla → i numeri possono differire: è voluto e coerente col resto del gestionale.
5. **Stampa**: chiave `top10` in `sectionTable`, con le **stesse 6 colonne compatte** + subtotale + totale.
   Il PDF stampa sempre l'ordine naturale (importo DESC), non l'eventuale riordino a video.

---

### 3.4 Pagina «Destinazioni»

File: **nuovo** `src/features/gestore-ddp/DdpDestinazioniView.tsx`.

1. **Normalizzazione** — nuova funzione esportata in `ddp-sintesi-logic.ts`:
   ```ts
   export function normDest(value: string | null | undefined): string {
     const v = (value ?? "").trim().toUpperCase().replace(/\s+/g, " ")
     return v || "NON DEFINITA"
   }
   ```
   Oggi il web raggruppa su `destination.trim()` **senza** uppercase né compattamento spazi
   (`ddp-sintesi-logic.ts:425`): «Gruppo  Pompa» e «GRUPPO POMPA» finiscono in due righe diverse. **Da correggere.**
2. **Ordinamento a due chiavi** (il comparatore del prototipo non è transitivo, non replicarlo così com'è):
   ```ts
   .sort((a, b) => Number(a.isNonDef) - Number(b.isNonDef) || b.n - a.n || a.dest.localeCompare(b.dest, "it"))
   ```
   → «NON DEFINITA» **sempre ultima**, poi conteggio DESC, poi alfabetico (tie-break esplicito: il prototipo
   si appoggiava alla stabilità del sort, qui serve un ordine riproducibile).
3. **Tabella** al posto della lista di barre: `Destinazione · N. Righe · % sul totale` (barra + etichetta),
   dentro `GridScroller`. Percentuale = `n / model.rows.length` (numero righe, **non** un valore economico).
   **Riga TOTALE** in coda: `TOTALE · N · 100,0%`, senza barra.
4. **Riga «non definita» in rosso**: `_isNonDef = /^NON\s*(DEF|DEST)/i.test(dest)` — il regex è **più largo**
   della sola costante e intercetta anche varianti già presenti nei dati («NON DESTINATA»).
   Stile: sfondo `bg-red-50 dark:bg-red-950/40`, testo `text-red-700 dark:text-red-400`, `font-semibold`.
5. **Stampa**: chiave `dest` già in `sectionTable` — aggiungere la riga TOTALE e togliere la barra
   (nel PDF la barra non c'è).
6. **Export Excel multi-DDP del prototipo (Commerciali + Officina della stessa commessa, ordine alfabetico,
   escluse le vuote e «NON DEFINITA»): NON portare in prima battuta.** Se servirà, è una query su entrambe le
   distinte della stessa commessa, non un merge da `localStorage`.

---

### 3.5 Pagina «Dati Mancanti»

File: **nuovo** `src/features/gestore-ddp/DdpMancantiView.tsx`.

1. **Criterio unico dei 5 campi** (in `ddp-sintesi-logic.ts:452-456`, da allineare):
   ```
   stato → !itemStatus.trim() || itemStatus === "ND"
   rif   → !daneaRef?.trim()
   dprev → !dateNeeded
   dest  → !destination?.trim()
   cu    → !(unitCost > 0)        // ← oggi è `unitCost === 0`: allineare (i negativi sono un dato sbagliato)
   ```
   Il prototipo aveva **due criteri divergenti** (cella vuota all'import vs `!(cu>0)` a runtime): qui ne esiste
   **uno solo** perché il DB non distingue vuoto da zero. Documentarlo nel commento della funzione.
2. **Esclusione per stato**: **A8** (`exclMissing`). ⚠ Il seed A8 contiene anche `DO, CHEK, IO, RO`, non solo i
   4 stati chiusi del prototipo: **è configurabile da «Aggregazioni DDP» ed è giusto così**. Il sottotitolo deve
   mostrare l'elenco effettivo, non una lista cablata:
   `` `Escluse dall'analisi le righe con stato: ${[...exclMissing].sort().join(", ")}.` ``
   Le righe con stato **vuoto/ND non si escludono**: è proprio il difetto che si vuole segnalare.
3. **Tabella a doppia intestazione**:
   riga 1 → `Riga` (rowspan 2) · `Stato` (rowspan 2) · `Descrizione` (rowspan 2) · **`Dati mancanti` (colspan 5)** · colonna azione (rowspan 2)
   riga 2 → `Stato · Rif. Danea · Data Prev. Cons. · Destinazione · Costo Unitario`
   Cella mancante = **nome del campo** su sfondo rosa (`bg-red-50`) con testo `flagColor` (già calcolato con
   contrasto WCAG, `ddp-sintesi-logic.ts:167-170`); cella OK = `–` grigio `#94A3B8`.
   `GridScroller` con intestazione fissa (entrambe le righe di `<thead>` devono restare sticky).
4. **Spegnimento riga** — stesso hook di §3.2.5 con `sectionKey = "mancanti"`:
   - pulsante tondo a fine riga (icona cerchio-barrato, `title="Spegni riga (nascondi)"`);
   - le righe spente **spariscono** dalla vista e dalla stampa;
   - piè di tabella con `N righe spente` + bottone **«Reset righe»** → `useConfirm`:
     ```ts
     if (await confirm({ title: "Ripristinare tutte le righe spente?",
       description: "Le righe nascoste torneranno visibili in elenco e nelle stampe.",
       confirmLabel: "Ripristina", destructive: false })) { … }
     ```
   - ⚠ semanticamente **è diverso** dallo spegnimento delle stampe di Avanzamento: qui significa «l'ho già
     gestito», là significa «non stampare». Trattarli come due `sectionKey` diversi dello stesso store, ma con
     testi distinti (e, se D1 sceglie il DB, con la stessa tabella).
5. **Testo dei contatori** (unico per video e PDF, non due formulazioni come nel prototipo):
   `` `${vis} righe visualizzate${off ? ` · ${off} ${off === 1 ? "riga spenta" : "righe spente"}` : ""} · ${withMissing} con almeno un dato mancante su ${analyzed} analizzate (${excluded} escluse per stato).` ``
6. **Stati vuoti**: 0 righe difettose → box verde «Nessuna riga con dati mancanti nei campi Stato, Rif. Danea,
   Data Prev. Cons., Destinazione e Costo Unitario.»; tutte spente → «Tutte le righe con dati mancanti sono
   state spente. Usa «Reset righe» per rivisualizzarle.».
7. **Stampa**: chiave `mancanti` in `sectionTable`, **8 colonne** (senza la colonna del pulsante), righe spente
   escluse, stesse 5 sottocolonne.

---

## 4) Backend

**Serve solo se la persistenza delle righe spente è condivisa (decisione D1).** Tutto il resto è **client-only**:
i dati necessari sono già tutti nei DTO esistenti.

### 4.1 Se D1 = «condivisa» (raccomandato)

1. **Migrazione v67** in `ATEC.PM.Server/Services/DbService.cs` (ultima applicata: **v66**, riga 3572) —
   ricalcata su `ddp_feedback_magazzino_hidden` (`DbService.cs:1726-1735`), che è il precedente già in produzione:
   ```sql
   CREATE TABLE IF NOT EXISTS ddp_row_off (
     id INT AUTO_INCREMENT PRIMARY KEY,
     project_id INT NOT NULL,
     ddp_type VARCHAR(20) NOT NULL,          -- COMMERCIAL | OFFICINA
     section_key VARCHAR(20) NOT NULL,       -- ver|ro|do|dc|tab|par|rit|mit|del|ass|mancanti
     item_id INT NOT NULL,
     created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
     UNIQUE KEY uq_ddp_row_off (project_id, ddp_type, section_key, item_id),
     FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
   ```
   `INSERT IGNORE INTO schema_migrations (version, description) VALUES (67, 'ddp_row_off: righe escluse dalle stampe di Avanzamento e da Dati Mancanti')`.
2. **Controller nuovo** `ATEC.PM.Server/Controllers/DdpRowOffController.cs`,
   `[Route("api/ddp-row-off")]`, `[RequireFeature("nav.gestore_ddp")]`:
   | Verbo | Rotta | Corpo | Risposta |
   |---|---|---|---|
   | GET | `/{projectId:int}/{ddpType}` | — | `ApiResponse<List<DdpRowOffItem>>` (`SectionKey`, `ItemId`) |
   | PUT | `/{projectId:int}/{ddpType}/{sectionKey}/{itemId:int}` | `{ off: bool }` | `ApiResponse<bool>` |
   | POST | `/{projectId:int}/{ddpType}/{sectionKey}/bulk` | `{ itemIds: int[], off: bool }` | `ApiResponse<bool>` («Tutte accese/spente») |
   | POST | `/{projectId:int}/{ddpType}/{sectionKey}/reset` | — | `ApiResponse<bool>` |
   Ogni scrittura notifica **SignalR** come già fa `DdpFeedbackController.NotifyDdpChange`
   (evento `DdpChanged`, gruppi `project-{id}` e `ddp-all`) → la Sintesi si aggiorna da sola via `useProjectHub`.
3. **DTO** in `ATEC.PM.Shared/DTOs/` (accanto ai DTO DDP esistenti) + client
   `atec-pm-web/src/lib/api/ddp-row-off.ts` con `fetchDdpRowOff / setDdpRowOff / setDdpRowOffBulk / resetDdpRowOff`.

### 4.2 Se D1 = «personale»

**Nessuna modifica al backend.** `useDdpRowOff` scrive in `localStorage` con chiave
`ddp.rowoff.{projectId}.{ddpType}` (versionata: `…-v1`), e il piano perde il blocco 4.

### 4.3 Cosa NON si tocca

- Nessuna colonna nuova su `bom_items` / `ddp_officina_items`.
- Nessuna modifica alle SELECT esistenti (`ProjectsController.cs:1051` e `:1278`).
- Nessun campo «Gruppo», nessun «totale di testata», nessun «totale riga memorizzato» (vedi §2.4).
- **Non riusare** `ddp_feedback_magazzino_hidden`: appartiene al modulo Feedback Magazzino (fuori scope) e la sua
  chiave non ha il concetto di sezione.

---

## 5) Ordine di esecuzione

| # | Blocco | Contenuto | Dipende da | Come si verifica da solo |
|---|---|---|---|---|
| **0** | Fondamenta | §3.0 punti 1-3: estensione di `DdpSintesiModel` (`rows`, `dated`, `overdueRows`, `insFinestra`, `finestraDal/Al/Gg`), parametro `today`, helper «aggregazione vuota = fallback», `staleTime: 0` sulle 4 query | — | `npx tsc -b` pulito; la pagina attuale mostra gli stessi numeri di prima (nessuna regressione visiva) |
| **1** | Schede + estrazione componenti | §3.0 punti 4-5: barra segmentata `?tab=`, estrazione di `DdpKpiCard`, `DdpSection`, `RowsTable` in file propri, sposto delle sezioni esistenti nelle nuove viste, chiave Colonne → `…-v2` sdoppiata per tipo | 0 | Le 6 schede si aprono, il deep-link `?tab=dest` funziona, il back del browser torna alla scheda precedente |
| **2** | Stato DDP | §3.1: KPI 8/9 card, finestra inserimenti, 9 card Avanzamento, tabella Ripartizione + TOTALE in `<Collapsible>`, fix `computeDdpHealthBuckets` (A2/A9) e testi hint | 1 | Su una commessa reale: somma delle card «Ripartizione» = N righe; hint delle pillole senza CON/COS/MOD |
| **3** | Top 10 + Destinazioni | §3.3 e §3.4: colonne compatte + subtotale/totale; `normDest`, ordinamento a due chiavi, tabella con barra + riga TOTALE + riga rossa | 1 | Σ % Destinazioni = 100,0%; «NON DEFINITA» ultima; Top 10 subtotale ≤ totale |
| **4** | Righe spente — persistenza | §4: migrazione v67 + `DdpRowOffController` + DTO + client `ddp-row-off.ts`, **oppure** solo `localStorage` se D1 = personale | 0 (indipendente dalle viste) | `dotnet build` + chiamate REST da REST client: PUT/bulk/reset ritornano `success` e la GET riflette lo stato |
| **5** | Avanzamento | §3.2 punti 1-6: `DDP_SECTION_ORDER`, `buildAvanzSections`, 6/7 KPI, 8/10 accordion, checkbox riga + «Tutte accese/spente», rosso su data scaduta via classe (rimozione del prefisso `⚠ `) | 2, 4 | Commerciali = 8 sezioni con `rit` prima di `par`; Officina = 10 sezioni con `par` prima di `rit`; somma dei contatori coerente con le card |
| **6** | Stampa Aggregato | §3.2 punto 7: `DdpStampaAggregatoDialog` + `printAggregato`, contatori delle sole righe accese, ordine canonico | 5 | PDF con N sezioni nell'ordine canonico; spegnendo 3 righe il PDF ne ha 3 in meno e il contatore del dialogo cala |
| **7** | Dati Mancanti | §3.5: criterio `!(unitCost>0)`, doppia intestazione, spegni riga + Reset con `useConfirm`, testo contatori unico, stampa a 8 colonne | 4 | Righe con costo 0 compaiono; il Reset chiede conferma e ripristina; il PDF non contiene le righe spente |
| **8** | Test + rifinitura | `ddp-sintesi-logic.test.ts` (insiemi di stato, finestre date, ordinamento destinazioni, composizione officina) + `npm run build` + `npx tsc -b` | 2-7 | Test verdi, build pulita |

> **Verifica a runtime**: solo alla fine e **solo se richiesta esplicitamente** (regola di progetto: verifica = build/tsc/eslint).
> Se si avviano server o Vite per una prova, vanno **spenti** al termine (porte 5150/5151/5173).

---

## 6) Decisioni da chiedere a Diego

**D1 — Le «righe spente» sono una preferenza personale o un dato di commessa?**
Nel prototipo erano due cose diverse: quelle delle stampe stavano solo in memoria (perse a ogni ricarica), quelle
di Dati Mancanti in `localStorage` del singolo PC. ATEC PM è multi-utente e condiviso.
*Raccomandazione*: **condivise sul DB** (blocco 4 completo, tabella `ddp_row_off` + SignalR) — «questa riga l'ho
già gestita / non va stampata» è un'informazione di commessa, e sul PC di un altro utente riapparirebbe.
Costo: ~mezza giornata in più. Alternativa a costo zero: `localStorage` per utente.

**D2 — Soglia «in ritardo»: `< oggi` o `<= oggi`?**
Il prototipo conta i ritardi con `< oggi` ma colora di rosso con `<= oggi`: card e celle rosse non tornano mai.
*Raccomandazione*: **`< oggi` ovunque** (già lo standard del gestionale, `ddp-sintesi-logic.ts:325` e
`DdpManagerController` con `date_needed < CURDATE()`). Una consegna prevista oggi non è in ritardo.

**D3 — «Mat. a Magazzino»: la card e la tabella devono dare lo stesso numero?**
Nel prototipo convivevano tre insiemi diversi con nomi simili: card «Materiale Consegnato» (CON,COS,DISP,ASS,MOD),
card «Mat. a Magazzino» (…+PAR), tabella «Materiale a Magazzino» (senza ASS e senza PAR). Post-v7 restano
A2 = `{DISP, ASS}` e A3 = `{PAR}`.
*Raccomandazione*: **card «Mat. a Magazzino» = A2 ∪ A3** (include i parziali, come il prototipo) e **tabella = A2**,
con la nota di sezione che lo dichiara esplicitamente; i parziali restano visibili nella loro sezione `par`.
Se Diego preferisce numeri identici, si allinea tutto su A2 (perdendo i parziali dalla card).

**D4 — «Da Costruire» (DC) e «Mat. in Trattamento» (MIT) solo sulle Officine o anche sulle Commerciali?**
Nel prototipo erano officina-only; nel gestionale entrambi gli stati sono validi anche sulle Commerciali
(la matrice transizioni v40 esclude DC dalle Commerciali, ma i dati storici possono averlo).
*Raccomandazione*: **card e sezione visibili sempre se il conteggio è > 0**, sempre presenti sulle Officine
(anche a zero). Così un dato «impossibile» non sparisce silenziosamente.

---

## 7) Trappole

**Numeri e formati**
1. `euro()` di `@/lib/format` per **ogni** importo (formattazione manuale: il CLDR it-IT ometterebbe il punto delle
   migliaia sotto 10.000). Mai `toFixed(2)` a mano, mai `toLocaleString` per gli importi.
2. Le **percentuali** restano a **1 decimale** it-IT (`pctLabel`, `ddp-sintesi-logic.ts:122`), **non** i 2 decimali
   di `percent()` di `@/lib/format`: le due funzioni convivono, non confonderle.
3. Date: **`formatDateShort` (gg/mm/aa) a video**, **`formatDateFull` (gg/mm/aaaa) in PDF ed export**. Mai
   `toLocaleDateString` nudo, mai ISO in una cella.
4. Divisione per zero: con `total = 0` la percentuale deve dare `—`, non `NaN%`. `pctLabel` ha già la guardia —
   ogni nuovo calcolo di percentuale deve passare da lì.
5. Confronti tra date **sempre su stringa ISO date-only** (`toDateOnly`), mai su oggetti `Date`: è la convenzione
   del progetto e evita gli scivolamenti di fuso.

**Su quale totale sta la percentuale**
6. Destinazioni e Ripartizione: denominatore = **numero di righe** (`model.rows.length`).
   Top 10: denominatore = **valore economico** `kpi.totValue` (Σ `quantity × unitCost` **escluse le righe A9**).
   Non scambiarli: sono le due percentuali più facili da sbagliare.
7. `kpi.totValue` esclude A9; la somma delle righe della Top 10 **non** esclude nulla se si dimentica il filtro →
   una riga annullata ad alto valore falserebbe sia il rank sia il subtotale. Le Top 10 partono da `model.rows`,
   che è già filtrato dei padri-con-figli ma **non** di A9: decidere in un punto solo e commentarlo.

**Righe officina**
8. `GET /api/projects/{id}/ddp-officina` restituisce **anche i padri di composizione**: usare sempre `model.rows`.
   Contare da `rowsQuery.data` significa contare due volte i sottoassiemi.
9. `rowNumber` è posizionale e per i figli vale `"•"`: **chiave React e chiave delle righe spente = `row.id`**.
10. Il ricalcolo del costo del padre (Σ figli × `compositionQty`) vale **solo** per «Dati Distinta»: nelle 5 pagine
    nuove il padre non c'è proprio.

**Stampa ed export**
11. **Le stampe devono rispettare le righe spente**, in tutte e tre le forme: PDF di sezione, PDF aggregato e PDF di
    Dati Mancanti. È il primo bug che si fa: la vista filtra e il PDF no (o viceversa).
12. Le funzioni di stampa **non leggono la tabella a video**: ricostruiscono l'insieme dalla sorgente con lo stesso
    filtro e lo stesso ordinamento. Quindi un riordino manuale per colonna **non** finisce nel PDF — comportamento
    del prototipo, da mantenere.
13. Aggiungere una tabella al PDF = aggiungere una chiave a `sectionTable` (`DdpSintesiPage.tsx:508-562`), **non**
    scrivere un nuovo template: `printDdpTables` + `printHtml` gestiscono già testata ATEC, logo, A4 landscape,
    piè di pagina e blocco popup.
14. `printHtml` apre un **popup**: se è bloccato mostra già un toast e ritorna `false`. Non ignorare il valore di
    ritorno quando si stampa in sequenza.
15. Il PDF aggregato stampa le sezioni **nell'ordine canonico**, non nell'ordine in cui l'utente ha spuntato le
    caselle. E stampa anche le sezioni vuote, con «Nessuna riga.».

**Componenti e regole UI**
16. `<Collapsible>` (`components/ui/collapsible.tsx`) vuole solo `open`: **mai** `{open ? … : null}`, altrimenti
    l'animazione non ha nulla da misurare e il contenuto sparisce di colpo.
17. Ogni griglia dentro `GridScroller` (intestazione fissa + barra orizzontale **sopra**): la doppia barra del
    prototipo (`dd-hbar`) è esattamente ciò che `GridScroller` fa già. Non reimplementarla.
18. Nella tabella Dati Mancanti **entrambe** le righe di `<thead>` devono restare sticky, non solo la prima.
19. **Niente `flexRender`**: celle rimontate a ogni refetch = popover e menu che si chiudono da soli. Usare
    `renderColumnDef` o JSX stabile, e nessun hook dentro le funzioni di colonna.
20. Menu «Colonne»: aggiungendo o cambiando colonne **si versiona la chiave** (`ddp-sintesi-columns-v2`),
    altrimenti gli utenti si ritrovano colonne nascoste da una configurazione vecchia che non corrisponde più.

**Dati e semantica**
21. `MIT` esiste anche fuori dalle Officine: non filtrarlo via a priori (vedi D4). Se uno stato non è in anagrafica,
    l'etichetta ricade sulla chiave — mai su una stringa cablata.
22. Lo stato **`ND` (vuoto)** non si esclude dai Dati Mancanti: è proprio il difetto da segnalare. Non confondere
    «escluso» (A8) con «segnalato».
23. La sezione `tab` («In Ordine / Consegna — IO / PAR») **non contiene solo IO e PAR**: il titolo è storico, la
    nota deve spiegarlo, il filtro non va «corretto».
24. Il numero di righe con dati mancanti (`withMissing`), le analizzate e le escluse restano sul **totale**: non
    devono calare quando l'utente spegne una riga. Cala solo «righe visualizzate».
25. `staleTime: 0` + `useProjectHub` su tutte le query della Sintesi: oggi manca (`DdpSintesiPage.tsx:364-400`) ed è
    l'unica pagina DDP senza — chi modifica una riga da un'altra pagina non vede l'aggiornamento.
26. `useConfirm` per «Reset righe» (e per qualunque azione che cancelli scelte dell'utente). Mai `window.confirm`.
