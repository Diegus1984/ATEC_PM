# HANDOFF — Verifica a runtime del refactoring web (27/07/2026)

> **Cosa devi fare:** provare sull'app vera le sei pagine che sono state
> ristrutturate, per confermare che il refactoring non abbia rotto niente.
> Non c'è codice da scrivere, se non i fix di quello che trovi rotto.

---

## 0 · ESITO — verifica eseguita il 28/07/2026 ✅

**Fatta sull'app vera (server .NET in Development + Vite 5173 + JWT coniato). Nessuna
regressione del refactoring trovata: nessun fix necessario.** Dati di test creati e poi
ripuliti; stack spento a fine verifica.

| § | Pagina | Esito |
|---|---|---|
| 3.1 | Pianificazione Risorse | ✅ tutto ok — **sospetto n.1 smentito**: dopo lo zoom «Largo» il drag usa il dayW nuovo (46 px → 2 giorni esatti) |
| 3.2 | DDP Officina | ✅ tutto ok — ogni salvataggio di campo conserva gli altri; popover/menu non si chiudono da soli |
| 3.3 | SAL | ✅ tutto ok — `salRowPayload` non perde campi |
| 3.4 | Inbox Acquisti | ✅ tutto ok — **il picker Danea si chiude senza chiudere il dialog RDO** |
| 3.5 | Lavorazioni | ✅ tutto ok — colonne di default corrette in tutte le viste |
| 3.6 | Costing / Prev vs Cons | ✅ tutto ok — con pin/escludi/«Ridistribuisci» i totali tornano al centesimo |

**Provato in dettaglio:** drag move/resize/create-by-drag + snap Shift 7gg + Esc + Canc con
conferma + Ctrl+F + Shift+rotella + stampa A4 orizzontale + filtri/legenda/pannello risorse ·
edit inline officina (fornitore, trattamento, destinazione+specifica, date, note da dialog),
stepper qtà con conferma «annulla riga» a qtà 1, pezzi prodotti → auto-stato DISP, menu riga,
spunta prezzo Codex a 0 · SAL: riordino drag&drop persistito, Invio verticale, clamp % a 100,
GG saldo che salva subito con anteprima Data prev. saldo, tendine anagrafica + «Aggiungi
nuovo…» da ADMIN, riga «Pagata» bloccata per non-ADMIN, inserisci/elimina riga, avviso Σ% ≠ 100 ·
Acquisti: `/acquisti/legacy` → `/acquisti`, dialog RDO con oggetto precompilato, dettaglio
auto-caricante, prezzo offerta salvato, conferma «Annulla RDO» · Lavorazioni: 5 viste, creazione
rapida, priorità inline, ultra-critica, elimina con conferma, menu «Colonne» · Costing: modifica
giorni risorsa con ricalcolo e salvataggio, distribuzione prezzo (pin, escludi/includi,
Ridistribuisci), BvA: inizializza preventivo, gruppi collassabili, dialog nuova risorsa.

**NON provato di proposito** (avrebbe scritto fuori dall'app o toccato dati reali):
«Componi/Ricomponi Email (Outlook)» (aprirebbe Outlook sulla macchina), «Scegli Vincitore» +
«Genera Ordine Danea» e «Ordina Danea» di commessa (scrivono nell'archivio Danea vero),
«Conferma Tutte» sulle bozze lavorazioni (toccherebbe il legame DDP↔lavorazione).
**Non verificabile in dev:** padre con componenti importati in DDP Officina — non esiste
nessuna riga con composizione nel database di sviluppo.

### Osservazioni emerse, ma NON causate dal refactoring

1. ✅ **RISOLTA (28/07/2026) — Stampa Gantt risorse.** *Sintomo:* mentre si stampa il contenuto
   si restringe alla finestra A4, il browser azzera lo scroll orizzontale → il sottotitolo
   stampato riportava il periodo dell'inizio banda (es. «Dicembre 2025 – Gennaio 2026») e a
   stampa finita il Gantt restava all'inizio (le barre stampate erano invece giuste).
   *Fix* in `use-planner-viewport.ts`: `printGantt` salva lo `scrollLeft` **prima** di
   restringere la banda, imposta l'etichetta sulla finestra realmente stampata
   (`labelForRange(printStart, printStart + windowDays − 1)`) e alza `printingRef`, che
   silenzia `updatePeriodLabel` per tutta la stampa; un effect sul ritorno a banda reale
   rimette lo scroll e ricalcola l'etichetta.
   *Verificato a runtime:* con il Gantt su ottobre, il sottotitolo al momento della stampa è
   «Ottobre 2026» (14 giorni dal 7/10) e a stampa finita `scrollLeft` torna esattamente dov'era.
2. ✅ **RISOLTA (28/07/2026) — Prezzo dal dettaglio RDO.** *Sintomo:* il prezzo salvava
   sull'offerta ma non sulla riga di distinta, perché la PUT `/api/projects/{id}/ddp/{row}`
   scriveva `unit_cost` solo con `UpdateCatalogSnapshot` (gli edit inline rimandano il costo
   invariato e non devono sovrascriverlo). *Fix:* nuovo flag esplicito `UpdateUnitCost`
   (`BomItemSaveRequest` + `unit_cost = IF(@UpdateCatalogSnapshot OR @UpdateUnitCost, …)` in
   `ProjectsController.UpdateDdpItem`), acceso dal client solo quando l'override del costo
   c'è davvero (`ddp-commercial-row.ts`; `AcquistiPage` non rimanda più il costo come eco).
   Stesso schema già usato per `UpdateSupplier`. *Verificato a runtime:* prezzo battuto nel
   dettaglio RDO → `bom_items.unit_cost` aggiornato; modifica della sola data prevista →
   costo a DB invariato anche con client disallineato.
3. **SignalR**: `/hubs/project/negotiate` risponde 200 ma l'upgrade WebSocket fallisce nel
   browser headless usato per la verifica («connection stopped during negotiation»). Il
   refactoring non ha toccato `src/lib/signalr/` e il proxy Vite ha `ws: true` → da riprovare
   nel browser vero prima di preoccuparsi.

---

## 1 · Contesto

Il 27/07/2026 il client web è stato riorganizzato: file monolitici spezzati in
moduli, helper duplicati unificati. **Nessun cambio di comportamento voluto** —
è una redistribuzione del codice.

Commit sul ramo `master` (in locale, non ancora su GitHub):

| commit | cosa |
|---|---|
| `0771b1e` | il refactoring (121 file, +16.352 / −7.091) |
| `0abfe70` | commit precedente a `0771b1e` — riferimento **parziale** per i confronti (vedi avvertenza) |

> ⚠️ **Avvertenza sulla storia git (verificata 28/07/2026).** `0771b1e` è stato committato
> PRIMA di `ad6fad2` («Lavoro applicativo non committato (8-24 luglio)»): il refactoring è stato
> fatto su un albero che conteneva già lavoro non committato. Quindi `0abfe70` **non è uno
> snapshot fedele del "prima"**: moduli interi (es. `atec-pm-web/src/features/acquisti/`) non
> esistono affatto a quel commit e non sono diffabili. Il confronto regge per i file che c'erano
> già (planner risorse, DDP Officina, SAL).

**Verificato:** `tsc --noEmit`, `eslint` (0 errori), `npm run build`, `dotnet build`.
**Non verificato: niente a runtime.** Da qui il tuo compito.

Il refactoring in sintesi (dettaglio nel messaggio di commit di `0771b1e`):

- eliminata la vecchia Inbox Acquisti duplicata; `/acquisti/legacy` ora reindirizza a `/acquisti`
- `lib/api/types.ts` (2541 righe) spezzato in `lib/api/types/*.ts` + barrel — **gli import non cambiano**
- 7 pagine oltre le 1000 righe divise in hook di stato + componenti presentazionali
- 46 helper duplicati accorpati in `lib/date-iso`, `lib/format`, `lib/wildcard`

---

## 2 · Come avviare lo stack

Segui la memoria `verifica_runtime_web_harness` (in `~/.claude/projects/.../memory/`).
I punti che fanno perdere più tempo:

1. **Server**: `dotnet run --project ATEC.PM.Server` dalla root `ATEC_PM/` → porta **5150**.
   ⚠️ **Deve girare in Development**: `$env:ASPNETCORE_ENVIRONMENT='Development'` prima di
   `dotnet run`. In Production scatta `UseHttpsRedirection()`, l'header `Authorization`
   si perde e ottieni **401 su qualunque token** — sembra un problema di autenticazione
   ma non lo è.
2. **Vite**: **non serve avviarlo a mano.** In Development il server .NET lo lancia da solo
   (log `[DevSpa] Avvio Vite`) su **5173** e apre il browser. Se lo si vuole separato,
   `.claude/launch.json` (config `web`, porta 5173) e `start-web.cmd` **esistono** nella root
   `ATEC_PM_CSharp_v5/` — la nota precedente («non esistono più») era sbagliata. Attenzione:
   `preview_start {name:"web"}` con il server già acceso avvia un **secondo** Vite su porta
   random; usare `preview_start {url:"http://localhost:5173"}`.
3. **Login**: l'unico ADMIN (`admin`, id 1) ha hash bcrypt ignoto → coniare un JWT con
   la chiave `Jwt:Key` e iniettarlo in `localStorage` (`atec_pm_token` + `atec_pm_user`
   con `userRole:'ADMIN'`), poi reload. Procedura completa nella memoria citata.
   Per provare i blocchi «non-ADMIN» basta lo stesso token con `role: "PM"` e
   `atec_pm_user.userRole = 'PM'` (la UI legge il ruolo da lì).
4. **Guidare la GUI**: i componenti Radix (Select, Tabs, DropdownMenu, AlertDialog) spesso
   non reagiscono a `el.click()` da JavaScript → usare il click reale del Browser MCP, oppure
   la sequenza sintetica `pointermove → pointerdown → pointerup → click` sulle coordinate
   del centro dell'elemento (funziona su tutti i Radix).
5. **GOTCHA che fa perdere ore (28/07/2026)**: se il pannello Browser non è visibile,
   `document.hasFocus()` è `false` e **Chrome non emette focus/blur** → tutti i campi che
   salvano `onBlur` (SAL, note DDP, specifica destinazione, prezzi RDO…) sembrano rotti: lo
   stato React si aggiorna ma la PUT non parte mai. **Rimedio: un `computer{action:"key",
   text:"Tab"}`** dà il focus al documento; da quel momento `el.blur()` funziona.
   **Va rifatto dopo ogni navigazione.** Senza pane visibile gli screenshot e le azioni mouse
   di `computer` restano comunque bloccati: si lavora con `javascript_tool` + `read_page` +
   `read_network_requests`.

> **Regola del progetto: a fine verifica SPEGNI server e Vite.** Se restano su
> 5150/5173 Diego non può provare a mano sulle stesse porte.
> **Pulisci i dati di test** che crei (righe DDP, commesse, allocazioni…).

---

## 3 · Cosa provare, pagina per pagina

Per ognuna: cosa toccare, cosa deve succedere, e **il sospetto numero uno** se si rompe.
Sono ordinate per rischio decrescente.

### 3.1 Pianificazione Risorse — `/risorse`
File: `features/risorse/ResourcePlannerPage.tsx` + `use-planner-drag.ts`, `use-planner-viewport.ts`,
`use-planner-rows.ts`, `use-planner-data.ts`, `planner-gantt.tsx`, `planner-geometry.ts`,
`planner-toolbar.tsx`, `planner-side-panel.tsx`

| prova | atteso |
|---|---|
| trascinare una barra | si sposta, al rilascio salva e resta dov'è |
| trascinare tenendo **Shift** | scatti di 7 giorni |
| trascinare i bordi della barra | ridimensiona inizio/fine |
| trascinare su una riga vuota | crea una nuova allocazione (causale = primo tipo attivo in legenda) |
| trascinare fino al bordo dello schermo | il Gantt scorre da solo (auto-pan) |
| selezionare una barra e premere **Canc** | chiede conferma ed elimina |
| **Esc** durante un trascinamento | annulla, la barra torna al posto |
| **Ctrl+F** | focus sulla casella di ricerca |
| **Shift + rotella** | scorrimento orizzontale |
| tasto **Stampa** | anteprima A4 orizzontale con il periodo visibile e il logo |
| zoom Compatto/Normale/Largo, filtri, pannello risorse, «In lista» | invariati |

**Sospetto n.1:** tutto il drag e le scorciatoie ora vivono in `use-planner-drag.ts`, che
monta i listener **una volta sola** e legge i valori vivi tramite ref. Se il drag usa una
larghezza-giorno sbagliata dopo aver cambiato zoom, il colpevole sono quelle ref.

### 3.2 DDP Officina — commessa → tab «DDP Officina»
File: `features/commesse/ProjectDdpOfficina.tsx` + `use-officina-row-mutations.ts`,
`officina-columns.tsx`, `officina-shared.ts`, `OfficinaDialog.tsx`, `OfficinaProducedCell.tsx`,
`use-codex-price-check.ts`

| prova | atteso |
|---|---|
| modificare **in griglia**: fornitore, trattamento, note, N° ordine, date, destinazione + specifica | salva e resta; **si disabilita solo la cella in scrittura**, non tutte |
| stepper quantità (solo stato «Da ordinare») | +/− con la conferma prevista |
| pezzi prodotti | commit su blur e su Invio |
| menu ⋮ → Annulla riga / Elimina definitivamente | conferme corrette (Elimina solo ADMIN/PM) |
| doppio clic su una riga → dialog | campi corretti, salvataggio ok |
| aprire il dialog su un articolo con prezzo 0 nel Codex | compare la spunta «Aggiorna prezzo in Anagrafica Codex» |
| padre con componenti importati | collasso/espansione, costo del padre = somma dei figli |

**Sospetto n.1:** le 10 mutation gemelle sono diventate una sola `useFieldMutation`
istanziata per campo. Se salvando un campo se ne azzera un altro, guarda `toForm()` +
la patch in `use-officina-row-mutations.ts`. Se invece i popover/menu si chiudono da soli
mentre scrivi, le colonne si stanno ricostruendo a ogni render: dipendenze del `useMemo`
in `ProjectDdpOfficina.tsx` e memoizzazione dell'oggetto `mutations`.

### 3.3 SAL — commessa → tab «SAL» (e `/sal`)
File: `features/commesse/ProjectSal.tsx` + `sal-row.tsx`, `use-sal-row-editing.ts`,
`sal-sheet-shared.ts`, `sal-sheet-fields.tsx`, `sal-sheet-head.tsx`, `sal-sheet-toolbar.tsx`,
`sal-new-row.tsx`

| prova | atteso |
|---|---|
| riordinare le righe col trascinamento (manico a sinistra) | l'ordine si salva |
| **Invio** dentro Step / % / N° fattura / GG saldo | il focus scende sulla stessa colonna della riga sotto e il valore si salva |
| % SAL fuori scala (es. 150) | si limita a 100 |
| GG saldo con le frecce dello spinner | salva subito; «Data prev. saldo» si aggiorna in anteprima |
| Conto SAP / Condizioni / Pagamento | tendina, e da ADMIN la casella «Aggiungi nuovo…» crea la voce |
| riga con Pagamento = «Pagata», da utente non-ADMIN | riga bloccata (lucchetto) |
| menu ⋮ → Inserisci sopra/sotto, Elimina | ok, la conferma compare solo se la riga ha dati |
| totali in fondo + avviso «Σ% ≠ 100» | corretti |

**Sospetto n.1:** il payload di riga ora è `salRowPayload(row, patch)` in `sal-sheet-shared.ts`.
Se salvando un campo se ne perde un altro, è lì che guardare.

### 3.4 Inbox Acquisti — `/acquisti`
File: `features/acquisti/` (tutto) — in particolare `RfqDetailDialog.tsx`, `CreateRfqDialog.tsx`

| prova | atteso |
|---|---|
| «Richiedi RDO» da riga e da commessa | il dialog si apre con gli articoli e l'oggetto precompilato |
| creare la RDO | si apre subito il dettaglio della RDO creata |
| nel dettaglio: prezzo unitario e data prevista sulle righe | salvano (griglia inbox aggiornata) |
| «Collega articolo» (picker Danea) | **chiudendo il picker NON deve chiudersi anche il dialog RDO** |
| «Componi Email (Outlook)» | si apre Outlook precompilato, la RDO passa a Inviata |
| «Scegli Vincitore» → «Genera Ordine Danea» | ordine creato, righe a «In ordine» |
| «Annulla RDO» | solo su RDO aperte |
| «Ordina Danea» di commessa | gruppi per fornitore, ordine multi-RDO |
| `/acquisti/legacy` | reindirizza a `/acquisti` |

**Sospetto n.1:** il dialog RDO ora è autonomo (si carica la RDO da solo con
`["purchase-rfq-detail", rfqId]`). Se dopo aver scritto un prezzo il dettaglio non si
aggiorna, è l'invalidazione di quella chiave dalla pagina.

### 3.5 Lavorazioni — `/work-requests` e tab in commessa
File: `features/commesse/ProjectWorkRequests.tsx` + `features/work-requests/wr-*.ts(x)`

Provare **tutte e cinque le viste** (Bozze, Priorità, Consegne, Trattamenti, Commessa):
modifiche inline (descrizione, note, date, priorità, stato, trattamento), creazione rapida
in fondo alla griglia, form bozze con commessa INTERNA, «Conferma Tutte», menu ⋮
(ultra-critica, conferma/riapri trattamento, elimina), menu «Colonne».

**Sospetto n.1:** i default delle colonne per vista sono in `wr-shared.ts`
(`defaultVisibleColumnsFor`). Se una vista mostra colonne sbagliate, è lì.

### 3.6 Preventivi / Costing e Preventivo vs Consuntivo
File: `features/preventivi/costing-*.ts(x)`, `CostingTree.tsx`; `features/commesse/bva-*.tsx`,
`ProjectBudgetVsActual.tsx`

Costing: aggiungere sezione costo/materiali/risorsa, modificare giorni-ore-costo-K,
scheda prezzi (contingency, margine) e soprattutto la **tabella Distribuzione prezzo**
(modifica %, lucchetto, occhio per escludere, «Ridistribuisci»: i totali devono tornare).

Preventivo vs Consuntivo: gruppi collassabili, dialoghi risorsa/materiale/sezione,
conto economico (Order price, trasferta a consuntivo), scheda prezzi.

---

## 4 · Se qualcosa è rotto

1. Confronta col codice pre-refactoring:
   `git show 0abfe70:atec-pm-web/src/features/<percorso vecchio>`
   (i file vecchi hanno nomi diversi: es. tutto il planner stava in `ResourcePlannerPage.tsx`).
2. Correggi **nel modulo nuovo**, non ripristinando il file monolitico.
3. Il refactoring non doveva cambiare comportamento: se trovi una differenza, è un bug
   del refactoring, non una scelta — tranne le due qui sotto, che sono volute.

**Differenze volute (non sono bug):**
- La richiesta offerta via mailto **non parte** se il testo supera 1800 caratteri: compare
  un errore che invita a ridurre le note o spezzare la RDO. Prima partiva e Windows la
  troncava in silenzio.
- Dopo aver cambiato costo/data dal dialog RDO, il dettaglio si aggiorna per invalidazione
  della query invece che con un refetch diretto.

---

## 5 · Fuori scope

- Il push su GitHub (credenziali dell'account sbagliato sulla macchina: 403).
- `debug_template_api.py` modificato e non committato.
- Nuove funzionalità: qui si verifica soltanto.

---

## 6 · Cosa resta da fare dopo la verifica del 28/07/2026

Il refactoring è considerato **verificato** e le due anomalie pre-esistenti del §0 sono state
**sistemate e riverificate a runtime** (stesso giorno). File toccati dai fix — da committare:
`ATEC.PM.Shared/DTOs/Bom_DTOs.cs`, `ATEC.PM.Server/Controllers/ProjectsController.cs`,
`atec-pm-web/src/lib/api/types/ddp.ts`, `atec-pm-web/src/features/commesse/ddp-commercial-row.ts`,
`atec-pm-web/src/features/acquisti/AcquistiPage.tsx`,
`atec-pm-web/src/features/risorse/use-planner-viewport.ts`
(`dotnet build`, `tsc --noEmit` ed `eslint` sui file toccati: 0 errori).

Restano aperti, come attività separate:

- Provare a mano i flussi esclusi dalla verifica: email RDO via Outlook, «Scegli Vincitore» →
  «Genera Ordine Danea», «Ordina Danea» di commessa, «Conferma Tutte» sulle bozze lavorazioni.
- Ricontrollare nel browser vero il real-time SignalR (`/hubs/project`).
- Una stampa vera del Gantt (con dialogo di sistema) per confermare l'intestazione: nella
  verifica `window.print()` era sostituita da uno stub per non aprire il dialogo.
- Il push su GitHub, quando le credenziali saranno a posto.
