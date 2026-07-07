# HANDOFF — Client Web ATEC PM (`atec-pm-web`)

> **Leggi questo file per primo** se riprendi il lavoro sul client web in una nuova
> chat. È l'indice di stato + le regole + i prossimi passi. Aggiornato al **06/07/2026** (Fase A — Commessa usabile + Fase C#8 — Prev vs Consuntivo + Fase D — Commerciale + **dettaglio commessa completo a 8 sezioni** + **Modulo Milestone** — anagrafica attività, tab Milestone + Gantt, pagina globale `/milestones`: completate) + **Sidebar PM condivisa** (06/07/2026, FATTA) + **Modulo SAL / Fatturazione** (07/07/2026: tab commessa, anagrafica condizioni, pagina PM globale `/sal` con Prospetto SAL, warning in campanella — verificato build/tsc/eslint).

## Cos'è

Client **React 19 + Vite 7 + TypeScript + shadcn (preset radix-vega)** che consuma
l'API ASP.NET Core esistente (`ATEC.PM.Server`). Sostituirà progressivamente il
client WPF (`ATEC.PM.Client`), che resta il client operativo completo finché i gap
non sono chiusi.

- **Percorso:** `C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\atec-pm-web`
- **Non è ancora in git** (cartella `atec-pm-web/` untracked sul branch `master`).

## Documenti di riferimento (fonti di verità)

| File | A cosa serve |
|------|--------------|
| **HANDOFF.md** (questo) | Punto d'ingresso: stato, regole, prossimi passi |
| [WEB-MIGRATION.md](WEB-MIGRATION.md) | **Stato modulo per modulo** (web vs WPF) + roadmap |
| [BLOCKS-RULES.md](BLOCKS-RULES.md) | **Regole layout pagine**: fedeltà ai blocchi shadcn, recipe copia-incolla |
| [DESIGN-RULES.md](DESIGN-RULES.md) | Tema/preset/token (preset `bIkeymG`, radix-vega, neutral, Inter, radius 0.625rem) |
| [README.md](README.md) | Avvio rapido e struttura cartelle |

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
- **Timesheet** (calendario mese/settimana fedele al WPF: chip ore per tipo, badge giornaliero, selezione+doppio-click, menu destro modifica/elimina, selettore dipendente PM/RESP, date future bloccate, dialog con date picker + validazione 24h live)
- **PM → Verbali (MoM)** — **gestione v9** (02/07/2026, prototipo `Gestione_MoM_v9.html`, verificata a runtime): pagina **Note MoM** (`/mom/note`, acquisizione rapida per utente → «Assegna» porta il testo nel campo Azione del verbale), lista con colonna Rev. e **doppia visualizzazione Tabella|Card come il Gestore DDP** (card raggruppate per commessa + Riunioni, scelta persistita per utente), **dettaglio a foglio editabile** (celle inline autosave, autocomplete definizioni, Invio su Azione=nuova riga, righe colorate+gruppi, riordino drag&drop su `sort_order`, giorno settimana/festivi sulle date), **revisioni** (cambio data riunione confermato ⇒ Rev+1, storico `mom_revisions`, regola server-side), export Stampa/Word/Excel/CSV, **realtime SignalR** (`MoMChanged`) + concorrenza `row_version`
- **PM → Gestore DDP** (gruppi espandibili per commessa + card Commerciale/Officina con realtime SignalR; sintesi completa: 7 KPI, Stati Avanzamento, accordion ripartizione/consegne/top10/destinazioni/mancanti/distinta/feedback, stampa per sezione+Excel)
- **PM → Check list** (`/checklist`, prototipo `Gestione_PM_V3.html`) — raccoglitore/generatore di check list: **board** con card per commessa (agganciate al `project_id` reale) e per **gruppo generico** (DIREZIONE/VARIE/custom, CRUD gruppo); tabella attività editabile inline (priorità **P0–P3** 0=Critica, scadenza con dow/festivi, **badge giorni**, flag critico, **riprogrammazione ±gg**, ordina per priorità/data, nuova riga); **viste Priorità aggregate** su tutte le commesse/gruppi; **inbox «Fissa attività»** personale (`checklist_inbox`) → «Assegna a…» commessa/gruppo; **realtime SignalR** (`ChecklistChanged`, gruppo `checklist-all` + `project-{id}`) + concorrenza `row_version`. Tab **Check list** anche nel dettaglio commessa (`ProjectChecklist`). Tabelle `checklist_groups`/`checklist_items`/`checklist_inbox`. Export rimandato. *Server compila, tsc/eslint web puliti; **verificato a runtime 02/07/2026**: API end-to-end (board/CRUD item, concorrenza `row_version`, vincolo XOR, inbox→assegna) + GUI (crea gruppo, aggiungi attività, priorità P0–P3, badge scadenza).*

- **PM → Milestone** (`06/07/2026`, port dal prototipo `Gestione_Commesse_V31.html`; parte fatta in questa chat, Gantt implementato da Antigravity su spec `MILESTONE-GANTT-SPEC.md`) — pianificazione a milestone di commessa:
  - **Anagrafica attività** = catalogo globale seedato (`/anagrafica-attivita` in Gestione avanzata): tabella con add/rinomina inline/riordino drag&drop/disattiva/«Ripristina standard». Tabella `activity_catalog` + controller `/api/activity-catalog`.
  - **Tab Milestone** (9ª sezione del dettaglio commessa, `ProjectMilestones`): tabella editabile inline (date con dow/festivi + regola fine≥inizio, avanzamento a barra, note, spegni/evidenzia, **drag&drop** + «inserisci sopra/sotto», textarea auto-cresce), header n°/avanzamento medio/periodo, **colori riga nella tenuità del Check list** (teal=completata/blu=in corso/rosso=in ritardo), **toggle Tabella|Gantt** persistito, **realtime SignalR** (`MilestonesChanged` su `project-{id}`) + `row_version`. Colonne settimana/W.Tot e stato = derivati client.
  - **Precarico dal catalogo** alla **creazione commessa** (checklist «Attività da precaricare» nel `ProjectDialog` → `/api/milestones/project/{id}/seed-from-catalog`, copia snapshot).
  - **Gantt** (`MilestoneGantt` + `milestones-gantt.css`, riusa `features/risorse/planner-logic.ts`): barre inizio→fine con riempimento avanzamento, linea oggi, weekend/festivi, mesi/settimane ISO, zoom, **filtro date con `DateField` standard** (regola Al≥Dal), combo **«Righe»** = `ColumnsMenu` per nascondere righe (persistite per progetto). Verificato a runtime 06/07.
  - **Pagina globale Milestones** (nav PM, `/milestones`, `MilestonesPage`): **sidebar PM condivisa** (`PmSidebar` — solo commesse con milestone attive, pallini di stato + conteggio), lazy-load milestone per card, toggle vista.
  - DB: tabelle `activity_catalog` + `project_milestones`, **migrazione schema v15**. DTO `ActivityCatalog_DTOs`/`Milestones_DTOs`. Build/tsc/eslint OK.

- **PM → SAL / Fatturazione** (07/07/2026, dal prototipo `Gestione_Commesse_V31.html`, implementato da Antigravity su spec `SAL-SPEC.md`/`SAL-PAGE-SPEC.md`, verificato da Claude) — piano di fatturazione a stati d'avanzamento:
  - **Tab SAL** nel dettaglio commessa (`ProjectSal`): header cliente/valore, tabella step editabile inline (%, condizioni pagamento, ipotesi fatturazione, stato fattura), importo derivato, **barra avanzamento incasso**, **semaforo warn/pre** fedele al prototipo, drag riordino, «Precarica modello standard», elimina con `useConfirm`, realtime `SalChanged` + concorrenza `row_version`.
  - **Anagrafica condizioni pagamento** in Gestione avanzata (`/admin/sal-conditions`, `SalConditionsPage`).
  - **Pagina PM globale** (`/sal`, `SalPage`): `PmSidebar` con commesse (dots warn/pre, conteggio ipotesi aperte) + viste rapide «Tutte le commesse» e **«Prospetto SAL»** (prime 2 ipotesi aperte per commessa, badge Scaduto/Pre-warning/In programma). Le card riusano `ProjectSal`.
  - **Warning fatturazione** nella campanella (`CheckSalDeadlines`, `SAL_DUE`/`SAL_ROW`, destinatari PM+ADMIN).
  - DB: `sal_conditions`/`project_sal`/`sal_rows`, **migrazioni v16+v17**; `SalController` (`/api/sal`), DTO `Sal_DTOs`. Build/tsc/eslint OK; **runtime GUI da verificare**.

- **Commesse (Fase A)** — lista paginata server-side (ricerca, «Colonne», «Nuova commessa») + `ProjectDialog` crea/modifica (codice auto, lookup cliente/PM, transizioni di stato, quote-lock, regola date inizio/fine), annulla (soft) ed elimina definitivo (ADMIN, doppia conferma); **dettaglio** con tab Panoramica + **Documenti** (cartella+breadcrumb, upload multiplo+drag&drop, cartelle, rinomina/sposta/elimina, anteprima PDF/immagini/Office) + **DDP commerciale e officina** (inserimento da picker Catalogo/Codex fedele al WPF — doppio clic, +1 su duplicato, «Nuovo codice Codex», scroll infinito, header sticky; lista/modifica/elimina riga, concorrenza ottimistica, realtime SignalR). *Build/tsc/eslint OK; inserimento DDP verificato a runtime.* **Colonna Destinazione con combo da Conf. DDP** (03/07/2026, parità WPF): menu ⋮ inline sulla cella (come Stato, `DdpDestinationMenu`) + Select nei dialog di modifica, opzioni da `/api/ddp-destinations/active` con regola «mantieni il valore corrente anche se non più attivo» (`ddp-destination-options.ts`); voce «Rimuovi destinazione»/(nessuna) per azzerare. *Verificato a runtime su entrambe le distinte.*

- **Commesse → Prev vs Consuntivo (Fase C#8)** — tab dedicato nel dettaglio commessa, gated PM/ADMIN: conto economico (offerta/order price/budget vs consuntivo/redditività/avanzamento/tecnici/fasi), gruppi→sezioni IN_SEDE/DA_CLIENTE con preventivo·assegnato·consuntivo e delta colorati, materiali, scheda prezzi; **fasi & assegnazioni** editabili (aggiungi/rimuovi tecnico, ore pianificate inline, importa fasi da template, fase locale), edit inline order price e trasferta consuntivo. *Build/tsc/eslint/vite OK; runtime: vista lettura + order price verificati via GUI, fasi/assegnazioni via contratto API.* NB: il «costing editor» standalone del WPF è codice morto — il costing vero è sui Preventivi (Fase D).

- **Commesse → dettaglio completo (8 sezioni)** — tutte le sezioni del WPF sono attive: Dettagli, **Flusso di Cassa** (`ProjectCashFlow`), Preventivo vs Consuntivo, **Chat** (`ProjectChat` + cartella `chat/` con composer/allegati/mentions/partecipanti + realtime SignalR `use-project-chat-hub`), **Verbali (MoM) per commessa** (`ProjectMoM`, lista filtrata per commessa + crea verbale `COMMESSA`), DDP Commerciali, DDP Officina, Documenti.

- **Notifiche** — ✅ centro notifiche nell'header: campanella + badge non-letti con **polling adattivo** (30/60/120s, fedele a `NotificationPollingService` WPF, pausa senza focus via react-query), popover con lista (icona severità, tempo relativo IT, vai-alla-commessa, segna letta / «segna tutte lette», elimina) + `check-pending` al login. *tsc/eslint OK; **verificato a runtime 25/06/2026**: badge=4, popover con 4 notifiche reali, zero errori console.*

🟡 **Gap trasversali ancora aperti (non legati a una singola pagina):**
- **Cambio password obbligatorio al login** — stub: `LoginPage` rileva `mustChangePassword` ma manca il dialog ("implementare dialog nel passo 2").
- **Import Easyfatt/Danea** per Clienti/Fornitori/Catalogo articoli.
- **Dashboard**: link rapidi + MoM in dashboard (minore).

- **Commerciale → Cat. Preventivi (Fase D2)** — albero listini→gruppi→categorie→prodotti→varianti (accordion, natural-sort, ricerca), CRUD gruppo/categoria/sotto-categoria/prodotto, sposta drag&drop, tabella prodotti (varianti espandibili, filtri colonna jolly, range prezzo/costo, auto-include), **editor descrizione TinyMCE 5 self-hosted** (asset copiati in `public/tinymce/`, upload immagini su `/api/quote-catalog/products/upload`, path relativi `/uploads/cms/`). *Build/tsc/eslint OK + asset TinyMCE serviti; runtime GUI autenticato da verificare. Manca import Excel.*

✅ **Fase D completata (Commerciale):** D1 layer API (`quotes.ts`/`quote-catalog.ts`/`quote-costing.ts`) · D2 Cat. Preventivi (TinyMCE) · D3 lista preventivi (catene revisione, filtri, stato inline, PDF) · D4 dettaglio SERVICE (prodotti/varianti editabili) · D5 costing tree IMPIANTO (sezioni/risorse/materiali/scheda prezzi + tabella distribuzione prezzo con pin/shadow/ridistribuisci) · D6 convert→commessa. Build/tsc/eslint OK; **runtime GUI autenticata verificata (25/06): Cat. Preventivi, lista (catene revisione), dettaglio SERVICE + IMPIANTO con costing tree, TinyMCE — zero errori console**.

✅ **Risorse/Gantt — sostanzialmente completo (port da `ATEC.Risorse.Web` Blazor, non dal WPF), verificato runtime 01/07/2026.** Gantt completo (header mesi/giorni, lane-packing, barre OP/FLEX/FERIE, weekend/festività/oggi, conflitti), filtri + zoom + pannello risorse + filtro "In lista" (persistenza localStorage), CRUD via `AssignmentDialog` (crea multi/modifica/duplica/elimina), **drag/resize/create-by-drag** (auto-pan, snap settimana, concorrenza ottimistica con messaggio 409 dedicato), scorciatoie tastiera (Ctrl+F/Canc), Shift+rotella, stampa Gantt, collassa pannello/colonna nomi, **presenza online** (pallino verde/rosso via SignalR), **dashboard Piano ferie** (`/risorse/ferie`), **digest email** (badge "modifiche da notificare", dialog "Notifica subito", pannello admin Config SMTP+scheduler in Gestione avanzata → Digest Email). File in `src/features/risorse/` + `src/features/admin/digest/`, API `src/lib/api/resource-planner.ts`+`digest.ts`+`settings.ts`. **Resta solo**: editor anagrafiche Service/Altre attività (bassa priorità, non blocca il flusso).

⛔ **Da fare (placeholder):** Gamma Robot

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
