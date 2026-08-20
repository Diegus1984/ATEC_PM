# Censimento N+1 — blocco E3

> Prodotto il 15/08/2026 da un rilevatore deterministico (240 candidati in 45 file) più otto
> classificazioni indipendenti sul codice vero, con verifica avversariale sui candidati in cima.
> **Non è la lista di lavoro definitiva**: va incrociata con la misura di E1 (dal 31/08). Serve a
> sapere *dove guardare* quando quei numeri arriveranno, e soprattutto a non correggere i punti
> che non sono difetti.

## Il quadro

| Classe | Quanti | Cosa vuol dire |
|---|---:|---|
| **VERO_LETTURA** | 35 | stessa SELECT ripetuta a ogni giro: si accorpa con `WHERE id IN` |
| **VERO_SCRITTURA** | 127 | INSERT/UPDATE ripetuti: accorpabili, ma con più cautela |
| **LEGITTIMO** | 51 | il ciclo deve chiamare una volta per giro, o gira una volta al mese |
| **FALSO_POSITIVO** | 27 | query fuori dal ciclo, o codice che fa già la cosa giusta |

**Il dato che cambia il piano di lavoro**: dei 240 candidati, **78 non sono difetti**.
Dei 162 veri, per impatto: **2 alto**, 50 medio, 110 basso.
Il piano parlava di «166 punti»: quelli su cui vale la pena lavorare sono un ordine di grandezza meno.

⚠️ **Un difetto di SICUREZZA trovato per strada, non di prestazioni** — vedi BUG-015 in [BUGS.md](BUGS.md):
la UPDATE delle righe materiale (`CostingDataService.cs:197` e l'endpoint singolo
`QuoteCostingController.cs:370`) filtra per `id` e basta, **senza vincolo di appartenenza al
preventivo**: un utente autenticato può sovrascrivere righe di qualunque altro preventivo passando
id arbitrari. Chi tocca quelle righe per E3 deve chiudere anche quello.

## Impatto ALTO — 2 punti

Entrambi passati da un verificatore avversariale che ha risalito i chiamanti fino al client web.

### `Services/CostingDataService.cs`:197 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Aggiorna contingenza/margine/pin/ombreggiatura di una riga materiale, una UPDATE per riga
- **Quante volte gira**: righe materiale del preventivo: decine-centinaia, a OGNI modifica di percentuale nel pannello distribuzione
- **Correzione**: Una sola UPDATE {materialItemsTable} SET contingency_pct = CASE id WHEN .. THEN .. END, margin_pct = CASE ..., contingency_pinned = CASE ..., margin_pinned = CASE ..., is_shadowed = CASE ... WHERE id IN @Ids. È l'accorpamento che rende davvero "batch" un endpoint che oggi si chiama batch ma esegue N statement.
- **Rischio**: Medio-alto per un motivo di sicurezza, non di dati: questa UPDATE NON ha filtro sul preventivo (WHERE id=@Id e basta, a differenza delle sezioni), quindi chi riscrive la query deve almeno mantenere lo stesso comportamento e valuterebbe bene di aggiungere il vincolo di appartenenza. Per il resto: transazione condivisa col chiamante e lista vuota/null da intercettare prima dell'IN.
- **Note**: Stesso ciclo-fratello della voce 189, ma su una lista molto più lunga: le righe materiale di un preventivo IMPIANTO sono decine-centinaia e il client le rimanda TUTTE a ogni singolo edit (costing-panels.tsx, persist). È il candidato con il rapporto beneficio/rischio migliore dei miei 30: un endpoint interattivo che oggi fa centinaia di round-trip per un clic.

> **Verifica avversariale** — confermato: True · impatto rivisto: **medio** · correzione praticabile: True
>
> CLASSE CONFERMATA (VERO_SCRITTURA), IMPATTO DECLASSATO da alto a medio.
> 
> 1) Quante iterazioni davvero. Unico chiamante server: QuoteCostingController.cs:376 [HttpPut("distributions/batch")] -> SaveDistributionsBatch(..., CostingScope.Quote, ...). Il ramo CostingScope.Project di SaveDistributionsBatch e' CODICE MORTO: nessun endpoint in ProjectCostingController lo chiama (grep su "distributions/batch" e "CostingScope." nel server: solo la Quote). Lato client: atec-pm-web/src/lib/api/quote-costing.ts:308 saveDistributionsBatch, usata SOLO da features/preventivi/costing-panels.tsx:181 (funzione persist del DistributionPanel), montata da features/preventivi/CostingTree.tsx:171. Le righe passate sono `sections = enabledCost` (sezioni costo abilitate, per giunta deduplicate per nome in costing-panels.tsx:144-146 -> tipicamente 3-10) e `materialItems = data.materialSections.flatMap(s => s.items)` filtrate su totalSale > 0. Poiche' TotalSale = Quantity*UnitCost*MarkupValue (ProjectCosting_DTOs.cs:120) e le varianti importate dal catalogo nascono con quantity 0 (QuoteCostingController.cs:239), le varianti figlie NON entrano: restano le righe materiale con quantita' valorizzata, cioe' decine (un impianto grosso puo' arrivare a 100+), non centinaia di norma. Quindi il ciclo gira su ~15-60 UPDATE per chiamata: il numero e' reale.
>    Ma il PERCORSO non e' caldo: non e' un'apertura di pagina (la lettura, GetCostingData, e' gia' fatta bene con query raggruppate e .Where in memoria), e' il salvataggio del pannello Distribuzione dentro il modulo Preventivi/Commerciale, toccato da pochi utenti quando si prezza un'offerta. In piu' la chiamata e' fire-and-forget lato client (`void saveDistributionsBatch(...).catch(...)`, costing-panels.tsx:181): l'utente non aspetta la risposta, non c'e' spinner ne' refetch. Decine di UPDATE per chiave primaria dentro una transazione su MySQL in LAN = qualche decina di ms, invisibili. Criterio "alto = percorso frequente E decine/centinaia di elementi": qui manca la frequenza -> medio.
> 
> 2) Correzione praticabile: SI, senza insidie. Le N UPDATE sono indipendenti (chiave primaria disgiunta), nessuna dipende dal giro precedente, niente LAST_INSERT_ID, nessun valore progressivo: i valori arrivano gia' calcolati dal client (computeDist in costing-distribution.ts). L'ordine e' irrilevante e la transazione si conserva (uno statement solo e' anche piu' atomico). Cautele da mettere per iscritto: (a) lista vuota -> non emettere lo statement, `IN ()` e' SQL non valido (oggi il foreach su `?? new()` semplicemente non fa nulla); (b) id duplicati nella request -> col foreach vince l'ultimo, con CASE vince il primo WHEN: normalizzare a monte; (c) i CASE vanno costruiti con parametri numerati (p0_cont, p0_marg, ...), Dapper espande `IN @Ids` ma non le braccia del CASE, e servono ELSE col nome della colonna per non azzerare righe non elencate; (d) 5 colonne x ~60 righe = ~300 parametri, ampiamente sotto il limite MySQL.
> 
> 3) COSA C'E' DI PEGGIO nello stesso metodo, che il rilevatore non vede — ed e' un difetto di sicurezza, non di prestazioni: nel ciclo delle righe materiale (CostingDataService.cs:199-202) la UPDATE e' `WHERE id=@Id` e BASTA, senza `ownerFilter`. Il ciclo delle sezioni sopra (riga 193) invece lo mette (`AND quote_id=@qid`). Quindi qualunque utente autenticato che possa aprire un preventivo puo' sovrascrivere contingency/margine/pin/ombreggiatura di righe materiale di QUALSIASI altro preventivo passando id arbitrari nel body; lo stesso buco c'e' nell'endpoint singolo QuoteCostingController.cs:370 (UpdateMaterialItemDistribution, `WHERE id=@Id`, il quoteId di rotta non e' usato). La riscrittura in batch DEVE aggiungere `AND section_id IN (SELECT id FROM quote_material_sections WHERE quote_id=@qid)`. Collegato: l'endpoint risponde sempre "Salvate N distribuzioni" senza controllare le righe realmente toccate, quindi un id sbagliato passa in silenzio.
>    Altro, meno grave ma piu' redditizio della SQL stessa: `persist()` parte a OGNI blur di una cella percentuale e a ogni toggle ombreggiatura, e rispedisce SEMPRE TUTTE le righe (non solo quella cambiata), senza debounce e senza await — due modifiche rapide sono due PUT concorrenti senza ordine garantito. Il guadagno vero e' meta' SQL (1 statement invece di N) e meta' client (inviare solo le righe cambiate / debounce).
>    NON e' un N+1 annidato: SaveDistributionsBatch ha un solo chiamante, l'action del controller, e non gira dentro nessun altro ciclo.
> 
> CONCLUSIONE: la voce e' vera e va corretta, ma non merita la cima della lista di lavoro E3 — va sotto i candidati che stanno sull'apertura di una pagina. Se invece la si tocca, la si tocchi per il filtro proprietario mancante, che e' il difetto serio nascosto in quelle righe.

### `Services/TravelFromTimesheet.cs`:104 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: per ogni giornata di cantiere (persona+giorno+fase) esegue un INSERT ... ON DUPLICATE KEY UPDATE su travel_step_rows
- **Quante volte gira**: tutte le giornate di cantiere STORICHE della commessa, non solo quelle appena toccate: 5 persone x 60 giorni x fasi = facilmente 200-500 righe su una commessa di cantiere avviata
- **Correzione**: un solo INSERT ... VALUES (...),(...),(...) ON DUPLICATE KEY UPDATE costruito con la lista di parametri (Dapper accetta un IEnumerable ma esegue comunque N comandi: qui serve la SQL multi-VALUES scritta a mano), oppure INSERT ... SELECT direttamente dalla vista v_timesheet_with_section filtrata, saltando del tutto il viaggio dei dati in memoria
- **Rischio**: tre cose si rompono facilmente. 1) il conteggio `righeToccate == 1` per contare le nuove: in multi-VALUES MySQL ritorna un totale aggregato (1 per inserita, 2 per aggiornata) e la distinzione sparisce - l'Esito e il messaggio a video ne dipendono. 2) il sort_order calcolato con la subquery MAX(sort_order)+1 sulla STESSA tabella di destinazione: in un INSERT multi-VALUES MySQL rifiuta la subquery sulla tabella target, va precalcolato per step in memoria. 3) l'ordine: travel_days vale 1 solo alla PRIMA riga della giornata di quella persona (HashSet primaDellaGiornata) e l'ordinamento della query di partenza decide chi e la prima - se si accorpa bisogna conservare esattamente quell'ordine.
- **Note**: e il candidato peggiore del gruppo. Rebuild() non gira su richiesta: TimesheetController.RigeneraTrasferta lo chiama a OGNI salvataggio di ore (riga 403-408, su 1-2 commesse per salvataggio) e PhasesController a ogni rinomina di fase. Ogni singola ora imputata da un tecnico riscrive per intero tutte le giornate di cantiere della commessa, una INSERT alla volta. Con ~50.000 righe di timesheet l'anno e decine di salvataggi al giorno e il ciclo che paga di piu. Da notare che e anche un N+1 annidato di fatto: il ciclo esterno e quello sulle commesse in RigeneraTrasferta.

> **Verifica avversariale** — confermato: True · impatto rivisto: **alto** · correzione praticabile: True
>
> Confermato VERO_SCRITTURA ad alto impatto, ma per un motivo più forte di quello dichiarato: il ciclo (riga 104) gira su TUTTE le giornate di cantiere storiche della commessa — la query di riga 57-69 non ha filtro temporale — e viene rieseguito a OGNI salvataggio di una singola riga di ore. Prove: TimesheetController.cs:382 chiama RigeneraTrasferta su ogni POST /api/timesheet e :440 su ogni DELETE; il client salva una riga per volta (atec-pm-web/src/features/timesheet/TimesheetEntryDialog.tsx:159, un POST per dialog). Il ciclo interno su `commesse` (TimesheetController.cs:403) è un HashSet di massimo 2 elementi, quindi NON è un N+1 annidato, ma può raddoppiare il costo. Altri chiamanti sporadici: PhasesController.cs:498 e :514 (rinomina fase/assegnazioni), TravelController.cs:81 (pulsante manuale). Precisazione: è alto solo sulle commesse di cantiere (sezioni DA_CLIENTE); sulle ore «in sede» giornate è vuota. In assoluto oggi valgono decine di ms su MySQL locale, ma il costo cresce senza limite con lo storico della commessa e si paga a ogni imputazione di 35 persone. Cose peggiori non viste: (a) SyncToBudget (riga 170) gira dentro ogni Rebuild e a valle ProjectCalcSheets.Save (ProjectCalcSheets.cs:124-155) cancella e reinserisce riga per riga il foglio Riepilogo Costi alzando row_version; (b) il difetto strutturale vero è che il metodo ricostruisce l'intero storico per una modifica puntuale — l'accorpamento è un guadagno costante, lo scoping su persona/giorno toccati sarebbe di ordine di grandezza; (c) il ciclo degli step (riga 74, query 77/83/92, 2 query per fase, 5-30 fasi) va corretto insieme o restano 60 round-trip.

## Impatto MEDIO — 50 punti

### `Controllers/CodexController.cs`:393 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: scrive il codice nuovo sulla riga Codex solo se nessun altro l'ha gia ricodificata
- **Quante volte gira**: decine-centinaia, quante le righe selezionate
- **Correzione**: UPDATE codex_items SET codice_nuovo = CASE id WHEN ... END WHERE id IN @Ids AND (codice_nuovo IS NULL OR codice_nuovo=''), preceduto da una SELECT degli id gia ricodificati per sapere quali contare come 'saltati'
- **Rischio**: ALTO: la WHERE per riga e la guardia di concorrenza (check-and-set atomico) e il conteggio assigned/skipped e per riga; codice_nuovo ha un vincolo UNIQUE, quindi un solo UPDATE massivo che sbatte su un duplicato fa fallire TUTTO il lotto invece di saltare una riga sola. Da accorpare con molta cautela, o da lasciare com'e
- **Note**: stesso ciclo della riga 393, seconda query. E accorpabile sulla carta ma la forma attuale sta comprando concorrenza sicura: e il candidato dove il guadagno rischia di non valere il pericolo

### `Controllers/CodexController.cs`:393 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: libera la prenotazione della riga appena trattata
- **Quante volte gira**: decine-centinaia, quante le righe non saltate
- **Correzione**: accumulare i ResId trattati in una lista e fare un solo DELETE FROM codex_reservations WHERE id IN @Ids prima del tx.Commit()
- **Rischio**: le righe che entrano nel `continue` di riga 405 NON devono finire nella lista (la loro prenotazione resta viva apposta); guardia sulla lista vuota; la DELETE deve restare dentro la stessa transazione
- **Note**: stesso ciclo della riga 393, terza query. E la piu semplice delle tre da accorpare e non tocca la concorrenza

### `Controllers/CodexController.cs`:393 — VERO_LETTURA
- **Cosa fa nel ciclo**: verifica che la prenotazione della riga esista, sia attiva e porti esattamente quel codice
- **Quante volte gira**: righe selezionate dall'operatore per l'assegnazione massiva dei codici Codex: decine, fino a centinaia
- **Correzione**: una sola SELECT id, reserved_code FROM codex_reservations WHERE id IN @ResIds AND status='RESERVED' AND expires_at > NOW() prima del ciclo, poi dizionario id->codice consultato in memoria
- **Rischio**: la NOW() verrebbe valutata una volta sola invece che a ogni giro: su un lotto lungo una prenotazione potrebbe scadere durante il ciclo e passare lo stesso (accettabile: tutto e dentro una transazione aperta a inizio richiesta)
- **Note**: tre query per riga selezionata (398 + 409 + 416) dentro una sola transazione: e il ciclo piu caro di questo gruppo su un gesto che l'utente fa davvero (pulsante Assegna della ricodifica massiva). Questa e l'unica delle tre puramente di lettura, quindi la piu facile da togliere

### `Controllers/CodexController.cs`:821 — VERO_LETTURA
- **Cosa fa nel ciclo**: cerca ogni codice della lista importata dentro codex_items (18.000 articoli)
- **Quante volte gira**: righe della distinta importata da file STEP/SolidWorks: decine, fino a qualche centinaio; l'utente aspetta guardando l'anteprima
- **Correzione**: una sola SELECT id, codice, descr FROM codex_items WHERE codice IN @Codes (codici gia ripuliti) prima del ciclo, poi dizionario codice->articolo; il ciclo diventa una lookup in memoria
- **Rischio**: basso; attenzione ai codici duplicati nella lista importata (il dizionario va costruito con un ToLookup o ignorando i doppioni) e alla lista vuota
- **Note**: caso da manuale di N+1 in lettura, su un percorso che l'utente attraversa davvero (anteprima import composizione). E il gemello esatto della riga 917 in ImportCommit: le due vanno corrette insieme, o l'anteprima e il salvataggio smettono di dire la stessa cosa

### `Controllers/CodexController.cs`:821 — VERO_LETTURA
- **Cosa fa nel ciclo**: per i codici non trovati nel Codex, li cerca in catalog_items
- **Quante volte gira**: solo le righe non trovate nel Codex: da poche a tutte, secondo il file importato
- **Correzione**: seconda SELECT unica: SELECT id, description FROM catalog_items WHERE code IN @CodiciNonTrovati, eseguita dopo aver risolto il Codex, poi lookup in memoria
- **Rischio**: basso; va mantenuto l'ordine di precedenza (prima Codex, poi catalogo) e il ramo 'Articolo non trovato'
- **Note**: stesso ciclo della riga 821, seconda query. Va corretto insieme al candidato 827: da soli si dimezza il problema

### `Controllers/CodexController.cs`:911 — VERO_LETTURA
- **Cosa fa nel ciclo**: per i codici non trovati nel Codex, li cerca in catalog_items, in transazione
- **Quante volte gira**: le sole righe non risolte dal Codex: da poche a decine
- **Correzione**: unica SELECT id, description FROM catalog_items WHERE code IN @CodiciNonTrovati prima del ciclo
- **Rischio**: basso; il ramo 'non trovato' lancia un'eccezione che fa rollback dell'intero import: il comportamento va conservato identico (primo codice mancante = tutto annullato)
- **Note**: stesso ciclo della riga 911, seconda query; gemello della riga 849

### `Controllers/CodexController.cs`:911 — VERO_LETTURA
- **Cosa fa nel ciclo**: cerca ogni codice della lista importata dentro codex_items, in transazione
- **Quante volte gira**: decine-centinaia di righe per import di composizione
- **Correzione**: una sola SELECT ... WHERE codice IN @Codes prima del foreach, dentro la stessa transazione, poi dizionario
- **Rischio**: basso, ma la query sta dentro una transazione aperta: accorpando si accorcia la finestra di lock, quindi il rischio e semmai positivo. Mantenere il ValidateHierarchy per riga (e in memoria, non tocca il DB)
- **Note**: identica per contenuto alla riga 827 (ImportPreview) ma nel commit: la stessa lista viene interrogata due volte, prima per l'anteprima e poi per il salvataggio. Sono due chiamate HTTP diverse, quindi il costo si paga due volte per ogni import

### `Controllers/CodexController.cs`:911 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: inserisce la nuova riga di composizione (figlio Codex o catalogo) sotto il parent
- **Quante volte gira**: le righe nuove dell'import: decine-centinaia
- **Correzione**: accumulare le righe nuove e fare un solo INSERT INTO codex_compositions ... VALUES (...),(...),... prima del commit
- **Rischio**: currentSort e un progressivo in memoria che decide l'ordine delle righe in distinta: va precalcolato. In piu l'accorpamento deve avvenire DOPO aver risolto le somme del ramo 952/961, altrimenti si insersice un figlio che andava sommato
- **Note**: stesso ciclo della riga 911, quinta query, ramo 'nuovo'. Le cinque query di questo ciclo (917, 934, 952, 961, 966) sono la stessa riscrittura: vanno affrontate come un blocco unico, non una alla volta

### `Controllers/CodexController.cs`:911 — VERO_LETTURA
- **Cosa fa nel ciclo**: cerca se il componente e gia figlio di questo parent, per sommare la quantita invece di duplicare la riga
- **Quante volte gira**: una per riga importata: decine-centinaia
- **Correzione**: una sola SELECT id, child_codex_id, child_catalog_id, quantity FROM codex_compositions WHERE parent_codex_id=@ParentId prima del ciclo (sono al massimo qualche decina di righe), poi confronto in memoria
- **Rischio**: ALTO se fatto male: il commento del codice dice che serve anche a intercettare il codice RIPETUTO NELLA STESSA LISTA, cioe deve vedere le righe inserite dai giri precedenti. La mappa in memoria va aggiornata a ogni INSERT del ciclo, altrimenti un codice ripetuto due volte nel file genera due righe invece di sommare le quantita. Attenzione anche al confronto NULL-safe (<=>) da riprodurre in C#
- **Note**: stesso ciclo della riga 911, terza query. E il candidato con la trappola: sembra una lettura banale da portare fuori, ma dipende dalle scritture del ciclo stesso

### `Controllers/ImportController.cs`:410 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni articolo marcato UPDATE nell'import da Easyfatt, esegue una UPDATE catalog_items ... WHERE id=@ExistingId.
- **Quante volte gira**: da centinaia a ~18.000 articoli (l'anteprima marca automaticamente Action=UPDATE tutti i duplicati, quindi in un reimport quasi tutte le righe finiscono su questo ramo)
- **Correzione**: Accorpare gli articoli con Action=UPDATE in blocchi da 200-500 con un unico INSERT INTO catalog_items (id, ...) VALUES ... ON DUPLICATE KEY UPDATE code=VALUES(code), description=VALUES(description), ... per blocco.
- **Rischio**: Con 18.000 righe fuori transazione, un accorpamento cambia la semantica del fallimento parziale (oggi si sa esattamente quante sono passate). Attenzione a easyfatt_id e a supplier_id risolto (ResolvedSupplierId può essere 0/NULL): un ON DUPLICATE KEY UPDATE scritto male azzererebbe il fornitore già collegato a mano su ATEC PM.
- **Note**: È il candidato con il volume più alto dell'ImportController: il catalogo Codex/articoli è di ~18.000 voci e la GET di anteprima (riga 357) propone GIÀ Action=UPDATE su ogni duplicato, quindi un reimport completo significa ~18.000 round-trip in una sola richiesta HTTP. Primo dei due rami dello stesso foreach (l'altro è riga 430).

### `Controllers/ImportController.cs`:410 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni articolo nuovo, esegue una INSERT INTO catalog_items con i dati Easyfatt e il fornitore già risolto in memoria.
- **Quante volte gira**: da centinaia a migliaia di articoli nuovi al primo import; poche decine nei reimport successivi
- **Correzione**: INSERT INTO catalog_items (...) VALUES (...),(...),... multi-riga a blocchi di 200-500 sulle sole righe con Action=INSERT.
- **Rischio**: Come per l'UPDATE: granularità del fallimento e diagnosi della riga colpevole. Va mantenuta la coerenza dei contatori Imported/Updated/Skipped restituiti al client.
- **Note**: Secondo ramo del foreach di riga 410. Va corretto insieme all'UPDATE: separando le due liste in due passate si accorpano entrambi senza toccare la logica di scelta del ramo.

### `Controllers/MilestonesController.cs`:182 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Riscrive sort_order di ogni milestone, una UPDATE per id, seguendo l'ordine dell'array arrivato dal client.
- **Quante volte gira**: tutte le milestone della commessa riordinate a video: 5-30 (nel Gantt possono essere di più, ma restano decine)
- **Correzione**: Una sola UPDATE project_milestones SET sort_order = CASE id WHEN 12 THEN 0 WHEN 7 THEN 1 ... END WHERE project_id=@Pid AND id IN @Ids, con il CASE costruito dall'indice della lista.
- **Rischio**: L'ordine è il dato stesso: il CASE deve mappare id→indice esattamente come fa order++. Attenzione che qui NON c'è transazione: oggi un errore a metà lascia l'ordine misto, e la riscrittura in una query sola in realtà migliora questo aspetto. Restano fuori dal CASE gli id che non appartengono alla commessa (li esclude già il WHERE project_id).
- **Note**: Drag&drop nel Gantt: ogni riordino spara N round-trip su una connessione senza transazione. È il candidato più concreto del file perché sta su un gesto interattivo, non su un import.

### `Controllers/MilestonesController.cs`:271 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Precarico da catalogo: una INSERT in project_milestones per ogni voce di activity_catalog scelta.
- **Quante volte gira**: le voci del catalogo attività selezionate (o tutte le attive se la lista è vuota): decine, tipicamente 10-40
- **Correzione**: Una sola INSERT ... SELECT dal catalogo: INSERT INTO project_milestones (project_id, descrizione, sort_order, source_catalog_id, created_by) SELECT @Pid, label, @Base + (ROW_NUMBER() OVER (ORDER BY sort_order, label)) - 1, id, @By FROM activity_catalog WHERE id IN @Ids (o is_active=TRUE), con @Base = il MAX(sort_order)+1 già calcolato a riga 266.
- **Rischio**: Il progressivo sortOrder++ va replicato con ROW_NUMBER() sullo STESSO ORDER BY della query di riga 258/262, altrimenti l'ordine delle milestone precaricate cambia. Va conservato anche il conteggio restituito nel messaggio («N milestone precaricate»), che con INSERT...SELECT diventa il valore di ritorno di Execute. Nota: nessuna transazione, quindi oggi un errore a metà lascia un precarico parziale.
- **Note**: Percorso frequente (si precarica ogni volta che si apre una commessa nuova) e la cardinalità è di decine. Il rimedio qui è elegante perché i dati da inserire sono già nel database: si può evitare del tutto di portarli in memoria.

### `Controllers/MilestonesController.cs`:318 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: È la INSERT delle singole milestone importate, vista dal ciclo ESTERNO sulle commesse.
- **Quante volte gira**: commesse agganciate (decine) × milestone per commessa (5-30) = da qualche centinaio a qualche migliaio di INSERT in un solo import
- **Correzione**: Una sola INSERT multi-VALUES per tutto l'import (o una per commessa se si vuole limitare la dimensione del comando), costruita accumulando le righe dei due cicli in una lista e scrivendola a blocchi di qualche centinaio.
- **Rischio**: Il progressivo sortOrder++ è per commessa e va ricostruito in memoria; il filtro seen.Add() (deduplica) deve continuare a girare riga per riga PRIMA di accodare. Tutto dentro la transazione già aperta a riga 314. Truncate e ClampAvanz vanno applicati prima dell'accodamento.
- **Note**: N+1 ANNIDATO: è lo stesso statement del candidato riga 340→348, ma qui contato sul ciclo esterno. È il punto in cui questo file fa più round-trip in assoluto (commesse × milestone). Resta impatto medio e non alto solo perché l'import del backup è un'operazione manuale una tantum, non una schermata.

### `Controllers/MoMController.cs`:315 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Riscrive sort_order riga per riga del foglio MoM, un UPDATE per ogni id ricevuto nell'ordine voluto.
- **Quante volte gira**: tutte le righe del verbale, quindi decine (10-60 azioni per MoM); il client manda SEMPRE l'elenco completo, anche spostando una riga sola
- **Correzione**: Un solo UPDATE mom_action_items SET sort_order = CASE id WHEN @Id0 THEN 0 WHEN @Id1 THEN 1 ... END WHERE mom_id=@MomId AND id IN @Ids, dentro la stessa transazione.
- **Rischio**: Va conservato il doppio filtro id + mom_id (è quello che ignora gli id non appartenenti al verbale); con CASE senza ELSE le righe non elencate devono restare intatte — il WHERE ... IN @Ids lo garantisce. Il controllo req.ItemIds.Count == 0 a monte impedisce già la lista vuota.
- **Note**: Ciclo di UPDATE identici che variano solo id e valore progressivo: caso da manuale di accorpamento con CASE WHEN. Sta sul drag&drop del foglio MoM, azione ripetuta più volte in una sessione di riordino, e ogni trascinamento costa oggi N round-trip in transazione.

### `Controllers/PhasesController.cs`:423 — VERO_LETTURA
- **Cosa fa nel ciclo**: Per ogni coppia (fase, sezione) da inserire legge il template e la sua sezione/sort_order con una JOIN su phase_templates + phase_template_sections.
- **Quante volte gira**: le fasi selezionate nel dialog «Aggiungi fasi da template» di una commessa: tipicamente 5-30, in un primo popolamento anche di più
- **Correzione**: Una sola query prima del ciclo: SELECT pt.id, pt.cost_section_template_id, pt.sort_order, pts.cost_section_template_id, pts.sort_order FROM phase_templates pt LEFT JOIN phase_template_sections pts ... WHERE pt.id IN @TemplateIds, messa in un dizionario per (templateId, sectionId) e risolta in memoria dentro il ciclo.
- **Rischio**: La COALESCE(@sid, pt.cost_section_template_id) va replicata fedelmente in memoria (sezione richiesta, altrimenti sezione principale della fase): sbagliandola le ore finiscono nella sezione sbagliata del Bilancio — è esattamente il punto che il commento v73 indica come delicato. Attenzione anche al caso row.TemplateId == 0 (template inesistente) che oggi fa `continue`.
- **Note**: Stessa SELECT ripetuta variando solo tid/sid: bersaglio tipico di E3. È la prima di TRE query nello stesso ciclo (con 438 e 446): oggi aggiungere 20 fasi costa ~60 round-trip dentro una transazione aperta.

### `Controllers/PhasesController.cs`:423 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Inserisce la riga project_phases per la coppia (fase, sezione) superstite dei due controlli.
- **Quante volte gira**: 5-30 INSERT per chiamata (quante fasi l'utente ha spuntato, meno quelle già presenti)
- **Correzione**: Accumulare le tuple superstiti e fare una sola INSERT INTO project_phases (project_id, phase_template_id, cost_section_template_id, sort_order) VALUES (...),(...),... a fine ciclo, dentro la stessa transazione.
- **Rischio**: Il contatore `inserted` alimenta il messaggio «N fasi aggiunte (M erano già in quella sezione)»: va ricalcolato dalla lunghezza della lista, non dal valore di ritorno della Execute. Lista vuota = nessuna INSERT.
- **Note**: Terza query dello stesso ciclo (con 426 e 438). Accorpando tutte e tre, l'inserimento massivo delle fasi passa da ~3N round-trip a 3 query totali; è il candidato più concreto del gruppo dopo il reorder MoM.

### `Controllers/PhasesController.cs`:423 — VERO_LETTURA
- **Cosa fa nel ciclo**: Per ogni item conta se la stessa fase esiste già sulla commessa nella stessa sezione, per non duplicarla.
- **Quante volte gira**: stesso ciclo della voce precedente: 5-30 giri; le fasi già presenti su una commessa sono 5-30
- **Correzione**: Una sola SELECT phase_template_id, cost_section_template_id FROM project_phases WHERE project_id=@pid prima del ciclo, caricata in un HashSet di coppie (con NULL come chiave a sé) e interrogata in memoria.
- **Rischio**: La condizione ha un ramo NULL esplicito (@sid IS NULL AND cost_section_template_id IS NULL): in memoria va riprodotta con int? e non con confronto secco, o si perde il caso «fase senza sezione». Inoltre il set va aggiornato man mano che si inseriscono le fasi, altrimenti due item identici nella stessa chiamata verrebbero inseriti due volte (oggi la seconda li vede già a DB perché l'INSERT è nella stessa transazione).
- **Note**: Secondo N+1 dello stesso ciclo di BulkCreate — lo segnalo separato ma va corretto insieme al 426, con la stessa pre-lettura. Il rischio del doppione nella stessa richiesta è il vero tranello della riscrittura.

### `Controllers/PhasesController.cs`:648 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni tecnico assegnato alla fase inserisce una riga in phase_assignments e, se la commessa è ACTIVE, gli crea una notifica.
- **Quante volte gira**: 1-5 tecnici per fase; chiamato dalla creazione fase (riga 399) e dal salvataggio assegnazioni (riga 495), non dentro altri cicli
- **Correzione**: Una sola INSERT INTO phase_assignments ... VALUES multi-tupla; e soprattutto una sola chiamata _notif.Create(...) con l'array completo dei destinatari invece di una per tecnico.
- **Rischio**: Le notifiche hanno titolo e testo identici per tutti i destinatari, quindi accorparle è sicuro; vanno però mantenute l'esclusione dell'utente corrente e il try/catch che impedisce a un errore di notifica di far fallire il salvataggio. L'INSERT deve restare dentro tx (le notifiche invece no, oggi come domani).
- **Note**: Il difetto vero non è l'INSERT (poche righe) ma _notif.Create dentro il ciclo: NotificationService.Create (Services/NotificationService.cs riga 31) APRE UNA NUOVA CONNESSIONE e fa 2 INSERT per chiamata, e ha a sua volta un ciclo sui destinatari — N+1 annidato che il rilevatore non vede, con una connessione al DB per tecnico assegnato aperta mentre la transazione del salvataggio è ancora in corso.

### `Controllers/ProjectsController.cs`:1596 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: quando il componente e gia in distinta, ne somma la quantita e lo aggancia al padre (UPDATE ddp_officina_items SET quantity = quantity + @Add, parent_officina_item_id = COALESCE(...))
- **Quante volte gira**: i figli diretti dell'assieme Codex importato che risultano gia presenti: da pochi a decine (la composizione di un assieme ha tipicamente 10-100 figli, divisi fra questo ramo e quello dell'INSERT)
- **Correzione**: raccogliere in memoria le coppie (existingId, addQty) e chiudere con un solo UPDATE ddp_officina_items SET quantity = quantity + CASE id WHEN 12 THEN 4 WHEN 15 THEN 2 ... END, parent_officina_item_id = COALESCE(parent_officina_item_id, @ParentRowId), composition_qty = COALESCE(composition_qty, CASE id ... END) WHERE id IN @Ids
- **Rischio**: medio. Il CASE WHEN va costruito con parametri, non per concatenazione (SQL injection e cache dei piani). Attenzione a due righe della composizione che puntano allo stesso codice normalizzato: oggi il secondo giro somma sopra il primo, con un CASE unico la seconda occorrenza cancellerebbe la prima - vanno pre-sommate in memoria. E non c'e transazione aperta in questo metodo: oggi un errore a meta lascia meta distinta aggiornata, accorpare in una query sola paradossalmente migliora anche questo.
- **Note**: vero N+1 di scrittura, ma soprattutto ANNIDATO: dopo l'UPDATE la riga 1613 chiama OfficinaRowSync.CongelaTipoDaStato che aggiunge SELECT+UPDATE per ogni figlio (3 query per giro invece di 1). E' un'azione manuale (import composizione da picker Codex), non l'apertura di una pagina, per questo non e alto.

### `Controllers/ProjectsController.cs`:1596 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: crea la riga di distinta officina per il componente non ancora presente e ne rilegge l'id con LAST_INSERT_ID()
- **Quante volte gira**: i figli nuovi dell'assieme importato: decine (fino a ~100 su un assieme grosso)
- **Correzione**: un solo INSERT INTO ddp_officina_items (...) VALUES (...),(...),(...) con tutti i figli nuovi, seguito da una SELECT id, part_number WHERE project_id=@Id AND id >= LAST_INSERT_ID() per ricostruire la mappa codice->id
- **Rischio**: alto, e va valutato bene. L'id restituito serve subito a due cose: existingByCode[key] = newId (riga 1643), che deduplica DENTRO lo stesso import quando la composizione contiene due volte lo stesso codice, e CongelaTipoDaStato(c, newId). Accorpando, la dedup interna va rifatta in memoria PRIMA di costruire il batch (raggruppando i figli per codice normalizzato e sommando le quantita), altrimenti nascono righe doppie. Nessuna transazione aperta: un errore a meta batch oggi lascia un import parziale, con l'INSERT unico diventa tutto-o-niente - cambio di comportamento da dichiarare.
- **Note**: stesso ciclo del candidato 1596/1606 (i due rami if/else dello stesso foreach): si correggono insieme, in un solo passaggio che accumula gli aggiornamenti da un lato e gli inserimenti dall'altro. Anche qui la riga 1642 aggiunge il doppio round-trip di CongelaTipoDaStato.

### `Controllers/ProjectsController.cs`:1825 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: per ogni componente figlio di una riga officina propaga il delta di quantita (UPDATE ddp_officina_items SET quantity = GREATEST(0, quantity + composition_qty * @Delta))
- **Quante volte gira**: i figli importati dalla composizione Codex di quella riga: decine, fino a un centinaio su un assieme grosso
- **Correzione**: un solo UPDATE ddp_officina_items SET quantity = GREATEST(0, quantity + composition_qty * @Delta), updated_at = NOW() WHERE id IN @ChildIds - il delta e identico per tutti i figli, cambia solo composition_qty che sta gia sulla riga: non serve nemmeno il CASE WHEN. E subito dopo un solo UPDATE ... SET work_type = ... WHERE id IN @ChildIds AND TRIM(COALESCE(work_type,''))='' al posto delle chiamate a CongelaTipoDaStato
- **Rischio**: basso sull'UPDATE della quantita (formula identica per tutte le righe, gia protetta da GREATEST). Il pezzo delicato e il congelamento del tipo: oggi legge item_status riga per riga e ne deduce il work_type, quindi accorparlo richiede o un UPDATE ... JOIN con la mappa stato->tipo espressa in SQL (CASE WHEN item_status IN (...)), o una sola SELECT id,item_status WHERE id IN @Ids seguita da un UPDATE per ciascun tipo distinto (i tipi sono 2-3, non N). Da non sbagliare: il work_type si scrive SOLO se vuoto, la condizione va conservata
- **Note**: N+1 ANNIDATO che il rilevatore non vede: OfficinaRowSync.CongelaTipoDaStato (riga 1830, Services/OfficinaRowSync.cs:59) esegue a sua volta una SELECT item_status + un UPDATE work_type per ogni figlio. Il costo reale non e 1 query per giro ma 3. Sta su un percorso utente frequente (modifica quantita di una riga in griglia distinta officina).

### `Controllers/PurchaseRfqController.cs`:221 — VERO_LETTURA
- **Cosa fa nel ciclo**: per ogni riga di distinta libera cerca in catalog_items tutti gli articoli equivalenti con lo stesso codice ATEC
- **Quante volte gira**: decine (le righe che l'utente ha selezionato in Inbox Acquisti prima di aprire il piano fornitori)
- **Correzione**: una query sola prima del ciclo: SELECT atec_code, supplier_id, id, code, description, unit_cost FROM catalog_items WHERE is_active=1 AND atec_code IN @Atecs AND supplier_id IS NOT NULL, poi GroupBy(atec_code) in memoria e lookup dentro il ciclo
- **Rischio**: le righe con Atec vuoto oggi non interrogano il database (ternario a riga 224): il lookup in memoria deve restituire lista vuota, non lanciare; l'ordine con cui le opzioni finiscono in supplier.Items cambia il solo ordinamento interno (il risultato finale e' comunque ordinato per SupplierName a riga 266)
- **Note**: SELECT identica ripetuta al variare del solo @Atec, su una tabella da ~18.000 articoli; con righe che condividono lo stesso codice ATEC la stessa identica query viene rifatta piu' volte. Sta su un percorso interattivo (bottone «piano fornitori»), quindi si sente.

### `Controllers/PurchaseRfqController.cs`:620 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: applica alla riga di distinta prezzo, fornitore, identita' articolo e nuovo stato
- **Quante volte gira**: decine, fino a ~200 righe per aggiudicazione
- **Correzione**: parzialmente accorpabile: tutti i parametri tranne BomId/ProjId/Status sono costanti nel ciclo, quindi si puo' raggruppare per applyStatus (di norma 1 o 2 valori distinti) e fare una UPDATE ... WHERE id IN @Ids AND project_id IN @Pids per gruppo; alternativa piu' letterale: item_status = CASE id WHEN ... END
- **Rischio**: la clausola WHERE oggi accoppia id E project_id (guardia contro id di altre commesse): l'IN va costruito a coppie o ristretto ai soli id gia' verificati; se una riga non esiste piu' l'UPDATE massivo non se ne accorge, mentre oggi il singolo Execute restituirebbe 0 (valore comunque ignorato); l'evento di cronistoria (riga 666) resta comunque uno per riga e andrebbe accorpato anche lui in un INSERT multi-VALUES
- **Note**: seconda query dello STESSO ciclo del candidato 620/622 — vanno corrette insieme, non separatamente. La riscrittura del ciclo e' la modifica piu' redditizia di questo file dopo quella di riga 853.

### `Controllers/PurchaseRfqController.cs`:620 — VERO_LETTURA
- **Cosa fa nel ciclo**: legge lo stato attuale (item_status) della riga di distinta, una riga alla volta
- **Quante volte gira**: decine, fino a ~200 righe (il commento in DdpTransitionService cita proprio «aggiudicare una RDO da 200 righe»)
- **Correzione**: una sola SELECT id, item_status FROM bom_items WHERE id IN @BomIds prima del ciclo → dizionario id→stato, letto in memoria dentro il ciclo
- **Rischio**: la riga potrebbe sparire fra la lettura massiva e l'UPDATE: oggi il caso e' gia' gestito con oldStatus null (transizione da 'INIZIO'), il dizionario deve comportarsi allo stesso modo con la chiave mancante; nessuna transazione aperta qui, quindi nessun vincolo di ordine
- **Note**: stessa SELECT per id ripetuta a ogni riga. ATTENZIONE ai vicini invisibili al rilevatore: nello STESSO giro ci sono anche l'UPDATE di riga 637 (candidato a parte) e DdpItemEvents.Registra (riga 666) che fa un'altra INSERT — quindi 3 round-trip per riga, ~600 su una RDO da 200 righe. La Validate (riga 624) invece e' gia' a posto: usa AnagraficheCache.

### `Controllers/PurchaseRfqController.cs`:834 — VERO_LETTURA
- **Cosa fa nel ciclo**: legge item_status di ogni riga di distinta per decidere se puo' avanzare a 'IO'
- **Quante volte gira**: decine-centinaia (tutte le righe di tutte le RDO messe nell'ordine)
- **Correzione**: una sola SELECT id, item_status FROM bom_items WHERE id IN @Ids prima del ciclo, poi Validate in memoria sul dizionario
- **Rischio**: nessun vincolo di transazione (il commento a riga 831 dice che questo blocco sta apposta FUORI dalla tx); attenzione al fatto che il flag advance calcolato qui oggi NON viene poi usato nell'UPDATE (riga 853 scarta il valore e mette sempre 'IO'): l'accorpamento non deve nascondere questa incoerenza gia' presente
- **Note**: stessa SELECT per id ripetuta. Sta sul percorso «genera ordine Danea», che passa dall'Inbox Acquisti piu' volte al giorno.

### `Controllers/PurchaseRfqController.cs`:853 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: scrive su ogni riga di distinta riferimento Danea, IDDoc, data ordine, data prevista e stato 'IO'
- **Quante volte gira**: decine-centinaia di righe per ordine
- **Correzione**: caso da manuale: TUTTI i parametri tranne @Id sono costanti nel ciclo (Ref, IdDoc, Expected) → una sola UPDATE bom_items SET ... WHERE id IN @Ids dentro la stessa transazione
- **Rischio**: si e' dentro tx: la UPDATE massiva va lasciata nello stesso punto della sequenza (dopo la UPDATE delle purchase_rfqs); COALESCE(@Expected, date_needed) resta valido riga per riga anche in forma massiva; su lista vuota Dapper genera IN () → va evitato con un controllo di lista vuota
- **Note**: e' l'accorpamento piu' facile e piu' redditizio dei tre di questo ciclo: nessun valore progressivo, nessuna dipendenza dal giro precedente. Il ciclo contiene in tutto TRE query (858, 868, 870), da correggere insieme.

### `Controllers/PurchaseRfqController.cs`:853 — VERO_LETTURA
- **Cosa fa nel ciclo**: rilegge project_id della riga di distinta per scrivere l'evento di cronistoria
- **Quante volte gira**: decine-centinaia di righe per ordine
- **Correzione**: eliminare del tutto la query: il ProjectId e' GIA' dentro rowUpdates (la tupla e' (BomItemId, ProjectId, Advance) riempita a riga 840) ma il foreach lo scarta con la destrutturazione «foreach (var (bomItemId, _, advance))». Basta usarlo — infatti il ciclo a riga 894 lo legge proprio da li'.
- **Rischio**: praticamente nullo: il valore in memoria viene dagli stessi bom_items letti poche righe sopra, nella stessa richiesta; l'unico scenario divergente sarebbe una riga spostata di commessa nel frattempo, che oggi comunque non e' gestito
- **Note**: non e' solo un N+1: e' una query completamente INUTILE, il dato e' gia' in memoria e viene buttato via dalla destrutturazione. Difetto da correggere anche indipendentemente da E3.

### `Controllers/PurchaseRfqController.cs`:853 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: INSERT dell'evento di cronistoria («ordinato il», con numero ordine Danea) per ogni riga
- **Quante volte gira**: decine-centinaia di righe per ordine
- **Correzione**: un solo INSERT INTO ddp_item_events (...) VALUES (...),(...),... costruito dopo aver risolto i project_id in memoria, dentro la stessa transazione
- **Rischio**: transazione condivisa con la UPDATE di riga 858: se si accorpa, i due comandi restano nello stesso tx e l'atomicita' e' invariata; changed_at NOW() diventa identico per tutte le righe (oggi puo' differire di millisecondi — irrilevante per la cronistoria); lista vuota da evitare
- **Note**: terza query dello stesso ciclo (con 858 e 868): il ciclo fa 3 round-trip per riga e su un ordine da 200 righe sono 600 comandi in una sola transazione. Corretti insieme i tre, il ciclo sparisce del tutto.

### `Controllers/QuoteCostingController.cs`:337 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Ribilancia le percentuali di contingency: una UPDATE per ogni riga diversa da quella bloccata dall'utente.
- **Quante volte gira**: le righe della distribuzione prezzi meno una, cioè il numero di sezioni del preventivo: 3-20
- **Correzione**: Una sola UPDATE quote_pricing_distribution SET contingency_pct = CASE id WHEN .. THEN .. END WHERE quote_id=@quoteId AND id IN @Ids, includendo nel CASE anche la riga fissa aggiornata a riga 336 (così le UPDATE diventano una sola invece di N+1).
- **Rischio**: Le percentuali devono continuare a sommare 1: i valori vanno tutti calcolati prima e messi nel CASE così come sono, senza arrotondamenti nuovi. C'è un difetto già presente da non peggiorare: NON esiste transazione, quindi un errore a metà lascia le percentuali sbilanciate su un preventivo — accorpare in una query sola risolve anche questo. Attenzione al ramo rows.Count==1 (divisione remaining/(rows.Count-1) per zero) che esiste già oggi.
- **Note**: Sta su un gesto interattivo del preventivista (sposta uno slider e tutte le altre righe si ribilanciano), quindi si ripete spesso anche se le righe sono poche: per questo lo tengo a impatto medio e non basso. Il vero guadagno è la coerenza, non i millisecondi.

### `Controllers/QuoteCostingController.cs`:349 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Ribilancia le percentuali di margine: una UPDATE per ogni riga diversa da quella bloccata.
- **Quante volte gira**: le righe della distribuzione prezzi meno una: 3-20
- **Correzione**: Identica al candidato di riga 337 ma sulla colonna margin_pct: un solo UPDATE ... SET margin_pct = CASE id ... END WHERE quote_id=@quoteId AND id IN @Ids.
- **Rischio**: Stessi rischi: somma delle percentuali, assenza di transazione, divisione per zero con una riga sola.
- **Note**: È il ramo else dello stesso if del candidato precedente: i due cicli non girano mai insieme, e vanno corretti con lo stesso identico intervento (una funzione sola parametrizzata sulla colonna).

### `Controllers/TravelController.cs`:297 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: riscrive sort_order di ogni riga-persona dentro uno step dopo un drag&drop
- **Quante volte gira**: decine-centinaia (le righe sono persona x giorno, generate dal Timesheet: 5 persone x 20 giorni = 100 righe in uno step)
- **Correzione**: una sola UPDATE travel_step_rows r JOIN travel_steps s ... SET r.sort_order = CASE r.id WHEN ... END WHERE r.id IN @Ids AND r.step_id=@Sid AND s.project_id=@Pid
- **Rischio**: identico al gemello di riga 175: progressivo da riprodurre nel CASE, guardie step_id/project_id da mantenere (qui sono la difesa contro id di altre commesse), lista vuota da evitare
- **Note**: stesso schema del candidato 175/177 ma su volumi ben piu' alti, perche' le righe-persona nascono automaticamente dal Timesheet (#37/#52) e uno step di cantiere lungo ne accumula centinaia.

### `Controllers/TravelController.cs`:405 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: applica notti / prezzo notte / trasporto a ogni riga selezionata nella tabella «Aggrega costi»
- **Quante volte gira**: decine-centinaia (le righe di trasferta selezionate; su un cantiere lungo si aggrega su tutte le giornate insieme)
- **Correzione**: la UPDATE ha TUTTI i parametri costanti tranne @Id: diventa una sola UPDATE ... WHERE r.id IN @Ids AND s.project_id = @Pid (gli id sono gia' stati filtrati sulla commessa dalla query di riga 390)
- **Rischio**: row_version viene incrementato: con l'IN si incrementa comunque una volta per riga, quindi la concorrenza ottimistica resta coerente; su lista vuota si esce prima (controllo gia' presente a riga 395); AfterWrite/SyncToBudget deve restare una volta sola a fine ciclo, come oggi
- **Note**: N+1 ANNIDATO GRAVE, invisibile al rilevatore: nello STESSO giro, per ogni riga, vengono chiamati fino a tre ApplyCalcTotal (righe 431-435) e ciascuno esegue ProjectCalcSheets.Save, che apre una PROPRIA TRANSAZIONE e fa ~7 round-trip (COUNT projects, SELECT ... FOR UPDATE, INSERT/UPDATE testata, DELETE righe, INSERT riga, piu' il Load finale a 2 query) + 1 UPDATE finale. Nel caso peggiore ~25 round-trip e 3 transazioni PER RIGA: su 100 righe sono migliaia di comandi. La correzione della sola UPDATE 409 toglie briciole — il lavoro vero e' accorpare le chiamate alle calcolatrici (una Save per foglio non e' evitabile per com'e' fatta oggi, ma il COUNT sulla commessa e il Load di ricarica si possono togliere dal giro).

### `Services/CodexGeneratorService.cs`:228 — VERO_LETTURA
- **Cosa fa nel ciclo**: Per ogni articolo selezionato prova un codice progressivo e chiede al database se esiste già (COUNT su codice o codice_nuovo), incrementando finché è libero.
- **Quante volte gira**: gli articoli Codex selezionati nella pagina Ricodifica per l'assegnazione massiva: decine, potenzialmente centinaia (l'anagrafica ne ha ~18.000, ma la selezione è manuale)
- **Correzione**: Caricare una volta sola, prima del ciclo, tutti i codici della famiglia+giorno (SELECT codice, codice_nuovo FROM codex_items WHERE codice LIKE @Pattern OR codice_nuovo LIKE @Pattern) in un HashSet, e fare il controllo di collisione in memoria aggiungendo via via i codici appena prenotati.
- **Rischio**: Il controllo va tenuto rigorosamente dentro GET_LOCK e dentro la transazione: l'insieme in memoria è valido solo perché nessun altro sta prenotando sulla stessa famiglia. Se qualcuno un giorno togliesse il lock, la versione in memoria diventerebbe cieca alle prenotazioni altrui mentre quella attuale no. Va aggiunto all'HashSet anche il codice appena generato a ogni giro, altrimenti si assegna N volte lo stesso.
- **Note**: Qui il rilevatore ha ragione: è una SELECT identica ripetuta a ogni giro variando solo il codice candidato, ed è dentro un ciclo che scorre la selezione dell'utente. Contando anche la INSERT di riga 239, una ricodifica massiva di 200 righe fa ~400 round-trip sotto un lock esclusivo che blocca gli altri operatori: è questo, più della latenza, il vero costo.

### `Services/CodexGeneratorService.cs`:228 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Inserisce una prenotazione in codex_reservations per ogni articolo selezionato e rilegge LAST_INSERT_ID() per restituire l'id all'anteprima.
- **Quante volte gira**: gli articoli selezionati per la ricodifica massiva: decine-centinaia
- **Correzione**: Una sola INSERT multi-VALUES con tutte le prenotazioni, poi ricavare gli id o rileggendoli (SELECT id, reserved_code FROM codex_reservations WHERE reserved_by=@User AND reserved_code IN @Codes) oppure appoggiandosi alla consecutività degli auto_increment di una singola INSERT.
- **Rischio**: Il punto delicato è proprio LAST_INSERT_ID(): con una INSERT multi-riga MySQL restituisce l'id della PRIMA riga e la consecutività vale solo con innodb_autoinc_lock_mode=1 e nessun inserimento concorrente — condizione oggi garantita dal GET_LOCK, ma è una garanzia implicita e fragile. Più sicuro rileggere gli id per reserved_code. Se gli id tornano sfasati, l'operatore conferma la ricodifica sbagliata: è un errore silenzioso che scrive codici sull'anagrafica.
- **Note**: Secondo statement dello stesso ciclo di riga 228, e insieme al controllo di collisione raddoppia i round-trip. Accorpabile, ma con la cautela sul LAST_INSERT_ID: consiglierei di correggere prima la SELECT (riga 235), che è a rischio zero, e valutare la INSERT solo dopo.

### `Services/CodexSyncService.cs`:135 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni riga remota che ha trovato una corrispondenza locale (per remote_id, o riagganciata per codice), esegue una UPDATE codex_items ... WHERE id=@LocalId con tutti i campi del remoto.
- **Quante volte gira**: la grande maggioranza delle ~18.000 righe a ogni sync (a regime praticamente tutte); ogni 6 ore
- **Correzione**: Accorpare in blocchi da 500 con INSERT INTO codex_items (id, remote_id, codice, ...) VALUES ... ON DUPLICATE KEY UPDATE remote_id=VALUES(remote_id), codice=VALUES(codice), ..., prezzo_forn = CASE WHEN (codex_items.prezzo_forn IS NULL OR codex_items.prezzo_forn=0) AND VALUES(prezzo_forn)>0 THEN VALUES(prezzo_forn) ELSE codex_items.prezzo_forn END, synced_at=NOW(). Meglio ancora: si può unificare con la INSERT di riga 159 in un'unica passata upsert, visto che la mappatura localId è già stata calcolata tutta in memoria prima del ciclo.
- **Rischio**: Alto se fatto in fretta. La UPDATE ha una regola condizionale non banale su prezzo_forn (il prezzo locale già valorizzato NON va sovrascritto): tradotta male in un upsert, si azzerano o si sovrascrivono prezzi fornitore inseriti a mano. Inoltre l'upsert per id deve NON creare righe nuove quando l'id non esiste, e i codici generati localmente (remote_id NULL/<=0) non vanno toccati. Tutto dentro la transazione unica, con la DELETE delle stale a seguire: l'esito del riaggancio per codice (adopted) dipende dallo stato calcolato prima del ciclo, quindi va conservato l'ordine logico calcolo → scrittura.
- **Note**: Secondo candidato sullo stesso foreach di riga 135 e il più pesante dei due a regime. Va corretto insieme alla INSERT, altrimenti si dimezza il beneficio. Da segnalare che la parte di LETTURA è già fatta bene: localRows viene caricata una sola volta fuori dal ciclo (riga 83) e le corrispondenze si risolvono su dizionari in memoria — il difetto è solo nella scrittura.

### `Services/CodexSyncService.cs`:135 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni riga letta dal Codex remoto che non trova corrispondenza locale (né per remote_id, né per codice non ancora sincronizzato, né per codice stale), esegue una INSERT in codex_items.
- **Quante volte gira**: circa 18.000 righe lette dal remoto per ogni sync; al primo giro sono quasi tutte INSERT, nei giri successivi poche decine. Il sync parte 5 secondi dopo l'avvio del server e poi ogni 6 ore (CodexSync:IntervalHours)
- **Correzione**: Accumulare le righe da inserire in una lista e scriverle con INSERT INTO codex_items (remote_id, codice, ...) VALUES (...),(...),... a blocchi di 500, dentro la stessa transazione già aperta a riga 81.
- **Rischio**: Tutto il ciclo sta dentro UNA transazione unica (riga 81, commit a riga 198) che copre anche la DELETE delle righe stale e l'aggiornamento di app_config: un blocco che fallisce annulla l'intero sync (comportamento voluto, ma va verificato che l'errore resti diagnosticabile). Attenzione alle righe dinamiche: i parametri arrivano da un `dynamic` letto con SELECT * dal remoto, quindi una INSERT multi-VALUES va costruita esplicitando i 22 campi. Un blocco troppo grande può superare max_allowed_packet.
- **Note**: Sync notturna/periodica senza schermata davanti, quindi non è la priorità di E3 — ma NON lo classifico LEGITTIMO perché 18.000 round-trip tengono aperta una transazione lunghissima su codex_items, tabella letta dalla Composizione Codex e dai picker: il lock prolungato si vede lato utente. Primo dei due candidati sullo stesso foreach: INSERT (riga 159) e UPDATE (riga 167) sono i due esiti alternativi dello stesso giro.

### `Services/CostingDataService.cs`:189 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Aggiorna contingenza/margine/pin/ombreggiatura di una sezione di costo, una UPDATE per sezione
- **Quante volte gira**: sezioni di costo del preventivo: 10-30, a OGNI modifica di una percentuale nel pannello distribuzione
- **Correzione**: Una sola UPDATE {costSectionsTable} s JOIN (VALUES/derivata con id, cont, marg, contPin, margPin, shadowed) v ON v.id = s.id SET ... WHERE s.id IN @Ids AND quote_id=@qid; in alternativa una UPDATE ... SET contingency_pct = CASE id WHEN .. END, ... WHERE id IN @Ids.
- **Rischio**: Medio: il filtro per proprietario (AND quote_id=@qid) è la guardia che impedisce di scrivere su un preventivo altrui e va mantenuto anche nella forma accorpata. Sta dentro una transazione aperta dal chiamante (QuoteCostingController riga 379-383), quindi il rollback deve continuare a coprire sezioni E righe materiale insieme. Da gestire la lista vuota: req.Sections può essere null e oggi il ciclo semplicemente non gira.
- **Note**: Il ciclo è breve ma il percorso è caldo: atec-pm-web/src/features/preventivi/costing-panels.tsx (funzione persist, riga 170) rimanda al server TUTTE le righe a ogni singola modifica di percentuale e a ogni toggle. Quindi 10-30 round-trip per ogni tocco dell'utente sul pannello distribuzione, non una tantum.

### `Services/DaneaSyncService.cs`:334 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: un INSERT ... ON DUPLICATE KEY UPDATE su catalog_items per ogni articolo dell'archivio Danea, dentro una transazione che ha appena messo is_active=0 su tutto il catalogo
- **Quante volte gira**: ~18.000 articoli Codex/catalogo, ogni 6 ore in background
- **Correzione**: upsert multi-VALUES a blocchi (500-1.000 righe per istruzione): INSERT INTO catalog_items (...) VALUES (...),(...) ON DUPLICATE KEY UPDATE is_active=1, description=VALUES(description), ... — da 18.000 round-trip a poche decine
- **Rischio**: la riga 330 apre una transazione che aggiorna TUTTE le righe attive di catalog_items: quei lock restano presi per l'intera durata del ciclo, e vanno mantenuti tali (è la logica «specchio»). Attenzione: la parte SET è costruita a stringa in base a mappingMaster, quindi il batch va composto con la stessa variante; il parametro CodexItemId può essere null e i tipi devono restare coerenti in tutte le tuple; il conteggio count (articoli con codice non vuoto) va preservato perché finisce nel log e in ArticlesCount
- **Note**: è l'unico caso del gruppo dove i round-trip toccano gli utenti: 18.000 scritture una alla volta allungano di molto una transazione che tiene bloccata l'intera catalog_items, tabella letta dalle pagine Catalogo/Acquisti/distinte. Non è il ciclo in sé a essere sbagliato, è la sua durata sotto lock

### `Services/MoMDbService.cs`:240 — VERO_LETTURA
- **Cosa fa nel ciclo**: Per ogni riga di mom_action_items (caricate TUTTE, senza filtro) conta quanti legami esistono già in mom_action_item_responsibles, per decidere se migrarla.
- **Quante volte gira**: tutte le azioni MoM esistenti: qualche migliaio e in crescita (decine di verbali all'anno × decine di righe); gira a OGNI avvio del server, via InitTables → EnsureModuleTables (DbService riga 1450)
- **Correzione**: Sostituire il COUNT per riga con una sola lettura fuori dal ciclo: SELECT DISTINCT action_item_id FROM mom_action_item_responsibles in un HashSet e un Contains dentro il ciclo. Meglio ancora: restringere la SELECT di riga 236 alle sole righe da migrare (WHERE NOT EXISTS (...) AND (resp1_id IS NOT NULL OR resp2_id IS NOT NULL OR resp3_id IS NOT NULL)), che a regime restituisce zero righe e azzera l'intero ciclo.
- **Rischio**: È una migrazione di dati storici: se il filtro sbaglia si riscrivono responsabili già migrati, e SaveItemResponsibles fa DELETE + reinserimento — si perderebbe un eventuale elenco esteso oltre i 3 legacy, sostituito dai soli resp1/2/3. Va mantenuta la condizione «migro solo se la riga non ha ancora NESSUN legame».
- **Note**: È il candidato più interessante del gruppo. La query è davvero dentro il ciclo, è la stessa COUNT con un solo parametro variabile, e il ciclo carica l'intera tabella mom_action_items senza filtro a ogni avvio del servizio: a regime sono migliaia di round-trip inutili prima che /api/health/ready risponda, cioè prima che il deploy consideri il server su. In più, per le righe da migrare, chiama SaveItemResponsibles (riga 169) che a sua volta cicla in INSERT: N+1 annidato. Non lo classifico «alto» solo perché sta sull'avvio e non su una schermata.

### `Services/NotificationService.cs`:51 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Inserisce una riga in notification_recipients per ogni destinatario della notifica appena creata
- **Quante volte gira**: destinatari: 5-15 di norma (PM + reparto ACQ), fino a tutti gli ADMIN attivi
- **Correzione**: Un solo INSERT INTO notification_recipients (notification_id, employee_id) SELECT @NotifId, id FROM employees WHERE id IN @EmpIds (che tra l'altro scarta da sé eventuali id inesistenti), oppure un multi-VALUES costruito dalla lista distinta.
- **Rischio**: Basso: nessun ordine da rispettare, nessun id da propagare. Da gestire la lista vuota (oggi il foreach non gira e resta una notifica senza destinatari: comportamento da conservare tale e quale). Nota: Create non apre una transazione, quindi già oggi la notifica e i suoi destinatari non sono atomici.
- **Note**: È il candidato più insidioso del gruppo perché è N+1 ANNIDATO e il rilevatore non lo vede: Create viene chiamato DENTRO altri cicli — righe 323, 388, 431, 532, 621, 683, 757, 841, 919 di questo stesso file e ProjectsController righe 1393/1862. Ogni chiamata apre pure una NUOVA connessione (_db.Open() a riga 34) e paga 1 INSERT + N INSERT. Su un giro del job notturno con 20 scadenze e 10 destinatari ciascuna sono 20 connessioni e ~220 round-trip. Impatto medio e non alto perché il grosso delle chiamate sta in un BackgroundService che gira ogni 6 ore, non su una schermata.

### `Services/PermissionAdminService.cs`:592 — VERO_LETTURA
- **Cosa fa nel ciclo**: rilegge l'elenco completo delle funzioni (auth_features) da capo per ogni persona del lotto
- **Quante volte gira**: le persone selezionate in Applica classe: da 1 a 35 (tutti i dipendenti); la pagina la usa un ADMIN, prima in anteprima e poi in applicazione
- **Correzione**: spostare la SELECT FROM auth_features PRIMA del foreach (riga 592) e riusare la stessa lista per tutte le persone: la query non usa nemmeno la variabile del ciclo
- **Rischio**: praticamente nullo: la lista e la stessa per tutti e non cambia durante la richiesta. Va solo tenuta la clonazione per persona di daConsiderare (riga 608 fa gia .Select(...).ToList(), quindi la riga del jolly aggiunta a riga 610 non sporca la lista condivisa) - se si riusa la lista, quel ToList() deve restare
- **Note**: query INVARIANTE nel ciclo: identica a ogni giro, ~100 chiavi funzione rilette 35 volte. Ma il difetto vero e piu grande di quello che il rilevatore vede: nello stesso ciclo ci sono altre TRE query nascoste dietro chiamate a metodo (LeggiPersona riga 595, RighePersona riga 599, PacchettoClasse riga 600) e soprattutto un ciclo ANNIDATO alla riga 612 su tutte le chiavi, che per ogni cambio chiama ScriviRiga (riga 646) = SELECT + INSERT/DELETE + registrazione nel log dei cambi. Applicare una classe a 35 persone su ~100 funzioni significa migliaia di round-trip dentro una sola transazione: e l'N+1 annidato peggiore del gruppo, e il candidato segnalato ne e solo la punta piu facile da togliere

### `Services/PermissionSeedService.cs`:133 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: È la INSERT ... ON DUPLICATE KEY UPDATE che materializza il permesso di una persona su una funzione, vista dal ciclo ESTERNO sulle persone.
- **Quante volte gira**: 35 persone × ~50-60 funzioni registrate = circa 2.000 scritture per ogni lancio del seed (più altrettante INSERT nel registro delle modifiche quando qualcosa cambia davvero)
- **Correzione**: Accumulare tutte le coppie (persona, funzione, accesso) da scrivere e mandarle con una INSERT multi-VALUES ... ON DUPLICATE KEY UPDATE a blocchi di qualche centinaio; lo stesso per le righe del log (PermissionChangeService.Registra).
- **Rischio**: La regola «MANO vince sul seed» va applicata in memoria prima di accodare, com'è oggi. Il registro (Registra) e la propagazione (Propaga, che alza la versione dei permessi e fa partire l'avviso realtime) devono restare allineati riga per riga: se si accorpa la scrittura ma non il log, si perde la corrispondenza fra ciò che è stato scritto e ciò che è stato registrato. Attenzione anche al fatto che qui NON c'è transazione: oggi un errore a metà lascia i permessi di metà ufficio a metà strada.
- **Note**: N+1 ANNIDATO, ed è il conteggio più alto del gruppo: persone × funzioni. È lo stesso statement del candidato 145→154, contato sul ciclo esterno. Impatto medio e non alto solo perché il seed è un'operazione di amministrazione lanciata a mano; se un giorno finisse in un hook di avvio o di deploy, diventerebbe alto.

### `Services/PermissionSeedService.cs`:145 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Scrive il permesso della persona su UNA funzione (INSERT ... ON DUPLICATE KEY UPDATE con origin='CLASSE').
- **Quante volte gira**: le funzioni che quella persona vede col motore vecchio: da poche a ~60, in media qualche decina
- **Correzione**: Stessa correzione del candidato 133→154: accumulare e scrivere a blocchi con una INSERT multi-VALUES ... ON DUPLICATE KEY UPDATE.
- **Rischio**: Identico: precedenza delle righe MANO, allineamento con il registro delle modifiche e con Propaga, assenza di transazione. In più il contatore esito.RigheScritte e il flag `cambiato` (che decide se far partire l'avviso realtime a quella persona) vanno calcolati sugli stessi criteri di oggi, altrimenti si notifica mezzo ufficio per niente o non si notifica chi doveva esserlo.
- **Note**: È il ciclo interno del candidato 133→154: stesso statement, contato sul ciclo che gli sta più vicino. Si corregge insieme, con un intervento solo.

### `Services/PlanNotificationService.cs`:154 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: In TakeSnapshot, per ogni assegnazione corrente del piano risorse esegue una INSERT in res_plan_snapshots (la «foto» del piano).
- **Quante volte gira**: tutte le assegnazioni attive del planner: 35 dipendenti × più attività ciascuno = da centinaia a qualche migliaio di righe; gira a ogni SendDigest (digest giornaliero automatico + «Esegui ora») e la primissima volta in assoluto da ComputeChanges
- **Correzione**: Sostituire il ciclo con una INSERT INTO res_plan_snapshots (...) SELECT @BatchId, a.id, a.employee_id, ... FROM res_assignments a — i dati inseriti vengono da CurrentSql, quindi la foto si può scattare interamente in SQL senza far transitare le righe dal C#. In alternativa, se serve continuare a passare dalla lista in memoria, una INSERT multi-VALUES a blocchi di 500.
- **Rischio**: batch_id deve restare quello appena creato con LAST_INSERT_ID (riga 150), quindi la INSERT ... SELECT va eseguita sulla stessa connessione subito dopo. Se in futuro CurrentSql e la SELECT della foto divergessero, gli snapshot smetterebbero di corrispondere allo stato confrontato e il digest rinotificherebbe variazioni fantasma. Nessuna transazione esplicita qui: un fallimento a metà lascia una foto parziale, e la foto parziale fa comparire come «nuove» tutte le righe mancanti al giro successivo.
- **Note**: INSERT identica ripetuta per riga, accorpabile in una sola istruzione. L'impatto non è alto perché il digest gira una volta al giorno, ma è il ciclo con più iterazioni di questo file e tiene la connessione occupata a lungo su un percorso che l'admin può lanciare a mano dalla pagina Risorse.

### `Services/ProjectCalcSheets.cs`:128 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Riscrive il foglio di calcolo: dopo la DELETE di tutte le righe, reinserisce una INSERT per ogni riga con importo, ricarico e sort_order progressivo.
- **Quante volte gira**: le righe della finestra di calcolo confermata: decine (una calcolatrice di lavorazioni officina o di trasferta arriva comodamente a 20-50 righe)
- **Correzione**: Una sola INSERT INTO project_calc_rows (...) VALUES (...),(...),... costruita dalla lista rows già filtrata e validata, a blocchi di qualche centinaio, dentro la transazione già aperta a riga 82.
- **Rischio**: Il sort_order è progressivo (++sortOrder) e definisce l'ordine a video: va precalcolato nella stessa sequenza. L'importo lo decide il server riga per riga (pinned ? Amount : ComputedAmount) e quella logica deve restare dove sta, prima dell'accodamento. Tutto va tenuto dentro la transazione con il lucchetto FOR UPDATE di riga 93 e l'incremento di row_version, altrimenti si rompe il controllo di concorrenza («Calcolo modificato da un altro utente»). Con lista vuota (foglio svuotato) non si deve costruire una VALUES vuota: oggi il ciclo semplicemente non gira e il foglio resta senza righe, comportamento da preservare.
- **Note**: È il candidato con il rapporto migliore fra frequenza e cardinalità di tutto il gruppo: sta sulla «Conferma» di ogni finestra di calcolo (calcolatrici a righe, lavorazioni officina, trasferta), gesto che i PM ripetono più volte al giorno, e riscrive SEMPRE tutte le righe perché è una sostituzione integrale — non un diff. Decine di INSERT dentro una transazione che tiene un FOR UPDATE aperto: accorpandole si accorcia anche la finestra di lock.

### `Services/QuoteService.cs`:68 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni riga del preventivo di origine esegue una INSERT in quote_items seguita da SELECT LAST_INSERT_ID() per costruire la mappa vecchio-id → nuovo-id (serve a rimappare parent_item_id).
- **Quante volte gira**: decine di righe per preventivo (righe di preventivo per commessa: decine); il ciclo parte a ogni «Duplica» o «Nuova revisione» dal dettaglio preventivo
- **Correzione**: Sostituire il ciclo con una sola INSERT INTO quote_items (...) SELECT ... FROM quote_items WHERE quote_id=@fromId ORDER BY sort_order, e ricostruire la gerarchia con un secondo UPDATE che rimappa parent_item_id via JOIN fra copia e originale su (quote_id, sort_order).
- **Rischio**: Alto: si perde LAST_INSERT_ID per riga, che è l'unico ponte fra id vecchio e nuovo. Se sort_order non è univoco dentro il preventivo il JOIN di rimappatura sbaglia padre. Va mantenuto l'ordine (le righe figlie devono restare sotto il padre) e il fatto che un padre non ancora copiato produce parent NULL invece di un id sbagliato.
- **Note**: INSERT ripetuta identica che varia solo nei parametri: accorpabile, ma l'accorpamento è meno banale del solito perché il ciclo usa il valore restituito (idMap) per la riga successiva. Non è un N+1 annidato: CopyQuoteItems è chiamata una volta sola da QuotesController.CreateRevision (riga 1068) e Duplicate (riga 1149), mai dentro un ciclo.

### `Services/QuoteService.cs`:159 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Vista dal ciclo esterno delle sezioni materiali: la INSERT in quote_material_items del ciclo interno (riga 181) viene eseguita sezioni × righe volte.
- **Quante volte gira**: sezioni (1-10) × righe materiali per sezione (decine-centinaia) = da qualche decina a qualche centinaio di round-trip per duplicazione
- **Correzione**: Sostituire i due cicli annidati con una sola INSERT INTO quote_material_items (...) SELECT ... FROM quote_material_items i JOIN mappa m ON m.old_section_id = i.section_id, più un UPDATE finale che rimappa parent_item_id appaiando copia e originale su (section_id, sort_order).
- **Rischio**: Uguale a CopyQuoteItems: si perde LAST_INSERT_ID per riga e con esso matIdMap, quindi la gerarchia padre-figlio delle righe materiali. Se sort_order non è univoco dentro la sezione la rimappatura sbaglia. Tutto avviene dentro la transazione della duplicazione: un errore a metà annulla tutto, va rifatta la prova su un preventivo con righe annidate.
- **Note**: N+1 ANNIDATO — è il caso peggiore di questo gruppo e il rilevatore lo vede solo perché ha emesso due candidati sulla stessa query (159/187 dall'esterno, 181/187 dall'interno). Sono lo stesso INSERT: vanno corretti in un colpo solo insieme a 159/176.

### `Services/QuoteService.cs`:181 — VERO_SCRITTURA
- **Cosa fa nel ciclo**: Per ogni riga materiale della sezione, INSERT in quote_material_items + SELECT LAST_INSERT_ID() per alimentare matIdMap (rimappatura parent_item_id).
- **Quante volte gira**: decine-centinaia di righe materiali per sezione; una duplicazione o una nuova revisione di preventivo IMPIANTO
- **Correzione**: Come sopra: INSERT ... SELECT unica per tutte le righe della sezione, poi UPDATE di rimappatura di parent_item_id su (section_id, sort_order).
- **Rischio**: Idem 159/187: perdita della mappa id vecchio→nuovo, gerarchia righe materiali, ordine sort_order, tutto dentro la transazione di duplicazione.
- **Note**: Stesso ciclo e stessa query del candidato 159/187, ma visto dal foreach interno: tenuti separati come richiesto. È qui che si concentrano davvero le iterazioni.

### `Services/TravelFromTimesheet.cs`:74 — VERO_LETTURA
- **Cosa fa nel ciclo**: per ogni fase di cantiere cerca lo step trasferta corrispondente (SELECT id FROM travel_steps WHERE project_id=@Pid AND project_phase_id=@Fase)
- **Quante volte gira**: le fasi DA_CLIENTE della commessa: tipicamente 1-10 (le fasi totali per commessa sono 5-30, ma solo quelle di cantiere entrano qui)
- **Correzione**: una sola SELECT id, project_phase_id FROM travel_steps WHERE project_id = @Pid prima del ciclo, caricata in un Dictionary<int,int>; dentro il ciclo si fa solo TryGetValue
- **Rischio**: praticamente nullo: la mappa e per project_id e la chiave (project_id, project_phase_id) e la stessa della WHERE attuale. Unica attenzione: se nella stessa esecuzione il ciclo INSERISCE nuovi step (riga 83), il dizionario precaricato va tenuto aggiornato con gli id appena creati, altrimenti due fasi uguali si duplicherebbero
- **Note**: lettura identica ripetuta, il caso da manuale di E3. Sta sullo stesso percorso caldo della riga 107 (ogni salvataggio di ore la riesegue), quindi vale la pena anche se il ciclo e corto.

## Impatto BASSO — 110 punti (elenco compatto)

Veri N+1, ma su cicli corti o percorsi rari: si toccano solo passando di lì per altri motivi.

| File | Riga | Classe | Cosa fa |
|---|---:|---|---|
| `Controllers/ActivityCatalogController.cs` | 127 | SCRITTURA | un UPDATE activity_catalog SET sort_order per ogni id ricevuto dal riordino drag&drop |
| `Controllers/CashFlowController.cs` | 102 | SCRITTURA | crea la categoria di cassa mancante per un robot nuovo |
| `Controllers/CashFlowController.cs` | 102 | SCRITTURA | per ogni robot (PRODUCT) gia collegato a una categoria, riscrive nome e totale netto della categoria di cassa |
| `Controllers/CashFlowController.cs` | 127 | SCRITTURA | cancella le percentuali mensili (CAT_PCT) delle categorie legate a robot non piu esistenti |
| `Controllers/CashFlowController.cs` | 127 | SCRITTURA | cancella la categoria di cassa del robot che non esiste piu |
| `Controllers/ChatController.cs` | 288 | SCRITTURA | Per ogni partecipante scelto alla creazione della chat esegue un INSERT IGNORE in project_chat_participants. |
| `Controllers/ChatController.cs` | 679 | SCRITTURA | Scorre i dipendenti attivi non ancora in chat e, per quelli davvero menzionati con «@Nome Cognome» nel messaggio, esegue un INSERT IGNORE fra i partecipanti. |
| `Controllers/CheckListController.cs` | 472 | SCRITTURA | inserisce una attivita di check list importata dal backup |
| `Controllers/CheckListController.cs` | 507 | SCRITTURA | inserisce una attivita di check list importata dal backup (ciclo interno sulle NewItems della tabella) |
| `Controllers/CheckListController.cs` | 537 | SCRITTURA | inserisce una nota personale nella inbox 'Fissa attivita' di chi sta importando |
| `Controllers/CodexController.cs` | 911 | SCRITTURA | somma la quantita alla riga di composizione gia esistente |
| `Controllers/ConfigController.cs` | 49 | SCRITTURA | scrive (upsert) una chiave di configurazione applicativa |
| `Controllers/CostSectionsController.cs` | 160 | SCRITTURA | lega i reparti selezionati alla sezione template appena creata |
| `Controllers/CostSectionsController.cs` | 195 | SCRITTURA | reinserisce i reparti della sezione dopo averli cancellati tutti (aggiornamento template) |
| `Controllers/CostSectionsController.cs` | 215 | SCRITTURA | reinserisce i reparti della sezione dall'endpoint dedicato ai soli reparti |
| `Controllers/DdpAggregationsController.cs` | 84 | SCRITTURA | Dopo aver cancellato tutte le righe dell'aggregazione, reinserisce una alla volta le chiavi di stato scelte (INSERT IGNORE in ddp_aggregation_states). |
| `Controllers/DdpStatusesController.cs` | 143 | SCRITTURA | e lo STESSO INSERT del candidato precedente, visto dal ciclo esterno sulle righe della matrice |
| `Controllers/DdpStatusesController.cs` | 143 | SCRITTURA | per una riga della matrice senza destinazioni ammesse scrive la sentinella terminale (INSERT IGNORE ... VALUES (@Type,@From,'')) |
| `Controllers/DdpStatusesController.cs` | 162 | SCRITTURA | inserisce una riga di transizione per ogni stato di destinazione ammesso (INSERT IGNORE INTO ddp_status_transitions ... VALUES (@Type,@From,@To)) |
| `Controllers/ImportController.cs` | 157 | SCRITTURA | Per ogni fornitore marcato UPDATE nell'import da Easyfatt, esegue una UPDATE suppliers ... WHERE id=@ExistingId. |
| `Controllers/ImportController.cs` | 157 | SCRITTURA | Per ogni fornitore nuovo, esegue una INSERT INTO suppliers con i campi presi dal record Easyfatt. |
| `Controllers/ImportController.cs` | 559 | SCRITTURA | Per ogni cliente marcato UPDATE nell'import da Easyfatt, esegue una UPDATE customers ... WHERE id=@ExistingId. |
| `Controllers/ImportController.cs` | 559 | SCRITTURA | Per ogni cliente nuovo, esegue una INSERT INTO customers con i dati Easyfatt. |
| `Controllers/MilestonesController.cs` | 318 | SCRITTURA | Con ReplaceExisting, cancella tutte le milestone della commessa: una DELETE per ogni commessa agganciata del backup. |
| `Controllers/MilestonesController.cs` | 318 | LETTURA | Rilegge descrizione+date delle milestone già presenti, una SELECT per commessa, per costruire l'insieme dei duplicati da saltare. |
| `Controllers/MilestonesController.cs` | 318 | LETTURA | Chiede il primo sort_order libero della commessa (MAX(sort_order)+1), una scalare per commessa. |
| `Controllers/MilestonesController.cs` | 340 | SCRITTURA | Inserisce una milestone importata per volta (descrizione, date, avanzamento, note, sort_order). |
| `Controllers/MilestonesController.cs` | 396 | LETTURA | Per ogni commessa del backup agganciata in anagrafica rilegge tutte le sue milestone esistenti, per contarle e calcolare nuove/duplicate. |
| `Controllers/ProjectCostingController.cs` | 66 | SCRITTURA | per ogni template di sezione di default inserisce una riga in project_cost_sections e ne rilegge il LAST_INSERT_ID |
| `Controllers/ProjectCostingController.cs` | 66 | SCRITTURA | per ogni sezione appena creata copia i reparti del template con una INSERT ... SELECT |
| `Controllers/ProjectCostingController.cs` | 89 | SCRITTURA | una INSERT in project_material_sections per ogni categoria materiali attiva |
| `Controllers/ProjectCostingController.cs` | 120 | SCRITTURA | dopo la DELETE, reinserisce una riga project_cost_section_departments per ogni reparto scelto |
| `Controllers/ProjectsController.cs` | 281 | SCRITTURA | inserisce una alla volta le fasi predefinite della nuova commessa (INSERT INTO project_phases ...) |
| `Controllers/ProjectsController.cs` | 1913 | SCRITTURA | cancella uno per uno i componenti figli della riga officina che si sta eliminando (DELETE FROM ddp_officina_items WHERE id = @Id) |
| `Controllers/PurchaseRfqController.cs` | 142 | LETTURA | per ogni fornitore cerca l'articolo di catalogo con quel codice ATEC (SELECT id ... ORDER BY id LIMIT 1) |
| `Controllers/PurchaseRfqController.cs` | 167 | SCRITTURA | INSERT di una riga purchase_rfq_items per ogni riga di distinta del gruppo, dentro la transazione |
| `Controllers/PurchaseRfqController.cs` | 175 | SCRITTURA | INSERT IGNORE di un'offerta (rfq, fornitore, articolo) per ogni fornitore interpellato |
| `Controllers/PurchaseRfqController.cs` | 236 | LETTURA | legge ragione sociale ed email del fornitore la prima volta che compare nel piano |
| `Controllers/PurchaseRfqController.cs` | 312 | LETTURA | per ogni coppia (gruppo commessa+ATEC, fornitore) cerca l'articolo di catalogo di quel fornitore |
| `Controllers/PurchaseRfqController.cs` | 340 | SCRITTURA | INSERT di una riga purchase_rfq_items per ogni riga di distinta del gruppo, in transazione |
| `Controllers/PurchaseRfqController.cs` | 345 | SCRITTURA | INSERT IGNORE di un'offerta per ogni fornitore selezionato sul gruppo |
| `Controllers/PurchaseRfqController.cs` | 500 | SCRITTURA | marca email_sent_at = NOW() sull'offerta appena accodata all'invio |
| `Controllers/PurchaseRfqController.cs` | 688 | LETTURA | legge lo stato della commessa per decidere se mandare la notifica di campanella |
| `Controllers/PurchaseRfqController.cs` | 760 | LETTURA | per ogni RDO da mettere in ordine Danea legge il codice articolo del catalogo dell'offerta vincente |
| `Controllers/QuoteCatalogController.cs` | 449 | SCRITTURA | una INSERT in quote_product_variants per ogni variante del prodotto appena creato |
| `Controllers/QuoteCatalogController.cs` | 514 | SCRITTURA | per ogni variante nuova (Id=0) esegue una INSERT in quote_product_variants |
| `Controllers/QuoteCatalogController.cs` | 514 | SCRITTURA | per ogni variante già esistente (Id>0) esegue una UPDATE quote_product_variants |
| `Controllers/QuoteCostingController.cs` | 27 | SCRITTURA | Dopo la DELETE dei reparti della sezione, reinserisce un legame sezione→reparto per ogni reparto scelto. |
| `Controllers/QuoteCostingController.cs` | 200 | SCRITTURA | Clonazione di un prodotto materiale: reinserisce ogni variante figlia agganciandola al nuovo parent. |
| `Controllers/QuoteCostingController.cs` | 236 | SCRITTURA | Aggiorna dal catalogo: reinserisce le varianti locali una per una copiando codice, nome, costo e ricarico dal catalogo CMS. |
| `Controllers/QuoteCostingController.cs` | 269 | SCRITTURA | Push verso il catalogo: una UPDATE su quote_product_variants (database CMS) per ogni variante locale. |
| `Controllers/QuoteCostingController.cs` | 303 | SCRITTURA | Genera la distribuzione prezzi: una INSERT in quote_pricing_distribution per ogni sezione di COSTO, con il peso calcolato sul totale. |
| `Controllers/QuoteCostingController.cs` | 308 | SCRITTURA | Stessa cosa del candidato precedente ma per le sezioni MATERIALE: una INSERT per sezione. |
| `Controllers/QuotesController.cs` | 457 | SCRITTURA | Inserisce una riga figlia di preventivo per ogni variante del prodotto |
| `Controllers/QuotesController.cs` | 457 | SCRITTURA | Inserisce la riga padre del prodotto auto-include e ne recupera l'id con LAST_INSERT_ID |
| `Controllers/QuotesController.cs` | 457 | SCRITTURA | Inserisce la riga di preventivo di un prodotto auto-include senza varianti |
| `Controllers/QuotesController.cs` | 476 | SCRITTURA | Inserisce la riga di preventivo di una singola variante sotto il padre appena creato |
| `Controllers/QuotesController.cs` | 552 | SCRITTURA | Inserisce una riga di preventivo per ciascuna variante del prodotto aggiunto a mano dal catalogo |
| `Controllers/QuotesController.cs` | 671 | SCRITTURA | Riscrive il sort_order di una riga di preventivo, una UPDATE per posizione |
| `Controllers/QuotesController.cs` | 765 | SCRITTURA | Copia le risorse (persone/giorni/trasferte) della sezione dal preventivo alla commessa |
| `Controllers/QuotesController.cs` | 765 | SCRITTURA | Crea in commessa la sezione di costo copiata dal preventivo e ne legge il nuovo id |
| `Controllers/QuotesController.cs` | 765 | SCRITTURA | Copia i reparti collegati alla sezione (INSERT ... SELECT dai reparti della sezione di preventivo) |
| `Controllers/QuotesController.cs` | 822 | SCRITTURA | Crea in commessa le sezioni di costo predefinite che il preventivo non aveva, leggendone il nuovo id |
| `Controllers/QuotesController.cs` | 822 | SCRITTURA | Copia i reparti del template sulla nuova sezione di commessa |
| `Controllers/QuotesController.cs` | 873 | SCRITTURA | Inserisce in commessa una riga project_phases per ogni fase da copiare |
| `Controllers/QuotesController.cs` | 896 | LETTURA | Rilegge le righe materiale del preventivo appartenenti alla sezione corrente |
| `Controllers/QuotesController.cs` | 896 | SCRITTURA | Inserisce una riga materiale in commessa e ne legge l'id per la mappa vecchio→nuovo |
| `Controllers/QuotesController.cs` | 896 | SCRITTURA | Crea la sezione materiali di commessa copiata dal preventivo e ne legge il nuovo id |
| `Controllers/QuotesController.cs` | 917 | SCRITTURA | Inserisce la singola riga materiale in commessa e ne recupera il nuovo id |
| `Controllers/QuotesController.cs` | 944 | SCRITTURA | Rimette il parent_item_id sulle righe materiale di commessa, una UPDATE per riga figlia |
| `Controllers/QuotesController.cs` | 1218 | SCRITTURA | Duplica le righe figlie (varianti) del prodotto clonato appendendole al nuovo padre |
| `Controllers/ResourcesController.cs` | 102 | SCRITTURA | INSERT di un'allocazione res_assignments per ogni dipendente selezionato |
| `Controllers/SalController.cs` | 445 | SCRITTURA | riscrive il sort_order di ogni step SAL trascinato (UPDATE sal_rows SET sort_order=@Sort WHERE id=@Id AND project_id=@Pid) |
| `Controllers/SalController.cs` | 583 | SCRITTURA | riscrive il sort_order di ogni condizione di pagamento riordinata (UPDATE sal_conditions SET sort_order=@Sort WHERE id=@Id) |
| `Controllers/SalController.cs` | 798 | SCRITTURA | helper generico ReorderLookupRows: riscrive il sort_order riga per riga sulla tabella di lookup passata (causali SAP, stati pagamento) |
| `Controllers/SalController.cs` | 1054 | LETTURA | per ogni commessa dell'import legge il prossimo sort_order libero (SELECT COALESCE(MAX(sort_order),-1)+1 FROM sal_rows WHERE project_id=@Pid) |
| `Controllers/SalController.cs` | 1054 | SCRITTURA | inserisce le nuove righe SAL della commessa (INSERT INTO sal_rows con 17 colonne) |
| `Controllers/SalController.cs` | 1054 | SCRITTURA | crea al volo l'header SAL della commessa se manca (INSERT IGNORE INTO project_sal ...) |
| `Controllers/SalController.cs` | 1054 | SCRITTURA | aggiorna l'header SAL della commessa con i valori del backup (UPDATE project_sal SET cliente=COALESCE(...), valore=..., po=..., rif_offerta=..., row_version=row_version+1) |
| `Controllers/SalController.cs` | 1089 | SCRITTURA | per ogni riga SAL nuova di quella commessa esegue l'INSERT INTO sal_rows |
| `Controllers/TemplateController.cs` | 432 | SCRITTURA | scrive il percorso su disco del file appena copiato (UPDATE project_template_files SET disk_path=@DiskPath WHERE id=@Id) |
| `Controllers/TemplateController.cs` | 524 | SCRITTURA | riscrive il sort_order di ogni cartella modello riordinata (UPDATE project_template_folders SET sort_order=@SortOrder WHERE id=@Id) |
| `Controllers/TravelController.cs` | 175 | SCRITTURA | riscrive sort_order di ogni step di trasferta dopo un drag&drop |
| `Controllers/UsersController.cs` | 217 | SCRITTURA | Dopo un DELETE totale, reinserisce una per una le righe employee_departments del dipendente. |
| `Controllers/UsersController.cs` | 245 | SCRITTURA | Dopo un DELETE totale, reinserisce una per una le competenze (employee_competences) del dipendente. |
| `Services/DaneaOrderService.cs` | 81 | LETTURA | Per ogni riga dell'ordine fornitore, interroga il Firebird di Danea con una SELECT su TArticoli LEFT JOIN TIva WHERE CodArticolo=@c per recuperare IDArticolo, descrizione, unità di misura, codice IVA e aliquota. |
| `Services/MoMDbService.cs` | 131 | LETTURA | Per ognuno dei 9 codici reparto conta se esiste già il dipendente wildcard «[XXX] Generico». |
| `Services/MoMDbService.cs` | 180 | SCRITTURA | Inserisce uno per uno i responsabili di una azione MoM (dopo averli cancellati tutti) in mom_action_item_responsibles. |
| `Services/NotificationService.cs` | 364 | LETTURA | Rilegge l'elenco degli ADMIN attivi, identico a ogni giro |
| `Services/NotificationService.cs` | 364 | LETTURA | Cerca i PM delle commesse su cui il dipendente ha lavorato in quel giorno |
| `Services/NotificationService.cs` | 581 | LETTURA | Legge il pm_id (se attivo) della commessa dell'azione MoM, una query per azione |
| `Services/NotificationService.cs` | 581 | LETTURA | Trova i responsabili attivi di una singola azione MoM (tabella N + fallback resp1/2/3) |
| `Services/PermissionSeedService.cs` | 133 | LETTURA | Per ogni persona attiva rilegge tutte le righe di employee_feature_access di quella persona (chiave, accesso, origine). |
| `Services/PermissionSeedService.cs` | 133 | SCRITTURA | È la DELETE della riga di permesso non più concessa, vista dal ciclo ESTERNO sulle persone. |
| `Services/PermissionSeedService.cs` | 167 | SCRITTURA | Cancella la singola riga di permesso di classe che il motore vecchio non concede più. |
| `Services/PlanNotificationService.cs` | 285 | LETTURA | Per ogni dipendente da notificare, esegue una SELECT email, CONCAT_WS(first_name,last_name) FROM employees WHERE id=@Id per recuperare indirizzo e nome del destinatario. |
| `Services/PlanNotificationService.cs` | 436 | SCRITTURA | In SyncSnapshotForAssignments, per ogni assegnazione nuova o modificata appena notificata esegue una INSERT ... ON DUPLICATE KEY UPDATE su res_plan_snapshots per riallineare la foto solo su quelle righe. |
| `Services/QuoteService.cs` | 118 | SCRITTURA | Per ogni sezione copiata, una INSERT ... SELECT che ricopia le righe di quote_cost_section_departments della sezione originale. |
| `Services/QuoteService.cs` | 118 | SCRITTURA | Per ogni sezione copiata, una INSERT ... SELECT che ricopia tutte le risorse (quote_cost_resources) della sezione originale. |
| `Services/QuoteService.cs` | 118 | SCRITTURA | Per ogni sezione di costing del preventivo di origine esegue una INSERT in quote_cost_sections + SELECT LAST_INSERT_ID() per ottenere l'id della sezione copiata. |
| `Services/QuoteService.cs` | 159 | SCRITTURA | Per ogni sezione materiali del preventivo di origine, INSERT in quote_material_sections + SELECT LAST_INSERT_ID() per ottenere l'id della sezione copiata. |
| `Services/QuoteService.cs` | 159 | LETTURA | Per ogni sezione materiali dell'origine esegue una SELECT * FROM quote_material_items WHERE section_id=@Id per leggere le righe da copiare. |
| `Services/QuoteService.cs` | 235 | SCRITTURA | Per ogni template di sezione di costing marcato come default, INSERT in quote_cost_sections + SELECT LAST_INSERT_ID() per creare la sezione del nuovo preventivo. |
| `Services/QuoteService.cs` | 235 | SCRITTURA | Per ogni sezione appena creata, INSERT ... SELECT che copia i reparti dal template (cost_section_template_departments) alla sezione del preventivo. |
| `Services/QuoteService.cs` | 279 | SCRITTURA | Per i prodotti auto-include SENZA varianti, INSERT della singola riga in quote_items (ramo else del ciclo). |
| `Services/QuoteService.cs` | 279 | SCRITTURA | Vista dal ciclo esterno dei prodotti: la INSERT delle varianti in quote_items (ciclo interno di riga 301) viene eseguita prodotti × varianti volte. |
| `Services/QuoteService.cs` | 279 | SCRITTURA | Per ogni prodotto auto-include che ha varianti, INSERT della riga padre in quote_items + SELECT LAST_INSERT_ID() per agganciarci sotto le varianti. |
| `Services/QuoteService.cs` | 301 | SCRITTURA | Per ogni variante del prodotto corrente, INSERT della riga figlia in quote_items con parent_item_id già noto. |
| `Services/TravelFromTimesheet.cs` | 74 | SCRITTURA | riallinea la descrizione dello step al nome attuale della fase (UPDATE travel_steps SET description=@Nome WHERE id=@Id AND description<>@Nome) |
| `Services/TravelFromTimesheet.cs` | 74 | SCRITTURA | crea lo step trasferta mancante per la fase e ne rilegge l'id con LAST_INSERT_ID() |

## Note dei revisori, gruppo per gruppo

- gruppo 1: Gruppo 1 (30 candidati su 3 controller): 14 veri (5 letture, 9 scritture), 10 legittimi/duplicati, 6 falsi positivi da doppia attribuzione.
- 
- SCHEMI RICORRENTI
- 1) Doppia attribuzione del rilevatore (6 casi su 30, tutti in PurchaseRfqController): quando una query sta in un ciclo interno, il rilevatore la segnala anche sul ciclo esterno. Coppie: 155/169 = 167/169, 155/177 = 175/177, 221/240 = 236/240, 308/315 = 312/315, 330/341 = 340/341, 330/346 = 345/346. Le ho marcate FALSO_POSITIVO per non contarle due volte; la correzione sta sul candidato interno.
- 2) Cicli con PIU' query dentro, da correggere insieme e non uno alla volta: SelectWinner riga 620 (622 + 637 + una terza INSERT invisibile, DdpItemEvents.Registra riga 666 = 3 round-trip per riga) e CreateDaneaOrderForRfqs riga 853 (858 + 868 + 870 = altri 3 per riga).
- 3) Pattern «leggo lo stato, poi lo riscrivo» ripetuto tre volte nel file (righe 620/622, 834/836 e in parte 760/772): sempre SELECT item_status FROM bom_items WHERE id = @Id dentro il ciclo. Si risolve con un'unica lettura IN @Ids a monte, ed e' lo stesso identico intervento in tutti e tre i punti.
- 
- N+1 ANNIDATI (il rilevatore NON li vede, sono i piu' costosi del gruppo)
- · TravelController riga 405: dentro il ciclo, fino a 3 chiamate ad ApplyCalcTotal → ProjectCalcSheets.Save, che apre una transazione propria e fa ~7 round-trip ciascuna. Caso peggiore ~25 comandi e 3 transazioni PER RIGA di trasferta. E' il punto peggiore del gruppo, e la query segnalata (409) e' la parte meno grave.
- · PurchaseRfqController riga 760: LoadDetail dentro il ciclo = 3 query per RDO, oltre alla SELECT segnalata.
- · PurchaseRfqController riga 620: DdpItemEvents.Registra = 1 INSERT per riga, non segnalata.
- · PurchaseRfqController riga 688: dentro il try ci sono GetProjectPmIds, GetAcqEmployeeIds e Create del NotificationService; oggi il ciclo gira 1 volta (RDO mono-commessa), quindi non morde — ma e' una mina se la regola cambiasse.
- 
- SORPRESE
- · Riga 868 (CreateDaneaOrderForRfqs): «SELECT project_id FROM bom_items WHERE id = @Id» e' una query del tutto INUTILE — il ProjectId sta gia' nella tupla rowUpdates ed e' scartato dalla destrutturazione «foreach (var (bomItemId, _, advance))». Il ciclo successivo (riga 894) lo legge proprio da li'. Si toglie senza sostituirla con niente.
- · Nello stesso ciclo, la variabile advance calcolata a riga 838-840 non viene mai usata: la UPDATE mette sempre item_status='IO'. La matrice stati e' quindi calcolata e buttata via — incoerenza gia' presente, da non nascondere durante l'accorpamento.
- · Riga 802 (claim ordine Danea) e' l'unico caso in cui l'N+1 e' VOLUTO e va protetto: il commento spiega che un UPDATE ... WHERE id IN libererebbe il claim di un altro utente a meta' generazione, con doppio ordine irreversibile in Danea. Da marcare «non toccare» nel piano.
- · Due cicli sono gia' virtuosi e non compaiono fra i candidati: MarkEmailed (riga 475) fa esattamente l'UPDATE ... WHERE id IN che serve altrove, e Validate (riga 624) legge la matrice stati da AnagraficheCache invece che dal database (correzione E4 gia' applicata). Sono il modello da imitare negli interventi di questo gruppo.
- · ResourcesController.CreateAssignment (riga 102) inserisce N righe SENZA transazione: accorpare in un INSERT multi-VALUES non e' solo piu' veloce, e' anche piu' corretto (oggi un errore a meta' lascia allocazioni parziali).
- 
- FILE ESAMINATI (percorsi assoluti)
- C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\ATEC.PM.Server\Controllers\PurchaseRfqController.cs
- C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\ATEC.PM.Server\Controllers\TravelController.cs
- C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\ATEC.PM.Server\Controllers\ResourcesController.cs
- Letti anche per valutare gli annidamenti: Services\ProjectCalcSheets.cs, Services\DdpTransitionService.cs, Services\DdpItemEvents.cs, Services\TravelFromTimesheet.cs. Nessun file modificato.
- gruppo 2: GRUPPO SENZA VERI N+1 IN LETTURA. Su 30 candidati: 0 VERO_LETTURA, 9 VERO_SCRITTURA, 10 LEGITTIMO, 11 FALSO_POSITIVO. Questi cinque file sono quasi tutti percorsi di scrittura (salvataggi, init, import, sincronizzazione), non pagine che leggono: il bersaglio tipico di E3 (una SELECT ripetuta per riga) qui non c'è.
- 
- DOPPI CONTEGGI DA CICLI ANNIDATI. 10 degli 11 FALSO_POSITIVO sono un solo fenomeno: l'import catalogo di QuoteCatalogController (righe 771-839) ha QUATTRO foreach annidati (781 listini → 790 gruppi → 800 categorie → 810 prodotti → 821 varianti) e il rilevatore attribuisce ogni query interna anche a tutti i cicli esterni. Le coppie reali sono cinque: 781/783, 790/792, 800/802, 810/813, 821/823. Chi legge il censimento non deve contare 15 problemi dove ce n'è uno solo. L'unico FALSO_POSITIVO di natura diversa è DaneaSyncService riga 301, dove il rilevatore ha scambiato l'espressione di iterazione (`foreach (var row in await QueryAsync(...))`) per una query dentro il ciclo: lì il codice fa esattamente la cosa giusta — una query fuori, dizionario in memoria, lookup dentro il ciclo (stesso schema alla riga 310 per i fornitori). Quel dizionario è ciò che evita 18.000 SELECT dentro il ciclo articoli.
- 
- L'UNICO CASO CHE VALE LA PENA TOCCARE è DaneaSyncService.cs riga 334/363: 18.000 upsert una alla volta dentro una transazione che ha appena fatto `UPDATE catalog_items SET is_active=0 WHERE is_active=1`. Non è il numero di round-trip a preoccupare (è un servizio di sfondo), ma il fatto che tenga i lock su tutta catalog_items per tutta la durata, ogni 6 ore, anche in orario di lavoro, mentre le pagine Catalogo/Acquisti/distinte leggono quella tabella. Accorpando a blocchi da 500-1.000 la transazione si accorcia di un ordine di grandezza. Gli altri due cicli dello stesso file (fornitori ~2.200, clienti ~400) NON hanno transazione esplicita e restano legittimi.
- 
- SCHEMA RICORRENTE «INSERT + LAST_INSERT_ID». Tre casi (ProjectCosting init, creazione/modifica prodotto, import catalogo) hanno lo stesso motivo di esistere: serve l'id della riga appena creata per inserire i figli. In ProjectCostingController si scioglie con eleganza — due INSERT ... SELECT al posto del ciclo, agganciando i reparti tramite project_cost_sections.template_id (voci 66/68 e 66/74: vanno riscritte INSIEME, l'una dipende dall'altra). Nell'import invece la catena a quattro livelli non si scioglie senza riletture: l'unico livello accorpabile è quello delle varianti (821/823), che non ha figli.
- 
- SORPRESA — UN N+1 VERO CHE IL RILEVATORE NON HA VISTO. `CollectChildCategoryIds` in QuoteCatalogController.cs righe 842-850: `SELECT id FROM quote_categories WHERE parent_id=@ParentId` seguita da un foreach che RICHIAMA se stesso su ogni figlio. È una SELECT per ogni nodo del sottoalbero, e viene chiamata da GetProducts (riga 349) — endpoint utente, la griglia prodotti del Catalogo Preventivi filtrata per categoria. Il rilevatore non la vede perché la query sta PRIMA del foreach e la moltiplicazione avviene per ricorsione. È l'unico VERO_LETTURA del gruppo (impatto basso-medio: l'albero categorie ha decine di nodi, non migliaia) e si sostituisce con una CTE ricorsiva `WITH RECURSIVE`, la stessa che servirebbe per il controllo antenati della riga 306. Consiglio: censirla a mano nel piano E3, insieme al fatto che il `while` di riga 306 non ha guardia sui giri e va in loop infinito se i parent_id contengono un ciclo che non passa per la categoria spostata.
- 
- RISCHIO TRASVERSALE PER TUTTE LE RISCRITTURE: quasi tutti questi cicli stanno dentro `using var tx = c.BeginTransaction()` e alimentano contatori progressivi (sort_order, count, totalVars) usati nei messaggi di ritorno. Accorpare significa quasi sempre spezzare il ciclo in «prepara la lista in memoria» + «una istruzione», e in quel passaggio è facile perdere l'ordine dei sort_order o cambiare quali colonne vengono aggiornate nella parte ON DUPLICATE KEY. Da controllare uno per uno, e in ProjectCostingController.SetSectionDepartments serve una guardia sulla lista vuota (oggi il ciclo semplicemente non gira, un `IN @Ids` vuoto in Dapper invece esplode).
- gruppo 3: Su 30 candidati: 5 FALSO_POSITIVO, 5 VERO_LETTURA, 20 VERO_SCRITTURA, 0 LEGITTIMO. Un solo impatto alto.
- 
- SCHEMI RICORRENTI.
- 1) Tutti e 5 i falsi positivi hanno la stessa causa: il rilevatore aggancia le query al `foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })` del metodo NotifyDdpChange, che sta in un ALTRO metodo e non contiene query, solo due SendAsync SignalR. Vale per DdpFeedbackController (righe 34→41/53/80/92) e DdpRowOffController (38→48). Ogni file del progetto con questo helper produrrà lo stesso rumore: si può filtrare a monte.
- 2) Questi due controller sono anzi gli esempi da IMITARE: DdpFeedbackController.GetAcquisti/GetMagazzino caricano universo, conteggi e override con 3 query totali e poi incrociano in memoria con ToDictionary/HashSet/.Where; DdpRowOffController ha già un endpoint bulk che accorpa N spegnimenti in un INSERT IGNORE multi-VALUES e una DELETE ... IN.
- 3) 17 dei 18 candidati di QuotesController stanno su due sole operazioni RARE — ReloadAutoIncludes (una volta per preventivo) e ConvertToProject (qualche volta al mese) — dentro transazioni, con propagazione di LAST_INSERT_ID di riga in riga. Sono tutti veri, tutti accorpabili in teoria, tutti a impatto basso e a rischio medio-alto: rifarli richiede materializzare mappe vecchio-id→nuovo-id che oggi non esistono. Il mio giudizio: E3 dovrebbe lasciarli stare, o al massimo prendere i tre a rischio quasi nullo (873/875 le fasi e 1218/1220 i figli del clone diventano INSERT...SELECT diretti; 944/949 diventa una UPDATE ... CASE WHEN da dati già in memoria).
- 
- IL VERO BERSAGLIO DEL GRUPPO è uno solo: CostingDataService.SaveDistributionsBatch righe 189-203. L'endpoint si chiama "distributions/batch" ma esegue N UPDATE separate, e il client (atec-pm-web/src/features/preventivi/costing-panels.tsx, funzione persist a riga 170) rimanda TUTTE le righe a ogni singola modifica di percentuale e a ogni toggle di ombreggiatura. Su un preventivo IMPIANTO con centinaia di righe materiale sono centinaia di round-trip per ogni clic dell'utente. Due UPDATE ... CASE WHEN ... WHERE id IN risolvono tutto. Attenzione: la UPDATE sulle righe materiale (riga 199) NON ha il filtro di appartenenza al preventivo che invece ha quella sulle sezioni (riga 193) — chi la riscrive dovrebbe aggiungerlo.
- 
- N+1 ANNIDATI (il rilevatore non li vede).
- · NotificationService.Create (righe 31-57): il ciclo sui destinatari è N+1 di per sé, ma il punto è che Create viene chiamato DENTRO altri cicli in 9 punti dello stesso file (righe 323, 388, 431, 532, 621, 683, 757, 841, 919) e in ProjectsController (1393, 1862). In più ogni chiamata apre una NUOVA connessione. Un giro del job con 20 scadenze × 10 destinatari = 20 connessioni e ~220 round-trip. È il difetto strutturalmente peggiore del gruppo, mitigato solo dal fatto che gira ogni 6 ore in background.
- · QuotesController righe 896→913→919: ciclo annidato sezioni materiali × righe. Il rilevatore ha contato la stessa INSERT due volte (896/919 e 917/919) e la stessa INSERT delle varianti due volte (457/481 e 476/481): quattro voci per due sole istruzioni. Le ho tenute separate come richiesto, indicandolo nel motivo.
- 
- SORPRESE.
- · QuotesController riga 671 (ReorderItems, UPDATE per riga): endpoint funzionante ma MORTO — `reorderQuoteItems` è definita in atec-pm-web/src/lib/api/quotes.ts riga 179 e non è richiamata da nessun componente. Correggerla non sposta nulla; semmai è codice da valutare per rimozione.
- · NotificationService riga 381: non è un N+1 parametrico ma una query COSTANTE dentro un ciclo (gli ADMIN attivi, senza parametri), per giunta copia testuale del metodo GetAdminIds() già presente a riga 106 dello stesso file. Rischio zero, si sposta di due righe.
- · NotificationService riga 608: la SELECT del pm_id viene rifatta con lo STESSO project_id per ogni azione MoM della stessa commessa — stesso parametro, stessa risposta, N volte.
- · Nessun candidato è risultato LEGITTIMO: anche i cicli su liste piccole o su operazioni rare sono tecnicamente accorpabili, e li ho classificati come veri abbassando l'impatto invece di assolverli.
- gruppo 4: Gruppo 4, 30 candidati su 6 file: 0 falsi positivi puri, 4 LEGITTIMO, 3 VERO_LETTURA, 23 VERO_SCRITTURA. Nessuno di alto impatto — è un gruppo fatto quasi tutto di percorsi «una tantum» (duplicazione preventivo, import Easyfatt, seed all'avvio, sync notturna, generazione ordine Danea), non di apertura pagine.
- 
- SCHEMI RICORRENTI. (1) Il gruppo è dominato da un unico anti-pattern: INSERT + SELECT LAST_INSERT_ID() dentro un foreach per costruire una mappa id-vecchio → id-nuovo. Compare 5 volte in QuoteService (righe 74, 120, 161, 187, 237, 290). È la ragione per cui questi cicli NON si accorpano con un semplice multi-VALUES: serve sostituirli con INSERT ... SELECT + una query di rimappatura che appaia copia e originale su (parent, sort_order). Il rischio è sempre lo stesso e sempre lo stesso il collaudo da rifare: duplicare un preventivo con righe ANNIDATE (padre + figli) e verificare che la gerarchia e l'ordine siano identici. (2) Il secondo schema è if/else dentro lo stesso ciclo (UPDATE se esiste, INSERT se è nuovo): 3 volte in ImportController, 1 in CodexSyncService, 1 in SalDbService. Il rilevatore li conta come due difetti; sono un ciclo solo, e la correzione giusta è una sola: separare la lista in due (o unificare in un upsert) e fare due passate accorpate.
- 
- N+1 ANNIDATI (il rilevatore non li vede, li ho ricostruiti aprendo il codice). Due, entrambi in QuoteService:
- · CopyQuoteCosting righe 159→176→181→187: per ogni sezione materiali si RILEGGE le sue righe (SELECT per sezione) e poi si INSERISCE riga per riga. Costo = sezioni × righe. È il peggiore del gruppo. I candidati 159/176, 159/187 e 181/187 sono lo stesso pezzo di codice visto da tre angolazioni: vanno corretti in un colpo solo.
- · AutoPopulateItems righe 279→301→312: prodotti auto-include × loro varianti. Stesso schema ma su numeri piccoli (poche decine di INSERT), quindi innocuo.
- 
- SORPRESE.
- · PlanNotificationService riga 293 è il difetto più assurdo e insieme il più facile: la query che rilegge email e nome del dipendente è del tutto SUPERFLUA — CurrentSql (riga 51) e SnapshotSql (riga 67) portano già a bordo EmployeeEmail e EmployeeName, ma GroupByEmployee li butta via tenendo solo il booleano HasEmail. Si elimina la query, non si accorpa. Costo zero, rischio quasi zero.
- · PlanNotificationService riga 154 (TakeSnapshot) è tecnicamente inutile come ciclo C#: la foto del piano si può scattare interamente in SQL con INSERT ... SELECT dalle res_assignments, senza far transitare centinaia di righe dal server applicativo. È il ciclo con più iterazioni del file.
- · CodexSyncService: la parte di LETTURA è già fatta bene (localRows caricata una volta sola fuori dal ciclo, corrispondenze risolte su dizionari) — il difetto è tutto nella scrittura. Attenzione però: 18.000 round-trip stanno dentro UNA transazione unica aperta a riga 81; il problema vero non è la lentezza del sync (nessuno lo guarda) ma il lock prolungato su codex_items, che è la tabella di Composizione Codex e dei picker articoli. È l'unico candidato del gruppo che tocca gli utenti pur non stando su nessun percorso di pagina.
- · Trappola da non toccare: SyncSnapshotForAssignments (riga 436) ha un commento esplicito che impone le DELETE PRIMA degli upsert (riassegnazione = stesso assignment_id sia fra le Deleted sia fra le New). Chi accorpa quel ciclo senza leggere il commento fa rinotificare le modifiche.
- · Trappola numero due: CodexSyncService riga 116, la UPDATE ha una regola condizionale su prezzo_forn (il prezzo locale già valorizzato NON va sovrascritto). Tradotta male in un ON DUPLICATE KEY UPDATE si perdono i prezzi fornitore inseriti a mano.
- · Le anteprime dell'ImportController (righe 337-379 e 501-528) fanno GIÀ la cosa giusta: una SELECT sola sui dati esistenti e poi dizionario in memoria per il confronto duplicati. Il rilevatore non le ha segnalate ed è corretto così — le ho verificate per escludere falsi negativi al contrario.
- 
- DOVE INTERVERREI, IN ORDINE: (1) PlanNotificationService riga 293, cancellare la query — 5 minuti, rischio nullo; (2) CodexSyncService righe 159+167, per accorciare la transazione lunga; (3) il blocco CopyQuoteCosting materiali (159/176/181/187), l'unico annidamento che pesa; (4) ImportController articoli (410), solo se si prevede un altro reimport del catalogo. Il resto (SalDbService per intero, i due rami clienti, InitQuoteCosting, AutoPopulateItems, SyncSnapshotForAssignments) non vale il rischio.
- gruppo 5: Gruppo di 30 candidati, esito: 1 FALSO_POSITIVO, 7 LEGITTIMI, 2 VERO_LETTURA, 20 VERO_SCRITTURA. Un solo candidato di impatto ALTO.
- 
- IL CASO SERIO — Services/TravelFromTimesheet.cs. È l'unico punto del gruppo che merita davvero E3. Rebuild() non è un'azione su richiesta: TimesheetController.RigeneraTrasferta (righe 403-408) lo invoca a OGNI salvataggio di ore, e PhasesController a ogni rinomina di fase. Ogni Rebuild riscrive da capo TUTTE le giornate di cantiere storiche della commessa, una INSERT ... ON DUPLICATE KEY per riga (riga 107): su una commessa di cantiere avviata sono centinaia di round-trip per una singola ora imputata. I tre candidati del ciclo sulle fasi (righe 77/83/92) stanno sullo stesso percorso e si sistemano insieme con una sola lettura anticipata degli step in un dizionario. Se di questo gruppo si tocca una cosa sola, è questo file.
- 
- DUE N+1 ANNIDATI CHE IL RILEVATORE NON VEDE. (1) OfficinaRowSync.CongelaTipoDaStato (Services/OfficinaRowSync.cs:59) esegue una SELECT item_status più un UPDATE work_type ad ogni chiamata, ed è invocata DENTRO tre cicli di ProjectsController (righe 1613, 1642, 1830): il costo reale di quei cicli non è 1 query per giro ma 3. Chi correggerà quei cicli deve accorpare anche il congelamento del tipo, o il guadagno si dimezza. (2) L'INSERT sal_rows dell'import SAL (SalController riga 1093) sta in un ciclo dentro un ciclo (commesse × righe), e per questo compare due volte fra i candidati (1054/1093 e 1089/1093): è un intervento solo, non due. Stessa cosa in DdpStatusesController, dove la riga 163 è attribuita sia al foreach interno (162) sia a quello esterno (143). E CopyFolderRecursive (TemplateController) è ricorsivo: i suoi cicli si moltiplicano per il numero di sottocartelle dell'albero.
- 
- SCHEMI RICORRENTI. Quattro riordini drag&drop identici, copiati parola per parola: SalController righe 445, 583, 798 e TemplateController riga 524, tutti «foreach id → UPDATE sort_order». Si risolvono con un unico helper UPDATE ... SET sort_order = CASE id WHEN ... END WHERE id IN @Ids; sono a basso impatto (6-30 elementi) ma è il tipo di correzione che si scrive una volta e vale per quattro punti. Due dettagli da non perdere nel farlo: il riordino degli step SAL ha una clausola AND project_id=@Pid che è un controllo di sicurezza (impedisce di riordinare righe di altre commesse passando id arbitrari) e va conservata; e il nome tabella in ReorderLookupRows è interpolato in stringa da una whitelist di costanti — costruendo il CASE non va interpolato nient'altro, o si apre una injection dove oggi non c'è.
- 
- I SETTE LEGITTIMI sono tutti dello stesso tipo e non vanno toccati: cicli su array letterali cablati nel sorgente (4 condizioni standard, 2 stati pagamento, 2 causali SAP, 6 step del modello SAL, 32 etichette del catalogo attività), più il DELETE di compensazione dentro un catch che rilancia e la INSERT che ha bisogno di LAST_INSERT_ID per costruire il nome del file su disco. Il seed del catalogo attività (MilestonesDbService) gira una volta nella vita del database, dentro la migrazione M015.
- 
- L'UNICO FALSO POSITIVO è ProjectsController riga 1587, ed è istruttivo: il rilevatore ha marcato foreach (... in c.Query<...>(...)) perché la parola Query compare sulla riga del foreach, ma la query si esegue una volta sola e alimenta un Dictionary. È esattamente il pattern giusto — una lettura fuori dal ciclo, il raggruppamento in memoria — e conviene usarlo come modello per correggere gli altri candidati dello stesso file. Vale la pena segnalare anche che BuildSalImportPlan (SalController riga 1174) fa già la cosa giusta in modo esemplare: tre query totali per progetti, header e righe esistenti, e poi tutto il piano si costruisce in memoria. Il rilevatore non l'ha segnalato, giustamente, ma è il termine di paragone per il resto del file.
- 
- SORPRESA COLLATERALE (fuori dal perimetro di E3, ma emersa leggendo). ImportOfficinaComposition (ProjectsController riga 1547) e ResetConditions (SalController riga 593) fanno scritture multiple SENZA transazione: un errore a metà lascia rispettivamente una distinta importata a metà e le condizioni di pagamento cancellate ma non ripopolate. Accorpare quei cicli in una query sola li renderebbe atomici per effetto collaterale — è un argomento in più a favore della correzione, ma il difetto esiste già adesso, indipendentemente da E3.
- gruppo 6: Radice dei file: C:/Users/diego/Desktop/ATEC_PM_CSharp_v5/ATEC_PM/ATEC.PM.Server (i percorsi in «file» sono relativi, come nel JSON dei candidati).
- 
- BILANCIO DEL GRUPPO 6 (30 candidati): 3 VERO_LETTURA, 9 VERO_SCRITTURA, 18 LEGITTIMO, 0 FALSO_POSITIVO in senso stretto — anche se le 5 voci di FullBackupService sono «falsi bersagli» del rilevatore (una query per TABELLA diversa, non la stessa query con un parametro diverso): le ho messe in LEGITTIMO perché la definizione di FALSO_POSITIVO parla di query fuori dal ciclo o di raggruppamento già fatto in memoria.
- 
- SCHEMI RICORRENTI
- 1) «DELETE-all + reinserisci a righe»: UsersController 217 e 245, MoMDbService 180. Sempre poche righe, sempre accorpabile in una INSERT multi-VALUES, rischio basso purché si conservi l'ordine (in MoMDbService l'ordine decide resp1/2/3).
- 2) Import/backup: le 8 voci dell'Import MoM e le 5 di FullBackupService sono tutte operazioni manuali e rare. Le ho tenute LEGITTIME seguendo la regola «codice che gira una volta sola», indicando comunque dove sarebbero accorpabili.
- 3) Duplicati del rilevatore per annidamento: nell'Import MoM la STESSA Execute compare fino a tre volte (riga 612 vista dai cicli 581 e 610; riga 638 dai cicli 581 e 634; riga 667 dai cicli 581, 634 e 665). Sono 3 punti di codice reali, non 8.
- 
- I DUE DIFETTI CHE VALE LA PENA CORREGGERE
- · MoMDbService.MigrateLegacyResponsibles (riga 240): carica TUTTA mom_action_items e fa un COUNT per riga a ogni avvio del server, chiamando poi SaveItemResponsibles che cicla a sua volta in INSERT. È l'unico N+1 annidato del gruppo e sta sul percorso di avvio, quindi sui tempi del deploy (il rollback guarda /api/health/ready). A regime non deve migrare più niente: basta filtrare la SELECT di riga 236 e il ciclo diventa vuoto.
- · PhasesController.BulkCreate (riga 423): tre query per ogni fase inserita, su un dialog dove l'utente ne spunta decine — ~60 round-trip in transazione per un inserimento da 20 fasi. Le tre si accorpano insieme (2 pre-letture + 1 INSERT multi-VALUES); il tranello è la sezione (COALESCE @sid / sezione principale del template, il punto delicato del commento v73) e il doppione all'interno della stessa richiesta.
- Subito dopo: MoMController.ReorderItems (riga 315), un UPDATE per riga a ogni drag&drop del foglio MoM, che diventa un solo UPDATE ... SET sort_order = CASE id WHEN ... END.
- 
- SORPRESE (cose che il rilevatore NON vede)
- · PhasesController.SaveAssignments (riga 648): il costo non è la INSERT ma _notif.Create dentro il ciclo. NotificationService.Create (Services/NotificationService.cs riga 31) fa `using var c = _db.Open()`, cioè APRE UNA CONNESSIONE NUOVA per ogni tecnico assegnato, e dentro ha un altro ciclo di INSERT sui destinatari. Accetta già un array di destinatari: basta chiamarlo una volta sola fuori dal ciclo.
- · ChatController.AggiungiMenzionatiAllaChat (riga 670) e MoMController.BuildImportPlan (riga 740) fanno GIÀ la cosa giusta: query massive fuori dal ciclo e confronto in memoria (BuildImportPlan risolve l'intero import con 4 query). Da non toccare.
- · MoMDbService.EnsureWildcardEmployees non è solo codice di avvio: MoMController riga 535 lo richiama sull'endpoint GET /lookups/wildcards, quindi le sue 9 COUNT girano a ogni apertura del foglio MoM. Costo modesto, ma è un metodo di seeding finito su un percorso di pagina.
- · MoMDbService riga 151: il COUNT sul legame di un dipendente appena creato non può mai essere soddisfatto (id nuovo di zecca): query inutile, non solo ripetuta.
- gruppo 7: Gruppo 7 (30 candidati su 7 file): 6 VERO_LETTURA, 14 VERO_SCRITTURA, 6 LEGITTIMO, 4 FALSO_POSITIVO. Nessun candidato ad alto impatto: questi file stanno tutti su gesti espliciti (import, assegnazione massiva, pagine di configurazione), non su schermate che si aprono di continuo.
- 
- SCHEMI RICORRENTI. (1) Un ciclo, piu query: 4 candidati sono lo stesso ciclo di CodexController riga 911 (917/934/952/961/966), 3 lo stesso ciclo riga 393, 2 lo stesso ciclo riga 821, 2 lo stesso ciclo CashFlow riga 102 e 2 la riga 127. I 30 candidati sono in realta 15 cicli distinti. Vanno riscritti a blocco: correggere una query sola di un ciclo lascia i round-trip identici. (2) Tre candidati di CostSectionsController (160/195/215) sono la STESSA riga copiata in tre endpoint. (3) Coppie preview/commit: CodexController 821 e 911 sono lo stesso algoritmo scritto due volte (anteprima import e salvataggio import); vanno corretti insieme o le due schermate iniziano a dire cose diverse.
- 
- FALSI POSITIVI: tutti e 4 in WorkRequestsController, tutti dallo stesso errore — il foreach di riga 63 e a una sola istruzione (chiude a riga 64) e itera su due gruppi SignalR cablati, senza nessuna query dentro; il rilevatore gli ha attribuito quattro query che stanno in metodi successivi (GetOfficinaRows, PatchOfficinaField). Il file e anzi un buon esempio del pattern corretto: due query totali e ordinamento in memoria.
- 
- N+1 ANNIDATI CHE IL RILEVATORE NON VEDE. Il caso serio del gruppo e PermissionAdminService.CalcolaApplicaClasse (riga 586): oltre alla query segnalata ci sono LeggiPersona/RighePersona/PacchettoClasse (righe 595-600, tre query per persona dietro chiamate a metodo) e un ciclo annidato su ~100 chiavi funzione che chiama ScriviRiga (2-3 query ciascuna). Applicare una classe a 35 persone = migliaia di round-trip in una transazione unica. Il candidato segnalato (auth_features rilette a ogni persona) e la parte piu facile: e una query invariante, si sposta fuori dal ciclo senza toccare la logica. Secondo caso annidato: CheckListController riga 472 x riga 507 (tabelle del backup x attivita), ma e un import manuale.
- 
- SORPRESE. (a) CashFlowController: SyncRobotCategories fa UPDATE/INSERT/DELETE ed e chiamato dalla GET (riga 35) — la pagina Flusso di Cassa SCRIVE a ogni apertura. Il numero di query e piccolo (pochi robot), ma vale la pena saperlo. (b) CodexController riga 409: l'UPDATE ... WHERE codice_nuovo IS NULL non e pigrizia, e la guardia di concorrenza per riga; con codice_nuovo UNIQUE, un UPDATE massivo che sbatte su un duplicato farebbe fallire l'intero lotto invece di saltare una riga — e il punto dove sconsiglio l'accorpamento. (c) CodexController riga 952: sembra la lettura piu facile da portare fuori dal ciclo, ma il commento del codice avverte che deve vedere anche le righe inserite nei giri precedenti (codice ripetuto nello stesso file = quantita sommata): portandola fuori senza aggiornare la mappa in memoria si duplicano le righe di composizione. (d) CheckListController.BuildImportPlan (riga 597) non e tra i candidati ed e la dimostrazione che qui si sa gia fare la cosa giusta: tre query fuori dal ciclo e poi tutto il matching con Where/HashSet in memoria.
- 
- SE SI DEVE SCEGLIERE COSA TOCCARE: primo PermissionAdminService riga 601 (una riga spostata, rischio nullo), poi il blocco CodexController 821+911 sulle sole letture di codex_items/catalog_items (WHERE codice IN, dizionario), poi CodexController 398 e 416. Il resto e rumore a basso impatto.
- gruppo 8: Trenta candidati su sette file, e il quadro è netto: 24 sono difetti veri (17 VERO_SCRITTURA, 4 VERO_LETTURA... in realtà 5 letture contando CodexGenerator), 6 sono legittimi. Nessun falso positivo puro nel senso di «query fuori dal ciclo»: il rilevatore non ha sbagliato bersaglio, ha solo agganciato sei volte cicli che non sono iterazioni su dati (le riprove d'avvio di DbService, il backfill una tantum, e le due condizioni while(query) del generatore Codex, dove la query È la condizione e ogni valutazione dipende dal risultato precedente).
- 
- SCHEMA DOMINANTE — il delete-and-reinsert. Undici candidati su trenta hanno la stessa forma: una DELETE che azzera tutto, poi un ciclo che reinserisce riga per riga. Lo si trova in ProjectCalcSheets (foglio di calcolo), DdpAggregations (stati), QuoteCosting (reparti sezione, varianti da catalogo, distribuzione prezzi), Milestones (import con ReplaceExisting). Si correggono tutti allo stesso modo — una INSERT multi-VALUES — e tutti hanno lo stesso identico trabocchetto: il sort_order progressivo calcolato in C# con ++, che va replicato esattamente o l'ordine a video cambia sotto il naso dell'utente.
- 
- N+1 ANNIDATI (il rilevatore non li vede, sono tre e li ho marcati nel motivo):
- · PermissionSeedService 133→145/167: persone × funzioni ≈ 35 × 60 ≈ 2.000 scritture per ogni lancio del seed, più altrettante nel registro. È il conteggio più alto del gruppo. Salvato dal fatto che è un'operazione di amministrazione manuale — ma se qualcuno la agganciasse al deploy diventerebbe subito alto impatto.
- · MilestonesController 318→340: commesse del backup × milestone per commessa, da centinaia a migliaia di INSERT in un import.
- · CodexGeneratorService 228: due query per articolo (controllo collisione + prenotazione) su una selezione che può essere di centinaia, il tutto sotto GET_LOCK esclusivo — qui il costo vero non è la latenza ma il lock tenuto aperto, che ferma gli altri operatori che stanno ricodificando.
- 
- DOPPIONI DA CORREGGERE INSIEME: 318→348 e 340→348 (Milestones) sono lo stesso statement visto dai due cicli annidati; idem 133→154 e 145→154, e 133→173 e 167→173 (PermissionSeed). QuoteCosting 337 e 349 sono i due rami di uno stesso if/else e vanno rifatti con una funzione sola parametrizzata sulla colonna. Nove candidati su trenta si chiudono quindi con quattro interventi.
- 
- DUE COSE TROVATE STRADA FACENDO, che non sono prestazioni ma valgono più di E3:
- 1. Cinque di questi cicli scrivono N righe SENZA TRANSAZIONE — Milestones.Reorder (182), Milestones.SeedFromCatalog (271), QuoteCosting.GeneratePricingDistribution (298-312, con la DELETE già eseguita), QuoteCosting.RebalancePricingDistribution (336-353) e PermissionSeedService.Seed. Un errore a metà lascia oggi un ordine misto, un precarico parziale, una distribuzione prezzi a pezzi o metà ufficio con i permessi a metà strada. Accorpare in una query sola chiude il buco per costruzione: è l'argomento migliore per fare questi interventi, più del risparmio di millisecondi.
- 2. MilestonesController esegue DUE VOLTE la stessa lettura per commessa durante l'import: BuildImportPlan la fa a riga 429 e Import la rifà a riga 331, a pochi millisecondi di distanza.
- 
- UN PUNTO SU CUI IL FILE FA GIÀ LA COSA GIUSTA e va riconosciuto: BuildImportPlan carica TUTTE le commesse in una query sola (riga 391) e poi cerca in memoria con FirstOrDefault — esattamente il pattern corretto. Lo stesso metodo però non lo applica alle milestone. Mezzo file corretto e mezzo no. Allo stesso modo FotografiaVecchia in PermissionSeedService sembra un N+1 (interroga il motore permessi per ogni funzione di ogni persona) ma NON lo è: FeatureAccessService tiene in cache i grant per persona, quindi il database lo vede una volta sola per persona. Da non segnalare come difetto.
- 
- SE SI DEVE SCEGLIERE COSA FARE PER PRIMO in questo gruppo, l'ordine è: (1) ProjectCalcSheets 128 — unico che sta su un gesto ripetuto più volte al giorno con decine di righe e che per giunta tiene aperto un FOR UPDATE mentre fa i round-trip; (2) Milestones.Reorder 182 e SeedFromCatalog 271 — gesti interattivi, e si guadagna la transazione che oggi manca; (3) QuoteCosting rebalance 337/349 — poche righe ma slider interattivo e coerenza delle percentuali a rischio. Tutto il resto (import backup, seed permessi, bulk Codex, push catalogo) è manutenzione: accorpabile, ma nessun utente lo sente. E QuoteCosting 269 (push verso il CATALOGO condiviso) lo lascerei proprio fuori: 2-10 UPDATE contro il rischio di scrivere valori incrociati in un'anagrafica che tutti i preventivi futuri erediteranno.