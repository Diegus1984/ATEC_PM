# ATEC PM — Bug da sistemare

Legenda stato: `[ ]` aperto · `[~]` in corso · `[x]` risolto · `[-]` non bug / wontfix

---

## Come registrare un bug

Copia il blocco sotto e compila. Una riga per campo, niente romanzi.

```markdown
### BUG-NNN — Titolo breve
- **Stato:** [ ]
- **Data:** YYYY-MM-DD
- **Modulo:** (es. Preventivi / Commesse / Server)
- **Passi:** 1. … 2. …
- **Atteso:** …
- **Ottenuto:** …
- **Errore/log:** (opzionale)
- **Note fix:** (compilare quando risolto)
```

---

## Elenco

### BUG-016 — Chat: «Aggiungi partecipanti…» dal menu contestuale non fa niente
- **Stato:** [x] corretto il 16/08/2026, riprodotto e riverificato a runtime, **IN PRODUZIONE dal 16/08/2026** (build `index-Cbvn1rJg.js`, 138 test verdi, `health/ready` ok)
- **Data:** 2026-08-16
- **Modulo:** Client web (`features/chat/ChatWorkspace.tsx`, `ChatListRow`)
- **Passi:** 1. Aprire la pagina Chat 2. Tasto destro su una riga della lista 3. Scegliere «Aggiungi partecipanti…»
- **Atteso:** si apre il pannello «Partecipanti» della chat
- **Ottenuto:** non succede niente. Nessun errore, nessuna richiesta al server. Le altre due voci dello stesso menu («Esci dalla chat», «Cancella») funzionano.
- **Errore/log:** nessuno.
- **Note fix:** il pannello **si apre davvero, e si richiude un decimo di secondo dopo** — troppo in fretta perché si veda, e per giunta lontano dal punto del clic (è ancorato al pulsante «N partecipanti» dell'intestazione, non alla riga). La catena: `onSelect` gira *prima* che il menu si chiuda (Radix lo esegue in `dispatchDiscreteCustomEvent`, flush sincrono), quindi il Popover monta a menu ancora aperto; subito dopo il menu si chiude e il suo `FocusScope`, allo smontaggio, **rimette il fuoco dove stava prima del tasto destro** — la riga della lista; quel `focusin` arriva a pannello già aperto e il `DismissableLayer` del Popover, che è **non modale**, lo legge come «interazione fuori» → `onDismiss()` → pannello chiuso. La guardia scritta a mano in `ChatParticipantsPopover.tsx:104` non para il colpo: cerca `[data-slot=popover-content]`, ma il bersaglio è il `<button>` della riga.
  **Perché le altre due voci si salvano:** aprono un `AlertDialog` (`useConfirm`), e Radix gli impone `preventDefault()` su `onInteractOutside`. È l'unica voce di menu del progetto che apre un **popover** invece di un dialogo — da qui l'asimmetria.
  **Correzione:** in `ChatListRow`, `ContextMenuContent onCloseAutoFocus` sopprime il ripristino del fuoco **solo** quando è stata quella voce ad aprire il pannello (ref alzato nell'`onSelect`). Con Esc o con le altre voci il fuoco torna alla riga come prima.
  **Verifica a runtime** (app vera, dati di prova in locale): prima della correzione il pannello passava a `data-state=closed` da solo entro ~500 ms; dopo, resta `open` a 100/300/600/1000 ms. Riprovate anche le altre due voci: `POST /leave` e `DELETE /api/chat/{id}` con la chat giusta, conferma col titolo giusto, riga via dalla lista. Provato sia col dev server sia col **bundle di produzione** (`index-C_u9fb0o.js`, quello servito da 192.168.2.150), che si comporta identico.
  🪤 Per chi verificherà cose simili: senza il pannello Browser a video Chrome non compone frame, quindi i menu Radix chiusi **non vengono mai smontati** e restano nel DOM. Un `querySelector('[role=menu]')` pesca il menu *vecchio*: così si arriva a cancellare la chat sbagliata credendo di aver trovato un bug che non esiste. Azzerare le animazioni con uno `<style>` iniettato e prendere sempre l'ultimo layer con `data-state=open`.

### BUG-015 — Distribuzione preventivi: si possono modificare le righe materiale di un preventivo altrui
- **Stato:** [x] corretto, verificato il 15/08/2026 e **IN PRODUZIONE dal 16/08/2026** (deploy delle 11:4x)
- **Data:** 2026-08-15
- **Modulo:** Server (`Services/CostingDataService.cs:201`, `Controllers/QuoteCostingController.cs:370`)
- **Passi:** 1. Autenticarsi come un utente qualsiasi che possa aprire i preventivi 2. Chiamare `PUT /api/quotes/{unPreventivoQualsiasi}/material-items/{id}/distribution` (o l'equivalente `distributions/batch`) passando l'**id di una riga materiale che appartiene a un ALTRO preventivo** 3. Riaprire il preventivo della vittima
- **Atteso:** la riga non viene toccata: l'id non appartiene al preventivo indicato nell'indirizzo
- **Ottenuto:** la riga viene aggiornata (contingenza, margine, pin, ombreggiatura), e l'endpoint risponde comunque «Salvate N distribuzioni» senza controllare quante righe abbia toccato davvero. Sono percentuali che concorrono al prezzo di offerta: cambiate di nascosto su un preventivo altrui, non se ne accorge nessuno.
- **Errore/log:** nessuno.
- **Note fix:** trovato dal censimento N+1 di E3 (15/08/2026) e **verificato a mano**. La prova che è una dimenticanza e non una scelta sta nella riga sopra, nello stesso metodo: le *sezioni* filtrano con `WHERE id=@Id{ownerFilter}` (`CostingDataService.cs:193`) e la sezione singola con `AND quote_id=@quoteId` (`QuoteCostingController.cs:362`); solo le due query sulle *righe materiale* si fermano a `WHERE id=@Id`. Correzione: vincolo di appartenenza via la sezione madre — `AND section_id IN (SELECT id FROM quote_material_sections WHERE quote_id=@qid)` — con la variante `project_material_sections`/`project_id` per lo scope Project. ⚠️ Da fare con un test: sbagliare la relazione fa smettere di salvare il pannello Distribuzione, che è peggio del buco. Va chiuso **insieme** all'accorpamento N+1 di quelle stesse righe (vedi [CENSIMENTO-N1-E3.md](CENSIMENTO-N1-E3.md)), non prima e non dopo.
- **Correzione applicata (15/08/2026, da verificare):** vincolo di appartenenza via sezione madre in
  `CostingDataService.SaveDistributionsBatch` (`AND section_id IN (SELECT id FROM {materialSectionsTable} WHERE {owner}=@qid)`,
  con le varianti quote/project) e in `QuoteCostingController.UpdateMaterialItemDistribution`
  (`AND section_id IN (SELECT id FROM quote_material_sections WHERE quote_id=@quoteId)`). Compila.
  Due test in `Permessi/AppartenenzaPreventivoTests.cs`: (1) la riga di un altro preventivo resta
  intatta; (2) **la riga del proprio preventivo continua a salvarsi** — il secondo è quello che
  conta di più, perché una relazione sbagliata farebbe smettere di salvare il pannello Distribuzione.
  **Verificato**: i due test passano, e la correzione è stata provata **al contrario** — rimesso il
  filtro com'era (`WHERE id=@Id`), il test di sicurezza torna rosso mentre quello del salvataggio
  resta verde, che è esattamente il comportamento atteso: il buco non impediva di salvare le proprie
  righe, permetteva di toccare quelle altrui.
  *(La verifica si era fermata un'ora perché MySQL locale era caduto — crash di `mysqld` su
  `ALTER TABLE project_chats MODIFY COLUMN project_id INT NULL`, `DIEGO_PC.err`; riavviato il
  servizio, lo schema era integro.)*
  **Deployato il 16/08/2026** insieme alle modifiche della chat.

### BUG-014 — Anomalie ore: la stessa notifica «Ore anomale» rinasce a ogni giro (fino a 8 volte)
- **Stato:** [x] corretto il 16/08/2026, 138 test verdi, **IN PRODUZIONE dal 16/08/2026** (v93 applicata in 26 ms).
  Il backfill non ha ricostruito nessuna giornata perché in produzione le notifiche `TIMESHEET_ANOMALY`
  erano **zero**: la correzione è arrivata prima che le copie si vedessero.
- **Data:** 2026-08-15
- **Modulo:** Server (`Services/NotificationService.cs`, `CheckTimesheetAnomalies`, dedup ~riga 360)
- **Passi:** 1. Registrare più di 10 ore su un giorno **diverso da oggi** (es. il 13/08, compilando il timesheet la mattina del 14/08 — il caso normale) 2. Lasciare girare il controllo notifiche (ogni 6 ore) 3. Guardare la campanella
- **Atteso:** una sola notifica «Ore anomale — Mario Rossi» al giorno, come per tutte le altre scadenze
- **Ottenuto:** una notifica **a ogni giro**. Il dedup di questo controllo — unico fra gli otto — cerca una notifica esistente creata **nel giorno lavorato** (`created_at` nella giornata di `te.work_date`), non nella giornata di oggi: se le ore sono state registrate il giorno dopo, non trova mai la propria notifica e ne crea un'altra. La finestra è `work_date >= CURDATE()-2` e il giro è ogni 6 ore → fino a ~8 copie identiche.
- **Errore/log:** nessuno: dal punto di vista del server è tutto riuscito.
- **Note fix:** difetto **preesistente**, trovato dalla revisione avversariale del blocco E2 il 15/08/2026 (la riscrittura E2 di quel punto è equivalente al `DATE(n.created_at) = te.work_date` di prima — verificato su 259.201 istanti a cavallo di mezzanotte, 0 divergenze — quindi non l'ha introdotto né peggiorato). Correzione non banale: allinearlo agli altri sette usando `CURDATE()` **richiede prima di portare il giorno dentro il riferimento** (es. `reference_type = 'EMPLOYEE_DAY'` con una chiave che contiene la data), altrimenti due giorni anomali della stessa persona si annullerebbero a vicenda e ne comparirebbe uno solo.
- **Correzione applicata (16/08/2026):** la giornata segnalata è entrata nel riferimento con una
  colonna, non con un tipo nuovo: `notifications.reference_date DATE NULL` (migrazione
  `M093_AnomalieOrePerGiorno`, valorizzata solo da questo avviso). Il dedup ora è
  «(persona, giorno lavorato) già segnalata?», **senza finestra su `created_at`**: non è un
  promemoria che si rinnova ogni mattina, è il resoconto di una giornata chiusa. Il controllo è
  uscito dal `BackgroundService` ed è diventato `NotificationService.SegnalaOreGiornaliereAnomale()`
  — dentro un metodo privato non era raggiungibile da nessun test, ed è il motivo per cui il
  difetto è vissuto indisturbato.
  🪤 **Il pezzo che mancava all'analisi.** La pulizia dei promemoria superati
  (`CleanResolvedNotifications`, punto 0, che parte da sola all'apertura della campanella) tiene una
  riga per (destinatario, tipo, riferimento): con il riferimento «persona», cancellava l'anomalia del
  13 nel momento in cui nasceva quella del 14 — cioè la perdita temuta come rischio della correzione
  **c'era già in produzione**. Quel raggruppamento ora include la giornata (`<=>` e non `=`, o con
  `reference_date` NULL la pulizia avrebbe smesso di funzionare su tutti gli altri tipi).
  La v93 ricostruisce anche la giornata delle copie già nate leggendola dal testo del messaggio
  (forma fissa «12,5h registrate il 13/08/2026») e ne tiene una per giornata; quelle di forma diversa
  restano a NULL e si comportano come prima. Sei test in `Notifiche/AnomalieOreTests.cs`, più il
  guardiano di E2 aggiornato (`IlDedupDelleNotifiche_restaUnaFinestraDiUnGiornoInSettePunti`), che
  ora **vieta esplicitamente** la finestra ancorata a `work_date`.

---

*Ultimo aggiornamento: 2026-08-16 — nessun bug aperto: BUG-014 (v93), BUG-015 e BUG-016 sono tutti in produzione.*
