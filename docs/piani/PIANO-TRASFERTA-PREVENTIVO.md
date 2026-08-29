# PIANO — Trasferta a righe dentro la sezione di preventivo (segnalazione #33)

> Segnalazione #33 — Paolo Zanoni, priorità alta, area «Sezione preventivo», 06/08/2026.
> Sostituire il riquadro «TRASFERTA (SEZIONE DA CLIENTE)» a 7 campi digitati a mano con la
> struttura a righe già esistente nel modulo **Menu PM → Trasferta** (gruppi «Alloggio / Vitto»
> e «Altri costi»), calcolatrici comprese, e portare al Riepilogo Costi un totale con il **K di
> ricarico sulle spese e nessun ricarico sull'indennità**.
>
> Documento verificato sul codice il 06/08/2026 (ogni riferimento `file:riga` è stato aperto).
> **Nessun file di codice è stato toccato**: questo è solo il piano.

---

## 1) Cosa c'è oggi

### 1.A — La «trasferta manuale» del preventivo

Vive tutta su **7 colonne di `project_cost_resources`** (una riga = una risorsa pianificata di una
sezione di costo), gemellata su `quote_cost_resources` per il lato Commerciale.

**DDL** — `ATEC.PM.Server/Services/DbService.cs:862-882`:

| colonna | tipo | etichetta a video |
|---|---|---|
| `num_trips` | INT | N° viaggi |
| `km_per_trip` | DECIMAL(8,1) | Km/viaggio |
| `cost_per_km` | DECIMAL(6,3) | €/km |
| `daily_food` | DECIMAL(8,2) | Vitto/g |
| `daily_hotel` | DECIMAL(8,2) | Hotel/g |
| `allowance_days` | INT | GG indennità |
| `daily_allowance` | DECIMAL(8,2) | Indennità/g |

Gemella lato offerta: `QuoteDbService.cs:357-363` (stesse colonne, stessi tipi).

**Dove si scrivono (4 percorsi, 2 tabelle):**
- Dialogo «Modifica/Nuova risorsa» — riquadro ambra, unico posto dove si vedono tutti e 7 i campi:
  `atec-pm-web/src/features/commesse/preventivo-dialogs.tsx:218-237` (stato locale 106-112,
  payload 156-162, blocco reso solo se `isClient = section.sectionType === "DA_CLIENTE"`, riga 83).
- Albero costing dell'offerta, versione ridotta a **2 campi su 7** (N. viaggi × Km/viaggio) con il
  totale in sola lettura: `features/preventivi/costing-rows.tsx:218-225` (payload completo in
  `buildResourceSave`, 160-180 — gli altri 5 campi si salvano ma non hanno UI).
- API: `ProjectCostingController.cs:212-238` (commessa), `QuoteCostingController.cs:84-99` (offerta).
- Copia in conversione/duplicazione: `QuotesController.cs:791-795`, `QuoteService.cs:146-150`.

**FORMULE REALI** — `ATEC.PM.Shared/DTOs/BudgetVsActual_DTOs.cs:345-348`:

```
TravelCost         = NumTrips × KmPerTrip × CostPerKm
AccommodationCost  = WorkDays × (DailyFood + DailyHotel)     ← moltiplica i GIORNI LAVORATIVI, non le notti
AllowanceCost      = AllowanceDays × DailyAllowance
TotalTravelCost    = TravelCost + AccommodationCost + AllowanceCost
```

Le stesse formule sono **duplicate** in `ProjectCosting_DTOs.cs:56-58`
(`TravelTotal` / `AccommodationTotal` / `AllowanceTotal`, più `TotalTravel` di sezione alla riga 30).

**Nessun totale è persistito**: sono proprietà calcolate (`=>`) ricostruite ad ogni GET.
Somma per sezione in `BudgetVsActualController.cs:208-210`; somma di commessa alla riga 357
(`budgetTravelTotal`), che alimenta:
- la voce «Spese Trasferta / indennità» lato **Preventivati** (`:509`);
- il Budget costi (`computedBudgetCost`, `:409`);
- il costo netto della Scheda Prezzi, **a costo secco** (`netCost`, `:333` — risorse e materiali
  entrano a VENDITA, la trasferta no).

**Il K oggi non tocca la trasferta.** `BudgetVsActualController.cs:75-79`:
`TotalSale = work_days × hours_per_day × hourly_cost × markup_value`. Il `markup_value`
(DECIMAL(5,3) default 1.450) moltiplica **solo la manodopera**.

**La «riga gialla»** che Paolo chiede di mantenere: `features/commesse/bva-sections.tsx:363-373`,
box `bg-amber-50/40`, «Trasferta preventivo: viaggi X · vitto/hotel Y · indennità Z = totale»,
condizionata a `isClient && section.budgetTotalTravelCost > 0` (riga 167-168). Lo stesso totale
compare come «+ Trasferta» in testata sezione (`bva-sections.tsx:245` → `bva-shared.tsx:99-101`).

### 1.B — La tabella del modulo Trasferta (quella da riusare)

**Tabelle** — `DbService.cs:586-623` (migrazione v63, `DbService.cs:3456-3512`):
`travel_steps(project_id, description, sort_order, row_version)` +
`travel_step_rows(step_id, employee_id, person_name, start_date, end_date, exclude_sat, exclude_sun,
hours, hourly_rate, nights, night_price, meal_cost, allowance_cost, car_cost, transport_cost,
sort_order, row_version)`.

**Griglia a 14 colonne, intestazione a due piani** — `features/trasferta/TravelStepTable.tsx:71-106`:

| gruppo | colonne |
|---|---|
| Personale (colSpan 6) | Nominativo · Inizio Trasferta · Fine Trasferta · Giorni trasferta · Ore Trasferta · Costi Personale |
| **Alloggio / Vitto** (colSpan 4) | **Notti · Prezzo · Costo · Vitto** |
| **Altri costi** (colSpan 3) | **Indennità · Auto · Treno/aereo** |

Riga TOTALI: `TravelTotalsRow`, `TravelStepTable.tsx:109-150` (montata in `<TableFooter>` alla 704-706).
Celle con calcolatrice: `CalcCell`, `TravelStepTable.tsx:152-182` (Ore, Vitto, Indennità, Auto).
Cestino per riga + drag&drop: righe 614-629 / 662.

**FORMULE REALI** — `ATEC.PM.Shared/DTOs/Travel_DTOs.cs:76-89`:

```
Days           = fine − inizio + 1, con esclusione selettiva sab/dom (TravelMath.Days, :215-232)
PersonnelCost  = HourlyRate × Hours
LodgingCost    = Nights × NightPrice          ← colonna «Costo» del gruppo Alloggio/Vitto, sola lettura
TravelCost     = LodgingCost + MealCost + AllowanceCost + CarCost + TransportCost
TotalCost      = PersonnelCost + TravelCost
```

`TravelTotalsDto.Of(rows)` (`Travel_DTOs.cs:111-126`) somma colonna per colonna. **Nessun markup,
da nessuna parte.**

**Calcolatrici** — `features/trasferta/TravelCalcDialog.tsx:39-80`, 4 configurazioni della finestra
riusabile `CalcSheetDialog` (`components/shared/calc-sheet.tsx:229`):

| kind | colonne | tariffa (`tariff_options`) | check |
|---|---|---|---|
| `ore` | Giorni × Ore Lav. (`valueKind: plain`) | — | Σ Giorni vs Giorni riga |
| `vitto` | Giorni × Diaria | `DAILY_FOOD` | Σ Giorni vs Giorni riga |
| `indennita` | Giorni × Indennità | `DAILY_ALLOWANCE` | Σ Giorni vs Giorni riga |
| `auto` | Km Tratta × Rimborso Km × Numero Tratte | `COST_PER_KM` | no |

`defaultMarkup={1}` alla riga 170: **le calcolatrici di trasferta non hanno il K**.

**Persistenza del dettaglio**: `project_calc_sheets` + `project_calc_rows` (`DbService.cs:552-580`),
chiave polimorfa `trasferta.{ore|vitto|indennita|auto}:{rowId}` (`BudgetVsActual_DTOs.cs:83-93`).
Formula di riga (`BudgetVsActual_DTOs.cs:158-166`):
`ComputedAmount = (Quantity==null ? UnitCost : Quantity×UnitCost) × (Multiplier ?? 1)` ·
`EffectiveAmount = AmountPinned ? Amount : ComputedAmount` · `SaleAmount = EffectiveAmount × MarkupValue`.

**Conferma calcolatrice** = una PUT che salva dettaglio **e** totale in colonna:
`TravelController.cs:286-320` (mappa kind→colonna alle righe 304-310; foglio vuoto → colonna NULL → «—»).

**Al Bilancio va solo la metà Spese**: `TravelPlanService.SyncToBudget` (`:89-109`) genera una riga
per step nel foglio `spese.actual` con `UnitCost = step.Totals.TravelCost`, `MarkupValue = 1.000m`,
`LinkedSource = "trasferta:step:{id}"` → letta lato **Consuntivati** in
`BudgetVsActualController.cs:395-397, 512`.

### 1.C — I due lati della stessa voce, oggi

| «Spese Trasferta / indennità» | sorgente | K |
|---|---|---|
| **Preventivati** | Σ dei 7 campi manuali (`budgetTravelTotal`, `:357` → `:509`) | nessuno |
| **Consuntivati** | foglio `spese.actual` dagli step di Trasferta, altrimenti `projects.actual_travel_cost` (`:395-397` → `:512`) | 1.000 fisso |

Nome reale della voce (i minuscola): `Label = "Spese Trasferta / indennità"`, `Key = "spese"`
(`BudgetVsActualController.cs:507-508`).

### 1.D — I ganci già in tabella e mai usati

`project_pricing.travel_markup` e `project_pricing.allowance_markup`, DECIMAL(5,3) NOT NULL
DEFAULT 1.000 (`DbService.cs:923-924`; gemelle `quote_pricing`, `QuoteDbService.cs:437-438`).
Verificato: si salvano (`ProjectCostingController.cs:346`, `QuoteCostingController.cs:282`), si
leggono (`CostingDataService.cs:75,164`), si copiano nella conversione (`QuotesController.cs:944-945`,
`QuoteService.cs:220-221`), esistono nei tipi TS (`lib/api/types/costing.ts:459-460`) — e **non
compaiono in nessuna formula né in nessuna UI**. La SELECT del Bilancio legge solo
`contingency_pct, negotiation_margin_pct` (`BudgetVsActualController.cs:320-322`); il `PricingBlock`
espone solo quelle due (`bva-economics.tsx:223-282`). Sono il gancio esatto della richiesta #33.

---

## 2) Cosa deve diventare

Dentro **ogni sezione di costo con Tag Cliente** (`section_type = 'DA_CLIENTE'`), al posto del
riquadro ambra a 7 campi, una **tabella a righe** con la stessa forma della Trasferta, **limitata ai
due gruppi chiesti da Paolo** (il gruppo «Personale» resta fuori: nel preventivo le ore/€/h/K stanno
già nella griglia «Risorse pianificate», `bva-sections.tsx:268-356`).

### 2.A — Colonne

| # | gruppo | colonna | tipo | come si compila |
|---|---|---|---|---|
| 1 | — | **Nominativo / voce** | testo | `LookupCombobox` sulle risorse pianificate della sezione, con voce «Manuale (testo libero)». Il nome resta scritto sulla riga (pattern `travel_step_rows.person_name`, `TravelController.cs:178-186`) |
| 2 | Alloggio / Vitto | **Notti** | INT | `Input` numerico |
| 3 | Alloggio / Vitto | **Prezzo** | € | `MoneyInput` + picker tariffe `DAILY_HOTEL` (**novità**: nella Trasferta il picker qui non c'è, `TravelStepTable.tsx:562-580`) |
| 4 | Alloggio / Vitto | **Costo** | € | **derivata, sola lettura** = Notti × Prezzo |
| 5 | Alloggio / Vitto | **Vitto** | € | 🧮 calcolatrice `vitto` (Giorni × Diaria, tariffa `DAILY_FOOD`) |
| 6 | Altri costi | **Indennità** | € | 🧮 calcolatrice `indennita` (Giorni × Indennità, tariffa `DAILY_ALLOWANCE`) |
| 7 | Altri costi | **Auto** | € | 🧮 calcolatrice `auto` (Km Tratta × Rimborso Km × Numero Tratte, tariffa `COST_PER_KM`) |
| 8 | Altri costi | **Treno/aereo** | € | `MoneyInput` digitato, **senza** calcolatrice |
| 9 | — | cestino | — | `useConfirm` (mai `window.confirm`) |

Intestazione a **due piani** identica alla Trasferta: `colSpan={4}` «Alloggio / Vitto» +
`colSpan={3}` «Altri costi». Riusare per copia la forma di `TravelTableHead`
(`TravelStepTable.tsx:71-106`), non reinventarla.

### 2.B — Formule di riga (nuove, gemelle di `Travel_DTOs.cs:82-89`)

```
LodgingCost    = Nights × NightPrice                      (NULL se manca uno dei due → «—»)
MarkableCost   = LodgingCost + MealCost + CarCost + TransportCost      ← soggetto a K
AllowanceCost  = AllowanceCost                                          ← MAI ricaricato
TravelCost     = MarkableCost + AllowanceCost                           (costo secco della riga)
```

> La separazione `MarkableCost` / `AllowanceCost` è **la sola differenza di modello** rispetto alla
> Trasferta, dove `TravelRowDto.TravelCost` (`Travel_DTOs.cs:85-87`) fonde tutto in un numero solo.

### 2.C — Riga dei totali

`<TableFooter>` con una riga «Totali» modellata su `TravelTotalsRow`
(`TravelStepTable.tsx:109-150`): etichetta con `colSpan`, poi le celle valorizzate
**Costo (alloggio) · Vitto · Indennità · Auto · Treno/aereo**. Notti e Prezzo restano **vuote**
(sommare notti e prezzo/notte non ha senso — è già la scelta della Trasferta, righe 130-131).
In coda, due totali di sezione dichiarati a testo:
`Spese ricaricabili {euro} × K {k} = {euro}` e `Indennità {euro}` .

### 2.D — Comportamento delle calcolatrici

Identico alla Trasferta, con chiavi nuove:

- Chiavi foglio: `preventivo.vitto:{rowId}`, `preventivo.indennita:{rowId}`, `preventivo.auto:{rowId}`
  — **da registrare in `CalcKeys` e in `CalcKeys.IsKnown`** (`BudgetVsActual_DTOs.cs:71-103`), che è
  una whitelist: chiave non prevista = GET/PUT rifiutate con «Foglio di calcolo sconosciuto»
  (`BudgetVsActualController.cs:563, 578`).
- Endpoint dedicati `GET/PUT /api/projects/{id}/costing/travel-rows/{rowId}/calc/{kind}`, modellati
  **alla lettera** su `TravelController.cs:266-320`: il server salva il foglio **e** scrive il
  totale nella colonna in un colpo solo. Foglio vuoto → colonna a NULL → cella «—», non 0,00 €.
- `defaultMarkup={1}` come in `TravelCalcDialog.tsx:170`: **il K non entra nella finestra di
  calcolo**, si applica dopo, sul totale (vedi §3).
- Nessuna colonna `markup`/`sale` nelle sezioni della finestra.
- `check` sui Giorni (`CalcSheetCheck`, `calc-sheet.tsx:104-111`): `expected` = `work_days` della
  risorsa collegata se la riga è agganciata a una risorsa pianificata, altrimenti `null` (check
  disattivato). Non blocca, informa.
- Picker tariffe: `unitCostOptions.values` da `fetchTariffOptions(tipo)`; aggiungere
  `manageLabel: "Gestisci tariffe…"` + `onManage` che apre `TariffOptionsPanel` in dialog, come fa
  `WorkshopCalcDialog.tsx:100-104, 124-137`.
- Cancellando una riga o una sezione, **cancellare a mano i fogli**: non c'è FK
  (`ProjectCalcSheets.DeleteSheets`, come `TravelController.cs:112-121, 239`).

### 2.E — Cosa resta

- La **riga gialla** per attività: resta esattamente dov'è (`bva-sections.tsx:363-373`), cambia solo
  la sorgente dei tre numeri. Proposta di testo aggiornato:
  «Trasferta preventivo: alloggio/vitto X · auto Y · treno/aereo Z → ricaricate ×K = W · indennità V = TOTALE».
- Il «+ Trasferta» in testata sezione (`bva-shared.tsx:99-101`) continua a leggere
  `section.budgetTotalTravelCost`.
- La griglia «Risorse pianificate» e il dialogo risorsa restano, **senza** il riquadro ambra a 7 campi.

---

## 3) Regola dei ricarichi

**Testo di Paolo, tradotto in formule.** Il K si applica ai costi **Alloggio/Vitto, Auto,
Treno/Aereo**; l'**Indennità no**, si somma tal quale.

### 3.A — Quale K

Si riusa **`project_pricing.travel_markup`** (DECIMAL(5,3), DEFAULT **1.000**, già in tabella e già
copiata nelle conversioni). `allowance_markup` **non si usa**: l'indennità non ha ricarico per
decisione #33 — va lasciata a 1.000 con un commento che lo dica, o si torna a cercarla fra un anno.

Perché va bene: default 1.000 su **tutte** le righe esistenti ⇒ attivare la formula **non sposta di
un centesimo** nessuna commessa storica finché qualcuno non digita un K diverso.
UI nuova: un campo «K trasferta» dentro `PricingBlock` (`bva-economics.tsx:210+`), accanto a
Imprevisti e Margine, salvato dalla PUT che già esiste (`ProjectCostingController.cs:338-357`).
Validazione: moltiplicatore **1,000 ≤ K ≤ 9,999** (stessa banda e stesso messaggio di
`ProjectCalcSheets.cs:22-23, 78-79` — «1,450 = +45%», **non** una percentuale).

### 3.B — Formula finale della voce «Spese Trasferta / indennità» (lato Preventivati)

```
perRiga:
  MarkableCost(r) = (Nights×NightPrice ?? 0) + (MealCost ?? 0) + (CarCost ?? 0) + (TransportCost ?? 0)
  AllowanceCost(r) = (AllowanceCost ?? 0)

perSezione DA_CLIENTE s:
  BudgetTravelMarkable(s) = Σ_r MarkableCost(r)
  BudgetAllowance(s)      = Σ_r AllowanceCost(r)

commessa:
  budgetTravelMarkable = Σ_s BudgetTravelMarkable(s)
  budgetAllowance      = Σ_s BudgetAllowance(s)

  ┌─────────────────────────────────────────────────────────────────────────┐
  │  «Spese Trasferta / indennità» (Preventivati) =                          │
  │        budgetTravelMarkable × project_pricing.travel_markup              │
  │      + budgetAllowance                                                   │
  └─────────────────────────────────────────────────────────────────────────┘
```

Sostituisce `budgetTravelTotal` di `BudgetVsActualController.cs:357`, che oggi entra in tre punti e
va aggiornato in **tutti e tre**:
1. voce `spese` del Riepilogo Costi (`:509`) — **valore ricaricato**;
2. `computedBudgetCost` del Budget costi (`:409`) e quindi `BudgetProfitability` (`:421, 455-456`);
3. `netCost` della Scheda Prezzi (`:333`) — **valore ricaricato** (coerente: lì risorse e materiali
   entrano già a vendita), che si propaga a `offerPrice` e `finalPrice`.

Aggiungere a `BvaEconomicSummary` (`BudgetVsActual_DTOs.cs:462`) i tre numeri separati —
`BudgetTravelMarkableCost`, `BudgetTravelMarkup`, `BudgetAllowanceCost` — perché il client possa
scrivere l'hint sotto il numero.

### 3.C — Asimmetria con il lato Consuntivati: dichiararla

Il lato **Consuntivati** della stessa voce resta a **costo secco**: viene dal foglio `spese.actual`
alimentato dagli step di Trasferta con `MarkupValue = 1.000m` (`TravelPlanService.cs:103`) e la
stessa regola è riscritta in SQL (`TravelPlanService.ActualTravelCostSql:127-132`, che somma
`r.amount`, cioè il costo, e serve `/bilancio` e la dashboard commessa).
**Non toccare quella SQL**: il consuntivo è un costo reale, non un prezzo.
Va però scritto a video, o si legge come un errore:
`BudgetHint = "spese ×K {k}, indennità senza ricarico"`, `ActualHint = "dalla Gestione Trasferta (costo, senza ricarico)"`.

> ⚠️ Questa voce diventa l'**unica** delle 4 del Riepilogo a mostrare un valore ricaricato: contraddice
> in modo esplicito la decisione D4 («il 45% è un ricarico commerciale e non deve gonfiare la voce di
> costo», commento a `BudgetVsActualController.cs:436-437` e `WorkshopCalcDialog.tsx:10-14`).
> È quello che chiede la #33: va annotato nel codice come decisione **D33-A**, con data e autore.

---

## 4) Dati

### 4.A — Si riusa o si crea?

| elemento | verdetto |
|---|---|
| `travel_steps` / `travel_step_rows` | **NON riusabili come sono**: pendono da `travel_steps.project_id`, non hanno alcun aggancio a `project_cost_sections` (`DbService.cs:586-596`). Servono da **modello di colonne**, non da contenitore |
| `project_calc_sheets` / `project_calc_rows` | **riusati così come sono** (owner = commessa + `calc_key`; bastano chiavi nuove) |
| `tariff_options` + `/api/tariff-options` + `TariffOptionsPanel` | **riusati, zero modifiche** |
| `CalcSheetDialog` (`calc-sheet.tsx`) | **riusato, zero modifiche** — ha già `multiplier`, `valueKind`, `hideDescription`, `unitCostOptions`, `check` |
| `project_pricing.travel_markup` | **riusata**, oggi solo trasportata |
| i 7 campi di `project_cost_resources` / `quote_cost_resources` | **NON si eliminano** (vedi §5) |
| tabella righe di trasferta di preventivo | **NUOVA** |

### 4.B — Tabella nuova (migrazione **v68**)

```sql
CREATE TABLE IF NOT EXISTS project_cost_travel_rows (
  id            INT AUTO_INCREMENT PRIMARY KEY,
  section_id    INT NOT NULL,
  resource_id   INT NULL,                       -- aggancio facoltativo alla risorsa pianificata
  person_name   VARCHAR(200) NOT NULL DEFAULT '',
  nights        INT NULL,
  night_price   DECIMAL(12,2) NULL,
  meal_cost     DECIMAL(12,2) NULL,             -- calcolatrice 'preventivo.vitto:{id}'
  allowance_cost DECIMAL(12,2) NULL,            -- calcolatrice 'preventivo.indennita:{id}'
  car_cost      DECIMAL(12,2) NULL,             -- calcolatrice 'preventivo.auto:{id}'
  transport_cost DECIMAL(12,2) NULL,            -- digitato
  sort_order    INT NOT NULL DEFAULT 0,
  row_version   INT NOT NULL DEFAULT 0,
  created_at    DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at    DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  CONSTRAINT fk_pctr_section  FOREIGN KEY (section_id)  REFERENCES project_cost_sections(id) ON DELETE CASCADE,
  CONSTRAINT fk_pctr_resource FOREIGN KEY (resource_id) REFERENCES project_cost_resources(id) ON DELETE SET NULL,
  INDEX idx_pctr_section (section_id, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

Tipi identici a `travel_step_rows` (`DbService.cs:600-623`) — deliberatamente, così le formule e i
componenti si trasportano senza conversioni. `row_version` = concorrenza ottimistica per riga
(ambiente condiviso, regola fissa di progetto).

Va creata **in due punti**, come tutte le altre: nel percorso dev (`InitDatabase`, accanto a
`DbService.cs:582-623`) **e** nel blocco della migrazione v68, con lo stesso DDL.

### 4.C — ⚠️ Trappola `LatestSchemaVersion`

`DbService.cs:1328` → `private const int LatestSchemaVersion = 67;` (ultima applicata: **v67**,
`ddp_row_off`, `DbService.cs:3603`). La nuova migrazione è la **v68** e
**`LatestSchemaVersion` va alzato a 68 nello stesso commit**.

Il commento alle righe 1320-1327 lo dice già con un precedente vero: dimenticarlo **non dà nessun
errore**, la migrazione semplicemente non gira mai su un DB già alla versione precedente — è
successo con la v66 il 04/08/2026, applicata in sviluppo (DB a v64) e **saltata in produzione**
(DB a v65), che si è ritrovata il ruolo AMM ancora vivo. Il cancello è
`if (currentVersion >= LatestSchemaVersion) return;` (`DbService.cs:1398`).

**Il server LAN 192.168.2.150 è in produzione**: la v68 girerà lì al primo riavvio del servizio
`AtecPmServer`. Fare il backup completo prima (funzione «Backup completo» già in produzione).

### 4.D — Lato Commerciale (offerte)

`quote_cost_sections` esiste con `section_type` (`QuoteDbService.cs:322-327`), ma **non esiste
`quote_calc_sheets`**: `ProjectCalcSheets.Save` verifica `SELECT COUNT(*) FROM projects WHERE id=@Pid`
(`ProjectCalcSheets.cs:84-90`) e `project_calc_sheets.project_id` ha FK su `projects`
(`DbService.cs:560`). Quindi **le calcolatrici non sono trasportabili sull'offerta senza un lavoro
a parte** (o tabelle gemelle `quote_calc_*`, o generalizzazione dell'owner a `owner_type/owner_id`).
→ è la **Decisione 2** di §7. In Fase 1 il lato offerta resta com'è (2 campi su 7, `costing-rows.tsx:218-225`).

---

## 5) Cosa succede ai dati già inseriti a mano — risposta esplicita

**I 7 campi NON si buttano e NON si cancellano dal database.** Restano in
`project_cost_resources` e `quote_cost_resources` perché:
(a) sono dati di produzione; (b) li usa ancora il Commerciale (`costing-rows.tsx`, `QuotePdfService.cs:548-564`,
conversioni `QuotesController.cs:791-795`, `QuoteService.cs:146-150`); (c) la guardia
anti-cancellazione delle tariffe li interroga per nome
(`TravelTariffsController.cs:117-150`: `cost_per_km` / `daily_food` / `daily_hotel` / `daily_allowance`
su **entrambe** le tabelle).

**Si convertono in righe della tabella nuova**, con una conversione **una-tantum dentro la v68**,
per ogni risorsa di una sezione `DA_CLIENTE` che abbia almeno un campo trasferta ≠ 0 —
**una riga per risorsa**, totale identico al centesimo:

| campo nuovo | valore |
|---|---|
| `section_id` | `r.section_id` |
| `resource_id` | `r.id` |
| `person_name` | `r.resource_name` |
| `nights` / `night_price` | `r.work_days` / `r.daily_hotel` **solo se `daily_hotel > 0`**, altrimenti NULL/NULL → `LodgingCost = work_days × daily_hotel`, identico alla metà «hotel» del vecchio `AccommodationCost` |
| `meal_cost` | `r.work_days × r.daily_food` (NULL se `daily_food = 0`) |
| `allowance_cost` | `r.allowance_days × r.daily_allowance` (NULL se 0) |
| `car_cost` | `r.num_trips × r.km_per_trip × r.cost_per_km` (NULL se 0) |
| `transport_cost` | NULL (il dato non esiste nel vecchio modello) |

> Il vecchio `AccommodationCost = WorkDays × (DailyFood + DailyHotel)` **non ha il concetto di
> notti**: la conversione usa `notti = giorni lavorativi`. È un'approssimazione **dichiarata** che
> lascia la somma invariata; l'utente potrà correggere le notti a mano.

**La conversione scrive anche il dettaglio delle calcolatrici**, così riaprendo la finestra non si
trova vuota (e una Conferma a vuoto non azzera la colonna — vedi §8):
- `preventivo.vitto:{rowId}` → 1 riga `quantity = work_days`, `unit_cost = daily_food`, `markup 1.000`;
- `preventivo.indennita:{rowId}` → 1 riga `quantity = allowance_days`, `unit_cost = daily_allowance`;
- `preventivo.auto:{rowId}` → 1 riga `quantity = km_per_trip`, `unit_cost = cost_per_km`,
  `multiplier = num_trips` (esattamente la forma della calcolatrice Auto, `TravelCalcDialog.tsx:69-79`).
Marcare tutte con `linked_source = 'migrazione:v68'` per poterle riconoscere.

**Doppio conteggio: come si evita.** I 7 campi restano scritti in tabella, quindi il totale di
preventivo deve avere **una sola** sorgente, con la stessa regola già collaudata sul lato consuntivo
(`travelSheetTotal ?? actual_travel_cost`, `BudgetVsActualController.cs:397`):

```
per sezione DA_CLIENTE:
    se esiste ≥ 1 riga in project_cost_travel_rows  → si usano SOLO le righe
    altrimenti                                       → fallback ai 7 campi legacy (formule di §1.A)
```

Il fallback serve alle commesse toccate solo dal Commerciale (o create dopo la v68 da una
conversione offerta→commessa, che continua a copiare i 7 campi). La conversione della v68 fa sì che
in pratica il fallback non scatti quasi mai; **non va rimosso** finché il Commerciale non è allineato.

**Il dialogo risorsa perde i 7 campi** (`preventivo-dialogs.tsx:218-237` e i `NumField` relativi);
il payload continua a mandarli, per non toccare il contratto: si mandano i valori esistenti
invariati (oggi il salvataggio li azzera se la sezione non è DA_CLIENTE, righe 156-162 —
quel comportamento resta).

---

## 6) Ordine di esecuzione (blocchi verificabili singolarmente)

**B0 — Preparazione (0,5 g).** Backup completo del DB di produzione. Registrare in
`BudgetVsActual_DTOs.cs` le 3 chiavi nuove in `CalcKeys` + `IsKnown`.
✅ *Verifica*: `dotnet build ATEC.PM.sln`; una GET `calc/preventivo.vitto:1` non risponde più
«Foglio di calcolo sconosciuto».

**B1 — Schema (0,5 g).** DDL `project_cost_travel_rows` nel percorso dev + migrazione **v68**;
`LatestSchemaVersion = 68`.
✅ *Verifica*: DB vergine → tabella creata; DB a v67 → migrazione applicata e log
`[Migration v68] …`; `SHOW CREATE TABLE project_cost_travel_rows`.

**B2 — DTO + formule (0,5 g).** `ProjectCostTravelRowDto` (gemello di `TravelRowDto`) con
`LodgingCost`, `MarkableCost`, `AllowanceCost`, `TravelCost`; `ProjectCostTravelTotalsDto.Of(rows)`.
Aggiungere a `BvaSectionDto` `BudgetTravelMarkableCost` / `BudgetTravelAllowanceCost` mantenendo
`BudgetTotalTravelCost` (lo legge la riga gialla e la testata sezione).
✅ *Verifica*: build; nessun consumatore rotto (`BudgetTotalTravelCost` conserva la firma).

**B3 — API CRUD righe (1 g).** In `ProjectCostingController`: `GET/POST/PUT/DELETE`
`sections/{sectionId}/travel-rows` + `travel-rows/reorder`, con `row_version` e messaggio di
conflitto come `TravelController.cs:221`. La DELETE riga e la DELETE sezione
(`ProjectCostingController.cs:200-206`) devono chiamare `ProjectCalcSheets.DeleteSheets`.
Ogni scrittura → `NotifyBudgetChanged` (`BudgetVsActualController.cs:31-40`).
✅ *Verifica*: da REST client, ciclo completo su una sezione DA_CLIENTE; cancellando la sezione non
restano fogli orfani (`SELECT * FROM project_calc_sheets WHERE calc_key LIKE 'preventivo.%'`).

**B4 — API calcolatrici (0,5 g).** `GET/PUT travel-rows/{rowId}/calc/{kind}` copiati da
`TravelController.cs:266-320` (salva foglio + colonna; vuoto → NULL).
✅ *Verifica*: conferma con righe → colonna valorizzata; conferma a vuoto → colonna NULL e cella «—».

**B5 — Sorgente del totale a preventivo (1 g).** In `BudgetVsActualController`: leggere le righe
nuove, riempire `BudgetTravelMarkableCost` / `BudgetTravelAllowanceCost` per sezione con la regola
di fallback di §5, e sostituire `budgetTravelTotal` (`:357`) con la formula di §3.B nei **tre** punti
(`:333`, `:409`, `:509`). Nuovi campi in `BvaEconomicSummary` + hint.
✅ *Verifica*: su una commessa con K = 1,000 il Bilancio è **identico al centesimo** a prima (è il
test di non-regressione più importante); portando K a 1,45 sale solo la parte ricaricabile.

**B6 — UI tabella (1,5 g).** `features/commesse/preventivo-travel-table.tsx`: `GridScroller` +
`Table` + intestazione a due piani + `TableFooter`, celle `CalcCell` per Vitto/Indennità/Auto,
`MoneyInput` per Notti/Prezzo/Treno-aereo, «Costo» derivata, cestino con `useConfirm`.
**Riusare il pattern di riga inline di `TravelStepTable.tsx:238-350`**: guardia anti-refetch su
`rowRef.current?.contains(document.activeElement)` (riga 275) e **un solo `commitRow` all'uscita
dalla riga** (righe 315-350). Importi con `euro()` di `@/lib/format`.
Montarla in `bva-sections.tsx` al posto del riquadro 7 campi, sotto «Risorse pianificate», visibile
solo se `isClient`, editabile solo se `canEditBudget` (`ProjectBudgetVsActual.tsx:55`).
✅ *Verifica*: `npx tsc -b` (**non** `tsc --noEmit`: esce 0 senza controllare) + `npm run build`.

**B7 — Dialogo calcolatrice (0,5 g).** `PreventivoTravelCalcDialog`, copia di
`TravelCalcDialog.tsx` con 3 configurazioni (vitto/indennità/auto), `defaultMarkup={1}`,
`unitCostOptions` con `manageLabel`/`onManage` verso `TariffOptionsPanel`.
✅ *Verifica*: build; le tendine tariffe mostrano i valori di `tariff_options`.

**B8 — Riga gialla + K in UI (0,5 g).** Aggiornare il testo della riga gialla
(`bva-sections.tsx:363-373`) alla nuova scomposizione; aggiungere il campo «K trasferta» in
`PricingBlock` (`bva-economics.tsx:210+`) con validazione 1,000–9,999; hint sulla voce `spese` del
`CostSummaryBlock` (`bva-order.tsx:496-519`).
✅ *Verifica*: build; K salvato e riletto; riga gialla coerente col Riepilogo.

**B9 — Rimozione riquadro 7 campi + conversione dati (0,5 g).** Togliere il blocco
`preventivo-dialogs.tsx:218-237` e i relativi `NumField`; nella v68 aggiungere la conversione di §5.
✅ *Verifica*: su una copia del DB di produzione, per ogni sezione DA_CLIENTE il
`BudgetTotalTravelCost` **prima** e **dopo** la migrazione coincide (query di confronto salvata).

**B10 — Runtime (0,5 g).** Prova sull'app vera su una commessa con Tag Cliente: creare righe,
aprire le 3 calcolatrici, cancellare una riga, verificare il Riepilogo Costi e la Scheda Prezzi con
K 1,000 e 1,450, e con **due browser aperti** che il realtime allinei (evento `BudgetChanged`).
Spegnere i server a fine prova (porte 5150/5151/5173).

Totale stimato: **~7,5 giornate** (Fase 1, sola commessa).

---

## 7) Decisioni da chiedere a Diego (3)

**D1 — Il K di ricarico è per COMMESSA o per ATTIVITÀ (sezione)?**
`project_pricing.travel_markup` è per commessa; Paolo parla di «attività con Tag Cliente», che
potrebbero volere K diversi.
👉 **Raccomandazione: per COMMESSA**, riusando `travel_markup` (esiste, default 1.000, già copiata
nelle conversioni ⇒ zero impatto retroattivo, zero migrazione aggiuntiva). Se serve il K per
attività si aggiunge poi una colonna `travel_markup` su `project_cost_sections` con fallback su
quello di commessa — è additivo e non rifà il lavoro.

**D2 — Il Commerciale (offerte) entra in Fase 1 o dopo?**
Le calcolatrici non funzionano sulle offerte: `project_calc_sheets` ha FK su `projects` e
`ProjectCalcSheets.Save` pretende una commessa esistente (`ProjectCalcSheets.cs:84-90`, `DbService.cs:560`).
Portarle sull'offerta significa o le tabelle gemelle `quote_calc_*` o generalizzare l'owner
(`owner_type/owner_id`) su tutte le chiamate esistenti.
👉 **Raccomandazione: DOPO.** Fase 1 solo commessa; l'offerta resta com'è (2 campi su 7) e la
conversione offerta→commessa continua a copiare i 7 campi, che il fallback di §5 sa leggere.

**D3 — Il valore ricaricato entra anche nella Scheda Prezzi (offerPrice / finalPrice)?**
Oggi la trasferta entra nel `netCost` a costo secco (`BudgetVsActualController.cs:333`) mentre
risorse e materiali entrano a vendita. Se ci mettiamo il ricaricato, cambiano `offerPrice` e
`finalPrice` di ogni commessa in cui qualcuno alza il K.
👉 **Raccomandazione: SÌ.** È lo stesso valore chiesto per il Riepilogo, ed è coerente con le altre
due voci che lì entrano già a vendita. Con K = 1,000 (tutte le commesse esistenti) non cambia nulla.

---

## 8) Trappole

1. **`LatestSchemaVersion` (`DbService.cs:1328`)** — alzarla a 68 nello stesso commit della v68.
   Dimenticarla non dà errori: la migrazione semplicemente non gira in produzione (precedente reale
   della v66, commento alle righe 1320-1327).
2. **La riga gialla sparisce da sola, in silenzio.** È condizionata a `budgetTotalTravelCost > 0`
   (`bva-sections.tsx:167-168, 363`): se la nuova sorgente non alimenta più quel campo, il box
   scompare **senza nessun errore**. Tenere `BudgetTotalTravelCost` valorizzato (= markable + allowance,
   costo secco) anche dopo il refactoring.
3. **Le formule sono duplicate in due DTO** — `BudgetVsActual_DTOs.cs:345-348` (Bilancio) e
   `ProjectCosting_DTOs.cs:53-58` (costing/offerta). Toccarne una sola fa divergere Bilancio e
   Commerciale.
4. **Non rimuovere le 4 colonne tariffate.** `TravelTariffsController.cs:117-150` costruisce una
   query dinamica su `cost_per_km` / `daily_food` / `daily_hotel` / `daily_allowance` di
   `project_cost_resources` **e** `quote_cost_resources`: senza quelle colonne la DELETE tariffa
   va in errore SQL o smette di proteggere.
5. **`ProjectCalcSheets.Save` fa sostituzione INTEGRALE** (`:124-155`): cancella tutte le righe e
   le riscrive, gli id cambiano ad ogni Conferma — nessuno ci si può agganciare. E valida il markup
   fra 1,000 e 9,999 (`:78-79`): il K va scritto come **moltiplicatore**, non come percentuale.
6. **Conferma a vuoto = colonna a NULL.** `TravelController.cs:303, 311-316`: se la migrazione
   scrive `meal_cost` senza il foglio, il primo che apre la calcolatrice e conferma **azzera il
   valore migrato**. È il motivo per cui la v68 deve scrivere anche il dettaglio (§5).
7. **Fogli orfani.** I fogli hanno owner polimorfo e nessuna FK: DELETE riga **e** DELETE sezione
   devono chiamare `ProjectCalcSheets.DeleteSheets` (modello: `TravelController.cs:112-121, 239`).
   La DELETE sezione attuale (`ProjectCostingController.cs:200-206`) è un `DELETE` secco: va estesa.
8. **`CalcKeys.IsKnown` è una whitelist** (`BudgetVsActual_DTOs.cs:95-102`). Chiave nuova non
   registrata = GET e PUT rifiutate. `calc_key` è **VARCHAR(40)**: `preventivo.indennita:` + id sta
   dentro, ma è vicino al limite — non allungare i prefissi.
9. **`— ≠ 0,00 €`.** `NullIfZero` (`BudgetVsActualController.cs:468`) vs. il comportamento della
   voce `lavorazioni` (`:494-496`): per una voce alimentata a righe, «—» = nessuna riga compilata,
   «0,00 €» = calcolo che somma zero. La voce `spese` lato Preventivati deve adottare la regola
   della `lavorazioni`, non più `NullIfZero`.
10. **Il «Totale Costi» lo somma il CLIENT**, la Redditività arriva dal SERVER
    (`bva-order.tsx:472-477` vs `BudgetVsActualController.cs:421-422`). Se si cambia il valore della
    voce `spese` senza cambiarlo anche in `budgetCost` (`:409`), le due righe non quadrano a video.
11. **Bilancio in SOLA LETTURA se la commessa nasce da un'offerta**: `canEditBudget =
    data.linkedQuoteId === 0` (`ProjectBudgetVsActual.tsx:55`). La tabella nuova deve rispettarlo
    (righe non editabili, niente pulsanti aggiungi/cestino, calcolatrici in sola lettura o chiuse).
12. **Griglia inline: le due trappole già pagate** (`TravelStepTable.tsx:1-11`) — (a) guardia
    anti-refetch su `rowRef` mentre il fuoco è dentro la riga; (b) **un solo** `commitRow` all'uscita
    dalla riga con tutti i campi, mai un commit per campo. E **niente `flexRender`**: usare
    `renderColumnDef` (gotcha di progetto: celle rimontate ad ogni refetch = popover suicidi).
13. **Realtime obbligatorio.** Ogni scrittura → `NotifyBudgetChanged` (gruppi `project-{id}` **e**
    `projects`, `BudgetVsActualController.cs:31-40`); il Bilancio ascolta con `useBudgetHub`
    (`ProjectBudgetVsActual.tsx:52`). `staleTime: 0` globale, come da regola di progetto.
14. **`SyncLinkedRows` non tocca le righe scritte a mano** (`ProjectCalcSheets.cs:182-200`): una
    migrazione che generasse righe nel foglio `spese.actual` senza `linked_source` produrrebbe
    doppioni non più ripulibili. La v68 **non deve** scrivere in `spese.actual` (è il lato consuntivo).
15. **`allowance_days` è INT in MySQL ma `decimal` nei DTO** (`ProjectCosting_DTOs.cs:49, 76`;
    `BudgetVsActual_DTOs.cs:342` lo dichiara `int`) e il client non arrotonda
    (`preventivo-dialogs.tsx:161`): un «2,5» viene troncato in silenzio. La conversione della v68
    legge dal DB, quindi il problema non si propaga — ma non fidarsi del DTO.
16. **Il picker tariffe `DAILY_HOTEL` è una novità**: nella Trasferta la colonna «Prezzo» è un
    `MoneyInput` libero senza `unitCostOptions` (`TravelStepTable.tsx:562-580`). L'anagrafica esiste
    già (seed 80/100/120, `DbService.cs:1186-1196`), va solo agganciata.
17. **`visibilityStorageKey` versionata** se la nuova griglia usa `ColumnsMenu` /
    `usePersistedColumnVisibility`, o chi ha già usato la pagina non vede le colonne nuove.
18. **Non toccare `TravelPlanService.ActualTravelCostSql` (`:127-132`)**: è la stessa regola in SQL
    letta da `/bilancio` (`BilancioController.cs:83`) e dalla dashboard commessa
    (`ProjectsController.cs:1799-1806`). Riguarda il **consuntivo**, che resta senza K. Verificato:
    il lato preventivo **non** viaggia su quelle query, quindi il cambio di §3 è contenuto in
    `BudgetVsActualController`.
19. **Produzione.** Il server LAN (192.168.2.150, servizio `AtecPmServer`) applica le migrazioni al
    riavvio: backup completo prima del deploy, e verificare nel log la riga `[Migration v68]`.
