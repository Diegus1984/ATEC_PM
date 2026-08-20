# Piano di lavoro — gap `Gestione_Commesse_V32.html` → ATEC PM

Data: 03/08/2026
Analisi di riferimento: [ANALISI-GAP-COMMESSE-V32.md](ANALISI-GAP-COMMESSE-V32.md)
(286 funzionalità del prototipo confrontate col codice reale: 106 presenti, 120 parziali, 60 assenti)

Ordine dei blocchi = ritorno percepito / costo. I blocchi 1-3 sono rifiniture e completamenti su
codice già esistente; dal 4 in poi sono moduli nuovi con migrazioni DB.

---

## PRIMA DI COMINCIARE — 5 decisioni tue

Queste cambiano il contenuto dei blocchi, non l'ordine. Servono risposte prima del blocco indicato.

| # | Decisione | Perché | Serve per |
|---|---|---|---|
| D1 ✅ | ~~Riga milestone **spenta**: sparisce o resta attenuata?~~ **DECISA 04/08: sparisce**, con dialogo «Spente» per riattivarla | Comportamenti opposti, entrambi difendibili | Blocco 1 — fatto |
| D2 ✅ | ~~Il **Prospetto SAL** deve mostrare anche le commesse non ACTIVE?~~ **DECISA 04/08: sì**, entrano le chiuse con righe ancora aperte (CANCELLED escluse) | Rischio di perdere di vista incassi aperti | Blocco 3 — fatto |
| D3 ✅ | ~~Serve l'**Anagrafica SAL autonoma**?~~ **DECISA 04/08: no**, il SAL resta figlio della commessa: limite voluto, non si tocca | È un limite strutturale, non una dimenticanza | Blocco 3 — chiusa |
| D4 ✅ | ~~**k officina interna**: percentuale additiva `costo × (1 + k/100)` come nel prototipo, o moltiplicatore `× 1,450` come in ATEC PM oggi?~~ **DECISA 04/08: resta com'è in ATEC PM** — il 45% è un **ricarico di vendita**, non un costo, e si scrive come moltiplicatore `1,450` | Le due semantiche non coincidono, i numeri storici cambiano | Blocco 5 — chiusa |
| D5 ✅ | ~~**Contingency**: nel prototipo è `Ordine − Vendita` (importo). In ATEC PM è una **percentuale di imprevisti** sul costo netto.~~ **DECISA 04/08: la voce nuova si chiama «Delta Ordine»**, la Contingency esistente non si tocca | Falso amico, rischio di errori di lettura del conto economico | Blocco 4 — fatto |

**Da NON fare senza tuo via libera esplicito**: reintrodurre il controllo periodico a 15 giorni del
Prospetto SAL (banner + «Conferma controllo»). Era stato implementato e l'hai fatto rimuovere il 03/08/2026.

---

## BLOCCO 0 — Bug da chiudere subito ✅ FATTO 04/08/2026

Non dipendono dal prototipo: sono difetti trovati durante l'analisi del codice attuale.
Dettaglio dei fix in [BUGS.md](BUGS.md), BUG-007 … BUG-012.

| Bug | Dove | Stato |
|---|---|---|
| `ParseImportDate` con `InvariantCulture`: `31/07/2026` → `null`, `03/08/2026` → **8 marzo** | 4 copie in Milestones / Check list / MoM / SAL | ✅ BUG-012 — nuovo `Services/ImportDates.cs` |
| `PUT /api/projects` aggiorna `code` senza controllo di unicità | `ProjectsController` | ✅ BUG-007 |
| `POST /api/employees` senza anti-duplicato | `EmployeesController` | ✅ BUG-008 (guardia su POST **e** PUT) |
| Stampa milestone: righe rinumerate 1..N | `milestone-print.ts` | ✅ BUG-010 |
| Stampa milestone: media in testata su righe non stampate | `milestone-print.ts` | ✅ BUG-011 |
| `printHtml` con popup bloccati ritorna in silenzio | `lib/print-template.ts` | ✅ BUG-009 (vale per tutti gli 8 punti di stampa) |
| `actualEmployees` arriva nel DTO e non viene mai renderizzato | web, Prev vs Consuntivo | ⏭️ **rinviato** — vedi sotto |

**Verifica**: `dotnet build` 0 errori · `npm run build` (tsc -b + vite) 0 errori · `eslint` 0 errori
(38 warning tutti preesistenti, nessuno nei file toccati) · parser date verificato caso per caso su
12 stringhe. **Non provato a runtime** (nessuna GUI avviata).

**Due cose da sapere:**
- Il parser ora **rifiuta** le date all'americana `07/31/2026`, che prima venivano accettate. È voluto:
  è l'unico modo di togliere l'ambiguità con `31/07/2026`. Se qualche file storico usa quel formato,
  va convertito prima dell'import.
- **Non** è stato aggiunto l'indice UNIQUE su `projects.code` né sul nominativo dipendente: se in
  produzione esistono già dei duplicati, la migrazione fallirebbe all'avvio del server. Le guardie sono
  applicative. L'indice va messo dopo una bonifica dei duplicati esistenti — da fare quando vuoi.
  Effetto collaterale voluto: se apri una commessa il cui codice è già duplicato e salvi, ora prendi un errore.

**Rinviato — `actualEmployees` non renderizzato.** Il dato non è buttato via: `ActualHours` e
`ActualCost` della sezione derivano proprio da lì (`BudgetVsActualController:430-431`). Quello che manca
è il **dettaglio per dipendente** a video, cioè esattamente la «riga RAC con N linee-risorsa» del blocco 5
dentro il «Riepilogo Costi voce-per-voce» del blocco 4. Farlo adesso significherebbe disegnare una UI che
quei due blocchi rifanno. Lasciato lì apposta.

---

## BLOCCO 1 — Rifiniture milestone e Gantt già portati ✅ FATTO 04/08/2026 (10 voci su 10)

Tutto su codice esistente, nessuna migrazione.

**Verifica**: `npm run build` (tsc -b + vite) 0 errori · `eslint` sui file toccati 0 errori
(1 warning preesistente su `date-field.tsx`) · **provato a runtime il 04/08/2026** sulla commessa
C260505_205 (33 milestone), stack avviato e rispento, dati di prova ripuliti.

Cosa è stato provato davvero a video:
- editor a segmenti: avanzamento automatico giorno→mese→anno, Invio, commit all'uscita dal campo
  (PUT 200 verificata), valore salvato corretto;
- **difetto trovato e chiuso in corsa**: con la digitazione molto rapida `maxLength=2` faceva scartare
  al browser la cifra in eccesso e usciva una data sbagliata **senza errore** («712» → 31/08 invece di
  07/12). Tolto `maxLength`, l'eccedenza trabocca nei segmenti successivi e un giorno impossibile
  (32-99) viene riletto come giorno+mese. Ricontrollato: «712» → 7 dicembre;
- evidenza: riga intera in rosa con barretta rossa;
- riga spenta: sparisce, contatore 33→32, numerazione con il buco al posto della riga, dialogo
  «Spente (1)» con «Riattiva» che la rimette a posto;
- barra «+ Inserisci attività»: compare all'hover fra le righe;
- Gantt: clipping (nessuna barra con `left` negativo, quella tagliata ha `rounded-l-none`),
  «Periodo commessa» che precompila l'intervallo, «Oggi» che porta la timeline sulla linea di oggi,
  badge «Mostra tutto (3)» = 2 colonne + 1 intervallo e reset che rimette tutto,
  colonne e intervallo persistiti **per commessa** (verificato cambiando commessa e tornando indietro);
- stampa Tabella con una riga nascosta: 32 righe, numerazione `1,2,4,5…` (buco conservato, BUG-010)
  e media 14% sulle sole righe stampate contro 17% in pagina (BUG-011).

Non provato: stampa Gantt A3 resa grafica, Backspace fra i segmenti, incolla nel campo data.

File toccati: `components/shared/date-field.tsx`, `features/milestones/milestone-table.tsx`,
`features/milestones/MilestoneGantt.tsx`, `features/milestones/MilestonesPage.tsx`,
`features/commesse/ProjectMilestones.tsx`.

Note di merito:
- L'editor a segmenti è **opt-in** (`<DateField segmented />`), non è stato acceso ovunque:
  per ora lo usano la tabella milestone e i due filtri data del Gantt. Per SAL e Trasferta basterà
  aggiungere la prop.
- «31/02» viene portato a 28/02 invece di produrre una data inesistente; una data digitata fuori
  dai limiti (`disableBefore`/`disableAfter`) viene portata al bordo, come fa già la regola «fine ≥ inizio».
- L'anno a 2 cifre viene letto come 20xx: «26» → 2026.
- La colonna NR del pannello Gantt aveva **lo stesso difetto della stampa** (BUG-010): numerava le sole
  righe visibili. Ora griglia, pannello Gantt, combo «Righe» e stampa usano tutte la stessa numerazione.

1. ✅ **Editor data a segmenti GG/MM/AAAA** dentro `DateField` (prop `segmented`) — si digita il giorno e
   il focus salta al mese, poi all'anno già compilato con l'anno corrente; Backspace su un segmento vuoto
   torna al precedente, Invio conferma, il calendario resta sull'icona. Il valore si scrive solo
   all'uscita da tutto il gruppo: muoversi fra i segmenti non salva.
2. ✅ **Evidenza urgenza su tutta la riga** — campitura rosa + barretta rossa a sinistra, e vince sul
   colore di stato. Prima si tingeva di rosso la sola descrizione.
3. ✅ **Riga spenta** → **D1 decisa: sparisce dalla tabella**, come nel prototipo. La numerazione resta
   quella dell'elenco completo, quindi al posto di una riga spenta si vede un buco. Aggiunto il dialogo
   «Spente (n)» in testata per rivederle e riattivarle, e la barra «+ Inserisci attività» usa la
   posizione nell'elenco completo (non quella fra le sole righe visibili).
4. ✅ **Stampa dalla vista Tabella** — pulsante «Stampa Tabella (A4)» accanto al menu Colonne, quindi
   presente sia in `/milestones` sia nella scheda della commessa. Stampa le righe attive con la
   numerazione della griglia.
5. ✅ **Barra «+ Inserisci attività» fra ogni coppia di righe** — invisibile finché non ci passi sopra.
   Il menu riga «Inserisci sopra/sotto» resta.
6. ✅ **Gantt — clipping delle barre col filtro Dal/Al** — stesso calcolo della stampa; le barre tagliate
   hanno il bordo squadrato dal lato del taglio, così si vede che continuano fuori dall'intervallo.
7. ✅ **Gantt — colonne spente per commessa** (`milestones:gantt:columns:{projectId}`).
8. ✅ **Gantt — range date persistito** per commessa + pulsante «Periodo commessa» che lo precompila col
   periodo reale delle righe attive.
9. ✅ **Gantt — contatore unico** «Mostra tutto (n)»: conta colonne spente + righe spente + intervallo
   attivo, con il dettaglio nel tooltip, e li azzera tutti insieme.
10. ✅ **Gantt — pulsante «Oggi»** in toolbar; se oggi è fuori dall'intervallo lo dice invece di non fare nulla.

**Dimensione**: M — l'editor data a segmenti da solo vale circa metà del blocco.

---

## BLOCCO 2 — Import / Export dati

Prerequisito: fix `ParseImportDate` (blocco 0).

1. **Helper CSV condiviso in `lib/`** — il formato `;` + BOM UTF-8 è già scritto a mano in 3 punti
   (`DdpConfigPage`, `SalProspettoView`, `FeriePage`): estrarlo e riusarlo. Stessa cosa per `download()`,
   duplicato in 6 punti.
2. **Export CSV commessa** — 9 colonne, `milestones_{codice}_{aaaammgg}.csv`.
3. **Parser CSV vero** — macchina a stati con virgolette, `""` escape, newline dentro i campi.
   Oggi in ATEC PM **non esiste nessun parser CSV**.
4. **Import CSV** — autodetect delimitatore `;`/`,`, riconoscimento riga di intestazione con mappatura
   colonne e fallback posizionale, normalizzazione header (minuscole/accenti/spazi), decodifica UTF-8
   con **fallback Windows-1252** e strip BOM. Senza quest'ultimo un CSV salvato da Excel italiano
   arriva con le accentate rotte, perché oggi tutti gli import fanno `file.text()` e basta.
5. **Import .xlsx lato server con EPPlus** (già referenziato nel progetto, oggi usato solo per
   l'anteprima documenti). **Non** riscrivere il lettore ZIP/XLSX nel browser come fa il prototipo:
   EPPlus gestisce già i seriali data e le basi 1900/1904. Limite a 4 colonne per titolo, scarto
   delle righe di legenda, pulsante «Importa da Excel» in testata commessa.
6. **Menu unico «Importa/esporta»** in testata (oggi «Importa» sta in `MilestonesPage` e «Stampa» dentro
   il Gantt) + **export .json del modulo** (l'import c'è già ed è migliore dell'originale, manca l'export).

**Dimensione**: M-L. Da tenere: l'import additivo con deduplica già in ATEC PM è **superiore** al
prototipo (che non ce l'ha) — non regredire su questo.

---

## BLOCCO 3 — Gantt consegnabile al cliente + rifiniture SAL ✅ FATTO 04/08/2026

Il caso d'uso vero: «un Gantt completo per l'interno, uno ridotto da mandare al cliente».

**Verifica**: `dotnet build` 0 errori · `npm run build` 0 errori · `eslint` 0 errori
(6 warning preesistenti in file SAL non toccati) · **provato a runtime il 04/08/2026**,
migrazione v60 applicata all'avvio, stack rispento e dati di prova ripuliti.

Cosa è stato provato davvero a video:
- **diagramma spegnibile**: voce «Diagramma» nella combo Colonne; spento resta la sola tabella con le
  colonne allargate su tutta la larghezza e «Oggi» disabilitato;
- **Componi**: ⊗ su 9 intestazioni e 33 righe; spenta la colonna Note e la riga 3 → NR `1,2,4,5,6`
  (buco conservato), badge «Mostra tutto (3)» = 1 colonna + 1 riga + diagramma;
- **viste salvate**: `PUT .../views/Vista Cliente` → 200, payload completo a DB (cols, timeline,
  hiddenRows), menu che passa da «—» a «salvata 04/08/2026» con Applica/Sovrascrivi/Elimina;
  reset totale e **riapplicazione** che rimette esattamente 8 colonne + diagramma spento + riga 3
  nascosta; `DELETE` → 200;
- **export Excel** in entrambe le configurazioni: con diagramma 121 colonne (9 pannello + 112 giorni),
  barre come celle colorate (`cBar` 6, `cBarDark` 6, `cDone` 36), bande mesi mergiate
  (APRILE ×11 · MAGGIO ×31 · GIUGNO ×30 · LUGLIO ×31 · AGOSTO ×9 = 112), 16 bande settimana,
  `FreezePanes` con `SplitVertical=9`; **XML validato col parser**: 121 celle logiche su ogni riga;
- **viste Warning**: presenti in sidebar con i contatori, deep-link `?view=warn-incasso`, riga in
  allarme con sommario, CSV e stampa;
- **D2**: con la commessa portata a **COMPLETED** le righe restano nel Prospetto e nel riepilogo;
  portata a **CANCELLED** spariscono (0 righe). Stato originale ripristinato;
- **modello a 6 step**: precaricato e verificato riga per riga — i 6 testi sono quelli di V32 e le
  condizioni sono `A Vista, 30gg, 30gg, 30gg, 30gg, 30gg`, totale 100%;
- **schede SAL persistite**: aperta una scheda, `sal:expanded-projects:v1` scritto, dopo il reload
  la scheda è ancora aperta.

Non provato: apertura reale del .xls in Excel (validato solo l'XML), stampa delle due viste Warning.

**Bozze escluse (deciso il 04/08 dopo la prova a runtime).** Il primo perimetro escludeva solo
CANCELLED e faceva entrare anche le commesse in **DRAFT**, che prima non comparivano — se n'è accorti
a runtime. Ora `ProjectScope` esclude sia CANCELLED sia DRAFT: un piano di fatturazione abbozzato non
finisce negli allarmi. Matrice verificata su tutti e cinque gli stati, sulla stessa commessa reale:

| Stato commessa | Righe nel Prospetto | In riepilogo |
|---|---:|---|
| DRAFT | 0 | — |
| ACTIVE | 2 | C260505_205 |
| COMPLETED | 2 | C260505_205 |
| ON_HOLD | 2 | C260505_205 |
| CANCELLED | 0 | — |

Cioè: le chiuse con righe aperte entrano (era lo scopo di D2), bozze e annullate no.

**Nota su una voce del piano che era sbagliata**: la scrollbar orizzontale in alto sul foglio
SAL per commessa **c'era già** — `ProjectSal` usa `GridScroller`, che ha `topScrollbar`
attivo di default. Non è stato toccato niente.

1. ✅ **Diagramma spegnibile** — voce «Diagramma» nella combo Colonne. Spento, resta la sola tabella e
   le colonne si riproporzionano (flex-grow sulla larghezza nominale) invece di lasciare mezza pagina
   vuota. Con il diagramma spento la tabella si vede anche senza date pianificate, che prima bloccavano
   la pagina con «Nessuna data pianificata».
2. ✅ **Modalità «Componi»** — pulsante che mette una ⊗ su ogni intestazione di colonna e a inizio riga:
   si spegne quello che non deve andare al cliente cliccandolo sul diagramma. I due menu restano.
3. ✅ **Viste salvate per commessa** — «Vista Interna» / «Vista Cliente» **lato server**
   (migrazione **v60**, tabella `milestone_gantt_views`), con stato «salvata gg/mm/aa» nel menu,
   Applica / Sovrascrivi / Elimina. Salvano colonne spente + righe spente + intervallo + diagramma on/off.
   Il payload è JSON opaco per il server: aggiungere una voce alla composizione non sarà una migrazione.
4. ✅ **Export Excel SpreadsheetML** (`milestone-excel.ts`) — colonne del pannello + una colonna per
   giorno, bande mesi/settimane mergiate, **barre come celle colorate** (con la parte già avanzata più
   scura), avanzamento a blocchi █/░, blocco riquadri sul pannello. Sta nel menu «Viste».
5. ✅ **SAL** — D2 applicata (vedi sotto), D3 chiusa come limite voluto, testi del modello a 6 step
   allineati a V32, espansione delle schede persistita. `topScrollbar` non serviva: c'era già.
6. ✅ **Due viste Warning dedicate** — «Warning Fatturazione» (scadute + pre-warning) e «Warning incasso
   fattura», con sommario a contatori, export CSV e stampa, raggiungibili dalla sidebar SAL e via
   deep-link `?view=warn-fatturazione` / `?view=warn-incasso`. Riusano le colonne del Prospetto:
   riepilogo, CSV e stampa sono stati estratti in `sal-prospetto-report.ts` invece di essere duplicati.

**D2 applicata** — il perimetro delle viste SAL aggregate (Prospetto, Cash Flow, Analisi, riepilogo)
non è più il solo `status = 'ACTIVE'`: entra anche la commessa chiusa che ha ancora almeno una riga
aperta (non emessa, oppure emessa e non «Pagata»). **CANCELLED e DRAFT restano fuori**: la prima è
lavoro annullato, la seconda è una commessa non ancora avviata. Il predicato è in un unico posto
(`SalController.ProjectScope`), usato dalle 4 query.

**Testi del modello a 6 step — cambiati, verificali.** Le percentuali erano già uguali
(15/15/10/20/20/20), cambiano descrizioni e **tre condizioni di pagamento**:

| # | Prima (ATEC PM) | Ora (da V32) | % |
|---|---|---|---:|
| 1 | 1° acconto all'ordine · *A Vista* | 1° acconto all'ordine per inizio progettazione · *A Vista* | 15 |
| 2 | 2° acconto ad approvazione disegni · *A Vista* | 2° acconto dall'ordine · **30 gg. dffm.** | 15 |
| 3 | 3° acconto ad avviso merce pronta · *A Vista* | Alla consegna ed accettazione del progetto e benestare per ordini materiali presso i fornitori · **30 gg. dffm.** | 10 |
| 4 | 4° acconto a consegna/installazione · *30 gg.* | Al sito pilota in ATEC – collaudo in bianco AT · *30 gg.* | 20 |
| 5 | 5° acconto a collaudo · *30 gg.* | Alla consegna materiali · *30 gg.* | 20 |
| 6 | Saldo a 30 gg. fine collaudo · *30 gg.* | Al collaudo presso sede Cliente · *30 gg.* | 20 |

Vale **solo per i SAL creati da qui in avanti**; le commesse già compilate non sono toccate.
Se la versione buona è quella vecchia si torna indietro in un minuto (`SalController.SeedTemplate`).

**Rimasto fuori di proposito**: rendere automatico il precarico del modello alla creazione della
commessa. Oggi resta un pulsante — è una scelta tua, non una dimenticanza.

**Dimensione**: L. L'export SpreadsheetML è la parte pesante; i punti 1-2 da soli sbloccano già il caso d'uso.

---

## BLOCCO 4 — Bilancio commessa ✅ FATTO 04/08/2026 (7 voci su 7)

Prima migrazione strutturale (**v61**).

**Verifica**: `dotnet build` 0 errori · `npm run build` (tsc -b + vite) 0 errori · `eslint` 0 errori
(warning tutti preesistenti, in file non toccati) · **provato a runtime il 04/08/2026**, stack avviato
e rispento, dati di prova ripuliti.

**Cosa è stato provato davvero:**
- **migrazione v61 applicata all'avvio su un DB a v60**: tabella `project_order_lines` creata con FK e
  indice, `projects.sale_total`, `ddp_officina_items.work_type`, feature `nav.bilancio` a livello 2;
  log `classificate 1 righe da lavorazione + 0 da stato`;
- **retrocompatibilità del seed**: commessa con `revenue` 250.000 € e nessuna riga → alla prima
  apertura del Bilancio nasce UNA riga da 250.000 € e il Totale Ordine non si muove;
- **scomposizione officine** su righe costruite apposta: interne 200 € · esterne 133 € · non
  classificate 50 €, con la riga in stato ANN esclusa (A9) e il **padre** escluso dal dedup mentre il
  figlio resta — la somma coincide con quella della dashboard commessa, quindi la ripartizione non ha
  spostato il totale;
- **incoerenza del margine sanata**: con 1.500 € di trasferta, MARGINE del tab Dettagli e Redditività
  del conto economico danno lo stesso identico numero (248.060,80 €);
- **API righe ordine**: PUT/POST/DELETE, sincronizzazione di `revenue` ad ogni scrittura, minimo 1
  riga (cancellata l'ultima ne ricompare una vuota e il ricavo torna a 0), `rowVersion` stantio
  respinto con il messaggio di conflitto, Posizione normalizzata a sole cifre max 5;
- **le tre strade che scrivono il ricavo**: `PATCH .../revenue` con più posizioni viene rifiutato con
  messaggio parlante, `PUT /api/projects/{id}` con un ricavo diverso viene riportato alla somma;
- **GUI**: compilazione a mano della riga, Totale Ordine, Totale Vendita, Delta Ordine (rosso in
  negativo), Riepilogo Costi con le 4 voci e la scomposizione interne/esterne, «Inserisci riga» e
  «Inserisci riga sotto», eliminazione **con dialogo di conferma**;
- **pagina `/bilancio`**: card con i due KPI, soglia modificata a 99 dalla testata → la card a 98,71%
  diventa rossa su **entrambi** i riquadri e il contatore «Sotto soglia» va a 1; una commessa senza
  ordine resta «—» e **non** finisce fra le rosse;
- **colonna «Tipo»** della distinta officina: valori Interna/Esterna/—, filtro «Esterna» che ora
  trova le 3 righe giuste, e menu «Colonne» che legge «Tipo».

Non provato: il comportamento con due utenti davvero simultanei (il lock del seed è stato verificato
solo leggendo il codice), la stampa, e i ruoli diversi da ADMIN.

**Due difetti trovati SOLO a runtime** — nessuno dei due sarebbe emerso da build o review, ed entrambi
perdevano dati in silenzio:
- **il refetch cancellava quello che si stava scrivendo**: ogni campo committava uscendo, e il
  ricaricamento che ne seguiva riallineava tutta la riga mentre l'utente era già nel campo accanto —
  la Posizione digitata spariva senza un errore. Ora il riallineamento si ferma finché il fuoco è
  dentro la riga;
- **tre campi compilati di fila ne salvavano uno**: il secondo e il terzo commit trovavano il primo
  ancora in volo e venivano **scartati dalla guardia `isPending`**, in silenzio. Ora c'è un solo
  salvataggio per riga, all'uscita dalla riga, con i tre valori definitivi.
- (minore) la voce **«Non classificata»** del menu Tipo è stata tolta: svuotare il campo non reggeva,
  perché al salvataggio il server ri-deduce subito la natura dallo stato DDP.

**Review avversariale** (6 lenti indipendenti + un confutatore per reperto): 33 reperti grezzi,
**6 confermati** = 4 difetti distinti, tutti corretti prima di chiudere:
- **la creazione della prima riga d'ordine era in corsa con sé stessa** — è fatta dentro una GET, e
  due letture simultanee della stessa commessa (il tab e la card su `/bilancio`) creavano DUE righe
  seed identiche, con il ricavo raddoppiato alla prima scrittura e il numero sbagliato che finiva in
  SAL, dashboard e cash flow. Ora è serializzata per commessa con `SELECT … FOR UPDATE` sulla riga di
  `projects` + ricontrollo dentro il lock;
- **`GET budget-vs-actual` su una commessa inesistente rispondeva 500** invece del payload vuoto di
  prima (l'INSERT violava la foreign key). Ora il seed non parte se la commessa non c'è;
- **menu «Colonne» della distinta officina**: la colonna nuova compariva come `workType` invece che
  «Tipo» (mancava in `COLUMN_LABELS`);
- **filtro della colonna «Tipo»**: cercava sul valore grezzo `External`, quindi digitando «Esterna»
  non trovava nulla. Ora l'accessor restituisce l'etichetta, come fa già la colonna «Stato».

**Migrazione v61**, quattro cose in un blocco solo (ognuna anche nel ramo dev, per i DB nuovi):
- tabella `project_order_lines` (Ordine / Posizione / Importo + `sort_order` + `row_version`);
- `projects.sale_total` — il Totale Vendita, NULL finché non lo si digita;
- `ddp_officina_items.work_type` — natura della lavorazione, con backfill a cascata;
- feature key `nav.bilancio` (livello PM), altrimenti la pagina sarebbe **visibile a tutti**.

**Tre decisioni da conoscere:**

1. **D5 applicata: la voce nuova si chiama «Delta Ordine»** (`Ordine − Vendita`), la `contingency_pct`
   della Scheda Prezzi non è stata toccata. Nessuna colonna rinominata, il preventivo resta com'era.
2. **`projects.revenue` = somma delle righe ordine.** SAL, dashboard e cash flow continuano a leggere
   `revenue` senza sapere nulla delle posizioni. La riconciliazione sta in **tre** punti di scrittura
   (`PUT /api/projects/{id}`, `PATCH .../revenue`, e le tre rotte delle righe ordine): con una riga sola
   il campo «Ricavo» della scheda la aggiorna, con più righe vince la tabella e il campo viene ignorato.
   **Nessun backfill**: la prima riga nasce alla prima apertura del Bilancio, seeddata dal `revenue`
   esistente, quindi i numeri non si muovono. Le commesse mai aperte restano come prima.
3. **La voce «Lavorazioni Officine» a PREVENTIVO è vuota di proposito** (mostra «—»): il calcolatore a
   righe che la alimenta è il blocco 5. Il lato consuntivo invece c'è tutto.

**Interne / esterne: il dato non esisteva.** `ddp_officina_items` non aveva alcun campo che
distinguesse una lavorazione interna da una esterna, e l'unica fonte (`project_work_requests.type`)
esiste solo per i codici 101 e si perde quando la riga chiude. Quindi:
- nuova colonna `work_type` (stessi valori `Internal`/`External` delle Lavorazioni);
- backfill in cascata: prima dalla lavorazione collegata, poi dallo stato DDP (`DC` → interna,
  `DO`/`RO`/`IO` → esterna). **Le righe già chiuse (DISP/PAR/MIT) senza lavorazione restano non
  classificate**: quell'informazione non esiste più da nessuna parte;
- da qui in avanti si popola da sola (`WorkRequestDdpSync` la congela prima che la riga chiuda);
- in lettura c'è comunque il ripiego a cascata, così una correzione manuale sulla lavorazione si vede
  subito;
- **nuova colonna «Tipo» nella distinta officina** per sistemare a mano le non classificate;
- il loro costo **non viene attribuito a caso**: compare come terza voce «non classificate» sotto le
  Lavorazioni Officine, così si vede quanto c'è da sistemare.

**Somma invariata.** Estrarre le officine da `ActualMaterialCost` è una *ripartizione*: il
`ActualTotalCost` e la Redditività già a video non cambiano di un centesimo.

1. ✅ **Tabella «Ordine Commessa» multi-riga** — N righe Ordine / Posizione / Importo, «Inserisci riga»
   in coda e «Inserisci riga sotto» per riga, elimina **con conferma** (il prototipo non la chiedeva,
   la regola di progetto sì), minimo 1 riga garantito lato server. Posizione = solo cifre, max 5.
   Concurrency token `row_version` + realtime `BudgetChanged` sull'hub commesse.
2. ✅ **Totale Vendita** — campo a mano con la sotto-etichetta «rif. CALCOLO G205», nel piede della
   tabella ordine. NULL ≠ 0: a 0 il Delta Ordine varrebbe l'intero ordine invece di restare vuoto.
3. ✅ **Delta Ordine = Ordine − Vendita** (D5) — terza riga del piede, rossa se negativa. Come nel
   prototipo si calcola appena UNO dei due termini esiste, ed è «—» solo se mancano entrambi.
4. ✅ **Redditività € e % sui due lati** — sempre rispetto al **Totale Ordine**, mai al Totale Vendita.
   **Incoerenza sanata**: il `TotalCost` del tab Dettagli ora comprende la trasferta a consuntivo, che
   prima mancava e faceva sembrare il MARGINE più alto della Redditività del conto economico.
5. ✅ **Voce «Lavorazioni Officine» autonoma con interne/esterne** — vedi sopra.
6. ✅ **Riepilogo Costi a due sezioni voce-per-voce** — tabella affiancata Costi Preventivati | Costi
   Consuntivati con le 4 voci del prototipo («Risorse Atec», «Materiali commerciali», «Lavorazioni
   Officine», «Spese Trasferta / indennità»), Totale Costi, Redditività e % Redditività per sezione.
   Un totale è «—» solo se **tutti** i suoi addendi mancano, come nel prototipo.
7. ✅ **Pagina `/bilancio` cross-commessa** — card per commessa con i due KPI «Consuntivo Redditività» e
   «Consuntivo % Redditività», rossi **entrambi** sotto soglia o a importo negativo (regola esatta del
   prototipo: `perc < soglia` STRETTO). **Soglia parametrica**, default 20%, in `res_settings`,
   modificabile in testata dal solo ADMIN e letta da tutti. Vista rapida «Sotto soglia», switch
   «Mostra anche le completate» (bozze e annullate sempre fuori), realtime, scheda espandibile che
   monta il conto economico completo.

**Due cose trovate e NON cambiate** (spostano numeri storici, decidi tu):
- il costo ore della **dashboard commessa** risolve il reparto del dipendente con
  `is_primary → is_responsible → id`, mentre la vista `v_timesheet_with_section` usata dal conto
  economico usa `MIN(department_id)`: per chi sta in più reparti i due costi possono differire. Il
  blocco 4 ha sanato solo la trasferta, che era la voce citata nel piano;
- il costo officina è sempre `quantità × costo unitario`, **mai** ponderato su `quantity_produced`
  (che pure esiste): «lavorato finora» oggi non è una formula usata da nessuna query.

**Dimensione**: L.

---

## BLOCCO 5 — Calcolatrici a righe + anagrafica tariffe

**È il mattone che abilita il blocco 6.** Da fare prima della Trasferta, non dopo.

> **Prima di cominciare leggi [BLOCCO5-CALCOLATRICI-SPEC.md](BLOCCO5-CALCOLATRICI-SPEC.md)**: contiene
> il dettaglio del calcolatore del prototipo (colonne, formule, etichette), la D4 con le sue
> conseguenze operative, l'elenco di cosa esiste già da riusare e le due trappole delle griglie
> editabili pagate nel blocco 4. Il prototipo è ora nel repo, in `prototipi/`.

**Due decisioni prese il 04/08 prima di scrivere codice** (cambiavano parecchio il lavoro):
- **il calcolatore lo prende SOLO «Lavorazioni Officine», lato preventivo.** È l'unica delle 4 voci
  senza una struttura che la alimenti: risorse, materiali e trasferta hanno già il preventivo
  strutturato, e a consuntivo tutte e quattro sono automatiche (timesheet, DDP commerciale, DDP
  officina). Mettere un calcolatore a mano sopra un dato automatico sarebbe stata una regressione.
  Il **componente** resta però generico e riusabile: il blocco 6 ci monta sopra le 4 calcolatrici
  di Trasferta senza altre migrazioni (basta una `calc_key` nuova);
- **Officine interne con «Ore» vuote**: il Costo vale come **importo manuale** della riga (come nel
  prototipo) — è quello che rende esprimibile un forfait senza inventarsi delle ore.

1. ✅ **Componente unico «finestra di calcolo a righe»** — `components/shared/calc-sheet.tsx`:
   N sezioni configurabili, colonne a scelta (quantità / costo unitario / importo / ricarico /
   vendita), **Invio = nuova riga**, totali live per sezione e totale generale, riordino drag&drop,
   conferma che **scarta le righe vuote**, sempre almeno una riga vuota per sezione, e soprattutto
   il **dettaglio persistito** (`project_calc_sheets` + `project_calc_rows`, migrazione **v62**).
   **È un dialogo con UNA conferma, non una griglia con commit su blur**: si lavora su una copia
   locale, quindi nessuna delle due perdite di dati del blocco 4 è possibile per costruzione.
2. ✅ **Override manuale dell'importo calcolato** — `amount_pinned` + lucchetto in riga: scrivere
   nell'importo lo congela, svuotarlo (o riaprire il lucchetto) lo rimette automatico. L'importo
   effettivo lo **ricalcola il server** a ogni salvataggio: quello che manda il client non fa testo.
3. ✅ **Marcatore di provenienza sulle righe automatiche** — colonna `linked_source` sulle righe di
   calcolo, con badge «AUTO» e campi in sola lettura (resta forzabile solo l'importo). Oggi nessuna
   riga officina nasce automatica: il posto è pronto per il blocco 6.
4. ✅ **UI per `tariff_options`** — pannello «Anagrafica tariffe» in Configurazione sezioni (aggiunta,
   **modifica dell'importo con il nuovo PUT**, eliminazione con conferma e blocco se il valore è in
   uso) + nuovo tipo **`HOURLY_RATE`** seedato a 40 e 50 €/ora, con il suo controllo di utilizzo su
   `project_calc_rows`. Lo stesso pannello si riapre dal calcolatore («Gestisci tariffe orarie…»).
5. ✅ **Riga a due livelli** — `actualEmployees` **veniva mandato nel DTO e non renderizzato da
   nessuna parte**: ora ogni sezione mostra le risorse a consuntivo come riga dipendente che si apre
   sulle sue ore versate (data · fase · causale · ore · €/h · costo). Il dato non è digitato: viene
   dal timesheet reale, che è più di quel che fa il prototipo.
6. ✅ **Fix lookup risorse** — tutti e tre i difetti: niente più filtro ai soli wildcard (le persone
   vere ora si vedono, con i wildcard in testa), esclusione dei nomi già usati nella sezione (con
   `excludeResourceId` per non far sparire la risorsa che si sta modificando) e ordinamento
   **italiano lato C#** invece che affidato alla collation della colonna.
7. ✅ **D4 decisa (04/08): non si adotta il «k correzione» del prototipo.** Il 45% in ATEC PM è un
   **ricarico di vendita**, non una maggiorazione di costo, e resta scritto come moltiplicatore
   `1,450` — la forma già usata da `departments.default_markup`, `cost_section_templates.default_markup`
   e `project_cost_resources.markup_value` (`DECIMAL(5,3)`).
   Conseguenze operative sul calcolatore Lavorazioni Officine:
   - il **costo resta il costo**: `Ore × Costo orario` finisce così com'è nella voce «Lavorazioni
     Officine» del Riepilogo Costi. **Il Bilancio del blocco 4 non si muove.**
   - le colonne del prototipo «k correzione» → «Costo finale» / «Totale» diventano un **markup di
     vendita** con la stessa semantica delle risorse: `Vendita = Costo × markup`. Chi confronta a
     video con il prototipo troverà numeri diversi: è voluto.
   - il campo accetta moltiplicatori (1,000–9,999), non percentuali: scriverci `45` sarebbe ×45.
     Va vincolato in UI, altrimenti è lo stesso errore silenzioso già visto altrove.
   Applicata: il ricarico è vincolato **sia in UI** (Conferma bloccata con un messaggio che spiega
   cosa succederebbe con «45») **sia lato server**, e la vendita delle lavorazioni entra nel costo
   netto della Scheda Prezzi come già fanno risorse e materiali.

**Due cose da sapere** (non sono difetti, ma spostano numeri):
- il **Totale Costi preventivati** e la **Redditività di preventivo** ora comprendono le lavorazioni:
  è il senso della voce. Sulle commesse che non usano il calcolatore non cambia nulla, la voce vale
  «—» e somma zero;
- lo stesso vale per il **costo netto della Scheda Prezzi** (quindi Offerta e Prezzo finale), che
  assorbe la *vendita* delle lavorazioni.

**Dimensione**: L. **Fatto 04/08/2026 e VERIFICATO A RUNTIME** (migrazione v62 applicata al DB di
sviluppo, dati di prova ripuliti, stack rispento). Provato a video:
- **la prova d'obbligo del blocco 4 — cinque campi compilati di fila senza pause — non perde nulla**:
  è la conferma che il dialogo su copia locale evita per costruzione i due difetti delle griglie inline;
- Invio = nuova riga **dentro la propria sezione**; drag&drop che riordina e **rifiuta** il
  trascinamento da una sezione all'altra; totali live e vendita coerenti (1.200 + 20×50 = 2.200 costo,
  3.190 vendita); conferma che scarta le righe vuote e riapertura fedele;
- **lucchetto**: importo forzato a 500 € che NON si muove cambiando le ore, e ritorno al calcolato
  (800 €) rilasciandolo; picker tariffe che riapplica 50 €/h e ricalcola;
- **ricarico «45»**: Conferma bloccata a video **e** PUT rifiutata dal server;
- concorrenza (`rowVersion` stantio rifiutato), importo fasullo su riga non congelata **ignorato dal
  server**, foglio svuotato → totale di nuovo «—»;
- anagrafica tariffe: aggiunta, modifica dell'importo, eliminazione con conferma e **blocco
  «Valore in uso in: Commesse: C260505_205»** sulla tariffa oraria agganciata a una riga di calcolo;
- lookup risorse: 11 voci (4 wildcard in testa + 7 persone in ordine italiano) dove prima ne
  tornavano 4; scende a 10 dopo aver usato un nominativo e risale a 11 con `excludeResourceId`;
- risorse a consuntivo a due livelli, con dettaglio data/fase/(OVERTIME)/ore/€h/costo che quadra.

**Due rifiniture trovate solo a video** (fatte): il messaggio del ricarico scriveva «tra 1.000 e
9.999» — con il punto, in un testo che parla di moltiplicatori, si legge «mille»; e il ricarico
tornava dal server come «1.45» invece di «1,450». Ora entrambi in forma italiana a tre decimali.

Non provati: il push realtime `BudgetChanged` (SignalR non negozia nel pane headless — è comunque la
stessa chiamata già verificata nel blocco 4) e la finestra «Gestisci tariffe orarie…» aperta *sopra*
il calcolatore (dialogo dentro dialogo).

---

## BLOCCO 6 — Gestione Trasferta (modulo nuovo)

Il modulo **non esisteva**: nessuna rotta, nessuna tabella, nessuna voce di menu. In ATEC PM «Trasferta»
era solo una voce di costo aggregata nel preventivo/BVA e un tipo di riga timesheet.

> **Dettaglio del prototipo e decisioni: [BLOCCO6-TRASFERTA-SPEC.md](BLOCCO6-TRASFERTA-SPEC.md).**

1. ✅ **Modello dati** — migrazione **v63**: `travel_steps` + `travel_step_rows`, `TravelController`
   con CRUD di step e righe, `row_version` su entrambi, realtime `TravelChanged` (+ `BudgetChanged`,
   perché la voce di costo si muove) e feature key `nav.trasferta`.
2. ✅ **Pagina «Gestione Trasferta»** (`/trasferta`) con `PmSidebar` come Milestones/SAL: card per
   commessa con i 4 KPI → dettaglio con N step collassabili.
3. ✅ **Griglia riga-persona a 14 colonne** con intestazioni raggruppate (Personale · Alloggio/Vitto ·
   Altri costi), nominativo da `LookupCombobox` sull'anagrafica.
4. ✅ **Giorni = fine − inizio + 1** con **toggle sab / dom per riga**: la formula sta in `TravelMath`
   lato Shared, non nel planner Risorse dove il weekend è cablato su entrambi i giorni.
5. ✅ **Le 4 calcolatrici** sopra il componente del blocco 5, con i valori da `tariff_options`.
   Il componente ha guadagnato due cose che gli servivano: la **modalità numerica** (la calcolatrice
   Ore somma ore, non euro) e il **terzo fattore** (Auto = Km × Rimborso × Numero Tratte).
6. ✅ **Costo alloggio = Notti × Prezzo** (le notti sono un campo a sé, non più uguali ai giorni) +
   campo **Treno/aereo**.
7. ✅ **Riga Totali dello step** (8 colonne) + **3 badge** in testata + riordino step.
8. ✅ **Tabella «Riepilogo Trasferta»** — una riga per nominativo distinto + «Totale Riepilogo».
   Nel prototipo le celle dei nominativi sono vuote: qui sono valorizzate coi totali della persona.
9. ✅ **Controllo di coerenza** «giorni del calcolo vs giorni trasferta»: banda verde/gialla dentro la
   finestra, informa e non blocca.
10. ✅ **Sincronizzazione Spese Trasferta → consuntivo del Bilancio** (D6-C): si sincronizza **solo**
    la metà spese, con una riga di calcolo per step marcata `trasferta:step:{id}` — le righe scritte a
    mano non si toccano. La metà «Risorse Atec» resta quella automatica dal timesheet reale, che è
    più affidabile dei nominativi digitati qui: rigenerarla come fa il prototipo sarebbe una regressione.
    Finché non ci sono step, la voce continua a leggere `projects.actual_travel_cost` come prima.
11. ✅ **KPI trasferta per commessa** — Giorni, Ore, Costi personale, Costi trasferta sulla card.

**Un numero, tre lettori.** Il costo trasferta a consuntivo lo leggono conto economico, pagina
`/bilancio` e dashboard commessa: la regola «foglio se c'è, altrimenti il campo a mano» sta in
`TravelPlanService` in due forme (C# e SQL) e la usano tutti e tre. Se divergessero tornerebbe
l'incoerenza che il blocco 4 ha sanato.

**Dimensione**: XL — era il blocco più grande del piano. **Fatto 04/08/2026 e VERIFICATO A RUNTIME**
(v63 applicata al DB di sviluppo, dati di prova ripuliti, stack rispento). Provato a video:
- **la prova d'obbligo**: quattro campi della riga-persona compilati di fila senza pause, tutti salvati;
- giorni con esclusione selettiva (20–26 lug = 7 → 6 senza domeniche → 5 senza sabati), alloggio
  Notti × Prezzo, Costi Personale = tariffa × ore, totali di step e Riepilogo coerenti;
- calcolatrice **Ore in numeri puri** («46», non «46,00 €») e **Auto a tre fattori** (120 km × 0,90 × 2 = 216 €);
- **controllo di coerenza** che passa da giallo a verde correggendo i giorni;
- sincronizzazione al Bilancio: voce «Spese Trasferta» a 906,00 € con la nota «dalla Gestione
  Trasferta», e stesso numero nella dashboard commessa;
- eliminazione step con conferma → righe e **fogli delle calcolatrici** ripuliti, voce di costo che
  torna a leggere il campo digitato a mano.

**Due difetti trovati solo provando** (corretti): `customers.name` non esiste — la colonna è
`company_name` — e la pagina delle card rispondeva 500; e con una sezione sola la finestra di calcolo
scriveva due volte «Totale ore».

Non provato: il push realtime `TravelChanged` (SignalR non negozia nel pane headless).

---

## BLOCCO 7 — Dashboard a cartelle ✅ FATTO 04/08/2026 (6 voci su 6)

1. ✅ **Flag «In dashboard» su `projects`** — migrazione **v64**, `in_dashboard TINYINT(1) DEFAULT 1`:
   all'aggiornamento tutte le commesse aperte sono già in dashboard, quindi la pagina parte come se il
   flag non esistesse. È un flag **condiviso** (sta sulla commessa, non sull'utente) come nel prototipo:
   per questo la scrittura parte dal livello **PM** — un tecnico che sfoltisce la propria vista la
   sfoltirebbe anche a chi quella commessa la deve seguire.
2. ✅ **Griglia di cartelle come pagina d'ingresso** — `/` si apre sulla scheda **«Cartelle»**
   (`DashboardFolders`), con la panoramica KPI storica spostata nella seconda scheda e la scelta
   ricordata. Le tre statistiche (milestone attive · avanzamento medio con barretta · periodo) sono
   **a video senza espandere**, calcolate lato server con le stesse regole di `avgAvanz`/`periodo`
   (le milestone senza avanzamento contano 0; il periodo è min/max su **entrambe** le date).
   Tutta la cartella è cliccabile, con link vero sul titolo per la tastiera.
3. ✅ **Righe della dashboard cliccabili** — `DashboardProjectRow` porta l'`id`, la riga apre la
   commessa col mouse e il **codice è un link vero** (da tastiera, senza rompere la semantica di tabella).
4. ✅ **Cartella «Pagamenti SAL» che si colora sulle scadenze** — pulsante-cartella in testata alla
   dashboard: **rosso** con scadenze raggiunte o incassi scaduti, **giallo** con soli pre-warning,
   badge col totale e tooltip che spiega il conteggio. Legge `/api/sal/summary` (stessa chiave
   react-query di `/sal`, quindi nessuna chiamata in più) e sparisce per chi non ha `nav.sal`.
5. ✅ **Link «Gestisci anagrafica attività» dal form commessa** — la griglia dell'anagrafica è stata
   estratta in `ActivityCatalogEditor` (usata dalla pagina **e** dal dialogo). Round-trip: la voce
   aggiunta lì torna **già spuntata**, la scelta fatta finora resta, le voci eliminate o disattivate
   escono dalla selezione da sole.
6. ✅ **Redirect automatico alla scheda SAL dopo la creazione** — `onSaved` porta l'id della commessa
   nuova e si atterra su `/commesse/{id}/sal`; chi il SAL non lo vede resta sulla scheda commessa.

**Il limite non è cablato.** Nel prototipo `DASH_MAX = 10` è una costante nel sorgente: qui sta in
`res_settings.dashboard_max_cards` (default 10, scrivibile solo dall'ADMIN, leggibile da tutti — lo
stesso store della soglia del Bilancio) ed è un campo in testata. Il limite **taglia le cartelle a
video, non l'elenco**: le commesse oltre il taglio non finiscono fra le escluse, restano fuori schermo
finché non si libera un posto, con la stessa nota esplicativa del prototipo.

**Dimensione**: M. **Fatto 04/08/2026 e VERIFICATO A RUNTIME** (migrazione v64 applicata al DB di
sviluppo, dati di prova ripuliti, stack rispento). `dotnet build` · `npm run build` · `eslint` 0 errori.
Provato a video:
- **migrazione v64** applicata all'avvio (`colonna in_dashboard su projects (nuova)`, schema a v64) con
  entrambe le commesse esistenti già a 1: dopo l'aggiornamento la pagina si comporta come prima;
- **cartelle come pagina d'ingresso** con le tre statistiche **senza espandere**: 33 milestone, 17%,
  26/04/26 → 31/07/26 — gli stessi numeri della scheda Milestones — e la commessa senza milestone che
  mostra `0 / — / —` invece di zeri finti;
- **spunta «In dashboard»**: la cartella sparisce, compare la fascia «Commesse non in dashboard (1)»
  col chip, `projects.in_dashboard` va a 0 e il chip la rimette a posto. **La spunta non apre la
  commessa**: era il rischio vero della cartella tutta cliccabile;
- **limite**: portato a 1 → una sola cartella e la nota «di 2», con la commessa tagliata che **non**
  finisce fra le escluse; valore persistito in `res_settings`;
- **cartella «Pagamenti SAL»**: neutra senza scadenze, **rossa** con una scadenza superata, **gialla**
  con solo pre-warning, badge col conteggio (righe SAL di prova create e poi cancellate);
- **righe della Panoramica cliccabili** (riga → `/commesse/25`, codice come link vero) e scelta della
  scheda ricordata;
- **round-trip anagrafica attività**: deselezionata una voce (31), aggiunta una voce dal dialogo
  dentro il form → **32**, con la voce nuova già spuntata e la deselezione rispettata;
- **atterraggio sul SAL** dopo la creazione: `/commesse/26/sal` con il SAL vuoto creato al volo.

**Una rifinitura trovata solo a video** (fatta): con il limite a 1 la nota diceva «Visualizzate le
prime 1 commesse», che in italiano non si può leggere. Ora al singolare.

Non provato: il push realtime `ProjectsChanged` sulla dashboard (SignalR non negozia nel pane
headless — è la stessa limitazione dei blocchi 4/5/6).

---

## Riepilogo

| Blocco | Contenuto | Dimensione | Migrazioni DB |
|---|---|---|---|
| 0 ✅ | Bug (date import, unicità codice/dipendenti, stampa) — FATTO 04/08 | S | nessuna (guardie applicative; UNIQUE rinviato) |
| 1 ✅ | Rifiniture milestone + Gantt, editor data a segmenti — FATTO 04/08 (10/10) | M | — |
| 2 | Import/Export CSV + XLSX server-side | M-L | — |
| 3 ✅ | Gantt viste salvate + export Excel, rifiniture SAL — FATTO 04/08 | L | **v60** `milestone_gantt_views` |
| 4 ✅ | Bilancio commessa — FATTO 04/08 (7/7), verificato a runtime | L | **v61** `project_order_lines`, `projects.sale_total`, `ddp_officina_items.work_type`, `nav.bilancio` |
| 5 ✅ | Calcolatrici a righe + anagrafica tariffe — FATTO 04/08 (6/6), verificato a runtime | L | **v62** `project_calc_sheets`, `project_calc_rows`, tariffe `HOURLY_RATE` |
| 6 ✅ | Gestione Trasferta — FATTO 04/08 (11/11), verificato a runtime | XL | **v63** `travel_steps`, `travel_step_rows`, `project_calc_rows.multiplier`, `nav.trasferta` |
| 7 ✅ | Dashboard a cartelle — FATTO 04/08 (6/6), verificato a runtime | M | **v64** `projects.in_dashboard` |

**Vincoli trasversali** (regole già in vigore sul progetto, valgono per ogni blocco):
- ambiente condiviso → real-time SignalR + concurrency token su ogni funzione collaborativa; le viste
  salvate e gli spegnimenti vanno **lato server**, non in localStorage;
- ogni griglia nuova ha menu «Colonne» con `usePersistedColumnVisibility` a chiave **versionata**;
- ogni tendina alimentata da un'anagrafica usa `LookupCombobox`, non `Select`;
- importi con `euro()` di `@/lib/format`; date gg/mm/aa con `formatDateShort`;
- ogni elimina/disattiva/ripristino chiede conferma con `useConfirm`.

**Cose in cui ATEC PM è già più avanti del prototipo — non regredire**: import additivo con deduplica,
risorse a consuntivo dal timesheet reale, colori configurabili degli stati pagamento, preservazione dei
valori storici «(non in anagrafica)», voci di trasferta tipizzate a preventivo, zoom Gantt a 3 livelli,
export CSV del Prospetto SAL, backup .zip DB+file con ripristino selettivo.
