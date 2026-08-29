# HANDOFF — Client Web ATEC PM (`atec-pm-web`)

> **Leggi questo file per primo** se riprendi il lavoro sul client web in una nuova
> chat. È l'indice di stato + le regole + i prossimi passi. Aggiornato al **06/07/2026** (Fase A — Commessa usabile + Fase C#8 — Prev vs Consuntivo + Fase D — Commerciale + **dettaglio commessa completo a 8 sezioni** + **Modulo Milestone** — anagrafica attività, tab Milestone + Gantt, pagina globale `/milestones`: completate) + **Sidebar PM condivisa** (06/07/2026, FATTA) + **Modulo SAL / Fatturazione** (07/07/2026: tab commessa, anagrafica condizioni, pagina PM globale `/sal` con Prospetto SAL, warning in campanella — verificato build/tsc/eslint).

## Cos'è

Client **React 19 + Vite 7 + TypeScript + shadcn (preset radix-vega)** che consuma
l'API ASP.NET Core esistente (`ATEC.PM.Server`). **È il client ufficiale** dal
**20/07/2026**: il WPF (`ATEC.PM.Client`) è stato ritirato e archiviato in
`backups/ATEC.PM.Client_retired_20260720/`.

- **Percorso:** `C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\atec-pm-web`
- **Solution:** `ATEC.PM.sln` = Server + Shared + stub Web (niente progetto Client WPF).

## Documenti di riferimento (fonti di verità)

| File | A cosa serve |
|------|--------------|
| **HANDOFF.md** (questo) | Punto d'ingresso: stato, regole, prossimi passi |
| [WEB-MIGRATION.md](archivio/WEB-MIGRATION.md) | **Stato modulo per modulo** (web vs WPF) + roadmap |
| [BLOCKS-RULES.md](regole/BLOCKS-RULES.md) | **Regole layout pagine**: fedeltà ai blocchi shadcn, recipe copia-incolla |
| [DESIGN-RULES.md](regole/DESIGN-RULES.md) | Tema/preset/token (preset `bIkeymG`, radix-vega, neutral, Inter, radius 0.625rem) |
| [README.md](../atec-pm-web/README.md) | Avvio rapido e struttura cartelle |

## Come avviare

```powershell
# API (terminale 1) — dalla root ATEC_PM
dotnet run --project ATEC.PM.Server          # http://localhost:5150

# Client web (terminale 2)
cd atec-pm-web
npm run dev                                   # http://localhost:5173 (proxy /api,/hubs,/uploads → 5150)
```

In **Release** il `.csproj` del server fa `npm run build` e serve la SPA da `wwwroot`
con `MapFallbackToFile` → tutto su `http://localhost:5150`.

> ⚠️ In questa shell **Node non è nel PATH**. Per i comandi npm/tsc/eslint usa:
> `$env:Path = "C:\Program Files\nodejs;" + $env:Path` e poi `.\node_modules\.bin\<tool>.cmd`.

## Stato attuale (sintesi — dettaglio in WEB-MIGRATION.md)

✅ **Parità col WPF (fatto):**
- App shell (sidebar inset + header, pattern `dashboard-01`), Dashboard (KPI+grafico+tabella), Login (`login-03`)
- **Gestione** (Clienti, Fornitori, Catalogo articoli, **Codex articoli** = lista paginata + ricerca + «Colonne» + sync + genera codice + modifica/elimina)
- **Gestione avanzata** (Config Sezioni, Conf. DDP, Aggregazioni DDP, Backup, Template Commesse)
- **Amministrazione** (Utenti CRUD + dialog 4 sezioni + reset/cessa/riattiva; Permessi = matrice funzioni×livelli)
  - **Ruoli di reparto** (`03/08/2026`, migrazione **v59**): oltre alla gerarchia TECH/RESP/PM/ADMIN esistono ruoli **fuori scala** (`auth_levels.access_mode = 'GRANTS'`) che **non ereditano niente dal livello** e vedono SOLO le funzioni elencate in `auth_role_features` (`FULL` = piene, `READ` = sola lettura, con le scritture respinte da `[RequireFeature]`). Primo ruolo: **AMM — Amministrazione** (Segnalazioni e SAL/Fatturazione piene, Clienti in sola lettura). Nella pagina «Permessi» ogni ruolo di reparto ha una colonna propria: clic sulla cella per girare tra · / ✓ / 👁. I dati economici del SAL non sono più «livello PM» cablato ma la funzione **`sal.economics`** (registrata a livello 2 = nessun cambiamento per PM/ADMIN). Chi non vede la Dashboard atterra sulla **prima voce di menu visibile** (per l'AMM: SAL / Fatturazione). **Segnalazioni** è uscita da «Principale»: ora è il gruppo **Supporto**, ancorato in fondo alla sidebar (`pinBottom` → `mt-auto`) ed escluso dalle candidate per la pagina iniziale.
- **Timesheet** (calendario mese/settimana fedele al WPF: chip ore per tipo, badge giornaliero, selezione+doppio-click, menu destro modifica/elimina, selettore dipendente PM/RESP, date future bloccate, dialog con date picker + validazione 24h live)
- **PM → Verbali (MoM)** — **gestione v9** (02/07/2026, prototipo `Gestione_MoM_v9.html`, verificata a runtime): pagina **Note MoM** (`/mom/note`, acquisizione rapida per utente → «Assegna» porta il testo nel campo Azione del verbale), lista con colonna Rev. e **doppia visualizzazione Tabella|Card come il Gestore DDP** (card raggruppate per commessa + Riunioni, scelta persistita per utente), **dettaglio a foglio editabile** (celle inline autosave, autocomplete definizioni, Invio su Azione=nuova riga, righe colorate+gruppi, riordino drag&drop su `sort_order`, giorno settimana/festivi sulle date), **revisioni** (cambio data riunione confermato ⇒ Rev+1, storico `mom_revisions`, regola server-side), export Stampa/Word/Excel/CSV, **realtime SignalR** (`MoMChanged`) + concorrenza `row_version`
- **PM → Gestore DDP** (gruppi espandibili per commessa + card Commerciale/Officina con realtime SignalR; sintesi completa: 7 KPI, Stati Avanzamento, accordion ripartizione/consegne/top10/destinazioni/mancanti/distinta/feedback, stampa per sezione+Excel)
  - **Report di Controllo cross-commessa + Analisi Consegne** (10/07/2026, dal prototipo `Gestione_DDP_New_V30.html`): l'intera sezione è un pannello con **PmSidebar condivisa** (stesso formato del Pannello Lavorazioni): `/gestore-ddp` = pannello con vista «Gestore DDP» (voce in testa alle viste rapide, contenuto = GestoreDdpPage), poi «Analisi Consegne» e i 6 report come elenco «Report di Controllo Commesse» con contatori, pallini di stato e sotto-voci Commerciali/Officine (`?type=`), su `/gestore-ddp/controllo[/:view]`; contenuto a destra (`GestoreDdpPage` / `DdpControlReportView` / `DdpConsegneView`). Nella vista Card del Gestore **ogni commessa ha la sua card** (header codice+cliente, distinte C/O affiancate dentro); anche i **report di controllo** raggruppano le righe in **una card per commessa** (header codice+cliente, badge ritardi, attiva/disattiva commessa, stampa a sezioni per commessa; colonna Commessa in tabella solo nel report IO per giorno). Report: tab Commerciali/Officine, sort per colonna, **righe attivabili/disattivabili per la stampa** (checkbox, anche per intero giorno sul report IO raggruppato), stampa orizzontale via `printDdpTables`. Analisi Consegne: Recharts barre appaiate C/O per giorno, metrica €/righe, tick e righe scadute in rosso, tabella riepilogo + stampa (`/gestore-ddp/consegne` fa redirect). Endpoint `DdpManagerController`: `control-summary`, `control-report`, `deliveries-by-day` (insiemi da aggregazioni A2/A9). **Sintesi estesa**: KPI «Mat. in consegna» ora include le righe **senza data** in IO/PAR/MIT, alert igiene dati (date implausibili, costo zero), KPI cliccabili→sezione. **Migrazione v25**: seed stato `MIT` + membership A1/A4. Realtime: refetch su `DdpChanged` (hub project). *Server compila, tsc/eslint/vite OK; runtime GUI da verificare (bottoni header verificati dall'utente il 10/07).*
- **PM → Lavorazioni Officine** (`/work-requests`, **segnalazione #83**, 15/08/2026, migrazione **v92**) — la pagina **non tiene più copie** delle righe di distinta: le **guarda dove stanno**. Niente più bozze da promuovere, niente Tracciamento Consegne, niente pagina «Inbox Officina» (gruppo Officina rimosso dal menu: faceva le stesse cose).
  - **Quattro viste rapide**, con le regole della segnalazione: **Interne** = Tipo Interna + stato `DC`/`PAR`; **Esterne** = Tipo Esterna + `DO`/`PAR`; `MIT` esce da entrambe e resta in **Trattamenti** (con tutte le righe che hanno un trattamento indicato); **Urgenze** = interne segnalate ultra critiche. Il filtro SQL è `WorkRequestsController.RigheDdpFiltro`, **eseguito anche dai test** (niente copie che divergono).
  - **Tre soli campi scrivibili**, quelli che questa pagina possiede davvero: «Data Richiesta» (`ddp_officina_items.date_needed`, che nella DDP di commessa è diventata **di sola lettura** — server compreso), **Note** (`workshop_notes`, colonna nuova: **non** sono le note della distinta) e **Urgente** (`is_ultra_critical`, colonna nuova). Tutto il resto segue la DDP.
  - **Righe manuali** in `project_work_requests` (`ddp_officina_item_id IS NULL`), con `project_id` **facoltativo**: una riga può non avere commessa né Altra Attività. Dialogo dedicato, elimina con conferma.
  - Filtri di testata per **commessa** e **intervallo di date** (Data Richiesta; «Consegnato il» sulle esterne, dove è anche l'ordinamento di partenza), ordine congelato fino ad «Aggiorna», realtime su `workrequests-all` **e** `DdpChanged`.
  - Il tab **Lavorazioni** della commessa è la stessa vista ristretta (`ProjectWorkshopRows`).
  - Del vecchio motore di copia (`WorkRequestDdpSync`) resta `OfficinaRowSync`: **congelamento del Tipo** (serve al Bilancio e alle viste) e chiusura della riga a consegna. La v92 fa il **backfill del Tipo** su tutto il pregresso (stato → cronistoria, `from_status` compreso → fornitore) e travasa note/urgenze delle bozze **prima** di cancellarle; travasa anche `nav.officina_inbox` → `nav.work_requests` sui permessi. *Build server + `tsc -b` + `npm run build` + eslint puliti, **130 test verdi** (6 nuovi in `Migrazioni/LavorazioniOfficineTests.cs`); **runtime GUI da verificare**.*
- **PM → Check list** (`/checklist`, prototipo `Gestione_PM_V3.html`) — raccoglitore/generatore di check list: **board** con card per commessa (agganciate al `project_id` reale) e per **gruppo generico** (DIREZIONE/VARIE/custom, CRUD gruppo); tabella attività editabile inline (priorità **P0–P3** 0=Critica, scadenza con dow/festivi, **badge giorni**, flag critico, **riprogrammazione ±gg**, ordina per priorità/data, nuova riga); **viste Priorità aggregate** su tutte le commesse/gruppi; **inbox «Fissa attività»** personale (`checklist_inbox`) → «Assegna a…» commessa/gruppo; **realtime SignalR** (`ChecklistChanged`, gruppo `checklist-all` + `project-{id}`) + concorrenza `row_version`. Tab **Check list** anche nel dettaglio commessa (`ProjectChecklist`). Tabelle `checklist_groups`/`checklist_items`/`checklist_inbox`. Export rimandato. *Server compila, tsc/eslint web puliti; **verificato a runtime 02/07/2026**: API end-to-end (board/CRUD item, concorrenza `row_version`, vincolo XOR, inbox→assegna) + GUI (crea gruppo, aggiungi attività, priorità P0–P3, badge scadenza).*
  - **Import backup prototipo** (`31/07/2026`, pulsante «Importa» nella toolbar → `ChecklistImportDialog`): carica il `.json` esportato da «Gestionale PM» (`{ groups:[{name,kind,code,title,items:[{desc,pri,date,critico}]}], inbox:[{text}] }`). Le tabelle con codice commessa vengono **agganciate a `projects.code`** (match esatto → normalizzato senza punteggiatura, candidati: `code`, testa del nome prima di « - », regex `C\d{6}(_\d+)?`), le altre diventano **gruppi generici** (o si saltano con l'opzione dedicata); inbox → note personali di chi importa. `POST /api/checklist/import/preview` (sola lettura, mostra destinazione e conteggi per tabella) + `POST /api/checklist/import` (transazionale). **Ripetibile**: le attività con la stessa descrizione nello stesso contenitore vengono saltate, non duplicate. Build/tsc/eslint OK, **manca verifica runtime**.

- **PM → Milestone** (`06/07/2026`, port dal prototipo `Gestione_Commesse_V31.html`; parte fatta in questa chat, Gantt implementato da Antigravity su spec `MILESTONE-GANTT-SPEC.md`) — pianificazione a milestone di commessa:
  - **Anagrafica attività** = catalogo globale seedato (`/anagrafica-attivita` in Gestione avanzata): tabella con add/rinomina inline/riordino drag&drop/disattiva/«Ripristina standard». Tabella `activity_catalog` + controller `/api/activity-catalog`.
  - **Tab Milestone** (9ª sezione del dettaglio commessa, `ProjectMilestones`): tabella editabile inline (date con dow/festivi + regola fine≥inizio, avanzamento a barra, note, spegni/evidenzia, **drag&drop** + «inserisci sopra/sotto», textarea auto-cresce), header n°/avanzamento medio/periodo, **colori riga nella tenuità del Check list** (teal=completata/blu=in corso/rosso=in ritardo), **toggle Tabella|Gantt** persistito, **realtime SignalR** (`MilestonesChanged` su `project-{id}`) + `row_version`. Colonne settimana/W.Tot e stato = derivati client.
  - **Precarico dal catalogo** alla **creazione commessa** (checklist «Attività da precaricare» nel `ProjectDialog` → `/api/milestones/project/{id}/seed-from-catalog`, copia snapshot).
  - **Gantt** (`MilestoneGantt` + `milestones-gantt.css`, riusa `features/risorse/planner-logic.ts`): barre inizio→fine con riempimento avanzamento, linea oggi, weekend/festivi, mesi/settimane ISO, zoom, **filtro date con `DateField` standard** (regola Al≥Dal), combo **«Righe»** = `ColumnsMenu` per nascondere righe (persistite per progetto). Verificato a runtime 06/07.
  - **Pagina globale Milestones** (nav PM, `/milestones`, `MilestonesPage`): **sidebar PM condivisa** (`PmSidebar` — solo commesse con milestone attive, pallini di stato + conteggio), lazy-load milestone per card, toggle vista.
  - **Import backup prototipo** (`31/07/2026`, pulsante «Importa» nella testata di `/milestones` → `MilestonesImportDialog`): carica il `.json` di «Gestione Commesse» (le milestone stanno nella chiave localStorage `mplanner:commesse`, che è una **stringa JSON** da spacchettare). Aggancio alla commessa per **codice** (esatto → normalizzato); le commesse non in anagrafica sono **saltate** (le milestone richiedono `project_id`, non c'è contenitore di ripiego). `POST /api/milestones/import/preview` + `POST /api/milestones/import` (transazionale). Anti-duplicati su **descrizione + data inizio + data fine** (la sola descrizione non basta: nel planner la stessa voce ricorre con date diverse); opzione «Sostituisci le milestone esistenti» con conferma esplicita. Build/tsc/eslint OK, **manca verifica runtime**.
  - DB: tabelle `activity_catalog` + `project_milestones`, **migrazione schema v15**. DTO `ActivityCatalog_DTOs`/`Milestones_DTOs`. Build/tsc/eslint OK.

- **PM → SAL / Fatturazione — PARITÀ v10** (09/07/2026, esteso al prototipo `Gestione_Pagamenti_SAL_v10.html` su piano `SAL-V10-PLAN.md`, implementazione multiagente + review avversariale; requisiti dal vocale di Diego 09/07): **foglio a 16 colonne** ordine Excel (IVA · %IVA · Tot+IVA · Data prev. saldo · GG saldo · Step · N° Fattura · Conto SAP · %SAL · Condizioni · Importo · Ipotesi Fatt. · Stato · **Pagamento** · Data incasso · Note), Pagamento/incasso separato dallo Stato (verde Pagata/rosso Parz./giallo emessa, lock e barra incasso su Pagamento), header con PO/Rif. Offerta, **anagrafiche a 3 tab** (`/admin/sal-conditions`: Condizioni · Causali SAP · Stati Pagamento, voci di sistema protette), **warning incasso** (`SAL_INCASSO_DUE` in campanella + sorgente «Incasso SAL» in `/scadenze`), **Prospetto v10** (tutte le righe aperte + emesse non incassate, Data prev. saldo, contatori, **controllo periodico 15 gg** con banner e conferma realtime), **Cash Flow** (5 card netto/con IVA) + **Analisi** (barre impilate mensili + linea «Incasso previsto» + drill-down cliccabile + stampa con grafico) come viste `/sal?view=` gated PM/ADMIN. DB: migrazione **v21** (7 colonne su `sal_rows`, po/rif su `project_sal`, `sal_sap_causali`/`sal_payment_states`/`sal_prospetto_checks`, migrazione dati `stato='pagata'`→`emessa`+`Pagata`). Build/tsc/eslint OK; **runtime GUI da verificare**. Base precedente (07/07, prototipo V31):
  - **Tab SAL** nel dettaglio commessa (`ProjectSal`): header cliente/valore, tabella step editabile inline (%, condizioni pagamento, ipotesi fatturazione, stato fattura), importo derivato, **barra avanzamento incasso**, **semaforo warn/pre** fedele al prototipo, drag riordino, «Precarica modello standard», elimina con `useConfirm`, realtime `SalChanged` + concorrenza `row_version`.
  - **Anagrafica condizioni pagamento** in Gestione avanzata (`/admin/sal-conditions`, `SalConditionsPage`).
  - **Pagina PM globale** (`/sal`, `SalPage`): `PmSidebar` con commesse (dots warn/pre, conteggio ipotesi aperte) + viste rapide «Tutte le commesse» e **«Prospetto SAL»** (prime 2 ipotesi aperte per commessa, badge Scaduto/Pre-warning/In programma). Le card riusano `ProjectSal`.
  - **Warning fatturazione** nella campanella (`CheckSalDeadlines`, `SAL_DUE`/`SAL_ROW`, destinatari PM+ADMIN).
  - DB: `sal_conditions`/`project_sal`/`sal_rows`, **migrazioni v16+v17**; `SalController` (`/api/sal`), DTO `Sal_DTOs`. Build/tsc/eslint OK; **runtime GUI da verificare**.

- **PM → Bilancio Commessa** (04/08/2026, blocco 4 di `PIANO-LAVORO-COMMESSE-V32.md`, build/tsc/eslint + **verificato a runtime**: migrazione v61 applicata al DB di sviluppo, stack rispento, dati di prova ripuliti):
  - **Tabella «Ordine Commessa»** nel tab Preventivo vs Consuntivo: l'ordine cliente si scompone in N posizioni (Ordine / Posizione / Importo) con Totale Ordine, **Totale Vendita** a mano (rif. CALCOLO G205) e **Delta Ordine** = Ordine − Vendita. Aggiungi in coda / inserisci sotto / elimina con `useConfirm`, minimo 1 riga garantito dal server, `row_version` + realtime `BudgetChanged`.
  - **`projects.revenue` = somma delle righe** (SAL, dashboard e cash flow non cambiano): riconciliato in `ProjectsController.ReconcileOrderLinesWithRevenue` sulle 2 scritture dirette del ricavo + sulle 3 rotte delle righe. Con una riga sola il campo «Ricavo» della scheda la aggiorna, con più righe vince la tabella. **Niente backfill**: la prima riga nasce alla prima apertura del Bilancio, seeddata dal ricavo esistente.
  - **«Riepilogo Costi»**: tabella affiancata Costi Preventivati | Costi Consuntivati con le 4 voci di V32, Totale, **Redditività in € e in %** per sezione (sempre sul Totale Ordine, mai sulla Vendita). «—» ≠ 0,00 €.
  - **Voce «Lavorazioni Officine» separata da «Materiali commerciali»** e divisa interne / esterne / non classificate: nuova colonna `ddp_officina_items.work_type` con backfill a cascata (lavorazione collegata → stato DDP), auto-congelata da `WorkRequestDdpSync` prima che la riga chiuda, più la colonna **«Tipo»** nella distinta officina per sistemare a mano le residue. **Ripartizione a somma invariata**: `ActualTotalCost` e la Redditività non si spostano. A preventivo la voce è vuota di proposito (il calcolatore a righe è il blocco 5).
  - **Pagina `/bilancio`** (`BilancioPage`, `PmSidebar` come `/sal`): card per commessa con badge di stato standard (`projectStatusMeta`), «Consuntivo Redditività» e «Consuntivo % Redditività», rossi **entrambi** sotto la soglia (confronto stretto) o a importo negativo. **Soglia parametrica** (default 20%, `res_settings.bilancio_profit_threshold`, scrivibile solo da ADMIN, letta da tutti). #97: vista rapida «Commesse chiuse» con caricamento pigro delle COMPLETATE (via la vista «Sotto soglia» e lo switch «Mostra anche le completate»; bozze e annullate sempre fuori).
  - **Sanata** l'incoerenza pre-esistente del MARGINE del tab Dettagli: `ProjectDashboardData.TotalCost` ora include la trasferta a consuntivo.
  - DB: **migrazione v61** (`project_order_lines`, `projects.sale_total`, `ddp_officina_items.work_type`, feature key `nav.bilancio`). `BilancioController` (`/api/bilancio`), `Services/ProjectEconomics.cs`, DTO in `BudgetVsActual_DTOs`.

- **PM → Gestione Trasferta** (04/08/2026, blocco 6 di `PIANO-LAVORO-COMMESSE-V32.md` e `BLOCCO6-TRASFERTA-SPEC.md`; build verdi + **verificato a runtime**, v63 applicata al DB di sviluppo, dati di prova ripuliti, stack rispento). Modulo **nuovo**: prima «trasferta» era solo una voce di costo aggregata e un tipo di riga timesheet.
  - **Pagina `/trasferta`** (`TrasfertaPage`, `PmSidebar` come SAL e Milestones): card per commessa con **Giorni / Ore / Costi personale / Costi trasferta**, switch «Mostra anche le completate» (bozze e annullate sempre fuori), realtime `TravelChanged`.
  - **Step collassabili** con descrizione inline, riordino, elimina con `useConfirm` e **3 badge** in testata (costi personale / costi trasferta / totale step).
  - **Griglia riga-persona a 14 colonne** (`TravelStepTable`) con intestazioni a due piani: Personale (Nominativo · Inizio · Fine · Giorni · Ore · Costi Personale) · Alloggio/Vitto (Notti · Prezzo · Costo · Vitto) · Altri costi (Indennità · Auto · Treno/aereo) + riga **Totali** a 8 colonne. **È una griglia inline, quindi applica le due regole del blocco 4**: guardia sul fuoco dentro la riga e **un solo salvataggio all'uscita dalla riga** — provato a video con quattro campi di fila senza pause.
  - **Giorni = fine − inizio + 1** con **toggle sab / dom per riga** (`TravelMath` lato Shared, usato da entrambi i lati); **Costo alloggio = Notti × Prezzo**; **Costi Personale = tariffa × ore**, con la tariffa proposta da `departments.hourly_cost` del reparto del dipendente e poi congelata sulla riga. **Fix 04/08/2026:** cambiando nominativo la tariffa **segue il nuovo reparto** se quella presente era la proposta della persona precedente; resta solo se l'ha digitata un umano (prima restava sempre, e il costo era di un'altra persona senza che niente lo segnalasse).
  - **Le 4 calcolatrici** (`TravelCalcDialog`) sopra il componente del blocco 5, con i valori da `tariff_options`: Ore (Giorni × Ore Lav., **in numeri puri, non in euro**), Vitto (`DAILY_FOOD`), Indennità (`DAILY_ALLOWANCE`), Auto (`COST_PER_KM`, **tre fattori**: Km × Rimborso × Numero Tratte). Dentro, il **controllo di coerenza** «giorni del calcolo vs giorni trasferta», verde o giallo. Il dettaglio vive nei fogli del blocco 5 con la chiave che porta l'id della riga (`trasferta.ore:{rowId}`, …).
  - **«Riepilogo Trasferta»**: un rigo per nominativo distinto (ordine italiano) con i suoi totali + «Totale Riepilogo».
  - **Sincronizzazione al Bilancio**: solo la metà **«Spese Trasferta»**, con una riga di calcolo per step marcata `trasferta:step:{id}` (le righe a mano non si toccano). La metà «Risorse Atec» resta quella automatica dal timesheet: sovrascriverla sarebbe una regressione. Il costo trasferta a consuntivo ha **una sola regola** («foglio se c'è, altrimenti `projects.actual_travel_cost`») condivisa da conto economico, `/bilancio` e dashboard commessa.
  - DB: **migrazione v63** (`travel_steps`, `travel_step_rows`, `project_calc_rows.multiplier`, feature `nav.trasferta`). `TravelController`, `Services/TravelPlanService.cs`, `Travel_DTOs.cs`, `lib/api/travel.ts`, `lib/signalr/use-travel-hub.ts`.

- **Calcolatrici a righe + anagrafica tariffe** (04/08/2026, blocco 5 di `PIANO-LAVORO-COMMESSE-V32.md` e `BLOCCO5-CALCOLATRICI-SPEC.md`; build .NET / `npm run build` / eslint verdi + **verificato a runtime**: v62 applicata al DB di sviluppo, dati di prova ripuliti, stack rispento. Provata anche la **prova d'obbligo del blocco 4** — cinque campi di fila senza pause, nessuna perdita. Restano non provati il push realtime `BudgetChanged` (SignalR non negozia nel pane headless) e la finestra tariffe aperta sopra il calcolatore):
  - **Componente riusabile `components/shared/calc-sheet.tsx`** — finestra di calcolo a righe con N sezioni configurabili, Invio = nuova riga, totali live, riordino drag&drop, conferma che scarta le righe vuote, sempre almeno una riga vuota, **dettaglio persistito**. È un **dialogo con UNA conferma su copia locale**, non una griglia con commit su blur: le due perdite di dati del blocco 4 (refetch che cancella quel che stai scrivendo, salvataggi di fila che si scartano) non sono possibili per costruzione. Il blocco 6 ci monta sopra le 4 calcolatrici di Trasferta aggiungendo solo una `calc_key`.
  - **Calcolo «Lavorazioni Officine» a preventivo** (`WorkshopCalcDialog`): due sezioni «Officine esterne» / «Officine interne» che nel Riepilogo restano **una voce sola**. La cella «Costi Preventivati» della voce non è più un testo: è il pulsante che apre il calcolo. Nelle interne il costo è `Ore × Costo orario`; **con le Ore vuote il Costo vale come importo manuale** della riga.
  - **D4 applicata**: niente «k correzione» del prototipo. Il 45% è un **ricarico di vendita** scritto come moltiplicatore `1,450`, la voce di costo resta il **costo puro** (il consuntivo del blocco 4 non si muove) e la *vendita* delle lavorazioni entra nel costo netto della Scheda Prezzi come già fanno risorse e materiali. Il ricarico è vincolato a 1,000–9,999 **sia in UI sia lato server**: «45» sarebbe ×45.
  - **Override manuale** (`amount_pinned` + lucchetto in riga) e **marcatore di provenienza** (`linked_source`, badge «AUTO» e campi in sola lettura) sulle righe: gli stessi pattern di `contingency_pinned` e `project_cashflow_categories.linked_source`. L'importo effettivo lo **ricalcola il server**: quello mandato dal client non fa testo.
  - **Anagrafica tariffe** (`TariffOptionsPanel` in Configurazione sezioni, riaperta anche dal calcolatore): l'API esisteva da sempre e **nessun file del web la chiamava**. Aggiunta, **modifica dell'importo (PUT nuovo)**, eliminazione con conferma e blocco se il valore è in uso; nuovo tipo **`HOURLY_RATE`** (40/50 €/ora) per le Officine interne.
  - **Risorse a consuntivo a due livelli** in ogni sezione del preventivo: `actualEmployees` arrivava nel DTO e **non veniva renderizzato da nessuna parte** — ora è una riga per dipendente che si apre sulle ore versate (data · fase · causale · ore · €/h · costo), dal timesheet reale.
  - **Lookup risorse sistemata**: non più i soli wildcard (le persone vere si vedono, wildcard in testa), via i nomi già usati nella sezione (`excludeResourceId` per la modifica), ordinamento italiano lato C#.
  - DB: **migrazione v62** (`project_calc_sheets`, `project_calc_rows`, seed tariffe `HOURLY_RATE`). `Services/ProjectCalcSheets.cs`, rotte `GET/PUT /api/projects/{id}/budget-vs-actual/calc/{calcKey}`, `lib/api/tariffs.ts`.

- **Dashboard a cartelle** (04/08/2026, blocco 7 di `PIANO-LAVORO-COMMESSE-V32.md`; build verdi + **verificato a runtime**: v64 applicata al DB di sviluppo, dati di prova ripuliti, stack rispento. Non provato il push realtime `ProjectsChanged` — SignalR non negozia nel pane headless):
  - **`/` si apre sulle CARTELLE** (`DashboardFolders`): una cartella per commessa con le **tre statistiche già a video** (milestone attive · avanzamento medio con barretta · periodo), cliente e PM, tutta cliccabile (link vero sul titolo per la tastiera). La panoramica KPI storica è nella seconda scheda «Panoramica» e la scelta è ricordata (`dashboard:view:v1`).
  - **Spunta «In dashboard»** = colonna `projects.in_dashboard`, quindi **condivisa**: chi la toglie la toglie a tutti (come nel prototipo) e la scrittura parte dal livello **PM**. Le escluse restano recuperabili dalla fascia di chip in fondo. Il realtime passa dall'hub commesse già montato nella shell (`useProjectsRealtime`, chiave `dashboard-folders`): nessuna seconda connessione SignalR.
  - **Limite di cartelle governabile**: nel prototipo `DASH_MAX = 10` è una costante nel sorgente, qui è `res_settings.dashboard_max_cards` (default 10, ADMIN scrive / tutti leggono, come la soglia del Bilancio). Taglia le cartelle **a video**, non l'elenco: le commesse oltre il taglio restano fuori schermo con la nota esplicativa, non finiscono fra le escluse.
  - **Cartella «Pagamenti SAL» che si colora**: rossa con scadenze raggiunte o incassi scaduti, gialla con soli pre-warning, badge col totale. Riusa la chiave react-query `sal-summary` di `/sal` (nessuna chiamata in più) e sparisce per chi non ha `nav.sal`.
  - **Righe della tabella «commesse recenti» cliccabili** (`DashboardProjectRow` porta l'`id`): il codice è un link vero, la riga si apre col mouse da qualsiasi punto.
  - **Anagrafica attività dal form commessa**: la griglia è stata estratta in `ActivityCatalogEditor`, condivisa fra `/anagrafica-attivita` e il nuovo `ActivityCatalogDialog`. Round-trip: la voce aggiunta lì torna già spuntata nel precarico, la selezione fatta finora resta, le voci eliminate o disattivate escono da sole.
  - **Dopo la creazione si atterra sul SAL** della commessa nuova (`/commesse/{id}/sal`); chi il SAL non lo vede resta sulla scheda commessa.
  - DB: **migrazione v64** (`projects.in_dashboard`, default 1 → nessun cambio di comportamento all'aggiornamento). `DashboardController` (`GET /folders`, `PUT /folders/{id}`, `GET|PUT /settings`), DTO in `Dashboard_DTOs.cs`, `lib/api/dashboard.ts`.

- **PM → Scadenze** (08/07/2026, implementato da Antigravity su spec `SCADENZE-SPEC.md`, verificato build/tsc/eslint) — cruscotto unificato di tutte le scadenze (SAL, commesse, checklist, MoM, DDP):
  - **Endpoint unificato** `/api/deadlines` con DTO `DeadlineDto` ed esecuzione UNION ALL delle 5 sorgenti di scadenza.
  - **Pagina master-detail** `/scadenze` con lista a sinistra filtrabile (ricerca, toggle per tipo, switch "Solo da gestire" <= 7 giorni) e dettagli del 'colpevole' a destra con tasto «Apri».
  - **Campanella:** pulsante nel footer del popover «Vedi tutte le scadenze» per accedere al cruscotto.
  - **Feature key:** `nav.scadenze` con migrazione database `v19`.

- **Commesse (Fase A)** — lista paginata server-side (ricerca, «Colonne», «Nuova commessa») + `ProjectDialog` crea/modifica (codice auto, lookup cliente/PM, transizioni di stato, quote-lock, regola date inizio/fine), annulla (soft) ed elimina definitivo (ADMIN, doppia conferma); **dettaglio** con tab Panoramica + **Documenti** (cartella+breadcrumb, upload multiplo+drag&drop, cartelle, rinomina/sposta/elimina, anteprima PDF/immagini/Office; **anteprima mail .msg/.eml** 17/07/2026 — `EmailPreviewService` server-side con MsgReader/MimeKit, intestazione+allegati+corpo, immagini inline cid→data URI, stesso iframe sandbox delle anteprime Office; le mail si caricano salvandole da Outlook come file, il drag diretto da Outlook classico al browser non è tecnicamente possibile) + **DDP commerciale e officina** (inserimento da picker Catalogo/Codex fedele al WPF — doppio clic, +1 su duplicato, «Nuovo codice Codex», scroll infinito, header sticky; lista/modifica/elimina riga, concorrenza ottimistica, realtime SignalR). *Build/tsc/eslint OK; inserimento DDP verificato a runtime.* **Colonna Destinazione con combo da Conf. DDP** (03/07/2026, parità WPF): menu ⋮ inline sulla cella (come Stato, `DdpDestinationMenu`) + Select nei dialog di modifica, opzioni da `/api/ddp-destinations/active` con regola «mantieni il valore corrente anche se non più attivo» (`ddp-destination-options.ts`); voce «Rimuovi destinazione»/(nessuna) per azzerare. *Verificato a runtime su entrambe le distinte.*

- **Commesse → Prev vs Consuntivo (Fase C#8)** — tab dedicato nel dettaglio commessa, gated PM/ADMIN: conto economico (offerta/order price/budget vs consuntivo/redditività/avanzamento/tecnici/fasi), gruppi→sezioni IN_SEDE/DA_CLIENTE con preventivo·assegnato·consuntivo e delta colorati, materiali, scheda prezzi; **fasi & assegnazioni** editabili (aggiungi/rimuovi tecnico, ore pianificate inline, importa fasi da template, fase locale), edit inline order price e trasferta consuntivo. *Build/tsc/eslint/vite OK; runtime: vista lettura + order price verificati via GUI, fasi/assegnazioni via contratto API.* NB: il «costing editor» standalone del WPF è codice morto — il costing vero è sui Preventivi (Fase D).

- **Commesse → dettaglio completo (8 sezioni)** — tutte le sezioni del WPF sono attive: Dettagli, **Flusso di Cassa** (`ProjectCashFlow`), Preventivo vs Consuntivo, **Chat** (`ProjectChat` + cartella `chat/` con composer/allegati/mentions/partecipanti + realtime SignalR `use-project-chat-hub`), **Verbali (MoM) per commessa** (`ProjectMoM`, lista filtrata per commessa + crea verbale `COMMESSA`), DDP Commerciali, DDP Officina, Documenti.

- **Notifiche** — ✅ centro notifiche nell'header: campanella + badge non-letti con **polling adattivo** (30/60/120s, fedele a `NotificationPollingService` WPF, pausa senza focus via react-query), popover con lista (icona severità, tempo relativo IT, vai-alla-commessa, segna letta / «segna tutte lette», elimina) + `check-pending` al login. *tsc/eslint OK; **verificato a runtime 25/06/2026**: badge=4, popover con 4 notifiche reali, zero errori console.*

🟡 **Gap trasversali ancora aperti (non legati a una singola pagina):**
- **Cambio password obbligatorio al login** — stub: `LoginPage` rileva `mustChangePassword` ma manca il dialog ("implementare dialog nel passo 2").
- **Import Easyfatt/Danea** per Clienti/Fornitori/Catalogo articoli.
- **Dashboard**: MoM in dashboard (minore). *I link rapidi sono coperti dalla scheda «Cartelle» (blocco 7): cartelle-commessa cliccabili + cartella «Pagamenti SAL» colorata sulle scadenze.*

- **Commerciale → Cat. Preventivi (Fase D2)** — albero listini→gruppi→categorie→prodotti→varianti (accordion, natural-sort, ricerca), CRUD gruppo/categoria/sotto-categoria/prodotto, sposta drag&drop, tabella prodotti (varianti espandibili, filtri colonna jolly, range prezzo/costo, auto-include), **editor descrizione TinyMCE 5 self-hosted** (asset copiati in `public/tinymce/`, upload immagini su `/api/quote-catalog/products/upload`, path relativi `/uploads/cms/`). *Build/tsc/eslint OK + asset TinyMCE serviti; runtime GUI autenticato da verificare. Manca import Excel.*

✅ **Fase D completata (Commerciale):** D1 layer API (`quotes.ts`/`quote-catalog.ts`/`quote-costing.ts`) · D2 Cat. Preventivi (TinyMCE) · D3 lista preventivi (catene revisione, filtri, stato inline, PDF) · D4 dettaglio SERVICE (prodotti/varianti editabili) · D5 costing tree IMPIANTO (sezioni/risorse/materiali/scheda prezzi + tabella distribuzione prezzo con pin/shadow/ridistribuisci) · D6 convert→commessa. Build/tsc/eslint OK; **runtime GUI autenticata verificata (25/06): Cat. Preventivi, lista (catene revisione), dettaglio SERVICE + IMPIANTO con costing tree, TinyMCE — zero errori console**.

✅ **Risorse/Gantt — sostanzialmente completo (port da `ATEC.Risorse.Web` Blazor, non dal WPF), verificato runtime 01/07/2026.** Gantt completo (header mesi/giorni, lane-packing, barre OP/FLEX/FERIE, weekend/festività/oggi, conflitti), filtri + zoom + pannello risorse + filtro "In lista" (persistenza localStorage), CRUD via `AssignmentDialog` (crea multi/modifica/duplica/elimina), **drag/resize/create-by-drag** (auto-pan, snap settimana, concorrenza ottimistica con messaggio 409 dedicato), scorciatoie tastiera (Ctrl+F/Canc), Shift+rotella, stampa Gantt, collassa pannello/colonna nomi, **presenza online** (pallino verde/rosso via SignalR), **dashboard Piano ferie** (`/risorse/ferie`), **digest email** (badge "modifiche da notificare", dialog "Notifica subito", pannello admin Config SMTP+scheduler in Gestione avanzata → Digest Email). File in `src/features/risorse/` + `src/features/admin/digest/`, API `src/lib/api/resource-planner.ts`+`digest.ts`+`settings.ts`. **Resta solo**: editor anagrafiche Service/Altre attività (bassa priorità, non blocca il flusso).

✅ **Gamma Robot** (20/07/2026) — port WPF completo: tab Per Robot / Magazzino / Composizione (ADMIN DnD + CRUD). Feature key `nav.gamma_robot` (migrazione v36). File: `src/features/gamma-robot/*`, `src/lib/api/gamma-robot.ts`.

✅ **WPF retired** (20/07/2026) — `ATEC.PM.Client` rimosso dalla solution e spostato in `backups/ATEC.PM.Client_retired_20260720/`. Client ufficiale = solo web.

## Regole NON negoziabili per ogni nuova pagina

1. **Fedeltà ai blocchi shadcn** — parti sempre da un blocco e dalle recipe in
   `BLOCKS-RULES.md`. Niente layout custom, niente HTML raw dove esiste il primitivo,
   solo token di colore/spazio, classi canoniche copiate alla lettera.
2. **Pattern API** — un file per dominio in `src/lib/api/` con funzioni tipizzate che
   chiamano `apiGet/apiPost/apiPut/apiPatch/apiDelete` + `unwrapApi`. DTO in
   `src/lib/api/types.ts`. Verifica sempre il contratto leggendo il controller in
   `ATEC.PM.Server/Controllers` e i DTO in `ATEC.PM.Shared/DTOs`.
3. **Menu:** tasto **destro** → `ContextMenu`; pulsante **`⋮`** → `DropdownMenu`.
4. **Wiring:** aggiungi la pagina a `LIVE_ROUTES` in `src/app/AppRoutes.tsx` e porta
   la voce a `status: "live"` in `src/config/navigation.ts`.
5. **Verifica obbligatoria** prima di chiudere: `tsc -b` e `eslint` puliti (vedi nota PATH).
6. **Ruoli:** gli endpoint admin (`/api/users`, `/api/employees`, `/api/auth-levels/features`,
   `/api/backup`) sono **ADMIN-only**. Le pagine funzionano solo con login ADMIN.
7. **Freschezza dati (14/07/2026, richiesta esplicita di Diego):** il default globale
   react-query è `staleTime: 0` + `refetchOnWindowFocus: true` (`App.tsx`) — ogni ingresso
   in pagina e ogni ritorno sulla finestra rileggono dal server; la cache è solo un
   placeholder visivo. **NON reintrodurre `staleTime` lunghi** (né globali né per-query)
   su dati operativi: sono ammessi solo su configurazioni statiche (es. causali SAL).
   A pagina aperta la diretta la fanno gli hub SignalR (vedi registro realtime in memoria).

## Metodo di lavoro consigliato (come abbiamo proceduto finora)

Per portare un modulo a parità: 1) mappa le funzionalità della pagina WPF corrispondente
(in `ATEC.PM.Client/Views/...`), 2) leggi gli endpoint/DTO server reali, 3) costruisci il
layer API, 4) costruisci la/le pagina/e seguendo `BLOCKS-RULES.md`, 5) collega rotte+nav,
6) `tsc -b` + `eslint`, 7) aggiorna `WEB-MIGRATION.md`.

## Prossimi passi suggeriti (roadmap WEB-MIGRATION.md)

- **Fase A — Commessa usabile:** albero commesse + crea/modifica; documenti (upload/cartelle/preview); DDP commerciale base.
- **Fase B — Operatività PM:** MoM completo; Timesheet calendario mese/settimana.
- **Fase C — Core business:** Gestore DDP + SignalR; Costing + Preventivo vs Consuntivo; Resource Planner Gantt *(deciso 18/06: portarlo da un progetto web già funzionante, NON convertirlo dal WPF — backend `/api/resource-planner` + hub già pronti; vedi WEB-MIGRATION.md §Risorse)*.
- **Fase D — Commerciale/Codex:** Preventivi + catalogo; Codex + composizione.

## Sidebar PM condivisa — FATTO (06/07/2026)

**Estratto** `src/components/shared/pm-sidebar.tsx` — componente **generico** `PmSidebar`
(props `quickViews[]` + `containers[]` con `{key,label,count,dots[],selected,onClick,railIcon?}`,
`storageKey`/`containersLabel`/`emptyLabel`; collasso persistito per-utente `<storageKey>.sidebar.collapsed.<id>`,
`QuickNavItem` con icona **o** pallino, `ContainerNavItem` con pallini di stato impilati, rail compresso
`w-14`/esteso `w-[286px]` con badge+tooltip). Estratto da `MoMSidebar` + supporto `dotClass` da `ChecklistSidebar`.

- `MoMSidebar` e `ChecklistSidebar` sono ora **adattatori** su `PmSidebar` (firme pubbliche
  `MoMView`/`ChecklistView` + props **invariate** → nessun cambio ai consumer `MoMPage`/`ChecklistPage`;
  viste rapide, dots priorità P0–P3, riunioni/gruppi generici tutti preservati).
- `MilestonesPage` usa `PmSidebar`: sidebar **«solo commesse con milestone attive»** (scelta utente),
  pallini = stato milestone (**rosso=in ritardo · blu=in corso · teal=completate**), conteggio = milestone
  attive; **rimossi** casella di ricerca e gruppi Attive/Altre; layout portato allo **standard**
  (titolo sopra il riquadro `border` + sidebar `border-r`, controlli in testa al `main`).
- **Dato:** nuovo endpoint **`GET /api/milestones/summary`** (`MilestonesController.GetSummary` +
  `MilestoneSummaryDto`) → per-commessa `active/late/current/done` calcolati in **SQL con `CURDATE()`**
  (replica la precedenza di `msStatus`: done→late→current), solo righe attive (`spento=0`), `HAVING COUNT>0`,
  JOIN `projects` per code/title. Client: `fetchMilestonesSummary` + tipo `MilestoneSummary` + helper
  `summaryStatusDots`. Il **lazy-load** delle card resta invariato; l'`invalidate` della card aggiorna anche
  la summary → pallini live quando la card è aperta / su evento realtime `MilestonesChanged`.

**Verifica:** `tsc -b` + `eslint` puliti, `dotnet build` OK (0 errori). Endpoint **testato a runtime**
(JWT admin coniato → `success`, conteggi coerenti, classificazione stato confermata sui dati reali).
La verifica **GUI** delle 3 sidebar non è stata pilotata in questa sessione perché era attivo un Vite
dell'utente sulla 5173 (ambiente concorrente da non disturbare) — da fare al prossimo giro.

**Nota edge:** se una commessa con milestone non rientra nei primi 250 progetti caricati per l'area
principale, selezionandola dalla sidebar la card non compare («Nessuna commessa trovata»). Raro; se
capitasse, alzare il `pageSize` di `fetchProjects` in `MilestonesPage` o costruire la card dal summary.
