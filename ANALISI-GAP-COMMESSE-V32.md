# Confronto `Gestione_Commesse_V32.html` → ATEC PM

## 1. Verdetto

Su **286 funzionalità** censite nel prototipo: **106 già fatte** (spesso meglio dell'originale), **120 parziali** (la funzione c'è, manca un pezzo), **60 assenti** — il prototipo è coperto per circa due terzi, e ciò che manca davvero si concentra in 4 aree: **Gestione Trasferta** (modulo inesistente), **Bilancio / Ordine commessa**, **import-export CSV e Excel**, **viste salvate + export Excel del Gantt**.

## 2. Quadro per modulo

| Modulo | Già fatto | Parziale | Assente | Tot |
|---|---:|---:|---:|---:|
| Milestones — tabella di commessa | 18 | 8 | 6 | 32 |
| Gantt di commessa | 9 | 14 | 5 | 28 |
| Import / Export / Stampa | 6 | 13 | 13 | 32 |
| Dashboard + anagrafiche (commesse, attività, personale) | 19 | 14 | 6 | 39 |
| Gestione Trasferta | 3 | 16 | 11 | 30 |
| Bilancio commessa | 7 | 13 | 9 | 29 |
| Calcolatrici di costo (RAC/MAC/LAC/SPC) | 4 | 24 | 2 | 30 |
| Pagamenti SAL (foglio + anagrafiche) | 20 | 9 | 4 | 33 |
| SAL analitiche (Warning, Prospetto, Cash Flow, Analisi) | 20 | 9 | 4 | 33 |
| **Totale** | **106** | **120** | **60** | **286** |

---

## 3. COSA MANCA DAVVERO

### Gestione Trasferta — il modulo non esiste

**IMPATTO ALTO**

- **Pagina «Gestione Trasferta» + modello dati (ASSENTE)** — Nel prototipo: una pagina con una card per commessa (Giorni / Ore / Costi personale / Costi trasferta) e, dentro, N «step trasferta» con righe per persona salvate sulla commessa. In ATEC PM: nessuna rotta, nessuna tabella `travel_*`, nessuna voce di menu. Serve: tabelle `travel_steps` + `travel_step_rows`, controller CRUD, pagina con sidebar PM come Milestones/SAL.
- **Griglia riga-persona a 14 colonne (ASSENTE)** — Nel prototipo: Nominativo, Inizio, Fine, Giorni, Ore, Costi Personale · Notti, Prezzo, Costo, Vitto · Indennità, Auto, Treno/aereo, con intestazioni raggruppate. In ATEC PM la cosa più vicina è la card risorsa di preventivo (Giorni/Ore-g/€-h/K), che non ha né date né notti né treno/aereo. Serve costruirla come foglio a righe (pattern SAL già pronto).
- **Calcolatrice Ore Trasferta (ASSENTE)** — Nel prototipo: righe «Giorni × Ore Lav.» sommate nel campo Ore, con dettaglio salvato. In ATEC PM c'è solo `WorkDays × HoursPerDay`, monoriga: non si può fare 3 gg × 8h + 2 gg × 10h.
- **Tabella «Riepilogo Trasferta» (ASSENTE)** — Nel prototipo: una riga per ogni nominativo distinto di tutti gli step + riga «Totale Riepilogo». Nessun equivalente.
- **Meccanica generica della calcolatrice (ASSENTE)** — Nel prototipo: una sola finestra riusata da 4 modalità via `CALC_CFG`, con Invio = nuova riga, totale live, conferma che scarta le righe vuote e persiste il dettaglio. In ATEC PM non esiste nessuna finestra di calcolo a righe né la persistenza del dettaglio: i campi trasferta sono valori finali digitati. **È il mattone da fare per primo se si porta il modulo.**
- **Calcolatrice Vitto/Diaria (PARZIALE)** — Nel prototipo la diaria si sceglie da un elenco di valori e il totale è Σ(giorni × diaria). In ATEC PM la formula esiste (`daily_food`) e i valori sono già a DB (`tariff_options` DAILY_FOOD 25/50/80) ma **non c'è nessuna UI che li usi**: il campo «Vitto/g» è testo libero. Serve il picker + la calcolatrice multi-riga.
- **Anagrafiche di valori (diarie, indennità, €/km) (PARZIALE)** — `TravelTariffsController` esiste già con GET/POST/DELETE, controllo duplicati e blocco cancellazione se il valore è in uso; **nessun file del web lo chiama**. Serve solo la pagina/dialog di gestione + il PUT per modificare l'importo.
- **Giorni trasferta = fine − inizio + 1 (PARZIALE)** — Il calcolo esiste identico in `planner-logic.ts` (`dayCount`) ma vive solo nel planner Risorse: le righe di costo non hanno date, i giorni sono digitati a mano. Serve collegarlo alle righe di trasferta.
- **Sincronizzazione Trasferta → Bilancio (PARZIALE)** — La metà «Risorse Atec» in ATEC PM è già automatica e strutturale (`BuildActualEmployees` dal timesheet). Manca la metà «Spese Trasferta»: a consuntivo è un solo numero digitato su `projects.actual_travel_cost`, senza dettaglio per step o per persona.

**IMPATTO MEDIO**

- **Toggle sab / dom per riga (PARZIALE)** — `workingDayCount()` esiste ma l'esclusione weekend è cablata su entrambi i giorni e decisa dal tipo FERIE, non da due flag per riga.
- **Calcolatrice Indennità e Calcolatrice Auto (PARZIALE)** — Le formule ci sono ma monoriga (`AllowanceDays × DailyAllowance`, `NumTrips × KmPerTrip × CostPerKm`); mancano le righe multiple e il picker dei valori.
- **Finestra «Costi Personale» con tariffa oraria selezionabile (PARZIALE)** — `tariff_options` non ha un tipo orario; il costo arriva dal reparto. Manca l'elenco di tariffe e l'anteprima «Tariffa × Ore = totale».
- **Costo alloggio = Notti × Prezzo (PARZIALE)** — In ATEC PM l'alloggio è giornaliero (notti = giorni forzatamente): serve il campo notti separato.
- **Step collassabile con descrizione, Riga Totali dello step (8 colonne), Campo Treno/aereo, Controllo di coerenza «giorni del calcolo vs giorni trasferta» (ASSENTI)**.
- **KPI trasferta per commessa (PARZIALE)** — Il totale trasferta di **preventivo** è già calcolato (`BudgetTravelCost`); mancano i KPI «Giorni trasferta» e «Ore trasferta» e il consuntivo di dettaglio.

**BASSO**: tendina nominativo con fallback «(non in anagrafica)», celle data a 3 segmenti, 3 badge di totale per step, riordino step drag&drop, aggiunta/eliminazione step e righe.

---

### Bilancio commessa

**IMPATTO ALTO**

- **Schermata «Bilancio» cross-commessa (ASSENTE)** — Nel prototipo: elenco di card, una per commessa, con 2 KPI di redditività e ingresso al dettaglio. In ATEC PM il confronto economico esiste solo dentro la singola commessa (tab Preventivo vs Consuntivo). Serve una pagina `/bilancio` con la stessa struttura a card già usata in `/sal` e `/milestones`.
- **Tabella «Ordine Commessa» multi-riga (ASSENTE)** — Nel prototipo N righe Ordine / Posizione / Importo, per spezzare un ordine cliente in posizioni. In ATEC PM l'ordine è un solo numero (`projects.revenue` / `project_sal.valore`). Serve tabella righe ordine + Totale Ordine calcolato.
- **Riepilogo Costi a due sezioni voce-per-voce (PARZIALE)** — In ATEC PM il confronto è a KPI («Budget costi» / «Consuntivo costi» con sottotitolo testuale). Manca la tabella affiancata Preventivati | Consuntivati con le 4 voci, il totale e la redditività ripetuti per sezione.
- **Voce «Lavorazioni Officine» con interne / esterne (PARZIALE)** — In ATEC PM non esiste come voce di conto economico: il costo officine finisce dentro `ActualMaterialCost` e `project_work_requests` non ha campi di importo. Il valore però c'è già nella DDP Officina (€ unit × qtà, con esclusione stati A9): serve estrarlo come voce autonoma e separare interne/esterne.
- **Redditività calcolata anche sul PREVENTIVO (PARZIALE)** — ATEC PM calcola solo `(orderPrice − actualTotalCost)/orderPrice`. Manca il lato preventivato, cioè «il preventivo era già in perdita?». **Attenzione**: il `margin` del tab Dettagli usa `totalCost` senza le trasferte, quindi non coincide con la redditività del conto economico — incoerenza pre-esistente da sanare insieme.
- **Sincronizzazione Trasferte → Costi Consuntivati (ASSENTE)** — vedi modulo Trasferta.

**IMPATTO MEDIO**

- **«Totale Vendita» (ASSENTE)** — Importo di vendita da preventivo digitato a mano (rif. CALCOLO G205). In ATEC PM il `FinalPrice` è derivato, non inseribile.
- **Contingency = Ordine − Vendita (ASSENTE)** — Attenzione al falso amico: in ATEC PM «contingency» è una **percentuale di imprevisti** sul costo netto, semantica opposta.
- **Gestione righe ordine (aggiungi / inserisci sotto / elimina con minimo 1 riga) (ASSENTE)**.
- **Card in rosso sotto soglia 20% (ASSENTE)** — In ATEC PM la colorazione scatta solo sul segno, nessuna soglia parametrica.
- **Spunta «In dashboard» + DASH_MAX + chip commesse escluse (ASSENTE)** — nessun flag di visibilità su `projects`.
- **Totale Ordine da somma righe (PARZIALE)**, **Voce Materiali commerciali separabile dall'officina a consuntivo (PARZIALE — la separazione esiste in `ProjectsController` ma non è usata dal conto economico)**, **Voce Spese Trasferta a consuntivo (PARZIALE — un solo numero)**, **riga «Commerciali in DDP» e «Lav. Esterne in DDP» come righe convivibili con righe manuali (PARZIALE)**, **apertura dei calcolatori per voce (PARZIALE — solo lato preventivo)**.
- **Tooltip con la formula del calcolo (PARZIALE)** — La spiegazione in chiaro con sostituzione numerica esiste per il prezzo d'offerta (`bva-economics.tsx`), non per la Redditività; il componente `Kpi` accetta solo una hint statica.

**BASSO**: % redditività a 2 decimali con virgola, aggiornamento incrementale dei totali senza refetch, ergonomia campi (larghezza in ch, Enter → campo successivo).

---

### Calcolatrici di costo RAC / MAC / LAC / SPC

**IMPATTO ALTO**

- **Formula officina interna: Ore × Costo orario × (1 + k/100) (ASSENTE)** — In ATEC PM il k è un **moltiplicatore** (1,450) non una percentuale additiva: le due semantiche non coincidono, va deciso quale tenere.
- **Finestra «Calcolo» agganciata a un campo di riepilogo (PARZIALE)** — Le finestre modali che riscrivono un dato economico esistono (`EconomicEditDialog`, tabella «Distribuzione prezzo» multi-riga con totale), ma sono a valore singolo o su un altro oggetto. Manca la struttura «4 campi, ciascuno con il suo calcolatore a righe».
- **Stesso calcolatore per Preventivati e Consuntivati (PARZIALE)** — In ATEC PM il preventivo è editabile a righe, il consuntivo è derivato e non modificabile: manca la simmetria.
- **Riga RAC = attività con N linee-risorsa (PARZIALE)** — A preventivo 1 riga = 1 risorsa. Il livello a due gradini esiste a consuntivo (`BuildActualEmployees`, che però il web **non renderizza**: `actualEmployees` arriva nel DTO e non viene usato) e nel foglio MoM. Manca ore/tariffa/importo per singola linea.
- **Calcolatore Lavorazioni a due gruppi con totali parziali (PARZIALE)** — vedi voce Lavorazioni del Bilancio.
- **Selettore tariffe orarie condivise (PARZIALE)** — `tariff_options` copre solo km/vitto/hotel/indennità; nessun tipo orario e **nessuna UI**.

**IMPATTO MEDIO**

- **Override manuale dell'importo calcolato (PARZIALE)** — Il pattern «valore digitato = congelato» esiste con tanto di flag DB e lucchetto (`contingency_pinned`/`margin_pinned` nella Distribuzione prezzo), ma applicato alle % di distribuzione, non agli importi di riga.
- **Marcatori di provenienza `src`/`stepId` sulle righe automatiche (PARZIALE)** — Il pattern esiste identico su `project_cashflow_categories.linked_source` (rigenera solo le righe marcate, non tocca le manuali, riga «[M]» non editabile) e su `project_work_requests.ddp_officina_item_id`. Manca solo sulle righe di costo.
- **Tendina risorsa dall'anagrafica (PARZIALE)** — Il picker restituisce **solo le risorse fittizie wildcard** (`AND e.first_name LIKE '[%'`), non deduplica i nomi già usati e ordina in SQL invece che con `localeCompare` italiano.
- **Importo = Ore × Tariffa (PARZIALE)**, **tariffe dedicate Officine interne 40/50 (PARZIALE)**, **riga MAC Costo × (1+k/100) (PARZIALE)**, **conferma che popola/azzera il campo Materiali (PARZIALE)**, **calcolatore SPC a righe Descrizione + Costo (PARZIALE)**.

**BASSO**: subtotale di riga, totale live, chip nome + «+ Risorsa», scorciatoia «Gestisci personale», validazione tariffa duplicata a video, voci di trasferta tipizzate (qui ATEC PM è già più avanti a preventivo), import della riga legacy, scarto righe vuote alla conferma, riordino drag&drop nelle finestre, Invio = nuova riga, «resta sempre una riga vuota», migrazione righe legacy.

---

### Import / Export / Stampa

**IMPATTO ALTO**

- **Export CSV delle milestone (ASSENTE)** — 9 colonne, separatore `;`, BOM UTF-8, nome `milestones_{codice}_{aaaammgg}.csv`. Il formato identico esiste già in `DdpConfigPage`, `SalProspettoView`, `FeriePage`: va estratto in un helper di `lib/` e applicato.
- **Parser CSV completo (ASSENTE)** — Macchina a stati con virgolette, `""` escape, newline dentro i campi. In ATEC PM **non esiste nessun parser CSV** (l'unico `Split(';')` è nell'anteprima documenti, non riusabile).
- **Riconoscimento riga di intestazione + mappatura colonne con fallback posizionale (ASSENTE)** — Nessuna importazione tabellare da griglia esiste: l'import .json legge chiavi fisse.
- **Import da file Excel .xlsx (ASSENTE)** — Il prototipo ha un lettore ZIP+XLSX scritto a mano. In ATEC PM EPPlus è già referenziato lato server ma serve solo all'anteprima documenti: **la strada giusta è un endpoint di import lato server con EPPlus**, non riscrivere il parser nel browser.
- **Import Excel limitato a 4 colonne per titolo + scarto righe legenda (ASSENTE)** — incluso il pulsante «Importa da Excel» in testata commessa.
- **Parsing date italiane in importazione (PARZIALE — BUG)** — `ParseImportDate` usa `DateTime.TryParse` con `InvariantCulture`: **`31/07/2026` fallisce e la data diventa null in silenzio**. Stesso difetto in `CheckListController.cs:804` e `MoMController.cs:970`. Da correggere prima di qualsiasi import CSV/Excel.

**IMPATTO MEDIO**

- **Autodetect del delimitatore `;` / `,` (ASSENTE)**, **normalizzazione intestazioni (lowercase / accenti / spazi) (ASSENTE)**, **decodifica UTF-8 con fallback Windows-1252 e strip del BOM (ASSENTE)** — senza questa un CSV salvato da Excel italiano arriva con le accentate rotte, perché tutti gli import fanno `file.text()`.
- **Conversione dei seriali data Excel (numFmt, base 1900/1904) (ASSENTE)** — EPPlus lo fa già, se l'import passa dal server.
- **Menu unico «Importa/esporta» (PARZIALE)** — oggi le azioni sono sparse: «Importa» in `MilestonesPage`, «Stampa» dentro il Gantt.
- **Export backup .json del modulo (PARZIALE)** — l'import c'è ed è migliore dell'originale; manca l'export. Il backup globale (.zip DB+file) copre «non perdere i dati», non «portarmi via un file leggibile».
- **Stampa raggiungibile dalla vista Tabella (PARZIALE)** — entrambe le stampe vivono solo nel menu del Gantt.
- **Stampa: numerazione righe (PARZIALE — BUG)** — escludendo le righe spente ATEC PM **rinumera 1..N**, quindi la stampa non è più allineata alla griglia; il prototipo conserva l'indice originale.
- **Stampa: avanzamento medio in testata (PARZIALE — BUG)** — usa `avgAvanz(allMilestones)`, che include righe nascoste dal Gantt e non stampate: il valore in testata può non corrispondere al foglio.
- **Avviso «Consenti i popup per la stampa» (PARZIALE)** — `printHtml` con popup bloccati ritorna in silenzio: si clicca Stampa e non succede nulla.

**BASSO**: riga titolo del CSV (CODICE + descrizione), creazione automatica della commessa da import (in ATEC PM si salta di proposito), helper `download()` duplicato in 6 punti, timestamp nei nomi file, parsing avanzamento «85%»/«85,5», suggerimento di backup prima della sostituzione, nota «settimane ISO 8601» nel piè della stampa Tabella, tag file/versione e chip data odierna in testata.

---

### Gantt di commessa

**IMPATTO ALTO**

- **Viste salvate per commessa: «Vista Interna» / «Vista Cliente» (ASSENTE)** — Salvano l'intera composizione (colonne spente + righe spente + range date) sotto due nomi, con stato «salvata / —» nel menu, e si riapplicano con un clic. È la funzione che rende operativo «un Gantt per l'interno, uno ridotto per il cliente». In ATEC PM non esiste nulla: né preset, né persistenza combinata, né tabella lato server.
- **Export Excel (.xls SpreadsheetML) della vista (ASSENTE)** — Workbook con colonne del pannello + una colonna per giorno, bande mesi/settimane mergiate, **barre rese come celle colorate**, colonna avanzamento a blocchi █/░, freeze panes. In ATEC PM gli unici export .xls (MoM, Sintesi DDP) sono tabelle HTML rinominate, non SpreadsheetML.

**IMPATTO MEDIO**

- **Colonna «gantt» spegnibile (ASSENTE)** — Spegnere la timeline lasciando solo la tabella (è ciò che rende utile la Vista Cliente). In ATEC PM `showTimeline` è cablato a `true`.
- **Modalità «Componi» con ⊗ sulle intestazioni (ASSENTE)** — Spegnere colonne e righe cliccando direttamente sul diagramma, invece che dai due menu a tendina.
- **Spegnimento colonne persistito PER COMMESSA (PARZIALE)** — Oggi la chiave localStorage è **globale** (`milestones:gantt:columns`): cambiare le colonne su una commessa le cambia su tutte.
- **Badge contatore degli spegnimenti (PARZIALE)** — Il pulsante «Ripristina (n)» conta solo le righe; colonne spente e filtro date non sono segnalati, quindi non si vede che la vista è «composta».
- **Range «dalla data alla data» (PARZIALE)** — I filtri Dal/Al **non sono persistiti** (stato React, si perdono al refresh); manca il toggle che precompila con il periodo reale della commessa.
- **Clipping delle barre ai bordi dell'intervallo (PARZIALE — BUG)** — Con il filtro Dal/Al attivo una milestone che inizia prima produce una barra con `left` negativo invece di una barra tagliata al bordo. Nella stampa il clipping c'è già (`milestone-print.ts:269-285`), va portato a video.
- **Pulsante «Oggi» (PARZIALE)** — Esiste solo lo scroll iniziale approssimato; manca il pulsante in toolbar (già presente nel planner Risorse e in Ferie, da riusare) e il messaggio «giorno di lavoro fuori dal periodo pianificato».
- **Guard popup bloccati in stampa (PARZIALE)** — come sopra.

**BASSO**: schermata a pieno schermo + ESC, riproporzionamento colonne a timeline spenta, «Anagrafica spegnimenti» come modale unica con stato Visibile/Spenta, reset unico «mostra tutto», editor date a segmenti nel range, banda di calendario che non chiude alla domenica, larghezza giorno adattiva/resize (in ATEC PM c'è lo zoom a 3 livelli, funzione in più), colonna Avanzamento a blocchi █/░, messaggio «tutte le colonne spente».

---

### Milestones — tabella di commessa

**IMPATTO MEDIO**

- **Editor data a segmenti GG / MM / AAAA (ASSENTE)** — Si digita il giorno, il focus salta da solo al mese, poi all'anno (preimpostato), Backspace torna indietro, Invio conferma. In ATEC PM la data si inserisce **solo dal calendario a popover**: compilare molte righe è sensibilmente più lento. È la lacuna UI più sentita del modulo (torna identica in SAL e Trasferta).
- **Evidenzia riga come urgenza (PARZIALE)** — Il dato è persistito (colonna `evidenza`) ma graficamente si colora solo la descrizione in rosso; nel prototipo si campisce **tutta la riga** in rosa con barretta rossa. In una tabella lunga oggi l'urgenza non si vede.
- **Spegni / riattiva riga (PARZIALE)** — In ATEC PM è più robusto (colonna DB condivisa fra utenti) ma la riga spenta **resta visibile** attenuata, mentre nel prototipo sparisce dalla tabella. Da confermare quale comportamento vuoi.
- **Inserisci attività in una posizione qualunque (PARZIALE)** — C'è come menu riga «Inserisci sopra/sotto»; manca la barra «+ Inserisci attività» fra ogni coppia di righe.
- **Stampa / PDF della tabella (PARZIALE)** — raggiungibile solo dal Gantt.
- **Export CSV commessa e Import CSV/Excel (ASSENTI)** — vedi sezione Import/Export.

**BASSO**: step di 5 e percentuale scritta sopra la barra di avanzamento, conteggio «Milestones» in testata (ATEC esclude le spente, il prototipo no), «Periodo» in testata (stessa differenza), data «proposta» del giorno in grigio nelle celle vuote, evidenziazione della cella data quando è **oggi**, export .json del modulo, chip data odierna in testata.

---

### Dashboard commesse + anagrafiche

**IMPATTO MEDIO**

- **Dashboard a griglia di cartelle (PARZIALE)** — La card-cartella per commessa **esiste già** (`ProjectMilestoneCard`, `ProjectSalCard`: icona Folder, codice in badge monospace, cliente, PM, pulsante «Apri») e le tre statistiche (n. milestone / avanzamento / periodo) compaiono espandendola. Mancano: la disposizione a griglia, il fatto che sia la **pagina d'ingresso** (`/` è la dashboard KPI) e le statistiche visibili senza espandere.
- **Spunta «In dashboard» per commessa (ASSENTE)** — Nessuna colonna di visibilità/pin su `projects`. Da questa dipendono anche il limite governabile e la fascia di chip delle commesse escluse.
- **Righe della dashboard cliccabili (PARZIALE)** — La tabella «commesse recenti» non ha `onClick`/`href`: per aprire una commessa si passa da `/commesse`.
- **Anti-duplicato codice commessa in MODIFICA (PARZIALE — BUG)** — Il controllo esiste solo nella POST; la **PUT aggiorna `code` senza alcun controllo di unicità** e `projects.code` non ha indice UNIQUE: rinominando si possono creare due codici uguali.
- **Anti-duplicato nominativo dipendente (ASSENTE — BUG)** — `POST /api/employees` fa INSERT diretta senza guard e senza UNIQUE: due dipendenti identici diventano indistinguibili nelle combo risorse.
- **Link «Gestisci anagrafica attività» dal form commessa (PARZIALE)** — Manca il round-trip dal dialogo con riallineamento della selezione; le voci nuove si recuperano comunque dopo, col «Precarica da catalogo» e con l'autocomplete in riga.
- **Campi SAL nel form commessa / commessa «solo SAL» (PARZIALE)** — PO e Rif. Offerta si modificano dalla testata SAL, ma non esiste il concetto di commessa presente solo nel registro SAL.

**BASSO**: barra pulsanti d'azione in testata dashboard (in ATEC PM sono voci di sidebar), cartella «Pagamenti SAL» che si colora sulle scadenze, limite 10 governabile + nota «Visualizzate le prime N di M», conferma eliminazione commessa che cita il numero di milestone, «se nessuna attività selezionata crea 1 milestone vuota», redirect automatico alla scheda SAL dopo la creazione (presupposti già pronti: il SAL vuoto viene creato al volo e il deep-link `/commesse/{id}/sal` esiste — manca solo il `navigate()`), nominativo come campo unico modificabile inline, «Ripristina elenco standard» del personale (qui non ha senso: sono utenti veri con credenziali), indicatore spazio localStorage (non applicabile).

---

### Pagamenti SAL

**IMPATTO MEDIO**

- **Modale «Anagrafica SAL» + aggiunta di una commessa solo nella pagina SAL (PARZIALE / ASSENTE)** — In ATEC PM il SAL è sempre figlio di una commessa reale (PK+FK su `projects`), quindi non si può creare una voce SAL autonoma né rimuoverne una dalla sola pagina SAL. Decisione da prendere: è un limite voluto o serve il registro autonomo?
- **Precarico automatico del modello a 6 step (PARZIALE)** — L'endpoint `seed-template` esiste con le stesse percentuali 15/15/10/20/20/20 ma è **manuale**, e le descrizioni degli step differiscono da quelle del prototipo (es. «2° acconto ad approvazione disegni» vs «2° acconto dall'ordine»). Da allineare i testi e decidere se automatizzarlo.
- **Editor data a segmenti sulle colonne Ipotesi Fatturazione / Data Incasso (PARZIALE)** — stessa lacuna delle Milestones.
- **Scheda commessa: stato aperto/chiuso non persistito (PARZIALE)** — è solo stato React.

**BASSO**: apertura posizionata con scroll + flash sulla card, voce «Gestisci anagrafica» nelle tendine (in ATEC PM c'è «Aggiungi nuovo…» in linea, funzionalmente superiore), etichetta abbreviata «Par. Pag.», SAL nuovo con 2 righe già pronte, conferma di eliminazione anche sulle righe vuote, %SAL formattata a 2 decimali con virgola, seconda scrollbar orizzontale sopra la tabella (la prop `topScrollbar` di `DataTableCard` **esiste già ed è usata nel Prospetto SAL**: basta collegarla al foglio per commessa, che oggi usa un `overflow-x-auto` scritto a mano).

---

### SAL analitiche

**IMPATTO MEDIO**

- **Pagina «Warning Fatturazione SAL» dedicata (PARZIALE)** — La regola c'è ovunque (Prospetto, `/scadenze`, campanella) ma non esiste una vista con il sommario «N scadute · M in pre-warning» e le 9 colonne del prototipo. Nota: il Prospetto **non è filtrabile alle sole righe in allarme** (la colonna Segnalazione espone un rank numerico, la ricerca testuale non la intercetta); `/scadenze` mostra solo righe in allarme ma con soglia fissa ≤7 giorni, diversa dal pre-warning «lunedì della settimana precedente» (fino a ~13 giorni).
- **Pagina «Warning incasso fattura» dedicata (PARZIALE)** — idem: regola e righe ci sono, manca la vista con badge e stampa.
- **Perimetro del Prospetto limitato alle commesse ACTIVE (PARZIALE)** — Il prototipo monitora tutte le commesse del registro. In ATEC PM una commessa con SAL aperti che passa a COMPLETED/ON_HOLD **sparisce** da prospetto, cash flow e grafico. Da confermare se è voluto.
- **Navigazione fra le schermate analitiche (PARZIALE)** — mancano solo le due voci Warning; per il resto ATEC PM accorpa Cash Flow + Analisi in una pagina sola con drill-down in dialog.
- **Controllo periodico 15 giorni con banner e «Conferma controllo» (ASSENTE)** — era stato implementato (tabella `sal_prospetto_checks`, endpoint, banner realtime) ed è stato **rimosso il 03/08/2026 su tua richiesta esplicita**. Non reintrodurre senza una tua conferma.

**BASSO**: colore rosso/giallo sul pulsante Warning (oggi c'è solo il conteggio), ordinamento composito rank+data delle segnalazioni, descrizione commessa accanto al codice nel Prospetto, priorità pill warn/pre vs incasso (ATEC PM ha unificato mettendo l'incasso davanti — il prototipo era incoerente con sé stesso), data di scadenza in rosso nella cella, stampa PDF delle liste Warning, asse Y con «nice max» e valori abbreviati k/mln, indicatore memoria localStorage (non applicabile).

---

## 4. GIÀ COPERTO

**Milestones — tabella**: 10 colonne · W.Inizio/W.Fine ISO 8601 · W.Tot · riga completata a 100% · msStatus done/late/current (anche server-side) · avanzamento medio sulle sole righe attive · barra avanzamento in testata · aggiungi attività in coda (+ autocomplete catalogo) · elimina con conferma · riordino drag&drop persistito · × per svuotare la data · giorno della settimana con festivi in rosso · calendario festività italiane (Pasqua inclusa) · selezione commessa (PmSidebar + tab) · salvataggio automatico con concorrenza ottimistica e realtime · precarico da catalogo · anagrafica attività · empty state.

**Gantt**: 9 colonne del pannello · spegnimento righe per commessa (+ spegnimento «vero» a livello di dato) · bande mesi/settimane/giorni · campitura weekend e oggi (+ festivi) · linea «oggi» · menu Stampa a due voci · stampa Gantt A3 (porting quasi letterale, con legenda e testata) · evidenza riga completata/urgenza · picker anagrafica attività.

**Import/Export/Stampa**: aggancio commessa per codice (con normalizzazione) · import additivo e ripetibile con deduplica (funzione che il prototipo non ha) · import backup .json del prototipo · ripristino backup (server-side, con job riagganciabile) · stampa/PDF A4 orizzontale della commessa · logo aziendale su schermate e documenti.

**Dashboard + anagrafiche**: stato vuoto dashboard · ordinamento per numero commessa · elenco commesse (albero con ricerca e paginazione) · apri / modifica / nuova commessa · codice obbligatorio · blocco «Attività da precaricare» solo in creazione · Tutte/Nessuna + contatore · anagrafica attività: rinomina inline, aggiunta con Invio e anti-duplicato, riordino drag&drop, elimina con conferma, ripristino delle 32 voci standard · copia per valore (il catalogo non tocca le commesse) · precarico su commesse già esistenti · anagrafica personale · elimina nominativo con conferma (soft delete) · i nominativi alimentano trasferte e Bilancio Commessa.

**Trasferta**: anagrafica personale come sorgente · costo personale = tariffa × ore · formattazione europea degli importi.

**Bilancio**: voce Risorse Atec (da timesheet reale, superiore) · totale di sezione con fallback · evidenza cromatica dei negativi · aggancio DDP→commessa via FK · Tot. Acquisti DDP con esclusione stati A9 e dedup dei padri · ricalcolo automatico ad ogni lettura · riservatezza del dato economico (RequireFeature).

**Calcolatrici**: textarea che cresce col testo · importi in formato € italiano · chiusura dialogo su click esterno · conferma = salvataggio + ricalcolo.

**Pagamenti SAL**: auto-allineamento del registro · anagrafica Condizioni pagamento · anagrafica Causali SAP · anagrafica Stati pagamento (con colori configurabili e voci di sistema protette) · preservazione dei valori storici «(non in anagrafica)» · aggiornamento realtime delle tendine · campi di riga + IVA 22 di default · migrazione stato «pagata» · 16 colonne · calcoli derivati (Importo, IVA, Tot.+IVA, Data prevista saldo) · riga totali con %SAL verde a 100 · colorazione riga per stato · barra Avanzamento Incasso · riordino step drag&drop · aggiunta step · Invio = riempimento verticale · N° fattura a sole cifre · formattazione euro · salvataggio immediato con concorrenza e realtime · ordinamento schede per codice.

**SAL analitiche**: semaforo warn/pre col lunedì della settimana precedente · data prevista saldo · regola «fattura no incasso» · regola di inclusione del Prospetto · ordinamento colonne · pill a 5 valori · riepilogo contatori · stampa Prospetto (+ export CSV, in più) · 5 totali Cash Flow netto/con IVA · 3 bucket mutuamente esclusivi · stampa Cash Flow · barre impilate mensili · linea «Incasso previsto» · serie mensile continua · legenda con i totali · etichetta totale cliccabile · drill-down a 5 tipi · pill del drill-down · vista di dettaglio · stampa Analisi (con tabella mese × categoria in più).

---

## 5. PROPOSTA DI PRIORITÀ

**1 — Rifiniture e bug delle milestone già portate** *(giorni, non settimane)*
Stampa raggiungibile dalla vista Tabella; numerazione delle righe in stampa allineata alla griglia; avanzamento medio in stampa calcolato sulle sole righe stampate; avviso «Consenti i popup»; evidenza urgenza sull'intera riga; clipping delle barre Gantt col filtro date; colonne Gantt persistite per commessa; pulsante «Oggi». Tutte correzioni puntuali su codice esistente, alto ritorno percepito.

**2 — Import / export dati milestone**
Fix `ParseImportDate` (date italiane, tocca anche Check list e MoM); helper CSV condiviso in `lib/`; export CSV commessa; import CSV con parser vero, autodetect delimitatore, mappatura header e fallback Windows-1252; import .xlsx **lato server con EPPlus** (già referenziato) invece di riscrivere il lettore ZIP nel browser; menu unico «Importa/esporta» + export .json del modulo.

**3 — Gantt: viste salvate e consegna al cliente**
Timeline spegnibile con riproporzionamento; range date persistito con toggle; badge contatore degli spegnimenti e reset unico; poi «Vista Interna» / «Vista Cliente» salvate per commessa (server-side, non localStorage); infine export Excel SpreadsheetML della vista. È il blocco che sblocca il caso d'uso «Gantt ridotto da mandare al cliente».

**4 — Bilancio commessa**
Tabella Ordine multi-riga (Ordine/Posizione/Importo) con Totale Ordine calcolato; Totale Vendita; Contingency = Ordine − Vendita (rinominando quella di preventivo per evitare l'omonimia); redditività € e % anche sul preventivo, sanando l'incoerenza del `margin` senza trasferte; voce «Lavorazioni Officine» autonoma con interne/esterne (il valore c'è già nella DDP Officina); Riepilogo Costi a due sezioni voce-per-voce.

**5 — Calcolatrici a righe + anagrafica tariffe**
Componente unico «finestra di calcolo a righe» (riusabile per RAC/MAC/LAC/SPC e poi per la Trasferta), con dettaglio persistito, override manuale e marcatore di provenienza sul modello di `linked_source` del cash flow; UI per `tariff_options` (l'API c'è già ma **nessuno la chiama**) + nuovo tipo «tariffa oraria»; sistemare la lookup risorse che oggi ritorna solo le wildcard.

**6 — Gestione Trasferta (modulo nuovo)**
Tabelle step + righe persona, pagina con card per commessa, griglia a 14 colonne, giorni con esclusione sab/dom per riga, le 4 calcolatrici sopra il componente del blocco 5, riepilogo per nominativo, totali di step e di commessa, sincronizzazione delle Spese Trasferta verso il consuntivo (la parte Risorse è già automatica dal timesheet).

**7 — Dashboard a cartelle**
Flag «In dashboard» su `projects` (+ chip delle escluse e limite governabile), griglia di cartelle come pagina d'ingresso con le tre statistiche visibili senza espandere, righe della dashboard cliccabili, colorazione della voce SAL sulle scadenze.

**8 — SAL: registro, warning dedicati e ergonomia**
Decidere se serve l'«Anagrafica SAL» autonoma (oggi il SAL è sempre figlio di una commessa); allineare i testi del modello a 6 step; collegare `topScrollbar` al foglio per commessa; persistere l'espansione delle schede; le due viste Warning dedicate con sommario e stampa. **Non reintrodurre il controllo periodico a 15 giorni** senza tua conferma esplicita: era stato fatto e l'hai fatto togliere il 03/08.

*Trasversale ai blocchi 1-8*: l'editor data a segmenti GG/MM/AAAA con auto-avanzamento è richiesto da Milestones, SAL e Trasferta — conviene farlo una volta sola dentro `DateField` (digitazione + calendario), non tre volte.

*Bug da chiudere comunque, ovunque cadano*: `PUT /api/projects` aggiorna `code` senza controllo di unicità; `POST /api/employees` non ha anti-duplicato né UNIQUE; `actualEmployees` arriva al client e non viene mai renderizzato.