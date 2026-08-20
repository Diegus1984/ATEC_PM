# Piano di lavoro — segnalazioni #34–#41 (Bilancio, Trasferta, Timesheet, elenchi)

> **Scritto il 06/08/2026.** Fonte: le segnalazioni di Paolo Zanoni lette dal DB di produzione
> (`bug_reports` 34-41; la 33 è chiusa, in produzione con la v68). La 40 «SPIEGONE» è il quadro
> d'insieme, non una richiesta a sé.
>
> ## Stato al 06/08/2026 sera — leggere questo, poi §0 per le decisioni
>
> | # | stato |
> |---|---|
> | 33 · 36 · 41 | ✅ **in produzione** |
> | **34** | ✅ **in produzione** (v71, collaudata sulla commessa di Paolo — §8) |
> | **35** | ✅ fatta, §9 — Conto Economico in produzione, i riquadri in Dashboard nel giro dopo |
> | **38** | ✅ fatta, §9 |
> | **37** · **39** | ❌ **da fare** — le due grosse (18 e 11 mezze giornate) |
>
> Le **13 ambiguità sono tutte decise** (§0): non resta niente da chiedere a Paolo sul *cosa*.
> Quello che manca per far partire la #37 è il **rimappaggio delle 37 fasi** (§7), che è lavoro
> di anagrafica e che **Paolo non può fare da solo**: `nav.config_sezioni` è riservata agli ADMIN.
>
> Le schede citano `file:riga` verificati sul codice del 06/08/2026: dopo qualche settimana di
> lavoro vanno ricontrollati prima di fidarsene.

---

## 0. Decisioni prese (06/08/2026) — tutte da Paolo, per iscritto

**Prime tre risposte**

1. **#34 — il «Totale Costi di Vendita» calcolato SOSTITUISCE il valore digitato a mano.**
   `projects.sale_total` smette di comandare. Conseguenza accettata consapevolmente: **al deploy
   tutti i Delta Ordine già compilati cambiano valore**. La colonna non si cancella (storico).
2. **#37 — generano una riga di trasferta SOLO le ore imputate su una fase la cui sezione di
   costo ha il tag «DA CLIENTE»** (`cost_section_templates.section_type = 'DA_CLIENTE'`), non
   l'`entry_type='TRAVEL'`. ⚠️ **Vedi §7: il collegamento fase → sezione era rotto; bonificato
   con la v70, ma il rimappaggio delle 37 fasi è ancora da fare.**
3. **#37 — vitto e indennità si imputano A MANO** (campo importo libero). Niente regola di scelta
   dall'anagrafica tariffe per ora: la si aggiungerà quando l'anagrafica avrà date e persone.

**Le altre dieci risposte**

4. 🔻 **IL K DI RICARICO DELLE TRASFERTE SI ELIMINA.** Testuale: *«Semplifichiamo il lavoro
   ELIMINIAMO LA LOGICA DI RICARICO K per le trasferte. Restano solo importi di costo Netto senza
   ricarichi e questo rende il tutto coerente.»*
   **Ribalta la regola della #33, andata in produzione il 06/08 alle 14:30 (v68)** — era una
   richiesta esplicita di Paolo stesso. Verificato in produzione: **nessuna commessa e nessuna
   offerta ha mai usato un K diverso da 1,000** (4 commesse con `project_pricing`, tutte a 1.000;
   0 offerte), quindi **la rimozione non muove un solo importo**. I totali trasferta restano
   quelli di oggi (INTERNA 2.736 + 570 di indennità; C260805.500 3.270 + 1.040).
   Da fare: togliere il campo «K trasferta» dalla Scheda Prezzi e il moltiplicatore da
   `BudgetVsActualController.cs:365-368`; **tenere la colonna** `project_pricing.travel_markup`
   (e la gemella su `quote_pricing`) forzata a 1, così la scelta è reversibile e il Commerciale
   non si rompe. Spariscono anche i due hint sull'asimmetria: senza K non c'è più asimmetria.
5. **#34 — «Delta Ordine» si rinomina «MARGINE DI SICUREZZA».** La Contingency resta quella che è
   oggi, con la sua gestione attuale. I due concetti restano distinti, cambia solo il nome del
   primo — che è esattamente ciò che serviva per non confonderli.
6. **#35 — le due redditività:** domanda superata dalla #4. Senza K esiste **un solo** «Totale
   Costi», quindi l'ambiguità «su quale dei due si calcola» non si pone più. Paolo conferma che il
   comportamento attuale (redditività visibile sia nel Conto Economico sia nel Riepilogo Costi) va
   bene così.
7. **#35 — «Prezzo offerta finale» imputato a mano: SOLO VALORE DA MOSTRARE.** Non si ripercuote
   su Offerta e Contingency. Serve comunque la colonna nuova (`final_price_override`, v71).
8. **#35 — nella Dashboard commessa vanno TUTTE E SEI le finestre**, e Paolo conferma che quelle
   economiche restano visibili solo a PM e amministratori.
9. **#37/#38 — il costo del personale NON entra nel Bilancio dalla trasferta.** Testuale: *«Resta
   NO. Dalla trasferta facciamo entrare solo i costi di trasferta e indennità»* e *«NON INCLUDE IL
   PERSONALE»*. Conferma la scelta D6-C: `TravelPlanService.cs:99` resta su `TravelCost`.
10. **#37 — il costo orario è quello DI REPARTO** (`departments.hourly_cost`).
    ⚠️ Resta una sotto-decisione tecnica, non da Paolo: **quale** reparto quando la persona ne ha
    più di uno. Oggi la vista timesheet usa `MIN(department_id)` e la dashboard
    `is_primary → is_responsible → id`. In produzione 4 persone su 31 hanno due reparti e le due
    regole divergono su **una sola** (Gianpiero Vinardi: la vista lo mette in UTE, la dashboard in
    ACQ). **Oggi non cambia nessun numero — entrambi i reparti costano 45,00 €/h.** Quindi è il
    momento giusto per allineare la vista alla regola `is_primary` (che è quella con un senso:
    il reparto *principale*), gratis. Se si aspetta che le tariffe divergano, non lo sarà più.
11. **#37 — le trasferte inserite a mano non si toccano.** Testuale: *«NON ANDREMO A GESTIRE
    TRASFERTE INSERITE A MANO… si parte con la logica aggiornata per trasferte gestite da
    Timesheet»*. Niente esplosione in righe giornaliere, niente migrazione del pregresso: in
    produzione ci sono comunque solo **2 step e 2 righe**. Le righe manuali restano visibili e
    modificabili; le nuove nascono dal Timesheet.
12. **#39 — «solo il PM» = chiunque abbia il ruolo PM.** Niente PM-per-commessa: basta una feature
    a `min_level = 2`, e si risparmiano ~1,5 mezze giornate di infrastruttura che non esiste.
13. **#39 — spostare su «Extra Lavoro» ESCLUDE subito dal costo**, ma la pagina Extra Lavoro deve
    permettere di **rimettere dentro** una riga alla volta. Testuale: *«…dove posso con calma
    riflettere e decidere se alcune di queste tornano nel bilancio commessa e quindi in tutti i
    conteggi relativi al bilancio commessa e redditività»*. Quindi il rientro vale per **tutti** i
    lettori (Bilancio, `/bilancio`, dashboard commessa, redditività), non solo per la redditività:
    conferma che il filtro va messo nella costante SQL condivisa, non copiato in tre punti.

## 1. Quadro d'insieme

Paolo sta chiudendo il cerchio sul **conto economico di commessa**: vuole che ogni numero a video abbia (a) un nome che dice cos'è davvero, (b) una finestra che spiega da dove esce, (c) una **fonte automatica** invece che un campo digitato a mano. Il filo che lega le 7 richieste è la sostituzione dell'input manuale con la derivazione dai dati operativi:

- **#34 + #36** rinominano e completano i totali del preventivo lungo l'asse **Netti vs Vendita**, portandoli entrambi allo stesso livello (commessa intera) e includendo la trasferta di preventivo — oggi quell'aggregato esiste ma è nascosto dentro `BvaPricingDto.NetCost`.
- **#35** chiede tooltip esplicativi ovunque + 2 KPI nuovi di redditività, e sposta le 3 card non economiche (Avanzamento/Tecnici/Fasi) verso la Dashboard di commessa.
- **#37 + #38** sono la coppia pesante: la Trasferta smette di essere compilata a mano e viene **derivata dal Timesheet**; il consuntivo «Spese Trasferta» del Bilancio deve poi quadrare con quel modulo.
- **#39** dà al PM il controllo sull'imputazione oraria: una pagina riga-per-riga con la possibilità di spostare ore su una causale «Extra Lavoro» ed escluderle/reincluderle nel costo, per simulare l'impatto sulla redditività.
- **#41** è residuale (ordinamento elenchi) ed è già risolta dove Paolo l'ha vista.

---

## 2. Schede per segnalazione

---

### #34 — Ordine Commessa: TOTALE COSTI, TOTALE VENDITA calcolato, spiegone sul DELTA

**Cosa chiede.** Aggiungere «TOTALE COSTI» (somma colonne *Netti* di tutte le sezioni + trasferta preventivo); trasformare «TOTALE VENDITA — rif. CALCOLO G205» in **«Totale Costi di Vendita» calcolato** (somma colonne *Vendita* + trasferta preventivo); DELTA ORDINE = Ordine − Totale Costi di Vendita, con finestra esplicativa che lo definisce come *contingency effettiva da gestire*.

**Com'è oggi.**
- Footer a 3 righe in `atec-pm-web/src/features/commesse/bva-order.tsx:333-381`: `Totale Ordine` (somma client delle righe, `:293-295`), `Totale Vendita` che è un **`MoneyInput` digitato a mano** (`:353-360`) e `Delta Ordine` (`:377`).
- Il valore digitato è `projects.sale_total DECIMAL(14,2) NULL` (`ATEC.PM.Server/Services/DbService.cs:511`), scritto da `PATCH .../sale-total` (`ATEC.PM.Server/Controllers/BudgetVsActualController.cs:611-624`) e letto **solo** per il Delta (`:439`, `:457-459`, `:487`). Non entra in nessuna redditività.
- Il numero che Paolo chiede **esiste già calcolato**, ma con un altro nome e in un'altra sezione: `BudgetVsActualController.cs:383`
  `netCost = resourceSale + TotalMaterialSaleCost + budgetTravelTotal + budgetWorkshopSale` — cioè esattamente «somma delle colonne Vendita di tutte le sezioni + trasferta preventivo ricaricata». È quello che la Scheda Prezzi mostra già come **«Totale Costi di Vendita»** dopo la #36 (`atec-pm-web/src/features/commesse/bva-economics.tsx:289`), e il commento a `:285-288` dichiara già l'intenzione: *«È lo stesso importo della voce omonima dell'Ordine Commessa»*.
- «Totale Costi» esiste **solo** nel Riepilogo Costi (`bva-order.tsx:553`, somma client su `costLines`, `:500-505`) e include la trasferta **con il K** (voce `spese`, `BudgetVsActualController.cs:577`), quindi **non** è il «somma dei Netti» che Paolo chiede.

**Cosa va fatto.**

*Server (`ATEC.PM.Server`)*
- `Controllers/BudgetVsActualController.cs`: estrarre in variabili i due nuovi aggregati accanto a `netCost` (`:374-403`) ed esporli:
  - `TotalBudgetNetCost` = `result.TotalBudgetCost` (risorse a costo, `:285`) + `result.TotalMaterialNetCost` (`:340`) + `budgetWorkshopCost` (`:347-350`) + **trasferta a costo secco** = `budgetTravelMarkable + budgetTravelAllowance` (`:365-372`, senza il `× travelMarkup`);
  - `TotalBudgetSaleCost` = il già esistente `netCost` di `:383`.
- `ATEC.PM.Shared/DTOs/BudgetVsActual_DTOs.cs`: due campi nuovi su `BvaEconomicSummary` (`:477-561`) + i 4 addendi separati di ciascuno, per poter scrivere lo spiegone senza rifare il conto lato client (stesso pattern già usato per `BudgetTravelMarkableCost`/`BudgetTravelMarkup`/`BudgetAllowanceCost`, `:507-509`).
- **Decidere la sorte di `sale_total`** (vedi ambiguità): se diventa calcolato, `PATCH .../sale-total` (`:610-624`) va deprecato e `orderDelta` (`:457-459`) ricablato su `TotalBudgetSaleCost`. Consiglio: **non cancellare la colonna**, tenerla come override manuale opzionale (`sale_total IS NULL` → usa il calcolato), così i valori storici non spariscono.

*Client (`atec-pm-web`)*
- `src/features/commesse/bva-order.tsx:333-381`: footer da 3 a 4 righe → `TOTALE ORDINE`, `TOTALE COSTI`, `TOTALE COSTI DI VENDITA`, `DELTA ORDINE`. Sostituire il `MoneyInput` (`:353-360`) con testo `euro(...)` (o input abilitato solo in modalità override).
- Nuovo dialog `OrderDeltaExplainDialog` — riusare la struttura di `WorkshopCalcDialog.tsx:86-118`: mostra la scomposizione `Ordine − (risorse vendita + materiali vendita + trasferta ×K + officine vendita) = Delta` e la frase di definizione («importo effettivo di Contingency da gestire»). Aggancio: icona/chevron sulla cella Delta.
- `src/lib/api/types/costing.ts`: aggiungere i campi nuovi a `BvaEconomicSummary` (`:131-184`).
- Allineare l'hint del KPI «Totale Ordine» del Conto Economico (`bva-economics.tsx:136-141`), che oggi scrive `Vendita … · Delta …` dal vecchio `saleTotal`.

*Migrazione DB*: **nessuna**, salvo la scelta di rimuovere `sale_total` (sconsigliato).

**Ambiguità da chiarire con Paolo.**
1. **Il «Totale Costi di Vendita» calcolato sostituisce o affianca il campo digitato `sale_total`?** Sono numeri diversi su ogni commessa già compilata: se sostituisce, **tutti i Delta Ordine storici cambiano il giorno del deploy**. Serve un sì esplicito.
2. **Nel «TOTALE COSTI» (Netti) la trasferta entra a costo secco o già ricaricata?** Paolo dice «costi netti delle righe Trasferta preventivo» → costo secco. Ma allora `TOTALE COSTI` ≠ `Totale Costi` del Riepilogo Costi (che porta il K, `BudgetVsActualController.cs:577`) ≠ `economic.budgetCost` (idem, `:468-470`): a video si vedrebbero **due «Totale Costi» diversi in due sezioni della stessa pagina**. Va deciso quale delle due è la verità, o vanno rinominate.
3. **Il DELTA ORDINE diventa la Contingency?** Paolo lo dice; ma sulla Scheda Prezzi «Contingency» è già un'altra cosa (`netCost × contingency_pct`, `:386`). Confermare che restano due grandezze distinte e che il testo dello spiegone spiega la differenza, altrimenti si crea confusione permanente.

**Rischi.** `sale_total` cambia significato → Delta storici. `TOTALE COSTI` a costo secco è più basso di `economic.budgetCost`: chi legge le due sezioni insieme vede due totali di preventivo diversi (rischio segnalazione futura). Nessun altro modulo legge `sale_total` (verificato: sole occorrenze in `BudgetVsActualController.cs` e `DbService.cs`).

**Taglia: M — 5 mezze giornate** (2 server + DTO, 2 client + dialog, 1 verifica runtime).

---

### #35 — Conto Economico: tooltip ovunque, 2 KPI di redditività, spostamento di 3 card in Dashboard

**Cosa chiede.** Ogni importo spiegato da un pop-up al passaggio del mouse; «Prezzo offerta finale» imputabile a mano; rinominare «Budget Costi» → «Totale Costi»; 2 card nuove (Redditività Teorica / Effettiva); togliere Avanzamento, Tecnici attivi, Fasi completate e portarle nel tab Dettagli, dove vanno riportate anche le altre finestre.

**Com'è oggi.**
- 8 KPI in `atec-pm-web/src/features/commesse/bva-economics.tsx:125-194`. **Nessun tooltip, nessun `title=`, nessun HoverCard**: gli «hint» sono `CardDescription` statiche sotto il valore (`bva-shared.tsx:32-70`). Verificato: `atec-pm-web/src/components/ui/` ha `tooltip.tsx` ma **non** `hover-card.tsx`.
- «Prezzo offerta finale» è **interamente derivato**: `FinalOfferPrice = Pricing.FinalPrice` (`BudgetVsActualController.cs:485` → `:391`). `project_pricing` (`DbService.cs:945-953`) non ha nessuna colonna prezzo: **non c'è dove scriverlo**.
- Etichetta `Budget costi` a `bva-economics.tsx:144`.
- Le due redditività **esistono già**: `budgetProfitabilityPct` e `profitabilityPct` (`BudgetVsActualController.cs:520-523`), mostrate però compresse in una sola card (`bva-economics.tsx:172-187`) e ripetute nel footer del Riepilogo Costi (`bva-order.tsx:563-604`). ⚠️ La formula server usa **`budgetCost` con la trasferta ×K**, mentre Paolo scrive «Totale Costi preventivati».
- Le 3 card da spostare: `bva-economics.tsx:188`, `:189`, `:190-193`.
- Il tab Dettagli (`ProjectDetailsSection.tsx`) **ha già** Avanzamento (`:174`, con `x/y Fasi` nel sottotitolo) e Tecnici (`:176`) — in forma diversa e con un `Kpi` **locale** (`:68-95`, div con bordo colorato), non lo shadcn `Kpi` di `bva-shared.tsx:32-70`.
- Il payload di Dettagli (`ProjectDashboardData`, `ATEC.PM.Shared/DTOs/Dashboard_DTOs.cs:82-123`) **non contiene** orderPrice/budgetCost/actualTotalCost/redditività: `GET /api/projects/{id}/budget-vs-actual` è `[RequireFeature("data.budget")]` sull'intero controller (`BudgetVsActualController.cs:15`) → 403 sotto livello PM.

**Cosa va fatto.**

*Client*
- `bva-shared.tsx:32-70`: estendere `Kpi` con prop `explain?: React.ReactNode` che avvolge il valore in `<Tooltip>` (provider globale già montato in `App.tsx:32` con `delayDuration={0}`). **Non** installare HoverCard: Tooltip c'è già ed è lo standard della pagina (`bva-order.tsx:212-243`, `preventivo-travel-table.tsx:115-128`).
- `bva-economics.tsx:125-194`: scrivere i 6 testi esplicativi (offerta finale, Totale Ordine, Totale Costi, Consuntivo Costi, Redditività Teorica, Redditività Effettiva); rinominare `Budget costi` → `Totale Costi` (`:144`); sostituire la card `Redditività` con le due card nuove; rimuovere le 3 card (`:188-193`).
- `ProjectDetailsSection.tsx:172-203`: aggiungere le card economiche nella griglia KPI, dietro il già presente `isPmLevel()` (`:99`, `:195`). Scegliere **uno solo** dei due stili `Kpi` (raccomando di adottare quello shadcn e cestinare il locale, altrimenti la Dashboard avrà due estetiche di card).

*Server*
- `ATEC.PM.Shared/DTOs/Dashboard_DTOs.cs:82` + `ProjectsController.BuildProjectDashboard` (`:1737-2040`): estendere `ProjectDashboardData` con orderPrice, budgetCost, actualTotalCost, le 2 redditività + le scomposizioni per i tooltip. **Preferibile** alla seconda chiamata a `/budget-vs-actual` (che darebbe 403 ai non-PM e rifarebbe una query pesante). Riusare le formule di `BudgetVsActualController.cs:467-523` estraendole in un helper condiviso in `Services/ProjectEconomics.cs` — altrimenti nascono i "tre totali che divergono" già noti (`PIANO-LAVORO-COMMESSE-V32.md:382-388`).
- **«Prezzo offerta finale» imputabile a mano**: colonna nuova `project_pricing.final_price_override DECIMAL(14,2) NULL` (migrazione, vedi §4) + endpoint di scrittura; lettura `FinalOfferPrice = override ?? Pricing.FinalPrice` (`BudgetVsActualController.cs:485`).

*Migrazione DB*: **v69** (colonna `final_price_override`).

**Ambiguità da chiarire con Paolo.**
1. **Le due redditività si calcolano su quale «Totale Costi»?** Oggi `budgetCost` porta la trasferta ×K (`BudgetVsActualController.cs:468-470`). Se il nuovo «TOTALE COSTI» della #34 è a costo secco, le due card danno **percentuali diverse** a seconda di quale si usa. Da fissare insieme all'ambiguità #34.2.
2. **Il «Prezzo offerta finale» imputato a mano si ripercuote a valle** (Offerta/Contingency della Scheda Prezzi) o è solo un valore visualizzato? Cambia se serve un solo campo o un ricalcolo inverso.
3. **«riporta tutte le finestre che ti ho appena commentato» nella Dashboard**: tutte le 6 card economiche o solo le 3 spostate? Le economiche in Dashboard sono visibili solo `isPmLevel()`, quindi un TECH vedrebbe una griglia mezza vuota.

**Rischi.** Duplicare le formule di redditività in `ProjectsController` crea un quarto lettore che può divergere. Le card «Tecnici» differiscono già oggi per definizione (Conto Economico = `COUNT(DISTINCT phase_assignments)` a `BudgetVsActualController.cs:417-421`; Dettagli = `activeTechnicians.length` da un'altra query, `ProjectsController.cs:1968`): unificarle cambia un numero già visto dagli utenti.

**Taglia: M — 6 mezze giornate** (2 tooltip+card, 2 estensione dashboard server+client, 1 override prezzo + migrazione, 1 runtime).

---

### #36 — Scheda prezzi: «Costo netto» → «Totale Costi di Vendita»

**Cosa chiede.** Rinominare l'etichetta.

**Com'è oggi.** **GIÀ FATTO.** `atec-pm-web/src/features/commesse/bva-economics.tsx:289` recita `Totale Costi di Vendita {euro(pricing.netCost)}`, con commento di tracciamento a `:285-288` che cita esplicitamente la segnalazione #36. Il file è datato 06/08/2026 14:49.

**Cosa va fatto.**
- Verifica a runtime che il deploy in produzione contenga questa versione (il bundle in `deploy/out` potrebbe essere precedente).
- Residuo **facoltativo** di igiene: il nome server è ancora fuorviante — `BvaPricingDto.NetCost` (`ATEC.PM.Shared/DTOs/BudgetVsActual_DTOs.cs:121-129`) e il commento a `:461` («Risorse + materiali + trasferte») non riflettono la formula reale di `BudgetVsActualController.cs:383`. Rinominare il campo DTO tocca client + server e conviene farlo **dentro la #34**, dove quel valore diventa un totale di prima classe.

**Ambiguità.** Nessuna.

**Rischi.** Nessuno (solo etichetta).

**Taglia: XS — 0,5 mezze giornate** (sola verifica).

---

### #37 — Trasferta derivata dal Timesheet

**Cosa chiede.** La pagina `/trasferta` si compila da sola dalle imputazioni Timesheet: Fase attività (al posto degli step manuali), Nominativo, Data (al posto di Inizio/Fine), Giorni = 1 se c'è la data, Ore = totale ore del giorno, Costo personale dall'anagrafica costi orari. Alloggio/Vitto: Notti e Prezzo manuali, Costo automatico, Vitto e Indennità = solo importo dall'anagrafica indennità **per la singola data**. Auto e Treno/Aereo invariati. + N.B. sulle anagrafiche fasi.

**Com'è oggi.** È la richiesta che ribalta il modulo.
- `travel_step_rows` (`DbService.cs:600-623`) **non ha nessuna colonna fase** né `work_date`: ha `start_date`/`end_date` + `exclude_sat`/`exclude_sun`, e i Giorni sono **derivati** (`TravelMath.Days`, `ATEC.PM.Shared/DTOs/Travel_DTOs.cs:215-232`). L'unica cosa che somiglia a una fase è `travel_steps.description VARCHAR(300)` (`DbService.cs:589`), **testo libero senza FK**.
- **Nessun collegamento con il timesheet in nessuna direzione**: `timesheet_entries` (`DbService.cs:973-986`) non ha colonne verso `travel_*`; `travel_step_rows` non ha `project_phase_id` né `timesheet_entry_id`. Unico punto di contatto: `employee_id`.
- Le righe si creano **solo a mano**: step vuoto (`TravelController.cs:66-87`) + riga vuota (`:144-165`). Nessun import.
- `hours` è scritta **solo** dalla calcolatrice `ore` (`TravelController.cs:302-316`); `hourly_rate DECIMAL(10,3)` è una **quarta copia scrivibile a mano** del costo orario (D6-B).
- Il costo orario "d'anagrafica" **non esiste per persona**: sta su `departments.hourly_cost` (`DbService.cs:174-182`), risolto con `MIN(department_id)` nella vista timesheet (`DbService.cs:1262-1266`) e con `is_primary → is_responsible → id` nella dashboard (`ProjectsController.cs:1793-1800`) — **due regole diverse per lo stesso numero**.
- L'«anagrafica indennità» è `tariff_options` con `tariff_type='DAILY_ALLOWANCE'` (`DbService.cs:243-248`, seed `:1216-1221`): è una **lista piatta di valori** (`UNIQUE(tariff_type, value)`), **senza data, senza persona, senza default**. Oggi ci sono 3 valori (20/40/60) e Vitto ne ha altri 3.

**Cosa va fatto.**

*Migrazione DB (v70, vedi §4)*
- `travel_step_rows`: aggiungere `work_date DATE NULL`, `project_phase_id INT NULL` (FK `project_phases` ON DELETE SET NULL), `phase_name VARCHAR(200) NOT NULL DEFAULT ''` (snapshot, come `person_name`), `source VARCHAR(20) NOT NULL DEFAULT 'MANUAL'` (`MANUAL` | `TIMESHEET`), `timesheet_day_key VARCHAR(60) NULL` con `UNIQUE` (chiave logica `{employee_id}:{phase_id}:{work_date}`, per l'idempotenza della sincronizzazione).
- **Non cancellare** `start_date`/`end_date`/`exclude_sat`/`exclude_sun`: servono alle righe storiche e alle righe manuali. Convivenza, non sostituzione.

*Server*
- Nuovo `Services/TravelFromTimesheetService.cs`: legge `v_timesheet_with_section` filtrata per commessa e **`entry_type = 'TRAVEL'`** (l'unico marcatore esistente, `DbService.cs:979`), raggruppa per (dipendente, fase, giorno) e fa **upsert** delle righe `source='TIMESHEET'` — stesso pattern di `ProjectCalcSheets.SyncLinkedRows` (`ProjectCalcSheets.cs:182-200`), che non tocca le righe scritte a mano. Popola `hours` (somma del giorno), `hourly_rate` (dal reparto), `phase_name`, `work_date`.
- **Trigger della sincronizzazione**: `TimesheetController.Save` (`:199-241`) e `Delete` (`:243-252`) devono chiamare il nuovo servizio + `TravelPlanService.SyncToBudget`. Oggi il timesheet non emette **nessun evento SignalR** (`TimesheetController` non inietta `IHubContext`): va aggiunto, modello `NotifyBudgetChanged` in `BudgetVsActualController.cs:35-40`.
- `TravelController.cs`: bloccare l'edit dei campi derivati sulle righe `source='TIMESHEET'` (nominativo, data, ore, costo personale) e la calcolatrice `ore` (`:286-320`).
- **Vitto/Indennità «solo campo euro da anagrafica per la singola data»**: le due calcolatrici Giorni×Diaria (`TravelCalcDialog.tsx:39-80`) diventano un `Select` sui valori di `tariff_options` moltiplicati per 1 giorno. Serve però una regola di **quale** valore (vedi ambiguità).
- `Services/TravelPlanService.cs:127-132`: `ActualTravelCostSql` resta valido.
- ✅ **Prerequisito bloccante RIMOSSO il 06/08/2026 (migrazione v69)**: la vista `v_timesheet_with_section` usava `JOIN phase_templates` INNER e le **fasi locali sparivano** dalla derivazione. Ora la definizione è unica (`DbService.TimesheetSectionViewSql`) e applicata anche in produzione.

*Client*
- `TravelStepTable.tsx:71-106`: colonna «Fase» nuova, «Data» al posto di Inizio/Fine per le righe derivate, celle in sola lettura con badge di provenienza; i 2 toggle sab/dom (`:498-543`) restano solo sulle righe manuali.
- `TrasfertaPage.tsx`: pulsante «Aggiorna dal Timesheet» + spiegazione della convivenza righe derivate/manuali.

**Ambiguità da chiarire con Paolo.** (sono tutte reali e cambiano i numeri)
1. **Quali imputazioni generano una riga di trasferta?** Solo `entry_type='TRAVEL'`, oppure tutte le ore imputate a una fase «da cliente»? Sono perimetri completamente diversi e `entry_type` è una `VARCHAR(20)` libera senza vincolo (`DbService.cs:979`): oggi nessuno garantisce che sia compilata bene.
2. **«Costo del personale automatico da anagrafica costi orari»: quale anagrafica?** Non esiste un costo orario del dipendente. Le opzioni sono `departments.hourly_cost` (3 regole di risoluzione diverse in giro) o `tariff_options[HOURLY_RATE]`. E: **se il costo personale della trasferta diventa automatico, va anche al Bilancio?** Oggi **no** per scelta esplicita (D6-C, `TravelPlanService.cs:99-104`): se lo si porta, si **conta due volte** con le Risorse Atec del timesheet.
3. **«Vitto/Indennità da anagrafica per la singola data»**: `tariff_options` non ha né date né valore predefinito, ha 3 valori alternativi. Serve sapere **come si sceglie**: un default globale? per reparto? per persona? Senza risposta la derivazione non è implementabile.
4. **Che fine fanno le trasferte già inserite a mano** (start/end su più giorni)? Restano com'è o vanno esplose in righe giornaliere? Un'esplosione cambia il totale (i Giorni cambiano regola).
5. **Quale anagrafica di fase** (vedi §6): quella del timesheet (`project_phases`) è l'unica compatibile, ma è diversa da quelle del Gantt.

**Rischi.** Alto. Il totale trasferta consuntivo alimenta il Bilancio, `/bilancio` e la dashboard commessa (`BilancioController.cs:83`, `ProjectsController.cs:1829-1831`): ogni riga generata cambia numeri già visti. Trappola nota già presente: appena esiste **anche un solo step**, il foglio `spese.actual` vince su `projects.actual_travel_cost` anche se somma 0 (`BudgetVsActualController.cs:451-453`) — con la generazione automatica questo scatterà su decine di commesse. Inoltre `TravelController.cs:128-140` e `:245-261` (riordino) chiamano `NotifyChanged` ma **non** `AfterWrite`: già oggi le etichette nel foglio possono restare disallineate.

**Taglia: XL — 18 mezze giornate** (4 modello dati + vista, 6 servizio di derivazione + trigger, 5 client, 3 runtime e riconciliazione dei numeri storici).

---

### #38 — Riepilogo Costi: «Spese Trasferte / Indennità» consuntivate = totale del modulo Trasferta

**Cosa chiede.** Il consuntivo della voce deve riportare il totale imputato nella Trasferta della commessa.

**Com'è oggi.** **Già così, ma solo per metà.** `BudgetVsActualController.cs:585-587`: `Actual = travelSheetTotal ?? NullIfZero(actualTravelCost)`, dove `travelSheetTotal` è il foglio `spese.actual` alimentato da `TravelPlanService.SyncToBudget` (`TravelPlanService.cs:89-109`) con una riga per step. Hint a video già presente: «dalla Gestione Trasferta (costo, senza ricarico)» (`:589-592`).
**La metà mancante**: `UnitCost = step.Totals.TravelCost` (`TravelPlanService.cs:99`) e `TravelCost` **esclude il personale** per definizione (`ATEC.PM.Shared/DTOs/Travel_DTOs.cs:104-105`). Il totale che il PM legge a video sulla pagina Trasferta è invece `TotalCost = PersonnelCost + TravelCost` (`:108`). **Da qui la percezione che "non riporta il totale".**

**Cosa va fatto.**
- Decidere il perimetro (ambiguità sotto). Se resta escluso il personale: **è già fatto**, serve solo rendere esplicito a video su `/trasferta` quale dei due badge finisce nel Bilancio (`TrasfertaPage.tsx:694-698`) e ripetere l'hint.
- Fix reale indipendente: `TravelController.cs:128-140` e `:245-261` (riordino step/righe) devono chiamare `AfterWrite` (`:48-53`) e non solo `NotifyChanged`, altrimenti le righe generate restano disallineate.
- Se e solo se si decide di includere il personale: cambiare `TravelPlanService.cs:99` in `step.Totals.TotalCost` **e contemporaneamente** escludere quelle ore da «Risorse Atec» — cosa che oggi non esiste e che si aggancia direttamente alla #39.

**Ambiguità da chiarire con Paolo.** **Una, decisiva.** «il totale di quanto imputato» include il **costo del personale**? Se sì si crea un **doppio conteggio** con la voce «Risorse Atec» (che arriva dal timesheet, `BudgetVsActualController.cs:539-546`) e il Consuntivo Costi della commessa si gonfia. È una scelta dichiarata (D6-C, `TravelPlanService.cs:7-14`) che Paolo sta implicitamente mettendo in discussione.

**Rischi.** Se cambia il perimetro, cambiano `actualTotalCost` (`:473`), la redditività a consuntivo (`:481`), la pagina `/bilancio` e la dashboard commessa — su **tutte** le commesse con trasferta.

**Taglia: S — 2 mezze giornate** se il perimetro resta invariato; **M — 4** se il personale entra (serve la de-duplicazione con le Risorse Atec). **Va fatta dopo la #37**, perché la #37 cambia cosa c'è dentro quel totale.

---

### #39 — Pagina PM «Riepilogo ore Commessa» + causale «Extra Lavoro»

**Cosa chiede.** Pagina per il PM con ogni riga di imputazione della commessa; il PM sposta righe su una causale «Extra Lavoro» (pagina propria con totale ore e costo); ogni riga si può selezionare/deselezionare dalla contabilità della commessa per valutare l'impatto sulla redditività.

**Com'è oggi.** Non esiste **niente** di questo.
- Tutti e 9 gli endpoint di `TimesheetController.cs` sono **per dipendente** (`:60`, `:84`, `:108`, `:121`, `:160`, `:199`, `:243`, `:254`, `:285`); nessuno per commessa.
- `timesheet_entries` (`DbService.cs:973-986`): **nessuna colonna di stato, approvazione, esclusione, `updated_at` o `row_version`**. Ricerca su tutto il repo di `is_billable|excluded|approval_status`: zero occorrenze. La tabella non è mai stata toccata da nessuna delle 68 migrazioni.
- `entry_type` (REGULAR/OVERTIME/TRAVEL, `atec-pm-web/src/features/timesheet/entry-types.ts:11-27`) è **puramente descrittivo**: `BuildActualEmployees` fa `hours × hourly_cost` per qualunque tipo (`BudgetVsActualController.cs:807-836`).
- **Non esiste nessuna autorizzazione «sono il PM di questa commessa»**: `projects.pm_id` serve solo per il nome a video e per i destinatari delle notifiche (`NotificationService.cs:55-83`). Il modello di permessi è solo livello+feature (`FeatureAccessService.cs:129-144`, `RequireFeatureAttribute.cs:35-71`).

**Cosa va fatto.**

*Migrazione DB (v71, vedi §4)* — usare il pattern **tabella laterale** già collaudato con `ddp_row_off` (v67, `DbService.cs:3608-3636`): la presenza del record È lo stato, nessuna colonna nuova sulla tabella storica.
- `timesheet_extra_work (id, timesheet_entry_id INT NOT NULL UNIQUE FK ON DELETE CASCADE, excluded_from_cost TINYINT(1) NOT NULL DEFAULT 1, note VARCHAR(300), created_by INT NULL, created_at DATETIME)`.
  Due flag distinti in una tabella sola: la **presenza** = «spostata su Extra Lavoro»; `excluded_from_cost` = «conta o non conta nella redditività» (è la seconda leva che Paolo chiede esplicitamente).

*Server*
- Endpoint nuovi: `GET /api/projects/{id}/timesheet` (tutte le righe della commessa con persona, data, fase, causale, ore, €/h, costo, flag Extra), `POST/DELETE .../timesheet/{entryId}/extra-work`, `PATCH .../extra-work/{entryId}/included`.
- **Feature key nuova** in `auth_features` (`DbService.cs:1171-1209`) con `min_level = 2`: `nav.ore_commessa`. ⚠️ **Trappola nota**: feature non registrata = accesso **libero** (`FeatureAccessService.cs:141-143`). `nav.timesheet` è a `min_level 0` (`:1173`) e non è riusabile.
- **Filtro nei calcoli**: `BudgetVsActualController.cs:131-163` (le due query del consuntivo), `BilancioController.cs:60-69` e `ProjectsController.cs:1761-1774` (dashboard) devono tutti escludere le righe con `excluded_from_cost = 1`. **Tre lettori** → mettere la condizione in una costante SQL condivisa in `Services/ProjectEconomics.cs`, come già fatto per `OfficinaParentDedup` (`:24-25`) e `ActualTravelCostSql`.
- SignalR: nessun evento timesheet esiste; aggiungere `TimesheetChanged` + `BudgetChanged` sul gruppo `project-{id}`.

*Client*
- Nuova sezione di commessa in `commessa-sections.ts` (dopo `budget_vs_actual`, `:29-33`) + case in `CommessePage.tsx:433-449`. **File esemplare da copiare integralmente**: `ProjectWorkRequests.tsx` — ha già `ColumnsMenu` (`:6`, `:299`), `usePersistedColumnVisibility` con chiave versionata (`:209-212`), `GridScroller` (`:311-418`), realtime e `useConfirm` (`:163-170`). Regole obbligatorie: `euro()`/`fmtHours()`/`formatDateShort`, e `renderColumnDef` **al posto di `flexRender`** se si usa TanStack.
- Pagina/tab «Extra Lavoro»: stessa griglia filtrata + totali ore/costo + toggle di inclusione, e una riga di confronto «Redditività con / senza Extra Lavoro».
- `useConfirm` sullo spostamento (è un'azione che cambia la redditività).

**Ambiguità da chiarire con Paolo.**
1. **«SOLO il PM»**: il **ruolo di livello PM** (feature con `min_level 2`, zero codice nuovo) o **il PM assegnato a questa commessa** (`projects.pm_id` = utente)? La seconda richiede **infrastruttura di autorizzazione che non esiste** e va scritta da zero (nessun attributo, helper o precedente in tutto il server). Differenza di taglia: circa +3 mezze giornate.
2. **Spostare su «Extra Lavoro» esclude automaticamente dal costo**, o sono due gesti separati? Il testo suggerisce due leve («A sua volta ogni riga deve poter essere selezionata o deselezionata»): confermare.
3. **Le ore escluse spariscono anche da `/bilancio`, dalla dashboard commessa e dai KPI ore?** Se sì i tre lettori vanno allineati; se no, i numeri divergeranno fra le pagine (problema già presente e documentato, `PIANO-LAVORO-COMMESSE-V32.md:382-388`).

**Rischi.** È l'unica funzione che permette di **cambiare la redditività senza toccare un dato economico**: senza tracciamento (chi/quando) diventa incontestabile. `timesheet_entries` non ha `created_by` né storico (a differenza dei DDP, che hanno `ddp_item_events`): mettere `created_by` + `created_at` sulla tabella laterale è il minimo. Se il filtro viene applicato in un solo lettore su tre, le pagine smettono di quadrare.

**Taglia: L — 11 mezze giornate** (2 DB+DTO, 4 server incl. i 3 lettori allineati, 4 client due griglie, 1 runtime). +3 se serve il PM-per-commessa.

---

### #41 — Elenco Commesse ordinato crescente, escludendo «Altre attività»

**Cosa chiede.** La pagina Commesse ordinata crescente, con le commesse a codice libero fuori dall'elenco principale.

**Com'è oggi.** **Già fatto sulla pagina che Paolo ha fotografato.**
- Server: `ATEC.PM.Server/Controllers/ProjectsController.cs:35-48` — `CodeDateSql` (gemello SQL di `projectCodeDate` del client, normalizza `C{aammgg}` e `C{aaaammgg}` a 8 cifre) e `ProjectOrderBySql` = `chiuse in fondo, poi codici-senza-data in fondo, poi data ASC, poi code ASC`. Applicato a `:181` (elenco paginato) e `:214`. Il commento a `:30-38` cita esplicitamente la #41 e il caso `C241204_166`.
- Client: `atec-pm-web/src/features/commesse/CommessaTree.tsx:132-141` — le commesse a codice libero (SERVICE _ SANGRATO…) si raggruppano in fondo sotto «Altre commesse», con intestazioni di gruppo solo se esistono entrambi i gruppi. `INTERNA` era già esclusa (`:127-130`).
- ⚠️ **Nota terminologica**: «Altre attività» come commessa/cartella **non esiste** nel repo — l'unica occorrenza è l'anagrafica `res_other_activities` del planner Risorse (`ResourcesDbService.cs:35-40`). Paolo intende le commesse di servizio a codice libero, ed è ciò che è stato implementato.

**Residuo.** ✅ **CHIUSO il 06/08/2026.** La regola è stata estratta in `ATEC.PM.Server/Services/ProjectSorting.cs` (`CodeDate(alias)` + `OrderBy(alias, statusColumn?)`, con il perché scritto nel doc-comment) e applicata a **tutti** gli elenchi che ordinavano per codice alfabetico: `ProjectsController` (elenco paginato e `tree`), `BilancioController`, `DashboardController` (cartelle della home), `MilestonesController`, `CheckListController` (sidebar + lookup), `MoMController`, `ResourcesController`, `SalController` (×4), `TimesheetController` (×3), `PurchaseRfqController`, `TravelPlanService` (era `DESC`).

Verificato prima di chiudere:
- l'ordinamento nuovo provato **sui dati veri di produzione**: le 18 commesse escono in ordine cronologico, e un codice a 8 cifre (`C20260805.500`) si incolonna con quelli a 6;
- le due forme SQL rischiose — `SELECT DISTINCT … ORDER BY <espressione>` (Check list) e `GROUP BY … HAVING … ORDER BY <espressione>` (Milestones, SAL, Timesheet) — girano su **MySQL 8.4 di produzione con `ONLY_FULL_GROUP_BY`**: è la trappola che il 31/07 aveva prodotto due 500 visibili solo sul server.

`TravelPlanService` da `DESC` ad `ASC` **non cambia niente a video**: la pagina Trasferta riordina comunque con `buildPmProjectSections`. Cambiava solo l'incoerenza fra i due lati.

**Ambiguità.** Nessuna.

**Taglia: fatto.**

---

## 3. Dipendenze e ordine consigliato

```
#36 (verifica)  ─┐
#41 (residuo)   ─┼─► quick win, indipendenti, sbloccano fiducia
                 │
#34 ─────────────┴─► definisce «Totale Costi» e «Totale Costi di Vendita»
  │                  → la #35 ne dipende: le due redditività si calcolano su quei totali
  ▼
#35 ─────────────► tocca ANCHE la Dashboard (ProjectDashboardData + ProjectDetailsSection)
                   → estrarre PRIMA le formule in ProjectEconomics, o nasce un 4° lettore

#37 ─────────────► ribalta il modulo Trasferta; PREREQUISITO: fix di v_timesheet_with_section
  │
  ▼
#38 ─────────────► dipende dalla #37: il "totale del modulo Trasferta" cambia contenuto
                   con la derivazione. Farla prima significa rifarla.

#39 ─────────────► indipendente dalle altre, ma tocca gli STESSI 3 lettori del costo ore
                   → conviene DOPO la #35 (che li ha già unificati) e in parallelo alla #37
```

**Ordine consigliato:** `#36 → #41 → #34 → #35 → #37 → #38 → #39`.

Motivazioni non ovvie:
- **#34 prima di #35** perché la #35 chiede due redditività che si calcolano su «Totale Costi preventivati/consuntivati»: se la #34 ridefinisce quel totale, le card della #35 vanno rifatte.
- **#35 tocca la Dashboard**, non solo il Bilancio: `ProjectDashboardData` (`Dashboard_DTOs.cs:82-123`) va esteso e `BuildProjectDashboard` (`ProjectsController.cs:1737-2040`) diventa un terzo calcolatore di redditività. **Estrarre le formule in un helper condiviso è parte della #35, non un refactoring facoltativo.**
- **#38 dipende dalla #37** in modo stretto: oggi la voce già legge il modulo Trasferta; ciò che cambierà è *cosa c'è dentro* quel modulo.
- **#39 in coda** perché è la sola che può cambiare la redditività retroattivamente: va introdotta quando i totali sono già stabili e unificati, altrimenti diventa impossibile capire se un numero è cambiato per l'esclusione o per un'altra modifica.

---

## 4. Decisioni di modello dati

⚠️ **Trappola nota, da non sbagliare.** `private const int LatestSchemaVersion = 68;` a **`ATEC.PM.Server/Services/DbService.cs:1355`** è il cancello d'ingresso di `ApplyVersionedMigrations` (`:1425`: `if (currentVersion >= LatestSchemaVersion) return;`). **Dimenticare di alzarla non produce nessun errore**: la migrazione semplicemente non gira mai in produzione. È già successo con la v66 (04/08/2026, applicata in dev a v64 e saltata in produzione a v65). Il commento a `:1347-1354` lo documenta. **Ogni migrazione qui sotto richiede di alzare quella costante al numero più alto aggiunto.**

Pattern da rispettare per ogni blocco (modello: v68 a `DbService.cs:3645-3681`): `if (currentVersion < N) { try { …DDL idempotente… ; c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (N, '…')"); _logger.LogInformation(...); } catch (Exception ex) { _logger.LogWarning("[Migration vN] Errore (non bloccante): {Message}", ex.Message); } }`. Il DDL va replicato anche nel ramo `InitDatabase` (schema completo) per i database nuovi.

| Ver. | Segn. | Oggetto | Dettaglio |
|---|---|---|---|
| ~~**v69**~~ | #37 | ✅ **FATTA il 06/08/2026 — fix `v_timesheet_with_section`** | Definizione unica in `DbService.TimesheetSectionViewSql`, letta sia da `InitDatabase` sia dalla migrazione v69: `LEFT JOIN phase_templates` + `COALESCE(pp.custom_name, pp.name, pt.name)` + `COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)`. Prima la versione corretta esisteva solo in `migrate_view_timesheet.py`, mai lanciato in produzione, dove la vista **INNER JOIN scartava le ore delle fasi locali**. `LatestSchemaVersion` alzata a 69. **Prerequisito bloccante della #37: tolto di mezzo.** |
| **v70** | #35 | `project_pricing.final_price_override DECIMAL(14,2) NULL` | Imputazione manuale del «Prezzo offerta finale», oggi solo derivato (`BudgetVsActualController.cs:485`). Lettura: `override ?? Pricing.FinalPrice`. `AddColumnIfMissing`, stesso helper usato a `DbService.cs:3374`. |
| **v70b** | #37 | `travel_step_rows`: `work_date DATE NULL`, `project_phase_id INT NULL` (FK `project_phases` ON DELETE SET NULL), `phase_name VARCHAR(200) DEFAULT ''` (snapshot), `source VARCHAR(20) DEFAULT 'MANUAL'`, `timesheet_day_key VARCHAR(60) NULL UNIQUE` | Colonne additive, righe storiche invariate (`source='MANUAL'`). `start_date`/`end_date`/`exclude_*` **restano**: le righe manuali continuano a funzionare. `timesheet_day_key` = idempotenza dell'upsert. |
| **v71** | #39 | `timesheet_extra_work (id, timesheet_entry_id INT NOT NULL, excluded_from_cost TINYINT(1) NOT NULL DEFAULT 1, note VARCHAR(300) NULL, created_by INT NULL, created_at DATETIME DEFAULT CURRENT_TIMESTAMP, UNIQUE KEY uq_tew_entry (timesheet_entry_id), FK → timesheet_entries ON DELETE CASCADE)` | **Tabella laterale**, pattern `ddp_row_off` (v67, `DbService.cs:3608-3636`): `timesheet_entries` non si tocca (non l'ha mai toccata nessuna delle 68 migrazioni). Presenza = «Extra Lavoro»; `excluded_from_cost` = seconda leva. `created_by` è l'unica traccia di chi ha spostato: `timesheet_entries` non ha né `created_by` né storico. |
| **v71b** | #39 | Seed `auth_features`: `('nav.ore_commessa', 2, …)` | ⚠️ Feature **non registrata = accesso libero** (`FeatureAccessService.cs:141-143`). `nav.timesheet` è a `min_level 0` (`DbService.cs:1173`) e non è riusabile. |

**Non servono migrazioni** per #34, #36, #38, #41. Per la #34, `projects.sale_total` (`DbService.cs:511`) va **conservata** anche se il valore diventa calcolato: cancellarla brucia i dati storici del Delta Ordine di ogni commessa.

---

## 5. Già fatto vs. contraddizioni

### Cose che il codice già fa (da non rifare)

| Richiesta | Stato reale |
|---|---|
| #36 rinomina «Costo netto» | ✅ **Fatto.** `bva-economics.tsx:285-289`, con commento che cita la #36. Resta solo la verifica di deploy. |
| #41 ordinamento + esclusione codici liberi su `/commesse` | ✅ **Fatto.** `ProjectsController.cs:35-48` (`CodeDateSql`, `ProjectOrderBySql`) + `CommessaTree.tsx:132-141`. Residuo: le **altre** liste (§ scheda #41). |
| #34 «Totale Costi di Vendita» calcolato | ✅ **Il numero esiste già**: `netCost` a `BudgetVsActualController.cs:383` è esattamente la somma delle colonne Vendita + trasferta preventivo. Va **esposto** e collegato al Delta, non calcolato da zero. |
| #35 le due redditività | ✅ **Già calcolate** server-side: `budgetProfitabilityPct` / `profitabilityPct` (`:520-523`) e già a video nel Riepilogo Costi (`bva-order.tsx:586-601`). Vanno solo promosse a card con tooltip. |
| #35 Avanzamento / Tecnici in Dashboard | ✅ **Già presenti** nel tab Dettagli in altra forma: `ProjectDetailsSection.tsx:174` (Avanzamento + `x/y Fasi`) e `:176` (Tecnici). Da armonizzare, non da creare. |
| #38 consuntivo trasferta dal modulo Trasferta | ✅ **Già collegato**: `BudgetVsActualController.cs:585-587` legge il foglio `spese.actual` alimentato da `TravelPlanService.SyncToBudget` (`:89-109`). Manca solo la questione del personale. |
| #35 componente per i tooltip | ✅ `tooltip.tsx` esiste e il provider è globale (`App.tsx:32`, `delayDuration={0}`). **Non serve installare HoverCard** (che infatti non esiste nel progetto). |

### Contraddizioni con scelte già prese e dichiarate a video

1. **#38 vs. D6-C** — il personale della trasferta è escluso dal Bilancio **per scelta esplicita** (`TravelPlanService.cs:7-14` e `:99-104`: le Risorse Atec a consuntivo vengono dal timesheet, che è più affidabile dei nominativi digitati). Se il «totale» della #38 lo include, si crea un doppio conteggio. **Non implementare senza risposta di Paolo.**
2. **#37 vs. D6-C** — derivare la Trasferta dal Timesheet è coerente con quella scelta *finché* il costo personale generato **non** entra nel Bilancio. Se ci entra, la scelta va formalmente ribaltata (e i numeri storici cambiano).
3. **#34 vs. D5 (Delta ≠ Contingency)** — `ATEC.PM.Shared/DTOs/BudgetVsActual_DTOs.cs:488-493` documenta che il nome «Delta Ordine» è stato scelto **apposta** per distinguerlo da `ContingencyAmount`. Paolo ora dice che il Delta *è* la contingency effettiva. Le due grandezze restano matematicamente diverse (`orderPrice − saleTotal` vs `netCost × contingency_pct`): lo spiegone deve dirlo, non equipararle, altrimenti si perde la distinzione appena costruita.
4. **#34 vs. l'asimmetria D33-A** — se «TOTALE COSTI» somma i **netti** (trasferta a costo secco) mentre «Totale Costi» del Riepilogo porta il K (`BudgetVsActualController.cs:577`, hint a video: «spese ×K, indennità senza ricarico»), la stessa pagina mostrerà **due totali di preventivo diversi**. È una regressione di leggibilità, non un bug: va risolta con i nomi o con una decisione di Paolo.
5. **#35 «Prezzo offerta finale» imputabile** — contraddice l'architettura dichiarata: *«In ATEC PM il FinalPrice è derivato, non inseribile»* (`ANALISI-GAP-COMMESSE-V32.md:66`), e `project_pricing` non ha nessuna colonna prezzo. Richiede colonna nuova + decisione se l'override si propaga a valle.

---

## 6. Anagrafiche delle fasi — risposta al N.B. della #37

**Il sospetto di Paolo è fondato. Sono 4 famiglie indipendenti, senza nessuna chiave in comune, più 2 campi di testo libero.**

| # | Famiglia | Tabelle | Chi la usa |
|---|---|---|---|
| 1 | **Fasi di lavorazione** | `phase_templates` (`DbService.cs:482-492`) → `project_phases` (`:628-651`) → `phase_assignments` | **L'unica che il Timesheet conosce**: `timesheet_entries.project_phase_id` (`:983`). È l'unica da cui può derivare la #37. |
| 2 | **Sezioni di costo** | `cost_section_groups` (`:355-363`) → `cost_section_templates` (`:403-415`) → `project_cost_sections` (`:834-852`) + gemelle `quote_cost_sections` | Preventivo e raggruppamento del Bilancio. A voce vengono chiamate «fasi» anche queste. **Unico ponte esistente**: `phase_templates.cost_section_template_id`. |
| 3 | **Attività / milestone** | `activity_catalog` (`MilestonesDbService.cs:40-46`) → `project_milestones` (`:50-68`) | Gantt e pianificazione di commessa. **Copia per valore**: `source_catalog_id` è **senza FK** (`MilestonesController.cs:260-264`). Nessuna imputazione ore, nessuna chiave verso `project_phases`. |
| 4 | **Testo libero** | `travel_steps.description VARCHAR(300)` (`DbService.cs:589`), `sal_rows.step VARCHAR(1000)` (`SalDbService.cs:56`) | La «attività della trasferta» e lo «step» del SAL. Nessuna anagrafica dietro. |

In più, **dentro `project_phases` convivono già tre varianti**: da template, **locale** (`phase_template_id NULL`, `PhasesController.cs:116-131`) e **degradata** (template cancellato, `:545-548`); il nome si legge con `COALESCE(custom_name, pp.name, pt.name)` — tre colonne per lo stesso dato.

**Risposta operativa:**
- **Per la #37 non serve unificare.** L'unica anagrafica compatibile è la **famiglia 1** (`project_phases`), perché è l'unica a cui punta un'imputazione oraria. La colonna `travel_step_rows.project_phase_id` va lì, con snapshot `phase_name` accanto (stesso pattern di `person_name`, `DbService.cs:603-604`).
- **Unificare le 4 famiglie è un lavoro a sé**, e grosso: significherebbe dare una chiave comune a Gantt, Timesheet e Sezioni di costo, riscrivere `v_timesheet_with_section` e migrare i dati storici. **Stima grezza: L/XL, 12–20 mezze giardine, con rischio alto** su numeri già in produzione. Da proporre come blocco separato, non da infilare nella #37.
- **Fix minimo e obbligatorio da fare comunque dentro la #37** (§4, v70a): allineare `v_timesheet_with_section` alla versione snapshot-aware, altrimenti le **fasi locali** restano fuori sia dalla derivazione della Trasferta sia dal Bilancio. È il difetto più concreto emerso in tutta questa analisi ed è indipendente dalla scelta di unificare o no.

---

## 7. VERIFICA DELLE ANAGRAFICHE FASI (06/08/2026) — il collegamento fase → sezione è rotto

Richiesta da Paolo insieme alla decisione «solo le fasi con tag DA CLIENTE». **Fatta sui dati veri
di produzione.** Esito: **la regola è giusta, i dati non la reggono.**

### Numeri

| | |
|---|---|
| Sezioni di costo in anagrafica | **22**, di cui **10 con tag DA CLIENTE** |
| Fasi in anagrafica (`phase_templates`) | **54** |
| …collegate a una sezione **che esiste** | **17** (16 IN_SEDE + **1 sola DA_CLIENTE**) |
| …**senza** collegamento (`NULL`) | **16** |
| …che puntano a una sezione **INESISTENTE** | **21** |
| Fasi dentro le commesse (`project_phases`) | **494**: 51 IN_SEDE, **17 DA_CLIENTE**, **426 non classificabili** |

### Che cos'è successo

`phase_templates.cost_section_template_id` punta agli id **1, 3, 6, 7, 9, 11, 12**. Le sezioni di
costo oggi esistenti hanno id **da 41 in su**: le sezioni sono state ricreate da zero e le fasi
sono rimaste appese ai vecchi id.

La FK `phase_templates_ibfk_2` esiste ed è `ON DELETE SET NULL` — quindi una cancellazione normale
avrebbe azzerato i riferimenti invece di lasciarli orfani. Gli orfani possono essere entrati solo
con `FOREIGN_KEY_CHECKS=0`, cioè **da un ripristino di dump**. Da lì in poi nessuno se n'è accorto,
perché il codice usa sempre `LEFT JOIN`: un riferimento rotto si comporta come «nessuna sezione»,
in silenzio.

### Perché blocca la #37

Le fasi che *dovrebbero* generare trasferta sono proprio quelle rotte o scollegate:
`Installazione elettrica in CANTIERE` (→ 11, non esiste), `Commissioning PLC in CANTIERE` (→ 12),
`Commissioning Robot in CANTIERE` (→ 12), `Collaudo finale in CANTIERE` (nessun collegamento).
L'unica fase che oggi risulta DA CLIENTE è `Installazione meccanica in CANTIERE`.

Applicando la regola così com'è, la derivazione scatterebbe su **17 fasi su 494** — e le altre
resterebbero fuori non perché non sono da cliente, ma perché il collegamento manca.

### Ricaduta già in produzione, indipendente dalla #37

Le sezioni di costo servono anche a distribuire il costo delle ore nel Bilancio. Con 426 fasi su
494 non classificabili, quel raggruppamento oggi è in gran parte cieco. Non è un numero sbagliato
(il totale ore torna), è un'attribuzione che non si può fare.

### Cosa fare, in ordine

1. **Rimappare le 54 fasi sulle 22 sezioni.** È un lavoro di anagrafica, lo fa Paolo dalla pagina
   **Configurazione Sezioni** (`/config-sezioni`), non è codice.
2. ⚠️ **Prima serve una correzione all'interfaccia**: `CostSectionsTreePanel.tsx:231` conta come
   «fasi collegate» tutte quelle con `costSectionTemplateId != null`, quindi **include le 21
   rotte** — a video si legge «38 fasi collegate» quando quelle vere sono 17. E le 21 rotte non
   compaiono sotto nessuna sezione, quindi **da lì non si possono nemmeno sistemare**. Vanno
   mostrate in un gruppo «Fasi senza sezione» insieme alle 16 scollegate.
3. **Migrazione di bonifica**: azzerare i 21 riferimenti rotti (`SET NULL`), così i dati dicono la
   verità e l'interfaccia le mostra fra le scollegate. Senza questo, il punto 2 deve inventarsi
   una terza categoria «rotte».
4. Solo dopo ha senso costruire la derivazione della #37.

**Taglia della bonifica (punti 2 e 3): S — 2 mezze giornate.** Il rimappaggio è tempo di Paolo.

### Stato dei punti 1-2 (06/08/2026): FATTI

**Punto 1 — pagina Configurazione sezioni** (`CostSectionsTreePanel.tsx`):
- contatore corretto: da «N fasi collegate» (che contava anche quelle rotte) a **«17 di 54 fasi
  collegate»**, in ambra quando ce ne sono di scollegate;
- nuovo pannello **«Fasi senza sezione»** in cima all'albero, con le fasi senza collegamento *e*
  quelle col collegamento rotto, **trascinabili su una sezione** per riassegnarle. Il drop su
  `SectionNode` esisteva già dal principio ma **non aveva nessuna sorgente**: fino a ieri
  «Scollega dalla sezione» faceva sparire una fase dalla pagina senza modo di recuperarla.

**Punto 2 — migrazione v70**: azzera i riferimenti rotti in `phase_templates` (+ le altre 3
tabelle esposte allo stesso incidente, oggi a 0). `LatestSchemaVersion` alzata a 70.
**Non cambia nessun numero a video**: il codice legge già in `LEFT JOIN`, quindi un riferimento
rotto si comportava esattamente come `NULL`. Cambia solo che ora il dato lo dice, e l'interfaccia
può mostrarlo.

Provata sul DB di sviluppo: **21 righe bonificate** — cioè il difetto c'è **anche in sviluppo**,
non è un incidente della sola produzione. Dopo: 0 orfani, 37 fasi senza sezione, 16 IN SEDE,
1 DA CLIENTE.

**Adesso tocca a Paolo:** rimappare le 37 fasi senza sezione dal pannello nuovo.

---

## 8. STATO DELL'IMPLEMENTAZIONE (06/08/2026 sera)

### ✅ #34 — COMPLETA (server + client), non deployata

- **K trasferta rimosso** ovunque: `BudgetVsActualController` (il totale è ora somma secca),
  campo «K trasferta» tolto dalla Scheda Prezzi, prop `travelMarkup` eliminata da
  `bva-sections.tsx` / `preventivo-travel-table.tsx` / `ProjectBudgetVsActual.tsx`, riga
  «spese × K» sostituita da «spese · indennità = totale». `project_pricing.travel_markup`
  resta a 1,000 come colonna dormiente (reversibile, e il Commerciale la legge ancora).
- **Footer Ordine Commessa a 4 righe**: Totale Ordine · **Totale Costi** · **Totale Costi di
  Vendita** · **Margine di Sicurezza**. Il `MoneyInput` è sparito: il valore è calcolato.
- **`SafetyMarginDialog`**: scompone Ordine − Vendita = Margine, definisce il numero come
  «importo di contingency effettivamente disponibile» e tiene esplicitamente distinta la
  Contingency della Scheda Prezzi (percentuale) dal Margine (importo).
- **`PATCH .../sale-total` dismessa**: risponde con un errore parlante invece di sparire, così
  un client vecchio in cache non scrive in silenzio un campo che nessuno legge più.
  `updateSaleTotal` rimossa dal client.
- DTO: `TotalBudgetNetCost` e `TotalBudgetSaleCost` nuovi, `BudgetTravelMarkup` eliminato.

### 🟡 #35 — Conto Economico FATTO, Dashboard NO

Fatto:
- `Kpi` accetta `explain`: **tooltip su ogni riquadro** (shadcn `Tooltip`, provider già globale).
- I 6 riquadri vivono in **`economicKpis()`**, funzione esportata: nasce già riusabile dalla
  Dashboard, che è il punto 3 rimasto.
- «Budget costi» → **«Totale Costi»**; «Consuntivo costi» → «Consuntivo Costi».
- Card «Redditività» unica sostituita da **«Redditività Teorica Commessa»** e **«Redditività
  Effettiva Commessa»**, ognuna col calcolo scritto per esteso nel tooltip con i numeri veri.
- **Avanzamento / Tecnici attivi / Fasi completate rimosse** dal Conto Economico. Non si è perso
  niente: `ProjectDetailsSection.tsx:174-176` le ha già (AVANZAMENTO con «x/y Fasi», TECNICI).
- **«Prezzo offerta finale» imputabile a mano**: migrazione **v71**
  (`project_pricing.final_price_override`), `PATCH .../final-price-override`, campo svuotabile
  per tornare al calcolato. È **solo il valore da mostrare** e il riquadro lo dichiara.

**Non fatto — punto 3 della #35:** portare i 6 riquadri economici anche nella Dashboard
commessa. Richiede di estendere `ProjectDashboardData` + `BuildProjectDashboard`
(`ProjectsController.cs:1737+`) con orderPrice / budgetCost / actualTotalCost / le due
redditività, e di chiamare `economicKpis()` in `ProjectDetailsSection` dietro `isPmLevel()`.
⚠️ Attenzione al debito già noto: sarebbe il **terzo** calcolatore di redditività. Le formule
vanno estratte in un helper condiviso, non ricopiate.

### ❌ #37, #38, #39 — non iniziate

Le specifiche sono complete (§0 + schede) e la #37 non è più bloccata dalla vista timesheet
(v69). Resta bloccata dal **rimappaggio delle 37 fasi**, che è lavoro di anagrafica.

### Verifiche fatte

`dotnet build` 0 errori · `npx tsc -b` pulito · `eslint` 0 errori (14 warning preesistenti) ·
`npm run build` ok. A runtime su dev: **migrazione v71 applicata**, `budget-vs-actual` 200,
scrittura e svuotamento dell'override del prezzo verificati, vecchia rotta `sale-total`
correttamente rifiutata, `pricing.netCost == totalBudgetSaleCost`.

⚠️ **I numeri NON sono stati verificati su dati veri**: il DB di sviluppo ha 2 commesse quasi
vuote. Il controllo che conta — che «Totale Costi», «Totale Costi di Vendita» e «Margine di
Sicurezza» diano gli importi giusti — va fatto sulla commessa di prova di Paolo dopo il deploy.
Vale la lezione del blocco 4: build verdi e review non hanno visto difetti che la prova a video
ha trovato in dieci minuti.

### Collaudo in produzione — 06/08/2026 ore 22:00 (schema v71)

Provato sulla **commessa di prova di Paolo, C260805.500 (id 45)**, con i suoi numeri veri.

**L'unica cosa che si è mossa, ed è quella prevista:**

| | prima | dopo |
|---|---|---|
| Totale Costi di Vendita | 101.238,00 € *(digitato)* | **103.680,00 €** *(calcolato)* |
| Margine di Sicurezza | 48.762,00 € | **46.320,00 €** |

Tutto il resto **invariato al centesimo**: Totale Ordine 150.000, Totale Costi 74.910, Consuntivo
720, Trasferta 4.310, Prezzo offerta 124.416, redditività 50,06 % e 99,52 %. La trasferta non si
è mossa perché il K era già 1,000 ovunque: rimuoverlo non ha cambiato un importo, come previsto.

**9 controlli su 9 passati**, ricalcolando le relazioni fuori dall'API:
Margine = Ordine − Vendita · Vendita = costo netto Scheda Prezzi · TOTALE COSTI del footer =
Totale Costi del Conto Economico (era il difetto trovato *prima* del deploy) · trasferta = spese
+ indennità senza ricarico (3.270 + 1.040, confrontati con la somma delle righe letta dal DB) ·
K sparito dal DTO · le 4 voci del Riepilogo sommano al Totale Costi · le due redditività tornano
con le formule dichiarate da Paolo.

**Prezzo offerta finale imputabile a mano:** scritto 130.000 → letto 130.000 con flag «manuale»;
Offerta e Contingency **non si sono mosse** (124.416 e 20.736), come richiesto («solo valore da
mostrare»); svuotato → tornato al calcolato 124.416. Dato di produzione ripristinato a NULL.
La vecchia rotta `sale-total` rifiuta con il messaggio parlante.

**Non provato a video.** Questo è un collaudo delle API con dati veri, non della GUI: il footer a
4 righe, la finestra del Margine e i tooltip vanno guardati da qualcuno sullo schermo.

---

## 9. Secondo giro (06/08/2026, dopo il deploy delle 22:00) — #35 chiusa, #38 chiusa

### ✅ #35 — ora COMPLETA

Mancava la replica dei sei riquadri nella Dashboard commessa. **Fatta senza estendere il payload
della Dashboard**: `ProjectDetailsSection` chiama lo **stesso endpoint del Bilancio** e passa il
risultato alla **stessa funzione** `economicKpis()`.

Perché così e non estendendo `ProjectDashboardData` come diceva la §4: perché la Dashboard
calcola già il consuntivo per conto suo (`CostWorked + MaterialCost + TravelCost`, con una regola
diversa da quella del Bilancio per il costo orario). Aggiungerci anche le redditività avrebbe
creato il **terzo** calcolatore, cioè esattamente il difetto che il piano voleva evitare. Leggere
la stessa fonte costa una chiamata in più e rende impossibile che i due numeri divergano.
Il permesso combacia: `data.budget` è `min_level 2` e i riquadri si vedono solo con `isPmLevel()`.
La query key è la stessa del tab Bilancio, quindi passando da una scheda all'altra non ricarica.

### ✅ #38 — chiusa

Il calcolo era **già giusto**: nel Bilancio va solo la metà «trasferta», il personale no.
Paolo l'ha confermato («NON INCLUDE IL PERSONALE»). Quello che mancava era dirlo e un difetto:

- I tre badge in fondo allo step ora dicono **dove va a finire ogni numero**: «dal Timesheet — non
  entra qui nel Bilancio» sul personale, «→ voce Spese Trasferta / indennità del Bilancio» sulla
  trasferta. Era l'origine dell'equivoco: leggendo «Totale costi step» ci si aspettava di
  ritrovarlo nel Bilancio.
- **Difetto corretto**: `ReorderSteps` e `ReorderRows` chiamavano `NotifyChanged` ma non
  `AfterWrite`, quindi dopo un riordino il dettaglio dietro la voce del Bilancio restava
  nell'ordine vecchio. Il totale era giusto (è una somma), l'elenco no.

### ✅ Decisione #10 — costo orario, regola unica (migrazione v72)

`v_timesheet_with_section` risolveva il reparto con `MIN(department_id)`, la dashboard commessa
con `is_primary → is_responsible → id`. Due regole per lo stesso costo orario. Allineate alla
seconda (il reparto **principale**), che è l'unica con un significato.

Misurato sui dati di produzione **prima** di scriverlo: diverge **1 persona su 31** (Vinardi, UTE
vs ACQ) e **0 persone cambiano costo orario** (entrambi i reparti 45,00 €/h). La sotto-query nuova
sceglie esattamente 1 reparto per persona (31 su 31). **Si fa adesso proprio perché è gratis**:
appena le tariffe dei due reparti si differenziano, allinearle sposterà numeri già visti.

### ❌ Restano #37 e #39

Non iniziate. Sono le due grosse (18 e 11 mezze giornate) e vanno affrontate con una sessione
davanti, non in coda a un'altra. Le specifiche sono complete: §0 per le decisioni, le schede per
il dettaglio, §7 per il prerequisito di anagrafica della #37.

### Verifiche di questo giro

`dotnet build` 0 errori · `npx tsc -b` pulito · `eslint` 0 errori · `npm run build` ok ·
regola del reparto provata sui dati di produzione (0 costi orari cambiati).
**Non ancora deployato** e **non provato a video.**

### Collaudo in produzione del secondo giro — 06/08/2026 ore 22:13 (schema v72)

**Nessun numero si è mosso, ed era il risultato atteso**: questo giro è interfaccia più una
migrazione misurata a impatto zero.

- Bilancio commessa 45: tutti e nove i valori **invariati** (ordine 150.000, vendita 103.680,
  margine 46.320, costi 74.910, consuntivo 720, trasferta 4.310, offerta 124.416, redditività
  50,06 % e 99,52 %) e **9 invarianti su 9** di nuovo verdi.
- Dashboard commessa 45: **0 differenze** su costo ore, ore, materiali, trasferta, totale,
  ricavo e budget. La v72 ha cambiato la regola del reparto senza spostare un euro, come misurato.

**Permessi verificati con token di ruoli diversi** — è il rischio della scelta di far leggere alla
Dashboard l'endpoint del Bilancio:

| ruolo | `/budget-vs-actual` |
|---|---|
| ADMIN | 200, conto economico letto |
| **PM** | **200, conto economico letto** |
| RESP_REPARTO | 403 |
| TECH | 403 |

Il PM ci arriva, ed è quello che serve: i riquadri economici della Dashboard sono dietro
`isPmLevel()`, quindi chi li vede può anche caricarli. Un TECH apre la Dashboard (200) ma il
client non monta nemmeno il componente, quindi non genera il 403.

**Non provato a video**, di nuovo: i sei riquadri nella Dashboard, i tooltip e le note nuove sui
badge della Trasferta vanno guardati da qualcuno sullo schermo.

---

## 10. #42 «08_BILANCIO COMMESSA» — allegati letti, la richiesta è molto più piccola di come sembra

Arrivata il 06/08 alle 18:04. Paolo formalizza il problema delle anagrafiche fasi del §7 e chiede
«una sola anagrafica» per le fasi + un'anagrafica delle sezioni di costo con un elenco preciso.

### Cosa mostrano i tre allegati

1. **Tendina fasi del Timesheet** — 29 voci, elenco piatto, nessuna sezione di costo a fianco.
2. **Bilancio → «Importa fasi da template»** — un elenco che *sembra* un'altra anagrafica. Le voci
   con una sezione sono **in arancione con la sezione scritta in grigio a destra**; quelle senza
   sono in nero e nude.
3. **«Nuova fase locale»** — c'è già una combo con ricerca per associare la fase a una sezione di
   costo. È il meccanismo che Paolo dice funzionare, e che vorrebbe ovunque.

### Le due liste NON sono due anagrafiche

Verificato sul codice: sono **due viste complementari della stessa tabella `phase_templates`**.
- Il Timesheet legge `project_phases` — le fasi **già dentro** quella commessa
  (`TimesheetController.GetPhasesForEmployee`).
- Il dialogo del Bilancio legge i **template ancora da aggiungere**.
Sembrano elenchi diversi perché sono insiemi complementari, non perché ci siano due anagrafiche.

**Quello che le fa sembrare incoerenti è un'altra cosa: 37 fasi su 54 non hanno una sezione di
costo**, quindi nel dialogo appaiono nude e nel Timesheet non portano nessuna informazione.
Non è un problema di modello dati: è un problema di **dati mancanti**.

### Le sezioni di costo che Paolo elenca ESISTONO GIÀ, identiche

Confrontato riga per riga l'elenco della #42 con la produzione: **coincidono al 100 %** — stessi
5 gruppi (GESTIONE · SITO PILOTA · INSTALLAZIONE CLIENTE · POST COLLAUDO CLIENTE · OPZIONI),
stesse 22 sezioni, stessi nomi, stesso ordine, stessa marcatura SEDE/CLIENTE.
**Non c'è niente da creare.** L'unica differenza chiesta è eliminare **«Ore Viaggio»**.

⚠️ «Ore Viaggio» (sezione 69) ha **0 fasi collegate e 0 offerte**, ma **4 sezioni di costo dentro
commesse reali**. La FK è `ON DELETE SET NULL`: cancellando il template quelle 4 restano nelle
loro commesse ma **scollegate dall'anagrafica**. Va deciso cosa farne prima di cancellare —
è lo stesso identico incidente che ha prodotto le 21 fasi orfane bonificate con la v70.

### Quindi la #42 si riduce a tre cose

1. **Assegnare le 37 fasi a una sezione** — lavoro di anagrafica, lo può fare solo chi conosce il
   mestiere. **25 delle 37 sono `is_default`**, cioè nascono su OGNI commessa nuova (oggi 17
   commesse ciascuna): sono quelle che pesano davvero. Le altre 12 sono quasi tutte inutilizzate.
2. **Eliminare «Ore Viaggio»**, dopo aver deciso la sorte delle 4 sezioni di commessa che la usano.
3. **(Da valutare)** la «logica a matrice» che Paolo ipotizza. Prima di costruirla vale la pena
   chiedersi se serve: con le 37 fasi assegnate, la combo che già esiste per le fasi locali
   (allegato 3) fa lo stesso lavoro.

**Taglia: S per il codice** (eliminazione sezione + eventuali ritocchi al dialogo). Il grosso è
tempo di Paolo sull'anagrafica. **Non è il lavoro architetturale che il titolo lascia temere.**

### Proposta di assegnazione delle 25 fasi → `ATEC_PM/PROPOSTA-FASI-SEZIONI.sql`

**Non sono accoppiamenti inventati: 20 su 25 sono stati RECUPERATI.** Le fasi rotte puntavano
alle sezioni di costo di prima (id 1-12); quelle sezioni esistono ancora nel backup
`atec_pm_manual_20260324_095548.sql` del 24/03/2026, con i loro nomi. Da lì si ricostruisce
l'intenzione originale e la si traduce nelle 22 sezioni di oggi.

Corrispondenza fra vecchia e nuova struttura, ricavata dai nomi:

| sezione di allora | → sezione di oggi |
|---|---|
| PROGRAM MANAGER | Program Manager (73) |
| ROBOT STUDIO - CELLA SIMULAZIONI | Robot Studio - Cella Simulazioni (42) |
| PROGETTAZIONE UT MECCANICO | Progettazione Ufficio Tecnico Meccanico (44) |
| PROGETTAZIONE UT ELETTRICO | Progettazione Ufficio Tecnico Elettrico (45) |
| ATEC INSTALLATORI · IN_SEDE (gruppo PRESCHIERAMENTO) | Allestimento Meccanico / Elettrico (55) — gruppo SITO PILOTA |
| ATEC INSTALLATORI · DA_CLIENTE (gruppo INSTALLAZIONE) | Installazione Meccanica / Elettrica (60) |
| ATEC COMMISSIONING · DA_CLIENTE | Commissioning PLC/HMI (61) o Commissioning Robot (62), secondo la fase |

Il file assegna **20 fasi** e ne lascia fuori 5, con la ragione scritta accanto a ognuna.
Verificato sui dati veri prima di consegnarlo: le 11 sezioni citate esistono, le 20 fasi esistono,
sono tutte `is_default` e tutte senza sezione. Dopo l'esecuzione restano **17 fasi scollegate,
di cui 5 `is_default`**.

**Le 5 che restano, e perché**

- **33 Richiesta offerte fornitori · 34 Emissione ordini · 35 Solleciti e tracking consegne** —
  sono lavoro dell'**Ufficio Acquisti**, e fra le 22 sezioni **non ne esiste una per gli
  acquisti**: sono tutte tecniche o di cantiere. Il reparto ACQ esiste in anagrafica reparti, la
  sezione di costo no. **È una casella che manca nella struttura**, non una dimenticanza:
  o si crea (es. «Ufficio Acquisti», SEDE, gruppo GESTIONE) o quelle ore restano fuori dalla
  ripartizione per sempre. Da chiedere a Paolo insieme alla #42.
- **39 Collaudo finale in CANTIERE** — il gemello «IN ATEC» va su Allestimento, ma lato cliente
  non esiste nessuna sezione «collaudo»: le quattro di INSTALLAZIONE CLIENTE sono Coordinamento,
  Installazione, Commissioning PLC/HMI, Commissioning Robot. La candidata è la 59 (Coordinamento
  Attività / Capo Cantiere), ma è una forzatura.
- **22 Programmazione Robot** — l'originale la metteva su ATEC COMMISSIONING **lato cliente**, il
  che è strano per un lavoro d'ufficio: la gemella «Simulazione RobotStudio» stava in sede su
  Robot Studio. **Sospetto un errore già nel dato vecchio**, quindi l'ho lasciata commentata
  invece di propagarlo. Da scegliere fra 42 (logica) e 62 (fedele all'originale).

---

## 11. Collaudo a video di #37 · #39 · #40 · #52 (09/08/2026)

Fatto sulla commessa di prova di sviluppo **C260505_205 (id 24)** con ore imputate davvero dal
Timesheet — la prima dalla finestra «Registra ore» come tecnico, le altre dalla stessa API che
quella finestra chiama. Persone, fasi e importi veri: 3 tecnici, 5 giornate, **65 h · 2.590,00 €**
di ore e **270,00 €** di spese trasferta.

### Cosa ha retto (nessuna modifica necessaria)

- **La derivazione della trasferta parte dal TAG della sezione, non dalla causale.** Le ore sono
  state scaricate come «Ordinarie», non «Trasferta», e la riga di cantiere è nata lo stesso: è
  esattamente la regola della #52. Le 8 h su una fase «in sede» non hanno generato niente.
- **Uno step per fase, una riga per persona-giorno**, con il titolo preso dalla fase e il badge
  `FASE`; nominativo, data e giorni in sola lettura, `TS` sulle righe derivate.
- **Due fasi di cantiere nello stesso giorno = UN giorno di trasferta** (1 sulla prima riga,
  0 sull'altra): verificato su Giorgio Maracich il 06/08.
- **Cancellare le ore non cancella la riga**: viene segnalata col triangolo ambra, i giorni vanno
  a 0 e le spese scritte a mano restano (60 € di treno sopravvissuti al giro completo
  cancella → reinserisci, con la segnalazione che si spegne da sola).
- **Extra Lavoro** sposta, rimette, e l'interruttore «conta nella commessa» muove il costo
  avanti e indietro: 2.590 → 1.900 → 2.590 €, con tracciato «Spostata da».
- **Le ore su Extra Lavoro NON tolgono la riga di trasferta**: la persona in cantiere c'è stata.
  Verificato anche dopo una rigenerazione forzata.
- **Permessi**: `Ore Commessa` e la trasferta sono ADMIN/PM (200); RESP_REPARTO e TECH prendono
  403 sulle API e «Accesso negato» sulla pagina, e la voce non compare nemmeno nel menu.

### Difetti trovati a video e corretti

| # | Difetto | Dove |
|---|---|---|
| 1 | **La Dashboard commessa non applicava il filtro Extra Lavoro.** Sulla STESSA schermata si leggeva «Consuntivo Costi 2.114,20 €» (48 h) accanto a «Ore totali 56,0», e il grafico per reparto, i tecnici e le settimane contavano le ore escluse. | `ProjectsController.BuildProjectDashboard` (5 query) + costante condivisa `ProjectEconomics.ExtraWorkJoin/ExtraWorkCounts` |
| 2 | **La Trasferta non si aggiornava in tempo reale.** Il Timesheet emetteva solo `BudgetChanged`, la pagina ascolta `TravelChanged`: il PM restava davanti a una tabella ferma mentre il tecnico scaricava le ore — cioè la pagina «che si compila da sola» sembrava non compilarsi. | `TimesheetController.NotifyTravelChanged` |
| 3 | **Rinominare una fase non aggiornava il titolo dello step**, che invece a video promette di seguirlo: restava il nome vecchio finché qualcuno non ritoccava un'imputazione. | `PhasesController.RiallineaTrasferta` (su `Update` e sul campo `custom_name`) |
| 4 | **Nessuna via d'uscita per uno step nato per sbaglio**: «Elimina step» era disabilitato su TUTTI gli step derivati, e la rigenerazione non cancella mai niente. | `TrasfertaPage`: si cancella quando non ha più ore vere dietro |
| 5 | **Mancava «Aggiorna dal Timesheet»** (era nel piano). Serve per il caso che nessuna scrittura di ore tocca: il tag di una sezione spostato da SEDE a CLIENTE in Configurazione sezioni. Provato: flip → il pulsante fa nascere lo step; flip indietro → la riga viene segnalata. | `POST .../travel/rebuild-from-timesheet` + pulsante |
| 6 | Ore scritte all'inglese (`8.0`) nella pagina Ore Commessa. | `fmtHours` |
| 7 | **`0` accanto a ogni commessa** nella barra laterale di Ore Commessa (era cablato), su commesse con decine di ore. | `count` reso opzionale su `PmSidebar` |
| 8 | Nessuna conferma sullo spostamento su Extra Lavoro, che cambia la redditività. | `useConfirm` con ore ed euro scritti nel testo |

Alla fine **tutte e quattro le letture del costo ore coincidono**: pagina Ore Commessa, Bilancio
commessa, Dashboard commessa e `/bilancio` danno 65 h · 2.590,00 € e totale 2.916,20 €, e si
muovono insieme quando una riga va su Extra Lavoro.

### Difetto delle ore orfane — CORRETTO il 09/08/2026 sera (era «serve una decisione»)

**Le ore su una fase la cui sezione di costo non esiste (o è disabilitata) nella commessa
spariscono dal consuntivo, in silenzio.** Il gruppo «NON ASSEGNATO / Fasi senza sezione costo»
raccoglie solo le fasi con `cost_section_template_id IS NULL`: se la sezione c'è ma la commessa
non ce l'ha fra le sue, quelle ore non finiscono da nessuna parte.

Riprodotto: la commessa di prova, prima di inizializzare il preventivo, mostrava **0 € di Risorse
Atec con 56 ore imputate**. Non è una divergenza fra pagine — Bilancio commessa e `/bilancio` si
comportano allo stesso modo, ed è scritto nel codice come scelta
(`BilancioController.cs:51-53`) — ma a video non lo dice nessuno.

Quando può capitare in produzione: una commessa a cui non è mai stato inizializzato il preventivo,
oppure una fase importata su una sezione nata dopo la commessa (`costing/init` copia solo le
sezioni `is_default_project`). **Non l'ho toccato perché allargare il gruppo orfano cambierebbe il
Consuntivo di commesse già viste.** Da decidere con Paolo: farle rientrare fra le orfane, o
lasciarle fuori scrivendolo a video.

### Nota minore

«Ultime Attività» della Dashboard elenca anche le righe spostate su Extra Lavoro: è un diario di
cosa ha scritto la gente, non un conteggio (non ha totali), quindi è rimasto com'è.

### Verifiche

`dotnet build` 0 errori · `npx tsc -b` pulito · `eslint` 0 errori sui file toccati ·
tutto provato a video sull'app vera, server spenti a fine prova.

### In produzione — 09/08/2026 ore 20:21

Build `20260809-2021`, bundle `index-DaNYu2Zu.js`, delta **5,4 MB su 160,5** (6 file, 1 rimosso),
con stop del servizio perché 2 file stanno fuori da `wwwroot`. **Nessuna migrazione: schema
fermo a 80.** Verificato dopo: health 200, servizio Running, `version.json` e `<meta app-build>`
allineati, bundle servito identico a quello locale, 8 rotte GET a 200 comprese quelle toccate
(`/dashboard`, `/hours`, `/travel`, `/budget-vs-actual`, `/bilancio/summary`).

**Nessun numero si è mosso, ed era l'esito atteso:** in produzione ci sono **0 imputazioni ore**
e 0 righe su Extra Lavoro, quindi la correzione della Dashboard è preventiva — comincerà a
contare il giorno in cui i tecnici imputeranno davvero. I 2 step di trasferta storici e le loro
2 righe sono intatti.

---

## 12. Ore orfane di sezione — corrette (09/08/2026 sera)

Deciso con Diego: **un'ora lavorata è un costo anche se la sua sezione non è fra quelle della
commessa.** Il filtro dei costi resta UNO solo, l'Extra Lavoro della #39.

**Prima**: il gruppo «NON ASSEGNATO / Fasi senza sezione costo» raccoglieva solo le ore con
`cost_section_template_id NULL`; quelle la cui sezione esiste in anagrafica ma non nella
commessa (preventivo mai inizializzato, sezione nata dopo, sezione disabilitata) sparivano dal
consuntivo in silenzio — e la Dashboard le contava, quindi le pagine divergevano.

**Adesso**: la query delle orfane è il **complemento esatto** di quella delle sezioni (LEFT JOIN
su `project_cost_sections` con `pcs.id IS NULL`): ogni ora imputata finisce O in una sezione O
fra le orfane, mai nel nulla. Nel gruppo «NON ASSEGNATO» nasce **una riga per ogni sezione
mancante, chiamata col nome della sezione** («Installazione Meccanica / Elettrica — sezione non
presente in questa commessa»), così si vede subito cosa aggiungere. `/bilancio` somma tutte le
ore senza più il filtro, quindi i due lettori non possono divergere.

**Misurato prima di scrivere** (produzione): 0 ore imputate → la correzione non muove un
centesimo oggi, ma 16 commesse su 18 hanno fasi su sezioni non presenti e 14 non hanno nessuna
sezione: la trappola era armata su quasi tutto il parco. Stessa logica della v72: si fa quando
è gratis.

**Provato a runtime sui due casi** (commessa 24, 65 h): sezione disabilitata → le sue 49 h
ricompaiono sotto «NON ASSEGNATO» col nome della sezione e il totale resta 65 h su entrambi i
lettori; fase senza sezione → 3 h sotto «Fasi senza sezione costo», totale 68 h ovunque.
Verificato anche a video sulla pagina Preventivo vs Consuntivo. Dati di prova ripristinati.

File: `BudgetVsActualController.cs` (query orfane + gruppo per sezione),
`BilancioController.cs` (via il filtro). Nessuna migrazione, nessun cambio client.

---

## 13. #55 — le fasi MUTE nella tendina delle ore (10/08/2026)

**Segnalazione** (Diego, 09/08 ore 22:54, area Time Sheet): *«Inserimento ore commessa — non
aggiorna tutte le causali della commessa»*, con lo screenshot della tendina «Fase dettaglio
commessa».

**Cos'era**: in fondo alla tendina, sotto «Senza sezione di costo», c'erano **5 voci vuote e
selezionabili**. Righe vere: **80 fasi senza nome in 16 commesse su 18** (5 per commessa, tutte
create il 31/07 alle 10:36). Nello screenshot si contano 1 riga vuota sopra «Cablaggio quadro
elettrico» e 4 sotto — esattamente le righe 622, 608, 619, 627, 628 della commessa 26.

**Causa**: il 07/08, rifacendo l'anagrafica fasi, cancellare una fase staccava le righe di
commessa dal template **senza congelarne prima il nome** → `name` NULL e `phase_template_id`
NULL insieme, cioè righe senza più identità. Il codice è già corretto
(`PhasesController.DeleteTemplate`, il nome si congela prima di staccare) ed è in produzione dal
deploy del 09/08 22:47: **previene, non ripara**. Le 80 righe erano il residuo.

**I nomi erano recuperabili**, al contrario di quanto dice il commento nel codice: nel backup
`atec_pm_auto_20260801_020000.sql` quelle righe hanno ancora il `phase_template_id`, e da lì si
risale al nome. La chiave reparto+ordine è risultata univoca su tutte e 80 (13 commesse
verificate riga per riga dal backup, le altre 3 — nate dopo il 01/08 — con lo stesso identico
schema di 5 righe).

| Reparto/ordine | Nome ripristinato | Sezione assegnata |
|---|---|---|
| UTM · 4 | Progettazione 3D | 44 · Progettazione Uff. Tecnico Meccanico |
| UTE · 15 | Collaudo Hardware | 55 · Allestimento Meccanico / Elettrico |
| ROB · 41 | Simulazione RobotStudio | 42 · Robot Studio - Cella Simulazioni |
| ACQ · 61 | Emissione ordini | 74 · Ufficio Acquisti |
| ACQ · 62 | Solleciti e tracking consegne | 74 · Ufficio Acquisti |

**Secondo difetto nello stesso punto**: «Cablaggio quadro elettrico» e «Project Management»
(16 + 16 righe) erano senza sezione di costo pur avendola in anagrafica — le loro ore sarebbero
finite sotto «NON ASSEGNATO». Assegnate rispettivamente a 55 e 73 (Program Manager).

**Riparazione fatta in produzione il 10/08/2026**, decisa con Diego (ripristina i nomi, assegna
tutte e 7 le sezioni). Backup mirato prima di toccare:
`C:\ATEC_Backups\fasi_prima_riparazione_bug55_20260810.sql`. Sulle 80 righe c'erano **0 ore e 0
assegnazioni**, e in produzione le ore imputate sono ancora 0 ovunque: **non si è mosso un
numero**, come per la v72 e le ore orfane — si fa finché è gratis. Dopo: `0` fasi mute e `0` fasi
senza sezione in tutto il database (erano 80 e 112). Nessuna migrazione, nessun cambio di codice.

**#55 CHIUSA il 10/08/2026 alle 15:00**, dall'API `PUT /api/bug-reports/55/status` (quindi con
broadcast SignalR) e con la nota di risposta scritta in pagina. 🪤 **Trappola**: la prima nota è
finita nel database doppio-codificata (`Â«` invece di `«`) perché Windows PowerShell 5.1 legge
gli `.ps1` senza BOM come ANSI. Rifatta leggendo il testo con
`[IO.File]::ReadAllText(path, UTF8)` e verificata sui byte (`HEX(...)` = `C2AB`). Se una nota va
scritta da script, il testo si tiene in un file a parte e si legge in UTF-8 esplicito.

**Deploy 10/08/2026 ore 15:01** — build `20260810-1501`, delta di 5 file (3 MB su 160,5), con
stop del servizio perché 2 file stanno fuori da `wwwroot`. **Nessuna migrazione: schema fermo a
80**, bundle SPA identico (`index-DaNYu2Zu.js`): l'unica modifica di codice è il commento
corretto in `DeleteTemplate`. Serviva soprattutto a far comparire l'avviso «nuova versione» a chi
aveva l'app aperta, visto che la riparazione dei dati era andata sul database in SQL diretto e
non passava da SignalR. Dopo: health 200 Production, servizio Running, `version.json` e
`<meta app-build>` allineati, 0 fasi mute, 0 fasi senza sezione, 0 segnalazioni aperte.
