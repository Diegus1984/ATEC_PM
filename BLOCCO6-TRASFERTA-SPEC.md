# Blocco 6 — Gestione Trasferta · specifica di partenza

Data: 04/08/2026. Punto d'ingresso: [PIANO-LAVORO-COMMESSE-V32.md](PIANO-LAVORO-COMMESSE-V32.md)
sezione «BLOCCO 6» (gli 11 punti). Qui c'è il **dettaglio del prototipo**
(`prototipi/Gestione_Commesse_V32.html`) più le decisioni prese per ATEC PM, così non va rifatta
l'analisi da capo. Il mattone del blocco 5 (`components/shared/calc-sheet.tsx`) è già pronto:
vedi [BLOCCO5-CALCOLATRICI-SPEC.md](BLOCCO5-CALCOLATRICI-SPEC.md).

---

## Il modulo del prototipo, com'è fatto

Una commessa ha **N step di trasferta** (`c.trasf.steps`), ognuno con una descrizione libera e una
tabella di **righe-persona**. Gli step sono collassabili e riordinabili a drag&drop.

### Griglia riga-persona — 14 colonne, intestazioni a due piani

| Gruppo | Colonne |
|---|---|
| **Personale** (6) | Nominativo · Inizio Trasferta · Fine Trasferta · Giorni trasferta · Ore Trasferta · Costi Personale |
| **Alloggio / Vitto** (4) | Notti · Prezzo · Costo · Vitto |
| **Altri costi** (3) | Indennità · Auto · Treno/aereo |
| — | colonna azioni (elimina persona) |

Formule (dal prototipo, alla lettera):
- `Giorni trasferta` = fine − inizio + 1, **inclusivo**, con **toggle sab / dom per riga**: i due
  pulsantini nella cella escludono selettivamente i sabati e/o le domeniche dal conteggio;
- `Costi Personale` = tariffa oraria della riga × `Ore Trasferta`;
- `Costo` (alloggio) = `Notti × Prezzo`;
- `Ore Trasferta`, `Vitto`, `Indennità`, `Auto` **non si digitano**: sono pulsanti che aprono la
  loro calcolatrice a righe e ne ricevono il totale;
- `Treno/aereo` è un importo digitato.

**Riga Totali** dello step (8 colonne valorizzate): Giorni, Ore, Costi Personale, Costo alloggio,
Vitto, Indennità, Auto, Treno/aereo. In testata dello step **3 badge**: «Totale costi personale»,
«Totale costi trasferta» (alloggio+vitto+indennità+auto+treno) e «Totale costi step».

### Le 4 calcolatrici (sopra il componente del blocco 5)

| Modalità | Col. 1 | Col. 2 | Col. 3 | Totale | Valori d'anagrafica |
|---|---|---|---|---|---|
| **Ore** | Giorni | Ore Lav. | Ore Tot. | «Totale ore» | — |
| **Vitto** | Giorni | Diaria | Tot. Diaria | «Totale diaria» | `DAILY_FOOD` |
| **Indennità** | Giorni | Indennità | Tot. Indennità | «Totale indennità» | `DAILY_ALLOWANCE` |
| **Auto** | Km Tratta | Rimborso Km | Costo | «Totale auto» | `COST_PER_KM` |

Due cose che il componente del blocco 5 **non** aveva e che servono qui:
1. la calcolatrice **Ore non è in euro**: colonne e totale sono numeri puri;
2. **Auto ha tre fattori** — `Km × Rimborso × Numero Tratte` (nel prototipo la terza colonna si
   chiama «Numero Tratte» e, se vuota, vale 1).

**Controllo di coerenza** (punto 9): nelle modalità con i Giorni, la finestra confronta la somma
della colonna Giorni con i Giorni trasferta della riga e mostra una banda verde («coerenti») o
gialla («anomalia: … non coincidono con …»). Non blocca nulla: informa.

### Tabella «Riepilogo Trasferta»

Stessa struttura a 14 colonne, un rigo per **nominativo distinto** di tutti gli step (ordine
alfabetico italiano, senza ripetizioni) e una riga finale **«Totale Riepilogo»** che somma i totali
di tutte le tabelle step. Nel prototipo le celle dei nominativi sono vuote: è un elenco, non un
per-persona. **In ATEC PM le valorizziamo**: avendo i dati, una riga per persona con i suoi totali
è più utile e non costa nulla.

### Card di commessa (punto 2)

Una card per commessa con 4 statistiche: **Giorni trasferta · Ore trasferta · Costi personale ·
Costi trasferta**, che apre il dettaglio con gli step.

---

## Decisioni per ATEC PM

**D6-A — Nominativo dall'anagrafica, con memoria dello storico.** Il prototipo pesca da
`personale` e tiene il valore con «(non in anagrafica)» se il nome non c'è più. In ATEC PM la riga
porta `employee_id` **e** lo snapshot `person_name`: la tendina è un `LookupCombobox` sui dipendenti
attivi, ma un nome storico resta leggibile anche se il dipendente sparisce. È la regola già in
vigore altrove, da non regredire.

**D6-B — La tariffa oraria si propone, non si impone.** Scegliendo la persona il campo si precompila
con il costo orario del suo reparto (dato che il prototipo non ha); resta modificabile e si può
prendere dall'anagrafica tariffe `HOURLY_RATE` del blocco 5.

**D6-C — Al Bilancio va SOLO la metà «Spese Trasferta».** Il prototipo rigenera anche le «Risorse
Atec» a consuntivo dagli step; in ATEC PM quella metà è **già** automatica e strutturale (dal
timesheet reale) ed è più affidabile: sovrascriverla sarebbe una regressione. Si sincronizza quindi
solo la voce «Spese Trasferta / indennità» del Riepilogo Costi, **usando il foglio di calcolo del
blocco 5** con `linked_source = 'trasferta:step:{id}'`: una riga per step, rigenerata a ogni
modifica, che **non tocca** le righe scritte a mano. Finché non esistono step, la voce continua a
leggere `projects.actual_travel_cost` esattamente come prima.

**D6-D — Il dettaglio delle 4 calcolatrici sta nei fogli del blocco 5**, con la chiave che porta
l'id della riga: `trasferta.ore:{rowId}`, `trasferta.vitto:{rowId}`, `trasferta.indennita:{rowId}`,
`trasferta.auto:{rowId}`. Nessuna tabella nuova per il dettaglio; alla cancellazione di una riga i
suoi fogli vanno cancellati a mano (non c'è FK: l'owner è polimorfo).

---

## Cosa esiste già in ATEC PM (non reinventarlo)

| Serve per | Esiste già come | Dove |
|---|---|---|
| Finestra di calcolo a righe | `CalcSheetDialog` (blocco 5) | `components/shared/calc-sheet.tsx` |
| Valori d'anagrafica dei calcoli | `tariff_options` + pannello di gestione | `TariffOptionsPanel`, `lib/api/tariffs.ts` |
| Pagina PM con lista commesse | `PmSidebar` | `components/shared/pm-sidebar.tsx` |
| Foglio a righe editabile | pattern SAL (`sal-row.tsx`, `sal-sheet-*`) | `features/commesse/` |
| Giorni fra due date | `dayCount` / `workingDayCount` | `features/risorse/planner-logic.ts` (weekend cablato) |
| Editor data a segmenti gg/mm/aaaa | `DateField` con `segmented` | `components/shared/date-field.tsx` |
| Marcatore righe automatiche | `linked_source` + badge «AUTO» | `project_calc_rows` (blocco 5) |

---

## Trappole

- **La griglia riga-persona è una griglia inline**, quindi valgono le due trappole del blocco 4:
  guardia «non riallineare mentre il fuoco è dentro la riga» e **un solo salvataggio per riga**,
  all'uscita dalla riga, con i valori definitivi. Il modello è `OrderLineRow` in `bva-order.tsx`.
  **Va provata a video compilando più campi di fila senza pause.**
- Le calcolatrici invece sono dialoghi con una Conferma sola: lì il problema non esiste.
- `Giorni` con fine < inizio vale «—», non un numero negativo.
- Regole di prodotto: importi con `euro()`, date gg/mm/aa con `formatDateShort`, `GridScroller` su
  ogni griglia, `LookupCombobox` per le tendine da anagrafica, `useConfirm` su ogni eliminazione,
  realtime + concurrency token sulle tabelle collaborative.
