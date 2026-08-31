# PIANO #142 — Lavorati 101 con grezzo commerciale nei picker DDP

> **STATO 31/08/2026: IMPLEMENTATO (fasi 0-7), in attesa di deploy.** Verifica: build
> .NET 0 errori, `tsc -b` + `npm run build` ok, ESLint 0 errori, test 443/443 verdi
> (23 sui grezzi, di cui 5 nuovi in `GrezzoScopertoTests`). La logica della scelta
> fornitore è in `GrezziDerivazione.ApplicaFornitore` (estratta dal controller per i test).

> Segnalazione **#142** (Zanoni, 31/08/2026, IMPROVEMENT/HIGH): trasferendo la DDP Excel
> della commessa C260415_203_1 SOLE, gli articoli non si ritrovano nelle tre ricerche del
> gestionale e i «commerciali da rilavorare come 101» non hanno un giro. Design congelato
> con Diego il 31/08/2026 in questa sessione (suggerimenti integrati in coda alla stessa
> giornata: grezzo «scoperto» bloccato e lampeggiante, creazione 2xx al volo).

## 1. Diagnosi (fatta, con prove su DB di produzione)

I «NO» di Zanoni **non sono ricerche rotte, sono dati mancanti**:

- Le ricerche di **Codex Articoli** coprono già fornitore/codice fornitore/codice
  produttore ([CodexController.cs:148-189](../../ATEC.PM.Server/Controllers/CodexController.cs)),
  ma le righe Codex citate hanno quei campi **vuoti**. Non è un caso: famiglia 101 = 95%
  senza fornitore (6.100 su 6.409), famiglia 301 = 94% (3.250 su 3.446).
- **Trasferimento Danea** cerca per costruzione SOLO nel vecchio archivio
  (`Srl-2020-2021.eft`, [DaneaMigrationService.cs:232-340](../../ATEC.PM.Server/Services/DaneaMigrationService.cs));
  i 4 articoli del ticket stanno nell'archivio corrente → lì non usciranno mai.
- Gli articoli Amazon/SODEMANN/UCIESSE **in Catalogo ci sono** (specchio ogni 6h) ma con
  `atec_code = NULL`: l'aggancio ATEC↔Danea non è mai stato fatto.
- Caso pilota creato a mano il 31/08 (primo in produzione): 101120526.004 (molla) →
  derivazione 201310826.001 → articolo Danea 17RF (SODEMANN). Funziona a livello dati,
  ma **i picker non risalgono la derivazione** → «nessun fornitore collegato».

## 2. Design deciso

1. **I 101 con derivazione 201 si vedono anche dal lato commerciale dei picker**,
   etichettati come lavorati col loro grezzo (es. `101120526.004 → grezzo 201310826.001`).
2. **Selezionare un 101-derivato inserisce la coppia**: riga 101 in **DDP Officina**
   (endpoint esistente `POST /{id}/ddp-officina`) e il **grezzo 201 in DDP Commerciale lo
   genera il motore #135** (`GrezziDerivazione.Sincronizza`, già agganciato al POST
   officina — [ProjectsController.cs:1879](../../ATEC.PM.Server/Controllers/ProjectsController.cs)).
   **Nessuna doppia scrittura nuova.**
3. **Multifornitore sul 201**: il pannello mostra le alternative Danea del grezzo;
   l'operatore **può scegliere il fornitore** (applicato alla riga grezzo) **oppure
   inserire senza fornitore** → decide la gara RDO (giro esistente). La regola
   «un grezzo = un fornitore, mai split» ([GrezziDerivazione.cs](../../ATEC.PM.Server/Services/GrezziDerivazione.cs))
   non cambia.
4. **Grezzo «scoperto» = riga ferma e lampeggiante** (suggerimento Diego): il progettista
   può creare un 2xx senza avere ancora l'articolo commerciale da assegnargli. Finché il
   201 di derivazione **non è associato a nessun articolo Danea**, la riga del grezzo in
   DDP Commerciale **non può cambiare stato** (né entrare in RDO) e si presenta con un
   **bordo lampeggiante**: «codice da associare a un commerciale». Lo sblocco è
   automatico appena l'associazione esiste.
5. **Creazione 2xx al volo dalla derivazione** (suggerimento Diego): dove oggi si può
   solo *cercare* un 201 esistente ([CodexRefSearch.tsx](../../atec-pm-web/src/features/codex/CodexRefSearch.tsx),
   condiviso fra nascita codice e scheda articolo), compare una sotto-finestra
   «Nuovo 2xx» che genera il codice al momento (famiglie 201/211/221 — in futuro
   restringibile al solo 201) e lo aggancia subito come derivazione. Il codice appena
   nato è per definizione scoperto → si applica il punto 4.

## 3. Fasi di lavoro

### Fase 0 — Quick win ricerca Catalogo (indipendente, subito)
La ricerca di `/api/catalog` non copre `supplier_code` (il codice articolo del fornitore:
`B0D12Z9NQP`, `17RF`…), né in globale né per colonna
([CatalogController.cs:109-123](../../ATEC.PM.Server/Controllers/CatalogController.cs)).
- Aggiungere `i.supplier_code` alla ricerca globale.
- Aggiungere il filtro per colonna `supplierCode` e la colonna nelle pagine che lo
  espongono (Catalogo Articoli; vista Catalogo del picker se utile).
È uno dei «NO» concreti di Zanoni (riga 0155/0179) e non dipende dal resto.

### Fase 1 — Server: dati e endpoint di appoggio
1. **`GET /api/codex`**: la derivata `rc` restituisce già `RefCommercialeId/Codice/Descr`
   ([CodexController.cs:246-261](../../ATEC.PM.Server/Controllers/CodexController.cs));
   aggiungere **`RefCommercialeCodexId`** (`x.id` del 201) al DTO `CodexListItem`, così il
   client arriva alle alternative Danea del grezzo con `by-codex/{id}` senza giri extra.
2. **Nuovo `GET /api/codex/picker/derivati-101`** (stile
   [CodexPickerController.cs](../../ATEC.PM.Server/Controllers/CodexPickerController.cs)):
   righe = 101 con derivazione, colonne d'acquisto prese dal **grezzo**:
   `FROM codex_items cx JOIN codex_item_references r ON r.source_codex_id = cx.id AND r.ref_type='201'
   JOIN codex_items g ON g.id = r.ref_codex_id
   LEFT JOIN catalog_items ci ON ci.codex_item_id = g.id AND ci.is_active=1
   LEFT JOIN suppliers s …` — un abbinamento Danea = una riga (come il picker #128).
   Filtri: `codice` (del 101), `descr`, `articolo`, `fornitore`, `produttore`.
3. **Nuovo `POST /api/projects/{id}/ddp/raw-supplier`** — body
   `{ rawCodexCode, catalogItemId }`: applica la scelta del fornitore alla riga grezzo
   (`bom_items` con `project_id` + `raw_codex_code`), **solo se libera** (niente RDO,
   niente ordine Danea, stato VER/DO — stessa nozione di `RigaGrezzo.Libera`).
   Scrive `supplier_id`, `catalog_item_id`, `part_number = code` Danea, `manufacturer`,
   `unit_cost` (rispettando la sensibilità prezzi §12.3 come il PUT esistente).
   Il ricalcolo non la toccherà: `Aggiorna()` non riscrive il fornitore per costruzione.
4. **Grezzo scoperto (design 4), lato server**:
   - il GET delle righe DDP Commerciale espone il flag **`rawNeedsMapping`** = riga con
     `raw_codex_code` il cui 201 non ha NESSUN `catalog_items` attivo agganciato
     (EXISTS, niente colonna nuova: lo sblocco all'associazione è automatico);
   - il PUT `/{id}/ddp/{itemId}` **rifiuta il cambio di `item_status`** su una riga col
     flag alzato (messaggio: «Grezzo da associare a un articolo commerciale»);
   - stessa guardia sull'**ingresso in RDO** (punto in cui la riga entra in
     `purchase_rfq_items`), così il blocco non si aggira dall'Inbox Acquisti;
   - valutare `NotifyDdpChange` dentro `catalog-mapping/assign` perché lo sblocco si
     veda in tempo reale a griglia aperta.

### Fase 2 — AtecPickerDialog risale la derivazione (il caso dello screenshot)
File: [AtecPickerDialog.tsx](../../atec-pm-web/src/features/commesse/AtecPickerDialog.tsx).
- Se il selezionato è un 1xx **con** `RefCommercialeCodexId`: il pannello destro carica
  `fetchCatalogByCodex(RefCommercialeCodexId)` e intesta chiaro:
  «Fornitori del grezzo 201310826.001 (derivazione)».
- **Inserimento**: per i 1xx la riga va in **Officina** (`addOfficinaItem`), NON in
  Commerciale — oggi il dialog chiama sempre `createDdpRow` e un 101 finirebbe in
  Commerciale (il POST `/{id}/ddp` non smista,
  [ProjectsController.cs:1493-1534](../../ATEC.PM.Server/Controllers/ProjectsController.cs)).
  Stesso avviso/conferma del picker unico (`destinazioneDi`, specchio di `DdpSmistamento`).
- Dopo l'inserimento, se l'operatore aveva scelto un'alternativa → `raw-supplier` (Fase 1.3).
- Pannello con **zero alternative** (201 scoperto): avviso esplicito «il grezzo nascerà
  bloccato finché il 201 non viene associato a un articolo commerciale».
- 1xx **senza** derivazione: stesso smistamento in Officina (coerenza col picker unico),
  nessun pannello fornitori (non c'è grezzo).

### Fase 3 — Vista 2xx del picker unico: sezione «Lavorati con grezzo commerciale»
File: [CodexPickerDialog.tsx](../../atec-pm-web/src/features/commesse/CodexPickerDialog.tsx).
- Quando `family` ∈ {201, 211, 221} (vista Catalogo), sopra/sotto la lista Danea compare
  una **sezione separata** alimentata da `/api/codex/picker/derivati-101` (stessa search,
  stessa paginazione a scroll): righe `101 → grezzo 201` con fornitore/articolo del grezzo.
  Sezione separata = niente incastri di paginazione fra entità diverse.
- Selezione di una riga della sezione = stesso giro della Fase 2 (riga officina + motore
  + eventuale `raw-supplier` con l'abbinamento della riga cliccata).
- Riusare la logica «già in distinta nello stato d'ingresso → propone +1» che il picker
  unico ha già per l'officina.

### Fase 4 — Grezzo scoperto in griglia: blocco e lampeggio (design 4, lato client)
File: `ProjectDdpCommercial.tsx` (griglia DDP Commerciale).
- Riga con `rawNeedsMapping`: **tendina stato disabilitata** (il server rifiuta comunque,
  Fase 1.4 — la UI lo anticipa) e **bordo lampeggiante** con animazione CSS sobria
  (token del tema, coerente con le regole «griglie piatte a riposo») + tooltip
  «Il grezzo 201… non è associato a nessun articolo commerciale».
- Dalla riga si arriva al rimedio: aprire il dialog di associazione del 201
  (`CodexDaneaMappingDialog`, già esistente dal Codex) senza cambiare pagina.

### Fase 5 — Creazione 2xx al volo dalla derivazione (design 5)
File: [CodexRefSearch.tsx](../../atec-pm-web/src/features/codex/CodexRefSearch.tsx)
(+ [CodexGeneratePanel.tsx](../../atec-pm-web/src/features/codex/CodexGeneratePanel.tsx),
[CodexEditDialog.tsx](../../atec-pm-web/src/features/codex/CodexEditDialog.tsx)).
- In `CodexRefSearch` (prefisso «2») un pulsante **«Nuovo 2xx…»** apre la sotto-finestra
  di generazione: famiglia a scelta fra **201/211/221** (default 201; nota di Diego:
  «magari poi diventerà solo 201» → la lista famiglie resta un parametro), descrizione,
  poi la stessa meccanica reserve/confirm di `CodexGeneratePanel`
  (`/api/codex/reserve|confirm`, stessi permessi del picker: `action.manage_codex` /
  `action.assign_atec_code` / `project.ddp_officina`).
- Alla conferma il codice appena nato viene **selezionato come derivazione**
  (`onSelect`) — vale sia alla nascita del 101 (GeneratePanel) sia nella scheda
  (EditDialog), perché il componente è condiviso (#135).
- Il 201 appena creato non ha articoli Danea → le sue righe grezzo nascono col flag
  `rawNeedsMapping` (Fase 1.4/4): il giro si chiude da solo quando qualcuno lo associa.

### Fase 6 — Messaggio di esito e ritocchi
- Dopo l'inserimento di un 101-derivato il messaggio deve dire la coppia:
  «`101120526.004` in Officina + grezzo `201310826.001` in Commerciale (SODEMANN /
  fornitore da definire / **da associare**)». Se il POST officina non restituisce l'esito
  del motore, aggiungere al payload di risposta il codice del grezzo creato/aggiornato.
- Tipo lavorazione della riga officina: nasce col default della griglia; la quota
  Bilancio (`raw_internal_share`) si riallinea da sola al cambio (già gestito).

### Fase 7 — Test (`ATEC.PM.Tests`, accanto a `GrezzoDerivazioneTests`)
- `raw-supplier`: applica su riga libera; rifiuta su riga in RDO/ordinata; `part_number`
  e `supplier_id` coerenti con l'articolo scelto; un secondo ricalcolo NON sovrascrive.
- `derivati-101`: catena 101→201→articoli corretta; un 201 con N articoli = N righe;
  un 101 senza derivazione non compare.
- **Grezzo scoperto**: flag `rawNeedsMapping` vero senza associazione e falso con;
  PUT che cambia stato → rifiutato col flag alzato, accettato dopo l'associazione;
  guardia RDO idem.
- Inserimento riga officina con derivazione via endpoint → il grezzo nasce (integrazione
  endpoint, oltre ai test già esistenti sul motore).

## 4. Ordine consigliato
Fase 0 (indipendente) → Fase 1 (server) → Fase 2 (AtecPicker) → Fase 3 (picker unico) →
Fase 4 (blocco/lampeggio) → Fase 5 (nuovo 2xx al volo) → Fase 6 → Fase 7 intrecciata.
Build + `prova-test.ps1` prima del deploy; deploy SOLO su ordine di Diego.

## 5. Fuori perimetro (restano sul tavolo della #142 con Zanoni)
- **Bonifica dati**: migliaia di codici Codex «muti» (senza fornitore/codici) e articoli
  Danea senza `atec_code`. Non si risolve con codice: serve il giro di codifica/aggancio
  (che con questo piano diventa più visibile) e una passata sui dati storici.
- **Rondella M5 (riga 0179)**: articolo solo-Catalogo → il flusso è «Codifica» dal
  picker/Catalogo (esiste già) e poi aggancio; da mostrare a Zanoni, niente codice.
- **Barra filettata (riga 0043)**: da ricodificare (`codice_nuovo` vuoto) — dato, non codice.
- **Regola di classificazione** «commerciale da rilavorare come 101»: con questo piano la
  risposta operativa è «101 + derivazione 201»; da validare con Zanoni sul campo.
- Chiusura #142 con nota per Zanoni dopo il deploy.
