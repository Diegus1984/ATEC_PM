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

### BUG-006 — Conversione preventivo→commessa porta dietro le opzioni materiali disattivate
- **Stato:** [x]
- **Data:** 2026-06-03
- **Modulo:** Server (`PreventiviController.ConvertToProject`)
- **Passi:** 1. Preventivo IMPIANTO con varianti materiale disattivate (toggle off → `is_active=0`) 2. Converti in commessa 3. Apri il foglio di calcolo costing della commessa
- **Atteso:** In commessa compaiono solo le opzioni selezionate (attive)
- **Ottenuto:** Compaiono anche tutte le varianti disattivate (righe a qta 0 / vendita 0) che intasano il foglio
- **Note fix:** Lo step 5 (copia `quote_material_items`) selezionava tutti gli item senza filtro. Aggiunto `AND COALESCE(is_active,1)=1` alla SELECT: copia solo le opzioni attive. Il toggle UI mappa su `quote_material_items.is_active` (cfr. `PreventiviCostingController` "Toggle is_active"). Le risorse (`quote_cost_resources`) non hanno flag di disattivazione → non serve filtro lì. Verificato sul preventivo id 39: 22 item totali → 6 copiati (3 header + 3 varianti selezionate, coppie parent/figlio complete, nessun orfano), 16 disattivati esclusi.

### BUG-001 — Conversione da Preventivi senza andare su Commesse
- **Stato:** [x]
- **Data:** 2026-05-22
- **Modulo:** Preventivi (`QuotesHomePage`, `QuoteDetailPage`) + Commesse (`MainWindow` / `ProjectsPage`)
- **Passi:** 1. Preventivi → «Converti in Commessa» (griglia o dettaglio) 2. Conferma PM 3. Vai su Commesse
- **Atteso:** Flusso guidato: dopo conversione l’utente trova la nuova commessa in Commesse (albero aggiornato / selezionata)
- **Ottenuto:** Solo MessageBox + refresh lista preventivi; Commesse può restare con cache vecchia, commessa non visibile/selezionata
- **Errore/log:** API restituisce `projectId` in `data` ma il client non lo usa (`RowBtnConvert_Click`, `BtnConvert_Click`)
- **Note fix:** Dopo convert OK → `NavigateToProject(projectId, reloadTree: true)`; `ProjectsPage.RefreshTreeAndNavigateToSection`. Parser in `ConvertQuoteDialog.TryParseConvertResponse`. ~~Voce «Convertito» nel menu stato riga~~ → rimossa.

### BUG-005 — Dashboard "Ore per Reparto" tutto in TRASV
- **Stato:** [x]
- **Data:** 2026-05-26
- **Modulo:** Server (`ProjectsController.GetDashboardData`)
- **Passi:** 1. Apri dashboard commessa con ore timbrate 2. Guarda grafico "Ore per Reparto"
- **Atteso:** Donut con i veri reparti (PM, OFF, PLC…)
- **Ottenuto:** 100% "TRASV"
- **Note fix:** Cause concorrenti:
  - Le fasi locali (`phase_template_id=NULL`) non venivano matchate dalle JOIN su `phase_templates`
  - Tutti i 18 dipendenti ACTIVE non avevano `is_primary=1` → la query `WHERE is_primary=1` ritornava NULL
  Fix: (1) le query snapshot-aware usano `COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)` con LEFT JOIN. (2) Sostituito `WHERE is_primary=1` con `ROW_NUMBER() OVER (ORDER BY is_primary DESC, is_responsible DESC, id)` — fallback robusto. (3) Auto-assign primary a 14 dipendenti single-dept + 4 multi-dept (manuali). (4) Query trasversale ora preferisce il reparto della sezione di costo della fase, poi reparto dipendente, poi 'TRASV'.

### BUG-004 — Conversione preventivo→commessa ignora fasi nuove di template
- **Stato:** [x]
- **Data:** 2026-05-26
- **Modulo:** Server (`PreventiviController.ConvertToProject`)
- **Passi:** 1. Crea una fase template nuova sotto una sezione (CostSectionsTreePage) 2. Converti un preventivo in commessa 3. Apri il BVA della commessa
- **Atteso:** La nuova fase compare nella sezione corrispondente
- **Ottenuto:** Solo le fasi storiche (`is_default=1`) sono visibili, le nuove (`is_default=0`) ignorate
- **Note fix:** Lo step "Crea fasi di default" filtrava `WHERE is_default=1`. Riscritto step 4c (dopo copia sezioni): copia tutte le fasi con `cost_section_template_id IN (template delle sezioni copiate)` + fasi trasversali con `is_default=1 AND cost_section_template_id IS NULL` per retrocompatibilità.

### BUG-003 — Sezioni duplicate in commessa dopo ConvertToProject
- **Stato:** [x]
- **Data:** 2026-05-26
- **Modulo:** Server (`PreventiviController.ConvertToProject` step 4b)
- **Passi:** 1. Preventivo con sezione ad-hoc (template_id=NULL) con stesso nome di un template `is_default_project=1` 2. Convert
- **Atteso:** Una sola sezione nella commessa
- **Ottenuto:** Due sezioni omonime: una dal preventivo (template_id=NULL, con risorse) + una dal template default (template_id valorizzato, vuota). Le fasi si attaccano alla seconda → la prima sembra vuota nel BVA.
- **Note fix:** Step 4b confrontava solo `template_id` per saltare i template già copiati. Aggiunto check anche per **nome** (case-insensitive, trim) usando `copiedNamesLower`. Riparo retroattivo per la commessa 17 con script `fix_dup_program_manager.py` (downgrade sezione null + delete duplicato vuoto).

### BUG-002 — Duplica preventivo IMPIANTO mostra prezzi a 0.00 €
- **Stato:** [x]
- **Data:** 2026-05-26
- **Modulo:** Server (`PreventiviController.Duplicate`) + Preventivi (`QuotesHomePage` lista)
- **Passi:** 1. Preventivi → pulsante «Duplica» su un preventivo di tipo IMPIANTO con costing valorizzato 2. Vai sulla lista preventivi
- **Atteso:** Il duplicato mostra gli stessi totali (`Totale`, `Utile`) dell'originale, perché è una copia identica.
- **Ottenuto:** Il duplicato mostra `0,00 € / 0,00 €` mentre l'originale ha valori corretti. Anche se il costing è stato copiato in `quote_cost_*` e `quote_material_*`, i campi denormalizzati `quotes.total / quotes.profit` letti dalla lista restano a NULL/0.
- **Errore/log:** Nessuno (silent: il record viene creato ma con campi totali vuoti).
- **Note fix:** Per i preventivi IMPIANTO i totali in `quotes.total / quotes.profit / cost_total` sono campi denormalizzati scritti dal client (`QuoteDetailPage:210-213` → `PATCH /api/quotes/{id}/field`) dopo l'elaborazione del costing. `RecalcTotals` somma `quote_items`, ma per IMPIANTO contengono solo testi/clausole con `line_total=0` → restituirebbe 0. Fix in `PreventiviController.Duplicate`:
  - **SERVICE** → `RecalcTotals(c, newId, tx)` come prima
  - **IMPIANTO** → `UPDATE quotes dst JOIN quotes src ON src.id = @OrigId SET dst.subtotal = src.subtotal, dst.total = src.total, dst.profit = src.profit, ...` (eredita totali dall'originale)
- **Lezione (anti-pattern):** in un duplicate, se un campo è "calcolato lato client e patchato sul server", **non ri-derivarlo lato server, copialo dall'originale**. Vale per tutti i campi cached/denormalizzati.
- **Pulizia codice contestuale:** rimosso anche `QuotesController.Duplicate` (dead code, mai chiamato dal client) e `PreventiviController.GetAll` (dead, la lista è in `/api/quotes`). Vedi voce "Audit endpoint server dead-code" in `ATEC_PM_ATTIVITA.txt` sez. H.

---

## Già risolti in sessione (riferimento)

| ID | Sintesi | Fix |
|----|---------|-----|
| — | `product_id` mancante su `quote_material_items` | Migrazione `QuoteDbService` |
| — | Preventivo `converted` non riconvertibile | Reset DB → `accepted` (PRV-2026-0001) |
| — | Porta 5100 occupata | Kill processo server duplicato |
| — | Totale gruppo materiali resta 0 dopo prezzo manuale | `ParentGroup` + `NotifyTotals()` in `RecalcTotals` |

---

*Ultimo aggiornamento: 2026-05-26 — BUG-003 (sezioni duplicate post-conversione), BUG-004 (fasi nuove ignorate da convert), BUG-005 (dashboard 100% TRASV) tutti risolti*
