# Migrazione ATEC PM → Web

Stato migrazione dal client WPF al client React (`atec-pm-web`).

> **Onestà sullo stato:** il web ha **le stesse voci di menu** del WPF, ma molti moduli sono solo **liste base** o **CRUD semplificato**. Il WPF resta il client completo per lavoro operativo quotidiano finché non chiudiamo i gap sotto.

## Legenda

| Stato | Significato |
|-------|-------------|
| **live** | Parità sufficiente per uso quotidiano |
| **partial** | Solo parte delle funzioni WPF |
| **shell** | Menu + lista/placeholder, poco usabile in produzione |
| **planned** | Non iniziato |

## Riepilogo rapido (~copertura funzionale)

| Area | Web | WPF |
|------|-----|-----|
| Menu + permessi | ✅ | ✅ |
| Liste anagrafiche | ~60% | ✅ |
| Commessa (lista + dettaglio + sotto-moduli) | ~90% (lista+CRUD, **dettaglio a 8 sezioni**: documenti, DDP commerciale+officina, **chat realtime**, **MoM per commessa**, **flusso di cassa**, preventivo-vs-consuntivo; manca solo la dashboard-commessa ricca del WPF) | ✅ |
| Timesheet | ~85% (calendario mese/settimana) | ✅ |
| Prev. vs Consuntivo (commessa) | ✅ tab dedicato (conto economico, prev/assegn/cons, fasi&assegnazioni) | ✅ |
| Preventivi / Costing (commerciale) | ✅ Fase D completa (lista+catene revisione, dettaglio SERVICE, costing tree IMPIANTO, convert→commessa) | ✅ |
| DDP (Gestore DDP + sintesi) | ~85% (gestore+sintesi completi, **distinte commerciale e officina sotto commessa** con inserimento da picker Catalogo/Codex fedele al WPF) | ✅ |
| PM — Milestone (per commessa + pagina globale) | ✅ **nuovo** (dal prototipo `Gestione_Commesse_V31.html`): anagrafica attività, tab Milestone tabella + **Gantt**, precarico dal catalogo, realtime | — (non esiste nel WPF) |
| PM — SAL / Fatturazione (per commessa + pagina globale) | ✅ **nuovo** (dal prototipo `Gestione_Commesse_V31.html`): tab SAL nel dettaglio commessa (step/%/condizioni/ipotesi fatturazione/stati, avanzamento incasso, semaforo warn/pre, realtime), anagrafica condizioni, **pagina PM globale `/sal`** (sidebar commesse + **Prospetto SAL**), warning in campanella. Migrazioni v16+v17 | — (non esiste nel WPF) |
| Risorse / Gantt | ~98% (Fase 1+2+3 + presenza online + rifiniture + **digest email**; **manca** solo editor anagrafiche Service/Altre) | ✅ |
| Codex / Catalogo | ✅ (lista+sync+genera codice, catalogo articoli, **composizione**) | ✅ |
| Gestione avanzata | ✅ parità | ✅ |
| Notifiche / Chat | ✅ Chat commessa (realtime SignalR) + Notifiche (campanella header, badge + polling adattivo) | ✅ |

---

## Gap dettagliato per modulo

### Principale

#### Dashboard
| WPF | Web |
|-----|-----|
| KPI commessa, grafici, scadenze | KPI + grafico ore + tabella commesse |
| Link rapidi, MoM in dashboard | ❌ |

#### Commesse — Fase A completata (gap residuo: costing/preventivi/chat)
| WPF | Web |
|-----|-----|
| Albero commesse + ricerca + nuova commessa | ✅ Lista paginata server-side + ricerca + «Colonne» + «Nuova commessa» (dialog) |
| Modifica / elimina / stati commessa | ✅ Modifica (dialog con transizioni di stato, quote-lock, regola date inizio/fine); annulla (soft → CANCELLED); elimina definitivo (ADMIN, doppia conferma) |
| **Dettagli** (dashboard commessa completa) | Parziale (KPI base, riepilogo reparti, ultimi timesheet) |
| **Documenti** upload/rename/move/preview | ✅ Gestore completo: cartella piatta + breadcrumb, upload multiplo + drag&drop, nuova cartella, rinomina/sposta/elimina, anteprima (PDF/immagini via blob, Office via `/preview`) |
| **Chat** commessa | ✅ `ProjectChat` + cartella `chat/` (composer, allegati, mentions @utente, popover partecipanti) + realtime SignalR (`use-project-chat-hub`) |
| **MoM** per commessa | ✅ `ProjectMoM` — lista verbali filtrata per commessa, crea verbale tipo `COMMESSA`, naviga al dettaglio MoM |
| **DDP Commerciali** | ✅ Tab dedicato: lista + modifica/elimina riga (concorrenza ottimistica + realtime SignalR); **inserimento da picker Catalogo** (doppio clic/«+», Qtà=1, stato DO, richiedente=utente, +1 su duplicato — fedele al WPF, verificato a runtime); in modifica solo i campi che il server persiste |
| **DDP Officina** | ✅ Tab dedicato: lista + modifica/elimina riga; **inserimento da picker Codex** (doppio clic/«+» + «Nuovo codice Codex», copia codice/descr/prezzo/fornitore, +1 su duplicato — fedele al WPF, verificato a runtime) |
| **Preventivo vs Consuntivo** | ✅ Tab dedicato (PM/ADMIN): conto economico (offerta/order price/budget vs consuntivo/redditività/avanzamento), gruppi→sezioni IN_SEDE/DA_CLIENTE con preventivo·assegnato·consuntivo e delta colorati, materiali, scheda prezzi; **fasi & assegnazioni** editabili (aggiungi/rimuovi tecnico, ore pianificate, importa fasi, fase locale), edit inline order price + trasferta consuntivo |
| **Flusso di cassa** | ✅ `ProjectCashFlow` (+ `project-cashflow.ts`) |
| Costing commessa (ProjectCostingControl) | ⚠️ editor standalone = codice morto nel WPF; il costing è in lettura dentro «Preventivo vs Consuntivo» (preventivato). Editor costing vero = sui Preventivi (Fase D) |

#### Timesheet ✅ parità con la pagina WPF
| WPF | Web |
|-----|-----|
| Vista **mese** + vista **settimana** calendario | ✅ toggle mese/settimana (griglia 7 colonne) |
| Chip ore per giorno, click giorno, doppio-click per aggiungere | ✅ chip colorati per tipo (max 3 + «+N altre»), badge ore/giorno, selezione + doppio-click |
| Modifica/elimina voce (click chip + menu destro) | ✅ click chip → dialog; tasto destro → `ContextMenu` (Modifica/Elimina) |
| Selettore dipendente (PM/RESP) | ✅ (registrable-employees, mostrato se >1) |
| Date future bloccate | ✅ (celle non cliccabili + Calendar con `after: today` disabilitato) |
| Validazione 24h visuale | ✅ hint live nel dialog (`day-total`) + enforcement server |
| Riepilogo mensile per fase | ❌ (endpoint `/summary` esiste ma non è in pagina **nemmeno** nel WPF) |

#### Risorse — 🟡 in corso (port da ATEC.Risorse.Web Blazor → React)
| WPF / Sorgente Blazor | Web (atec-pm-web) |
|-----------------------|-------------------|
| Gantt assegnazioni (header mesi/giorni, lane-packing, barre OP/FLEX/FERIE, weekend/festività/oggi, conflitti) | ✅ Fase 1 (lettura) |
| Filtri (solo mie/conflitti/occupate, ricerca, legenda tipo, zoom, pannello risorse on/off, **filtro "In lista"**) + persistenza UI | ✅ Fase 1 (localStorage). **Fix 01/07/2026**: le risorse senza allocazioni ora restano visibili (righe vuote pronte per drag-create) invece di sparire sempre; aggiunto popup "In lista" (pool ristretto, Tutte/Nessuna) e "Solo occupate" |
| CRUD allocazioni | ✅ Fase 1 via dialogo (`AssignmentDialog`): crea multi-risorsa / modifica singola, regole FERIE, date inizio≤fine; menu destro Modifica/Duplica/Elimina. **Solo campo Commessa** (Service/Altra attività rimossi dalla UI web 01/07/2026 — la commessa segue già la filiera offerta-accettata→conversione, lookup filtrato a `status='ACTIVE'`) |
| SignalR realtime | ✅ Fase 1 (`use-resource-planner-hub`, `AssignmentsChanged`, merge non-interruttivo) — **verificato a runtime 30/06** |
| Drag/resize barre, auto-pan ai bordi, snap settimana | ✅ Fase 2 (move/resize/**create-by-drag**, quantizzato al giorno come il sorgente Blazor, auto-pan ai bordi, snap settimana con Shift, Escape annulla, conferma conflitto, concorrenza ottimistica) — **verificato runtime 30/06** |
| Piano ferie / dashboard ferie + export CSV | ✅ Fase 3 (route `/risorse/ferie`, pulsante «Piano ferie» nel planner): 3 KPI (colleghi/giorni lavorativi/picco contemporaneo), timeline solo-ferie, filtri Tutti/Con ferie/Selezionati, «+» aggiungi e click-barra modifica via `FerieEditDialog`, export CSV — **verificato runtime 30/06** |
| Anagrafiche Service / Altre attività (editor) | ❌ |
| Presenza online + digest email notifiche | ❌ (richiede lavoro backend PM — pianificato) |

> **Decisione 18/06 → eseguita 30/06/2026.** Il Gantt è **portato da `C:\Users\diego\Desktop\ATEC_Risorse\ATEC.Risorse.Web`** (Blazor WASM, programma in uso), NON convertito dal WPF. Essendo Blazon→React la logica è **tradotta** (planner-logic.ts = port di `PlannerLogic.cs`), non copiata. Backend PM riusato così com'è (`/api/resource-planner` + hub `/hubs/resource-planner`); allineata solo la regola conflitti server (`FERIE+FERIE` = conflitto). File: `src/features/risorse/*`, `src/lib/api/resource-planner.ts`, `src/lib/signalr/use-resource-planner-hub.ts`.

---

### PM

#### Verbali (MoM) — ✅ live (**gestione v9**, prototipo `Gestione_MoM_v9.html` — 02/07/2026)
| Prototipo v9 / WPF | Web |
|-----|-----|
| **Note MoM — acquisizione rapida** (note → MoM di destinazione) | ✅ pagina `/mom/note` (nav «Note MoM»): righe nota con autosave **server per utente** (`mom_notes`), select destinazione, «Assegna» → il testo va nel campo **Azione** della prima riga vuota del verbale (o nuova riga in fondo, priorità 1); riga verde e rimozione |
| Lista verbali | ✅ **doppia visualizzazione** come il Gestore DDP (select Tabella \| Card persistita per utente in localStorage): tabella shadcn con KPI card filtranti e colonna **Rev.**, oppure card-verbale raggruppate per commessa + gruppo «Riunioni» (badge tipo colorato v9, pill P1/P2/P3, periodo, data riunione, Rev, checkbox dashboard, menu ⋮); realtime su entrambe |
| **Dettaglio = foglio editabile** (tabella inline stile Excel) | ✅ `/mom/:id` riscritto: celle inline con **autosave debounced** (coda seriale), textarea auto-grow, righe vuote ammesse («+ Nuova riga», Ctrl+N) |
| **Autocomplete** definizioni su Attività/Descrizione | ✅ tendina «Definizioni disponibili» (valori già usati nella MoM, filtro contains, frecce+Invio, portal) |
| Invio nel campo Azione = nuova riga | ✅ (Shift/Ctrl+Invio = a capo) |
| Ordinamento colonne + gruppi rosse→aperte→standby→chiuse | ✅ click su header (asc/desc), gruppi fissi; righe colorate (critica rossa, stand-by gialla, chiusa verde) |
| **Riordino righe drag&drop** (maniglia ⠿) con auto-scroll | ✅ persistito su `sort_order` (`POST /api/mom/{id}/items/reorder`); disattiva l'ordinamento automatico come v9 |
| **Revisioni**: cambio di una data riunione già impostata ⇒ conferma + Rev+1 | ✅ regola applicata **server-side nella PUT** (vale anche per WPF), storico in `mom_revisions`, badge Rev. + tooltip storico, conferma `useConfirm` nel dettaglio |
| Date con **giorno della settimana** (rosso se festivo) | ✅ `lib/it-holidays.ts` (Pasqua/Pasquetta + fisse), dow sotto DateField e nella stampa |
| Stampa / Word / Excel / CSV | ✅ tutti e 4 (A4 landscape con Rev + dow, .doc HTML, .xls, .csv `;` con BOM); Word/Excel/CSV raccolti nel pulsante **«Esporta ⋮»** (DropdownMenu) accanto a Stampa |
| Responsabili multipli (fino a 3 slot legacy, N reali) | ✅ combo multi-selezione con ricerca (pool employees + wildcard) |
| Multi-utente | ✅ **realtime SignalR** (`JoinMoM`/`MoMChanged` su `/hubs/project`, refetch se non si sta editando) + **concorrenza ottimistica** `row_version` sugli item (409 → ricarica foglio); i client legacy senza rowVersion non sono vincolati |
| MoM per commessa (ProjectMoMControl) | ✅ tab «Verbali (MoM)» nel dettaglio commessa (`ProjectMoM`, API `?projectId`) |
| ❌ non portati (scelta) | Import CSV e backup/import JSON del prototipo (il DB centrale + Backup admin li rendono superflui); Dash_Control e Anagrafica Colleghi (coperti da nav app e anagrafica employees) |

#### Gestore DDP — ✅ live (parità WPF)
| WPF | Web |
|-----|-----|
| Sintesi multi-commessa, cartelle per commessa | ✅ **cartelle hover-popover** (stile MoM): tile per commessa, al passaggio del mouse si aprono le card Commerciale/Officina; badge ritardi + pill tipi, ricerca |
| Card per tipo (Commerciale/Officina) con KPI 2×2 | ✅ Tot. acquisti/Inserimenti/Mat. consegna/Mat. ritardo + date inserita/consegne |
| Sintesi commessa completa | ✅ `/gestore-ddp/:projectId` — 7 KPI, **Stati Avanzamento** (8 card), accordion: Ripartizione, Materiale in Consegna/Consegnato, Top 10 Costi, Destinazioni, Dati Mancanti, Dati Distinta, Feedback Acquisti/Magazzino |
| Aggregazioni A2/A3/A6/A7/A8 (config-driven) + colori stato | ✅ da `/api/ddp-aggregations` + `/api/ddp-statuses` (fallback cablati) |
| Realtime SignalR | ✅ hub `/hubs/project` (JoinAll / JoinProject) → refresh live |
| Stampa per sezione + report + Esporta Excel | ✅ stampa (finestra print) per sezione e completa + Excel (.xls) |

#### Milestone — ✅ live (NUOVO, dal prototipo `Gestione_Commesse_V31.html` — 06/07/2026; Gantt implementato da Antigravity su spec `MILESTONE-GANTT-SPEC.md`)
| Prototipo Gestione Commesse | Web |
|-----|-----|
| Anagrafica attività (catalogo voci standard, precaricabili) | ✅ `/anagrafica-attivita` (Gestione avanzata): tabella con add/rinomina inline/riordino drag&drop/disattiva/«Ripristina standard». Tabella `activity_catalog`, controller `/api/activity-catalog` |
| Tabella milestone di commessa (date, avanzamento, settimane, stato) | ✅ tab **Milestone** (9ª sezione commessa, `ProjectMilestones`): tabella editabile inline (date con dow/festivi + **regola fine≥inizio**, avanzamento a barra, note, spegni/evidenzia, **drag&drop** + «inserisci sopra/sotto», textarea auto-cresce), header n°/avanzamento medio/periodo, **colori riga nella tenuità del Check list** (teal=completata/blu=in corso/rosso=in ritardo). W.Inizio/W.Fine/W.Tot e stato = derivati client. **Realtime** `MilestonesChanged` su `project-{id}` + `row_version` |
| Precarico attività standard alla creazione commessa | ✅ checklist «Attività da precaricare» (tutte pre-spuntate) nel `ProjectDialog` → `/api/milestones/project/{id}/seed-from-catalog` (copia snapshot: nessun legame residuo col catalogo) |
| Diagramma di Gantt | ✅ `MilestoneGantt` + `milestones-gantt.css` (riusa `features/risorse/planner-logic.ts`): barre inizio→fine con riempimento avanzamento, linea oggi, weekend/festivi, mesi/settimane ISO, zoom 14/30/60, **filtro date con `DateField` standard** (regola Al≥Dal), combo **«Righe»** (`ColumnsMenu`) per nascondere righe (persistite per progetto). Toggle **Tabella \| Gantt** persistito. Verificato a runtime 06/07 |
| — (nuovo, non nel prototipo) | ✅ **Pagina globale Milestones** (`/milestones`, nav PM): commesse raggruppate (attive/altre), **lazy-load** milestone per card, toggle vista globale. ⚠️ **la sua sidebar è fuori standard** → vedi TODO in `HANDOFF.md` «Sidebar PM condivisa» |
| ❌ rimandato (Fase 4) | viste salvate/spegnimenti Gantt, drag/resize barre, import/export Excel/CSV, dashboard milestone, stampa |

> DB: tabelle `activity_catalog` + `project_milestones`, **migrazione schema v15**. DTO `ActivityCatalog_DTOs`/`Milestones_DTOs`. Build/tsc/eslint OK.

---

### Commerciale

| Modulo WPF | Web |
|------------|-----|
| **Preventivi** (QuotesHomePage + costing tree) | ✅ Lista con catene di revisione, filtri, vista griglia/cliente, stato inline, PDF, revisione/duplica/elimina; **dettaglio SERVICE** (prodotti/varianti editabili, contenuti automatici, totali) e **IMPIANTO** (costing tree: sezioni costo+risorse+trasferte, materiali, scheda prezzi, tabella distribuzione prezzo con pin/shadow/ridistribuisci) |
| **Cat. Preventivi** (QuoteCatalogPage) | ✅ Albero listini→gruppi→categorie→prodotti→varianti (accordion, natural sort, ricerca), CRUD gruppo/categoria/sotto-categoria/prodotto, sposta (drag&drop), tabella prodotti (varianti espandibili, filtri colonna jolly, range prezzo/costo, auto-include), **editor descrizione TinyMCE 5 self-hosted** con upload immagini. Manca: import Excel |
| **Gamma Robot** | ❌ (sottosistema a sé, fuori Fase D) |
| Conversione preventivo → commessa | ✅ `ConvertQuoteDialog` (scelta PM → commessa) dalla lista, su IMPIANTO accettati |

---

### Gestione

| Modulo WPF | Web |
|------------|-----|
| **Clienti** lista + edit inline + create/delete | ✅ DataTable (ricerca, ordinamento, «Colonne», selezione), crea/modifica (tutti i campi), disattiva. Manca import Easyfatt |
| **Fornitori** idem | ✅ DataTable (ricerca, ordinamento, «Colonne», selezione), crea/modifica, disattiva |
| **Catalogo articoli** | ✅ **paginazione server-side** (50/pagina), ricerca server con regole jolly, ordinamento per colonna (server), «Colonne» (persistite in localStorage), crea/modifica (tutti i campi + fornitore), elimina (bloccato se in composizione). Manca: import Easyfatt |
| **Codex** generazione/sync | ✅ lista paginata server-side, ricerca globale con regole jolly, ordinamento per colonna (server), «Colonne» (persistite in localStorage), stato + avvio sincronizzazione con polling, **Genera Codice** inline (prefisso → prenotazione/conferma + rif. 201/401 per i 101 via typeahead), modifica descrizione, elimina (admin). Manca: filtri per-colonna (solo ricerca globale) |
| **Composizione Codex** | ✅ selettore tipo (501/601/701), lista compositi, albero distinta ricorsivo, aggiungi (picker Codex/Catalogo con **ricerca server-side** + filtro prefisso server + quantità) e rimuovi componenti (admin), validazione lato server. No drag&drop (add via dialog) |

---

### Amministrazione — ✅ parità col WPF

| Modulo WPF | Web |
|------------|-----|
| **Utenti** CRUD + credenziali + reparti | ✅ tabella (avatar/ruolo/stato/reparti), ricerca, mostra cessati, dialog 4 sezioni (anagrafica, accesso+ruolo+credenziali, reparti resp./primario, competenze), reset password, cessa/riattiva |
| **Permessi** (AuthLevelsPage) | ✅ matrice funzioni × livelli, livello minimo + comportamento inline, crea/elimina funzione, ricerca |

---

### Gestione avanzata — ✅ parità col WPF

| Modulo WPF | Web |
|------------|-----|
| **Config sezioni** albero drag-drop fasi/template/reparti/tariffe | ✅ Albero gruppi→sezioni, reparti trascinabili, fasi CRUD/reorder, dialog «Nuova sezione» completo (tipo, ordine, default commessa/preventivo, reparti) |
| **Conf. DDP** destinazioni + stati | ✅ CRUD + preset colori + anteprima live |
| **Aggregazioni DDP** matrice | ✅ matrice stati×A1–A8 + edit nome/descrizione |
| **Backup** | ✅ (ADMIN) — backup/restore/download/elimina, mostra path del backup di sicurezza post-restore |
| **Template commesse** rename, move, copy | ✅ Tree unificato, taglia/copia/incolla (Ctrl+X/C/V), F2/Canc, upload multiplo con filtro estensioni |

> Nota: né WPF né Web hanno un drag&drop di riordino manuale nei template (il `sort_order` è gestito lato server).

---

### Globale (stato misto)

- Notifiche badge + polling — ✅ campanella nell'header: badge non-letti, polling adattivo 30/60/120s, popover con lista (severità, tempo relativo, vai-alla-commessa, segna letta/tutte, elimina) + check-pending al login. *Verificato a runtime 25/06/2026.*
- Cambio password obbligatorio al login — ⚠️ stub (`LoginPage` rileva `mustChangePassword`, manca il dialog)
- Danea / Easyfatt import (anagrafiche) — ❌ assente
- Preview file: PDF / immagini / Office ✅ · CAD ❌
- Integrazione SignalR: chat ✅, DDP ✅ (`/hubs/project`), risorse ⏸ (hub pronto, UI rinviata)

---

## Roadmap consigliata (impatto operativo)

### Fase A — Commessa usabile ✅ FATTA (24/06/2026)
1. ✅ Lista commesse ricca + crea/modifica/annulla/elimina (dialog, transizioni stato, quote-lock)
2. ✅ Documenti: lista/breadcrumb, upload multiplo+drag&drop, cartelle, rinomina/sposta/elimina, anteprima
3. ✅ DDP commerciale (lista + crea/modifica/elimina, concorrenza + realtime)
   - Build/tsc/eslint OK; **manca verifica runtime GUI**.

### Fase B — Operatività PM (2 sprint)
4. MoM completo
5. Timesheet calendario mese/settimana
6. Clienti/Fornitori/Utenti CRUD

### Fase C — Core business (grande)
7. ✅ Gestore DDP + SignalR
8. ✅ **Preventivo vs Consuntivo** (24/06/2026) — tab dedicato con conto economico + prev/assegn/cons + fasi&assegnazioni editabili. NB: l'«editor costing standalone» del WPF è codice morto; il costing è in lettura nel BVA, l'editor vero vive sui Preventivi (Fase D). Build/tsc/eslint/vite OK; runtime: vista+order-price verificati via GUI, fasi/assegnazioni verificate via contratto API.
9. 🟡 Resource Planner Gantt — **port da `ATEC.Risorse.Web` (Blazor)**, non dal WPF. **Fase 1 FATTA + verificata runtime 30/06** (Gantt lettura + CRUD dialogo + filtri + realtime). Restano: Fase 2 (drag/resize/auto-pan), Fase 3 (dashboard Ferie + CSV), anagrafiche Service/Altre, ed extra backend (presenza online + digest email).

### Fase D — Commerciale + Codex (in corso)
- ✅ **D1 — Layer API** (`quotes.ts`, `quote-catalog.ts` + tipi) (25/06/2026)
- ✅ **D2 — Cat. Preventivi** (QuoteCatalog: albero + dialoghi + editor TinyMCE 5 self-hosted + immagini) (25/06/2026). Manca import Excel. Build/tsc/eslint OK + asset TinyMCE serviti; runtime GUI autenticato da verificare.
- ✅ **D3 — Preventivi lista** (QuotesHomePage: catene di revisione espandibili, filtri tipo/stato/ricerca/colonne, vista griglia o per cliente, stato inline, PDF anteprima/scarica, revisione/duplica/converti/elimina/riattiva) (25/06/2026). tsc/eslint OK; runtime GUI da verificare.
- ✅ **D4 — Dettaglio preventivo SERVICE** (QuoteDetailPage: header, info auto-save, opzioni PDF, prodotti con varianti editabili inline (qtà/costo/K/attiva), contenuti automatici, editor descrizione TinyMCE per riga, sconto/note/totali, picker catalogo, variante locale, PDF) (25/06/2026). tsc/eslint OK; runtime GUI da verificare.
- ✅ **D5 — Costing tree IMPIANTO** (`quote-costing.ts` = 29 endpoint + tipi; editor `CostingTree`: sezioni costo raggruppate con risorse editabili gg/ore/€h/K + trasferte, reparti per sezione, sezioni materiali con righe qtà/costo/K/attiva, scheda prezzi netto→contingency→offerta→margine→finale, **tabella distribuzione prezzo** per-sezione con pesi proporzionali alla vendita, pin/shadow/ridistribuisci + salvataggio batch; gated PM/ADMIN). tsc/eslint OK + verificata a runtime (calcoli esatti). (25/06/2026)
- ✅ **D6 — Convert → commessa**: `ConvertQuoteDialog` (scelta PM → POST convert → naviga alla nuova commessa), agganciato dalla lista sui preventivi IMPIANTO accettati. Fedele al WPF (il dettaglio WPF non espone un bottone convert). (25/06/2026)

> **Fase D completata** (Commerciale: Preventivi + Cat. Preventivi). Gamma Robot resta fuori scope. Build/tsc/eslint OK; **verifica runtime GUI autenticata FATTA (25/06/2026)**: Cat. Preventivi (albero, 721 prodotti reali), lista con catene di revisione/stati/azioni, dettaglio SERVICE (prodotti/varianti), IMPIANTO con costing tree (sezioni/risorse/materiali/scheda prezzi + **tabella distribuzione prezzo**, calcoli corretti), editor TinyMCE — **zero errori console**. Fase D completa, nessun gap residuo (Gamma Robot e import Excel restano fuori scope).
11. ✅ Codex + composizione (19/06/2026)

---

## Come avviare (sviluppo)

```powershell
# Terminale 1
dotnet run --project ATEC.PM.Server

# Terminale 2
cd atec-pm-web
npm run dev
```

Browser: http://localhost:5173

## Come avviare (tutto in uno — come Risorse)

```powershell
dotnet run --project ATEC.PM.Server
```

Browser: http://localhost:5150
