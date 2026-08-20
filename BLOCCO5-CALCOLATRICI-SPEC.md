# Blocco 5 — Calcolatrici a righe + anagrafica tariffe · specifica di partenza

Data: 04/08/2026. Punto d'ingresso: [PIANO-LAVORO-COMMESSE-V32.md](PIANO-LAVORO-COMMESSE-V32.md)
sezione «BLOCCO 5» (i 7 punti e la decisione D4). Questo file contiene il **dettaglio del prototipo**
che serve per implementarli, estratto leggendo `prototipi/Gestione_Commesse_V32.html` — così non va
rifatta l'analisi da capo.

> Il prototipo è stato **copiato dentro il repo** (`prototipi/Gestione_Commesse_V32.html`, 567 KB):
> prima esisteva solo in `C:\Users\diego\Downloads\`, dove prima o poi sarebbe sparito.

---

## D4 — decisa, leggerla prima di scrivere codice

**Il «k correzione» del prototipo NON si adotta.** In ATEC PM il 45% è un **ricarico di vendita**,
non una maggiorazione di costo, e resta scritto come **moltiplicatore `1,450`**.

| | Prototipo V32 | ATEC PM (scelta di Diego, 04/08) |
|---|---|---|
| Cos'è | maggiorazione di **costo** | ricarico di **vendita** |
| Formula | `Costo finale = Costo × (1 + k/100)` | `Vendita = Costo × markup` |
| Nel Bilancio | ci finisce il valore **gonfiato** | ci finisce il **costo puro** |
| Come si scrive | `45` | `1,450` |

Conseguenze:
- nel calcolatore Officine il costo che alimenta la voce «Lavorazioni Officine» è
  `Ore × Costo orario`, **senza** maggiorazione → il Bilancio del blocco 4 non si muove;
- le colonne «Costo finale» / «Totale» del prototipo diventano una colonna **Vendita** con la stessa
  semantica delle risorse;
- il campo accetta moltiplicatori (1,000–9,999), **non** percentuali: `45` sarebbe ×45. Va vincolato
  in UI, altrimenti è un errore silenzioso.

Forma già in uso, da riusare: `DECIMAL(5,3)`, default `1.450` in `departments.default_markup`,
`cost_section_templates.default_markup`, `project_cost_resources.markup_value`. I materiali usano
`1.300` + provvigione `1.100`.

---

## Il calcolatore del prototipo, com'è fatto

Una sola finestra «Calcolo» riusata da 4 voci del Riepilogo Costi (nel prototipo via `CALC_CFG`).
Ogni voce del Riepilogo **non è un campo libero**: è un pulsante che apre il suo calcolatore
(`title` = «Apri il Calcolo Risorse Atec», «Apri il Calcolo Materiali commerciali», …).

Comportamenti comuni a tutte le modalità:
- **Invio = nuova riga**, totale live in fondo;
- **riordino drag&drop** delle righe;
- la **Conferma scarta le righe vuote**, scrive il totale nella voce e **persiste il dettaglio**
  (è il punto che in ATEC PM oggi manca del tutto: si salva solo il valore finale);
- resta sempre almeno 1 riga vuota;
- alla conferma, se tutte le sezioni sono vuote il totale della voce va a `null` → «—», non 0,00 €.

### Lavorazioni Officine (LAC) — due sezioni

Intestazioni esatte: **«Officine esterne»** e **«Officine interne»**. Non sono due voci del
Riepilogo: il Riepilogo vede una voce sola, «Lavorazioni Officine», pari alla somma delle due.
Banner in fondo alla finestra: **«Totale Lavorazioni Officine»**.

| Sezione | Colonne (prototipo) | Riga di totale |
|---|---|---|
| Officine esterne | ⠿ · Descrizione · Costo · ~~k correzione~~ · ~~Costo finale~~ | «Totale officine esterne» |
| Officine interne | ⠿ · Descrizione · Ore · Costo · ~~k correzione~~ · ~~Totale~~ | «Totale officine interne» |

Le colonne barrate cambiano per la D4: al posto di «k correzione»/«Costo finale» ci va il markup di
vendita `1,450` e una colonna **Vendita**, e la colonna che alimenta il Bilancio è il **Costo**.

Da decidere consapevolmente (comportamento del prototipo, non ovvio): nelle **interne**, se «Ore» è
vuoto il «Costo» vale come **importo manuale** invece che come tariffa oraria.

Le interne pescano il costo orario da un **elenco tariffe dedicato** (nel prototipo `tariffeOffInt`,
modal «Costo orario per Officine interne»), separato dalle tariffe risorse → è il punto 4 del piano
(UI per `tariff_options` + nuovo tipo «tariffa oraria»).

### Le 4 voci del Riepilogo Costi (etichette già in produzione dal blocco 4)

`Risorse Atec` · `Materiali commerciali` · `Lavorazioni Officine` · `Spese Trasferta / indennità`.
Non cambiarle: sono già a video nel tab «Preventivo vs Consuntivo».

---

## Cosa esiste già in ATEC PM (non reinventarlo)

| Serve per | Esiste già come | Dove |
|---|---|---|
| Override manuale «valore digitato = congelato» | flag DB + lucchetto | `contingency_pinned` / `margin_pinned`, Distribuzione prezzo |
| Marcatore di provenienza sulle righe automatiche | `linked_source` (rigenera solo le righe marcate, non tocca le manuali) | `project_cashflow_categories`, e `ddp_officina_item_id` su `project_work_requests` |
| Riga a due livelli (attività → N risorse) | `BuildActualEmployees` a consuntivo, e il foglio MoM | `BudgetVsActualController` |
| Anagrafica tariffe | `TravelTariffsController` completo (GET/POST/DELETE, anti-duplicati, blocco cancellazione se in uso) | **nessun file del web lo chiama**: manca solo la UI e il PUT |
| Griglia editabile inline | `GridScroller` + `DataTableCard` + celle `Ddp*Cell` | `features/commesse/` |

**`actualEmployees` arriva nel DTO e non viene renderizzato**: rinviato dal blocco 0 apposta, è la
«riga RAC con N linee-risorsa» del punto 5. Va reso qui, dentro `SectionBlock`.

**Fix lookup risorse (punto 6)**: il picker oggi restituisce **solo le risorse fittizie wildcard**
(`AND e.first_name LIKE '[%'`), non deduplica i nomi già usati e ordina in SQL invece che con
`localeCompare` italiano.

---

## Trappole pagate a caro prezzo nel blocco 4 — valgono identiche qui

Il blocco 5 è tutto griglie editabili con commit su blur. Questi due difetti **non li ha visti né la
build né una review avversariale a 6 lenti**: sono usciti solo provando a video, ed entrambi
perdevano dati in silenzio.

1. **Il refetch cancella quello che l'utente sta scrivendo.** Un campo committa uscendo, il
   ricaricamento che ne segue riallinea tutta la riga mentre l'utente è già nel campo accanto → il
   valore digitato sparisce senza errore. Serve la guardia «non riallineare mentre il fuoco è dentro
   la riga» (vedi `OrderLineRow` in `features/commesse/bva-order.tsx`).
2. **Più campi compilati di fila ne salvano uno.** Il secondo e il terzo commit trovano il primo
   ancora in volo e vengono **scartati** da `if (mutation.isPending) return`. Soluzione adottata: un
   solo salvataggio per riga, all'uscita dalla riga, con i valori definitivi.

Regola pratica: **ogni griglia editabile va provata a video compilando più campi di fila senza
pause**, non solo compilata.

Altre regole di prodotto che valgono qui: importi con `euro()`, percentuali con `percent()`,
`GridScroller` su ogni griglia, menu «Colonne» con chiave versionata, `LookupCombobox` per le tendine
da anagrafica, `useConfirm` su ogni eliminazione, realtime + concurrency token sulle tabelle
collaborative.
