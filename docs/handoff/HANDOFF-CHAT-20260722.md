# Handoff sessione 20–22/07/2026 — Matrice stati DDP v7 · Codex doppia codifica · Mapping Danea

> Contesto per riprendere il lavoro in un'altra chat. Progetto: ATEC PM (`ATEC_PM/`),
> server ASP.NET 8 + MySQL (Dapper, migrazioni all'avvio in `Services/DbService.cs`),
> client SOLO web `atec-pm-web/` (React+Vite+shadcn). WPF ritirato. Porte 5150/5151, Vite 5173.

## 1. Matrice stati DDP v7 (FATTO, migrazioni v39+v40)

- Da `MATRICE_STATI_DDP_V7.xlsx` + relazione tecnica (Desktop): 14 stati, finestra opzioni
  ristretta per stato corrente. Legacy CON/COS/SPED/MOD eliminati (righe rimappate
  CON/COS/SPED→DISP, MOD→RAM; stati cancellati da `ddp_statuses`).
- Tabella `ddp_status_transitions (ddp_type, from_key, to_key)` — **per tipo di distinta**:
  COMMERCIAL **senza DC** (il commerciale si compra), OFFICINA con DC incluso **DO→DC**.
  Riga speciale `from_key='INIZIO'` = finestra delle righe senza stato. Sentinella
  `(from,'')` = terminale (ANN/SOST). Coppia assente = non governata → finestra completa.
- Server: `DdpTransitionService.Validate(ddpType,…)` nelle PUT/POST di ProjectsController.
  Web: `filterStatusOptions` (ddp-constants.ts) su menu ⋮ e dialog; editor a 2 tab
  «Matrice» in Conf. DDP. Auto-stati: pezzi prodotti pieni → DISP; lavorazione consegnata → DISP.
- Inbox Officina: **whitelist** `VISIBLE={DC,PAR,IO,MIT}`, vista principale «Da produrre»
  = {DC,PAR}. Stati a monte (VER/CHEK/RO/DO) e conclusi fuori dalla coda.
- Dati dev resettati: tutte le righe DDP in DO (officina anche produced=0), pannello
  Lavorazioni svuotato (si rigenera dal sync alla prima modifica riga officina).
  Dump di backup nello scratchpad della sessione precedente.

## 2. Codex — doppia codifica (FATTO, migrazione v41)

- **Regola codifica Codex: 12 cifre = famiglia(3) + data creazione ggMMaa(6) + progressivo
  del giorno(3)**, punto di display prima delle ultime 3 (`CodexListItem.FormatCodice`).
- `codex_items.codice_nuovo` (UNIQUE, di proprietà ATEC PM — i sync remoti NON la toccano).
  Famiglie nuove: **201 Generici / 211 Elettrici / 221 Pneumatici**. Perimetro attuale:
  ricodifica manuale dei 201xxx vecchi (~8.064 righe), famiglia libera (201→211/221 ok).
- **Il codice NON si digita mai a mano**: generazione SOLO dal sistema con PRENOTAZIONE
  (`codex_reservations`, GET_LOCK, TTL 10 min) e accettazione passiva. La PUT
  `/api/codex/{id}/new-code` esige prenotazione valida e combaciante. Ruoli ADMIN/PM/RESP_REPARTO.
- Pagina **/codex/ricodifica**: grid con filtri/ordinamento per colonna (pattern Codex
  Articoli), avanzamento fatti/da fare, selezione massiva multi-pagina → flusso a 2 fasi:
  `bulk-reserve` → **form anteprima vecchio→nuovo+descrizione** → «Assegna» = `bulk-commit`
  / Annulla = `bulk-release`; reset massivo `bulk-remove` con conferma.
- Generatore standard: prefissi estesi (201/211/221), `ReserveNextCode` collision-safe
  anche contro `codice_nuovo`. Campo «Rif. Materia Prima (401)» NASCOSTO nella creazione 101.
- Descrizioni dal Codex remoto con entità HTML → `decodeHtmlEntities` in `lib/format.ts`.
  (Restano mojibake tipo "3/8â€™": dati sporchi alla fonte, bonifica non fatta.)

## 3. Mapping Danea ↔ codice ATEC (FATTO + VERIFICATO runtime, migrazione v42)

- Accesso Firebird Danea VERIFICATO: `192.168.2.115:31976`, **SYSDBA** / password in
  appsettings (`DaneaSync`), `Charset=NONE` + `WireCrypt=Disabled`. Runbook scritture:
  `D:\Dropbox\05 - R&D\ATEC_Warehouse\ATEC_Warehouse\ATEC.Warehouse\docs\danea.md`.
- Convenzione: **Extra1 dell'articolo Danea = codice ATEC (SOLO codici nuovi)**; l'etichetta
  in Danea resta "Extra 1" (non rinominare). Extra2 è GIÀ occupato da annotazioni.
- `catalog_items.atec_code + codex_item_id` (v42); DaneaSync legge Extra1 e risolve su
  `codice_nuovo`. Sync ATTIVO (`Services:DaneaSync=true` in appsettings base E Development —
  il Development sovrascrive i Services!). Primo sync ok: 2.224 fornitori, 406 clienti,
  10.610 articoli. Fix fatti: WireCrypt mancante; lookup fornitori articoli (PK `IDAnagr`,
  il refuso `IDAnag`→fallback CodAnagr rompeva il match: al prossimo sync si agganciano).
- `DaneaMappingService.WriteExtra1` (UPDATE diretto TArticoli, fail-fast) +
  `CatalogMappingController` (`by-codex`, `assign` con Force per riassegnazione confermata,
  `unassign`). **1 articolo = 1 codice ATEC.** Reversibilità: `DaneaSync:MappingMaster=false`
  → mapping solo locale (gestione interna senza Danea).
- UI: dialog «Articoli Danea» dal Codex (Ricodifica + Codex Articoli) e dal Catalogo
  (colonna/filtro Codice ATEC + assegna/sgancia). **Giro end-to-end testato**: ricodifica →
  assign → Extra1 riletto da Firebird → unassign/rollback pulito.

## 4. IN CORSO — Cursor sta implementando (Claude farà la REVIEW a fine lavoro)

Dal piano `PIANO-ACQUISTI-CODICE-ATEC.md` (leggerlo: contiene le decisioni vincolanti):
1. **Report "mapping orfani"** (Extra1 senza match Codex) — già iniziato: endpoint
   `orphans` in CatalogMappingController + `atecState=orphans` in CatalogController.
2. **Fase 2 — Codice ATEC in distinta commerciale**: colonna, picker per codice ATEC con
   alternative fornitore, snapshot `bom_items.atec_code`.
3. **Fase 3 — Inbox Acquisti**: pagina cross-commessa modello Officina (stati VER/CHEK/RO/DO
   COMMERCIAL), aggregazione fabbisogni per codice ATEC, fornitori proposti dal mapping,
   ciclo RDO completo (email + offerte + vincitore che applica fornitore/prezzo e avanza gli
   stati secondo matrice), realtime + row_version; assorbire l'attuale Feedback Acquisti.

**Checklist review**: coerenza col piano; regole progetto (realtime `staleTime 0`,
`renderColumnDef` MAI flexRender per le celle, scroll/picker patterns, date gg/mm/aa via
`formatDateShort`, conferme distruttive con `useConfirm`, ruoli blindati server-side,
matrice per tipo rispettata); build + tsc + eslint.

## 5. Da provare a runtime GUI (build ok, mai visti)

Matrice per tipo (menu ⋮ e Conf. DDP) · inbox Officina whitelist · ricodifica GUI
(singola, massiva, concorrenza 2 operatori) · mapping GUI (dialog Danea, Catalogo).

## 6. Lavoro operativo (utente)

Ricodificare i 201xxx · compilare le associazioni Danea · rimettere gli stati DDP veri
(tutto è in DO) e rilasciare in produzione (DO→DC) per ripopolare le Lavorazioni.

## Regole di collaborazione con Diego

Rispondere in italiano, **risposte minime («Fatto»)** salvo decisioni/errori/azioni sue;
conferma su ogni azione distruttiva; MAI runtime GUI di propria iniziativa (verifica =
build/tsc/eslint); spegnere i server avviati per i test (porte 5150/5151/5173).
