# Piano — Codici ATEC ↔ Danea + Inbox Acquisti

> Definito con Diego il 21/07/2026. Obiettivo: associare i codici ATEC (Codex) agli articoli
> Danea (1 codice ATEC → N articoli/fornitori), far apparire il codice ATEC in distinta
> commerciale, e dare al commerciale un'inbox del "da comprare" cross-commessa con fornitori
> proposti e ciclo RDO completo.

## Decisioni prese

| Tema | Decisione |
|---|---|
| Codice ATEC generico | **Mix**: codici Codex esistenti + generici nuovi creati man mano (es. "alimentatore 24V") |
| Dove vive il mapping | **Danea Extra1 = master**, MA editabile anche da ATEC PM con scrittura diretta di Extra1 nel DB Danea (pattern ATEC_Warehouse). **NON rinominare l'etichetta in Danea**: resta "Extra 1" (decisione 21/07/2026, per non avere problemi in caso di reinstallazione); l'etichetta "Codice ATEC" compare solo nelle UI di ATEC PM |
| RDO (inbox Acquisti) | **Ciclo completo**: lista aggregata + fornitori proposti + invio email + registrazione offerte + scelta vincitore |

## Fatti verificati (21/07/2026)

- Accesso Firebird Danea VERIFICATO: `192.168.2.115:31976`, utente **SYSDBA** (appsettings ATEC PM corretto — prima c'era `ma`), `Charset=NONE`, `WireCrypt=Disabled`.
- `TArticoli` ha **Extra1..Extra4**: Extra1/3/4 liberi, **Extra2 GIÀ OCCUPATO** (629 annotazioni) → si usa SOLO Extra1.
- Runbook per scrivere nel DB Danea senza romperlo: `D:\Dropbox\05 - R&D\ATEC_Warehouse\ATEC_Warehouse\ATEC.Warehouse\docs\danea.md` (identificatori quotati, generator, cache — per un semplice UPDATE di Extra1 non si toccano giacenze/cache).

## Fase 0 — Sync Danea operativo (prerequisito)

Il DaneaSync di ATEC PM non è mai andato contro il server vero (era disabilitato, utente errato).

- [ ] Aggiungere `WireCrypt = Disabled` a `DaneaSyncService.BuildConnectionString` (il Warehouse ce l'ha, ATEC PM no — regola #7 del runbook).
- [ ] Abilitare `Services:DaneaSync` in dev e verificare un giro completo (fornitori/clienti/articoli) senza danni su `catalog_items` esistente (upsert per `code`).

## Fase 1 — Mapping Extra1 → catalogo

- [ ] Convenzione (niente da fare in Danea): **Extra1 = codice ATEC**, etichetta lasciata "Extra 1" di default. Chi compila da Danea sa che quel campo è il codice ATEC; le UI di ATEC PM lo etichettano "Codice ATEC".
- [ ] Migrazione: `catalog_items.atec_code VARCHAR + indice` e `catalog_items.codex_item_id INT NULL`.
- [ ] Sync articoli: leggere `Extra1`, salvarlo in `atec_code`, risolverlo verso `codex_items` per codice normalizzato (senza punti) → `codex_item_id`.
- [ ] Report "mapping orfani": Extra1 valorizzati che NON esistono nel Codex (refusi) — endpoint + vista (es. tab in Conf. DDP o pagina Catalogo).
- [ ] **Editor mapping in ATEC PM**: dato un codice ATEC, elenco articoli Danea associati; aggiungi/togli associazione = `UPDATE "TArticoli" SET "Extra1"=@Code WHERE "IDArticolo"=@Id` diretto sul Firebird di Danea (ruolo ADMIN/COMM, con conferma). Dopo la scrittura, refresh della riga locale senza attendere il sync.

### Step Codex — doppia colonna (definito 21/07/2026, PRIMA delle fasi Acquisti)

> **IMPLEMENTATO 21/07/2026** (build/tsc/eslint OK, manca runtime): migrazione **v41**
> (`codex_items.codice_nuovo` + UNIQUE), endpoint `PUT /api/codex/{id}/new-code` +
> `GET new-code/suggest` + `GET recode-stats`, colonna «Codice nuovo» e azione riga in
> Codex Articoli, pagina **/codex/ricodifica** (avanzamento, tab Da fare/Fatti/Tutti),
> ricerca globale anche sul codice nuovo. Ruoli ADMIN/PM/RESP_REPARTO blindati server-side.

Ampliamento della gestione Codex: seconda colonna per la **nuova codifica** che nel tempo
sostituisce la vecchia (famiglie nuove: **201 commerciali generici, 211 elettrici,
221 pneumatici** — numerazione corretta da Diego il 21/07/2026). Quando il vecchio Codex
sarà smantellato resteranno solo codici nuovi,
niente più "sostituzioni".

**Regole decise:**
- `codex_items.codice_nuovo` di proprietà di ATEC PM: il sync col Codex remoto NON la tocca mai;
  nasce vuota, **si riempie SOLO a mano** (nessuna conversione automatica, nessun batch).
- Perimetro attuale: ricodifica dei **201xxx** vecchi. **Famiglia libera**: un vecchio 201 può
  diventare 211/221 se è quella la sua natura (validazione su formato+unicità, non sul prefisso).
- **Formato identico al Codex attuale** (niente spazi né trattini). Unicità del codice nuovo
  contro ENTRAMBE le colonne (mai un nuovo uguale a un vecchio esistente).
- Aiuto non-automatismo: suggerimento del prossimo progressivo libero della famiglia, campo
  sempre editabile.
- **UI: entrambe le vie** — dialog sulla riga nella pagina Composizione Codex (caso singolo) +
  pagina dedicata di ricodifica a tappeto (lista 201xxx da fare, contatore avanzamento).
- **Permessi**: ADMIN, PM e RESP_REPARTO.
- Visualizzazione ovunque: *codice nuovo se presente, altrimenti vecchio* (vecchio in
  tooltip/parentesi); ricerche e picker su entrambe le colonne.
- Fuori perimetro: 101 (Lavorazioni) e 5xx (assiemi) restano sul codice vecchio, vincoli invariati.
- **Il "codice ATEC" del mapping Danea (Extra1) = SOLO codici nuovi.**

### Flusso associazione operatore ↔ Danea (definito 21/07/2026)

L'operatore fa le associazioni **in autonomia da ATEC PM** (scrittura diretta di Extra1 nel
Firebird di Danea + aggiornamento immediato dello specchio locale `catalog_items`):

- **Due direzioni, entrambe**:
  1. *dal Codex*: riga con codice nuovo → pannello «Articoli Danea associati» → cerca nel
     catalogo (descrizione/fornitore/codice) → aggancia/sgancia. Vista d'insieme delle
     alternative fornitore del codice.
  2. *dal catalogo*: lista articoli Danea (filtro «senza codice ATEC» per la bonifica a
     tappeto) → assegna il codice su ogni articolo.
- **Un articolo Danea appartiene a UN solo codice ATEC** (Extra1 è uno). Se è già associato a
  un altro codice: **riassegnazione con conferma esplicita** («già associato a 202xxx —
  spostarlo su 203yyy?»), poi Extra1 sovrascritto. Sgancio = Extra1 svuotato (con conferma,
  regola azioni distruttive).
- **Solo codici nuovi** in Extra1 (mai i vecchi).
- **Permessi**: stessi della ricodifica — ADMIN, PM, RESP_REPARTO.
- Scrittura sincrona e fail-fast: se il Firebird di Danea non è raggiungibile l'operazione
  fallisce con messaggio chiaro, lo specchio locale NON viene toccato (niente code, niente
  divergenze). Caveat noto: se la scheda articolo è aperta in Danea in quel momento, un
  salvataggio da Danea può ripristinare il valore precedente (ultimo che scrive vince).

## Fase 2 — Codice ATEC in distinta commerciale

- [ ] Migrazione: `bom_items.atec_code` (snapshot, coerente con lo stile denormalizzato delle distinte).
- [ ] Griglia DDP commerciale: colonna "Cod. ATEC" (→ bump `visibilityStorageKey`).
- [ ] Picker "per codice ATEC": cerca nel Codex/mapping → scegli il codice ATEC → vedi le alternative Danea (fornitore, codice, prezzo) → o scegli subito l'articolo (riempie fornitore/costo/codice Danea) o lasci solo il codice ATEC con fornitore "da definire" (stato VER/CHEK/DO: ci pensa l'inbox Acquisti).
- [ ] Riga DDP: mostrare le alternative fornitore anche a posteriori (dialog riga → "Alternative dal mapping").

## Fase 3 — Inbox Acquisti (modello pagina Officina)

Whitelist stati del ramo commerciale a monte dell'ordine: **VER / CHEK / RO / DO** (matrice v7,
tipo COMMERCIAL). Cross-commessa, sidebar per commessa + viste rapide, come l'Officina.

- [ ] **Aggregazione per codice ATEC**: "alimentatore 24V — 7 pz su 3 commesse" (somma fabbisogni, elenco commesse/righe sotto).
- [ ] Fornitori proposti automaticamente dal mapping (tutti gli articoli Danea con quel codice ATEC).
- [ ] **RDO completa**: nuove tabelle (es. `purchase_rfqs`, `purchase_rfq_items` → righe bom coinvolte, `purchase_rfq_offers` → offerte ricevute con prezzo/scadenza/note). Flusso: crea RDO su un gruppo → seleziona fornitori → invio email (EmailService) → registra offerte → scegli vincitore → applica fornitore+prezzo alle righe e avanza gli stati secondo matrice (→ RO/DO/IO).
- [ ] Realtime + `row_version` (regola fissa ambiente condiviso) + conferme su azioni distruttive.
- [ ] Decidere il destino di **DdpFeedbackAcquistiPage** (stati A6): probabile evoluzione/assorbimento nella nuova inbox per non avere due pagine acquisti sovrapposte.

## Clausola di reversibilità — «gestione interna senza Danea» (21/07/2026)

Approccio «vediamo cosa succede»: si parte con Danea Extra1 come master, ma il sistema va
costruito in modo che il **piano B sia un interruttore, non un rifacimento**:

- la copia di lavoro del mapping è SEMPRE lo specchio locale (`catalog_items.atec_code`):
  tutte le pagine (associazione, distinta, inbox Acquisti) leggono SOLO quello;
- la scrittura di Extra1 su Danea è un passo **isolato e opzionale** dietro config
  (es. `Danea:MappingMaster` true/false);
- con l'interruttore spento: il sync NON sovrascrive più `atec_code` da Extra1 (il mapping
  diventa di proprietà di ATEC PM) e l'associazione scrive solo in locale. Stesse tabelle,
  stesse UI, zero migrazioni.

## Ordine consigliato

Fase 0 → 1 (con chiarimento prefisso generici) → 2 → 3. Le fasi 1–2 sono piccole; la 3 è il
grosso (RDO). Ogni fase è utile da sola: già con la 1+2 il codice ATEC appare in distinta con
le alternative fornitore.
