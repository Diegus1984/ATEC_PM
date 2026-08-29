# Piano — Ripartenza archivio Danea «Atec» + trasferimento catalogo selettivo

> Definito con Diego il 22/07/2026. Obiettivo: nuovo archivio Danea **vuoto** chiamato
> «Atec» e una pagina in ATEC PM per trasferire articoli scelti dal vecchio catalogo al
> nuovo, portandosi dietro tutto il necessario (fornitore, IVA, UM, prezzi) senza
> corrompere l'archivio.

## Decisioni prese (22/07/2026)

| Tema | Decisione |
|---|---|
| Codice articolo nel nuovo archivio | **Resta il codice Danea attuale**; il codice ATEC continua a vivere in Extra1 |
| Criterio di trasferimento | **Tutti gli articoli trasferibili**, con o senza codice ATEC (nessun blocco) |
| Prezzi/listini | **Si portano i prezzi correnti** (costo fornitore + listini) |
| Sync ATEC PM | **Subito sul nuovo «Atec»**: lo specchio `catalog_items` riflette solo il nuovo archivio |
| Anagrafiche | **Tutte in blocco all'inizio**: 2.214 fornitori + 406 clienti copiati prima degli articoli |
| Giacenze/movimenti/documenti | **NON si trasferiscono**: ripartenza pulita, la giacenza rinasce dai documenti nuovi |
| Immagini articolo | **SI trasferiscono** insieme all'articolo (aggiunta 22/07/2026) |

## Conseguenze architetturali

- **Doppia connessione Firebird** in ATEC PM: `DaneaSync` punta ad «Atec» (sync + scrittura
  Extra1), nuova `DaneaOld` **in sola lettura** verso il vecchio archivio (sorgente della
  pagina di trasferimento — lo specchio locale non basta più, perché rifletterà il nuovo).
- Il sync fa upsert per `code`: gli articoli trasferiti (stesso codice) RIAGGANCIANO le
  righe `catalog_items` esistenti (cambia solo `easyfatt_id` → id del nuovo archivio).
  I riferimenti `bom_items.catalog_item_id` e il mapping ATEC **sopravvivono**.
- DA VERIFICARE prima dello switch: cosa fa il sync con gli articoli spariti dal Danea
  di riferimento (deve disattivare `is_active=0`, non cancellare — altrimenti si
  orfanizzano le distinte).
- Scritture nel nuovo archivio secondo il runbook Warehouse (`docs/danea.md`):
  identificatori quotati, ID via `GEN_ID`, transazione per unità di lavoro, fail-fast,
  Danea chiuso sull'archivio target durante i lotti, sempre backup prima.

## Fasi

### F0 — Preparazione (manuale + config) — FATTA 22/07/2026
- [x] Archivio creato da Diego: **`Atec_PM.eft`** in `D:\DANEA\NON TOCCARE DANEA\Archivi\`
      (stesso server Firebird 192.168.2.115:31976, stesse credenziali).
- [x] Config: `DaneaSync:EftFilePath` → **Atec_PM.eft** (sync/mapping sul NUOVO, come da
      decisione) + `DaneaSync:EftFilePathOld` → Srl-2020-2021.eft (sorgente trasferimento).
      ⚠️ Finché F2 non trasferisce articoli, «Assegna ATEC»/mapping falliranno (l'articolo
      non esiste in Atec_PM): è il transitorio accettato.
- [x] Ricognizione: schema identico (63 tabelle, generatori inclusi); colonne testo tutte
      **WIN1252**; IVA/pagamenti/categorie custom individuate e COPIATE (v. F1);
      FK TAnagrafica → TIva/TPagamenti/TAgenti/TConti/TRisorse/TNazioni;
      FK TArticoli → TAnagrafica/TCategorie/TIva/TSottocategorie/TClassiProvv.
- [x] **GOTCHA CHARSET (fondamentale per F2)**: connettersi con `Charset=WIN1252` +
      `CodePagesEncodingProvider` registrato, MAI NONE per le SCRITTURE — con NONE il
      provider .NET encoda asimmetrico, i non-ASCII diventano mojibake (`ï¿½`) e i campi
      pieni esplodono in "string right truncation". (NONE resta ok per le sole letture
      del sync legacy.)
- [x] Ricognizione immagini: **file JPG esterni** (non BLOB), referenziati per solo nome
      file in `TArticoli.PathImmagine_Import` (9.082 articoli su 10.609). La CARTELLA
      sul server Danea è ancora da individuare (chiedere a Diego / guardare sulla
      macchina 192.168.2.115) → serve per la copia file in F2.

### F1 — Bootstrap anagrafiche (one-shot) — FATTA 22/07/2026
- [x] Eseguita via runner (`scratchpad/fbprobe`, comando `bootstrap`, dry-run + run):
      lookup (TIva +15, TConti, TPagamenti +192, TRisorse +7, TCategorie +40,
      TSottocategorie +5, TClassiProvv, TNazioni) → **TAnagrafica 2.656 righe con gli
      IDAnagr ORIGINALI preservati** (target vuoto: così i riferimenti articoli→fornitori
      e gli easyfatt_id dei clienti nello specchio restano 1:1) + TAnagraficaContatti (6)
      + TAnagraficaDest (258); generatore `TAnagrafica__IDAnagr` riallineato a 3080.
- [x] Verifica valore-per-valore old vs new: anagrafiche/contatti/destinazioni 100%
      identiche; bonificati 5 pagamenti e 1 aliquota IVA corrotti dal primo giro in
      charset NONE. Restano differenze VOLUTE: righe standard del nuovo archivio
      (etichette di fabbrica) e saldi TRisorse a 0 (ripartenza).
- [ ] (Facoltativo, per ri-esecuzioni future) endpoint admin `bootstrap-anagrafiche`
      in ATEC PM che replica il runner — utile se nel vecchio nascono nuove anagrafiche
      durante la convivenza.

### F2 — Pagina «Trasferimento catalogo» — IMPLEMENTATA 22/07/2026
- [x] `DaneaMigrationService` + `DaneaMigrationController` (`/api/danea-migration`:
      status / old-articles / transfer, ruoli ADMIN/PM/RESP_REPARTO, lotti max 500).
      Charset WIN1252 + `CodePagesEncodingProvider` registrato in Program.cs.
- [x] **IDArticolo PRESERVATO** (come gli IDAnagr in F1): riferimenti 1:1 e
      `catalog_items.easyfatt_id` resta valido senza attendere il sync; generatore
      `TArticoli__IDArticolo` riallineato al MAX dopo ogni lotto.
- [x] Con l'articolo viaggiano `TArticoliForn` (fornitori alternativi) e
      `TArticoliCodBarre`; copia dinamica colonne comuni. TDiba (distinte base) ESCLUSA.
- [x] Immagini: copia file da `AllegatiPathOld\Prod|Prod2` → `AllegatiPathNew\Prod`
      (jpg + miniatura " Small.bmp"), riferimento in `PathImmagine_Import` già nella riga.
      File mancante = warning nel report, non blocca l'articolo.
- [x] Pagina web `/danea-migrazione` («Trasferimento Danea», sezione Codex/Catalogo,
      feature key `nav.danea_migration`, migrazione **v48**, HIDDEN di default):
      ricerca server-side, filtro «Solo da trasferire», selezione multi-pagina,
      badge «In Atec_PM», conferma, report esiti per riga.
- [x] **Smoke test riuscito 22/07/2026** con la stessa meccanica: articolo GV460
      (#1525) trasferito in Atec_PM con fornitore agganciato, riga TArticoliForn,
      generatore e 2 file immagine copiati sulla share (permessi di scrittura UNC OK).
      Il test ha stanato e fatto fixare un bug reale (metadati su connessione con
      transazione pendente).
- [ ] Verifica runtime GUI (riavviare il server per v48 + endpoint) + abilitare la
      feature `nav.danea_migration` dai Permessi.

### F3 — Specchio pulito + sync manuale — FATTA 22/07/2026 (rivista con Diego)
- [x] **Sync articoli = SPECCHIO VERO**: in transazione disattiva tutto lo specchio e
      ogni upsert riattiva la propria riga → gli articoli assenti dall'archivio Danea
      restano `is_active=0` (mai DELETE: le distinte non perdono i riferimenti).
      Guardia anti-disastro: archivio remoto senza articoli → specchio NON toccato.
      `ON DUPLICATE` ora rimette `is_active=1` (prima non riattivava).
- [x] **Pulizia one-shot eseguita sul dev**: 10.616 articoli disattivati, riattivati gli
      11 presenti in Atec_PM. Il Catalogo Articoli mostra solo il nuovo archivio.
- [x] **Pulsante «Sincronizza Danea»** nella pagina Catalogo Articoli: usa gli endpoint
      esistenti `/api/danea-sync/run` + `/status`, polling 1,5s con spinner/progress,
      toast di esito coi conteggi, refetch del catalogo.
- [ ] Restano (facoltativi/da fare a valle): apertura operativa di Atec_PM in Danea
      (documenti veri), endpoint bootstrap anagrafiche riesumabile per la convivenza.

## Rischi e paletti

- L'INSERT in TArticoli/TAnagrafica è più invasivo dell'UPDATE di Extra1 già collaudato:
  MAI sul vivo senza il giro di prova sulla copia; backup dell'archivio target prima di
  ogni lotto; Danea chiuso durante i lotti.
- Campi obbligatori/default di TArticoli da censire sulla copia (valori che Danea si
  aspetta non-NULL) prima di scrivere il servizio.
- La pagina non tocca MAI il vecchio archivio (connessione read-only a livello di codice).

## Stato

- F0 + F1 **FATTE 22/07/2026** (archivio `Atec_PM.eft`, anagrafiche e lookup dentro e
  verificate byte-per-byte, config switchata sul nuovo).
- F2 **IMPLEMENTATA 22/07/2026** (build verdi + smoke test reale su GV460); manca la
  verifica GUI (riavvio server + feature `nav.danea_migration` da abilitare).
- Cartella immagini individuata: `\\Server-maga\d\...\Archivi\<archivio> - Allegati\Prod`
  (+`Prod2` overflow), 18.289 file; jpg + miniatura " Small.bmp" per articolo.
- Poi F3: trasferimenti veri + verifiche post-switch (sync riaggancio per codice,
  disattivazione articoli non trasferiti nello specchio — DA IMPLEMENTARE al momento
  giusto, il sync oggi non disattiva nulla).
