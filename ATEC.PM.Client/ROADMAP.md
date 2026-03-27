# ATEC PM — Roadmap Aggiornata (Marzo 2026)

## ⚠️ Istruzioni per Claude Code — Inizio Sessione

**SEMPRE all'inizio di ogni sessione leggere:**
1. `.claude/skills/atec-design-system/SKILL.md` — Design system (flat design, palette, spacing, brush)
2. `.claude/skills/wpf-xaml-guide/SKILL.md` — Template XAML e pattern WPF
3. Questo file `roadmap.md` — Stato attuale del progetto

**Regole:**
- Mai generare XAML senza aver letto il design system
- Usare sempre `StaticResource` per colori — mai hardcodare hex nei XAML
- Font: Segoe UI, 12px body, 11px secondary, 14px header
- Spacing: multipli di 4px (4, 8, 12, 16, 20)
- L'utente comunica in italiano

## Stack
- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token
- **Grafici**: OxyPlot.Wpf 2.2 (migrato da LiveCharts2)
- **PDF**: QuestPDF (Community License)
- **Excel**: ClosedXML (import catalogo)
- **Rich Text**: TinyMCE 5 self-hosted + WebView2
- **GitHub**: github.com/Diegus1984/ATEC_PM



---

## Blocco 1 — Infrastruttura Base ✅ COMPLETATO



| Funzionalità | Stato | Note |
|---|---|---|
| Autenticazione JWT | ✅ | Login, token, refresh |
| Gestione Dipendenti | ✅ | CRUD, reparti, competenze, credenziali |
| Gestione Clienti | ✅ | CRUD completo |
| Gestione Fornitori | ✅ | CRUD + import da Easyfatt (.eft Firebird) |
| Gestione Reparti | ✅ | Costo orario + K ricarico diretto (default_markup) |
| Configurazione App | ✅ | app_config DB table, DPAPI per secrets |
| Sidebar + Navigazione | ✅ | MainWindow con sidebar scura, sezioni collassabili (Expander) |

---

## Blocco 2 — Commesse & Fasi ✅ COMPLETATO

| Funzionalità | Stato | Note |
|---|---|---|
| Commesse (CRUD + TreeView) | ✅ | Codice auto AT2026001, cartelle template |
| Fasi Template | ✅ | 43 fasi ATEC, raggruppate per categoria/reparto |
| Fasi → Sezione Costo | ✅ | FK cost_section_template_id su phase_templates |
| Fasi Commessa | ✅ | Copia da template, assegnazione tecnici |
| Timesheet Settimanale | ✅ | Inserimento ore per fase, validazioni, note visibili in dashboard |
| Documenti Commessa | ✅ | Upload/download, preview Word via Mammoth |
| DDP Commerciali (BOM) | ✅ | 12 colori stato, duplicati, filtri, preview Word |

---

## Blocco 3 — Preventivazione / Costing

Architettura MVVM in `Views/Costing/` con ViewModel a cascata: `CostResourceVM → CostSectionVM → CostGroupVM → CostingViewModel`.

### 3a. Sezioni Costo Template ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Gruppi sezioni (CRUD) | ✅ | GESTIONE, PRESCHIERAMENTO, INSTALLAZIONE, OPZIONE |
| Sezioni template (CRUD) | ✅ | 15 sezioni con tipo IN_SEDE/DA_CLIENTE |
| Associazione sezione → reparti | ✅ | Checkbox per reparto, filtra dipendenti |
| Categorie Materiali | ✅ | K materiale + K provvigione per categoria |

### 3b. Risorse (ore lavoro) ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Init costi commessa | ✅ | Copia template → commessa (sezioni, materiali, pricing) |
| Vista risorse gruppi/sezioni | ✅ | Expander colorati, righe collassabili |
| K ricarico per riga risorsa | ✅ | Dal reparto dipendente, editabile per riga |
| Risorse DA_CLIENTE (trasferta) | ✅ | Viaggi, km, vitto, hotel, indennità |
| Selezione dipendente ComboBox | ✅ | Filtrato per reparti sezione, precompila €/h e K |
| Aggiungi sezione da template/custom | ✅ | Dialog combo template + "Personalizzata..." |
| Aggiungi gruppo da template/custom | ✅ | Dialog combo + "Nuovo gruppo..." |
| Endpoint available-templates | ✅ | Filtra template non ancora nella commessa |
| Endpoint set section departments | ✅ | PUT sections/{id}/departments |

### 3c. Materiali ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Vista materiali (categorie collassabili) | ✅ | Stessa pagina risorse, sotto |
| K per riga materiale | ✅ | Default da categoria, sovrascrivibile |
| Tipo MATERIAL / COMMISSION | ✅ | Badge provvigione, K provvigione separato |
| Spese trasferta calcolate | ✅ | Auto da risorse DA_CLIENTE, K editabile |
| Indennità trasferta calcolate | ✅ | Auto da risorse DA_CLIENTE, K editabile |
| Autocomplete descrizioni da storico | ❌ | SELECT DISTINCT description FROM project_material_items |

### 3d. Scheda Prezzi / Riepilogo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Totale risorse + materiali + trasferte | ✅ | Barre nere + barra blu TOTALE GENERALE |
| Scheda Prezzi (NET → OFFER → FINAL) | ✅ | Struttura%, contingency%, rischi%, margine% — piè di pagina fisso |
| Shadow (nascondi e spalma) | ✅ | Icona 👁 per riga, nasconde voce e spalma il costo proporzionalmente sulle visibili. Colonne SHADOW € e SH %. Persistente su DB (is_shadowed). |
| Filtro righe vuote e duplicati | ✅ | DistributionRows esclude TotalSale==0 e deduplica per nome |
| Export Excel/PDF preventivo | ❌ | EPPlus per Excel |

---

## Blocco 4 — Pulizia / Refactoring ✅ COMPLETATO

| Funzionalità | Stato | Note |
|---|---|---|
| Eliminazione tabella markup_coefficients | ✅ | K ora su departments.default_markup |
| Eliminazione project_markup_values | ✅ | Non più usata |
| Rimozione markup_code da departments | ✅ | Sostituito da default_markup |
| Rimozione markup_value da sezioni costo | ✅ | K sulla riga risorsa, non sulla sezione |
| Eliminazione MarkupPage/MarkupDialog | ✅ | File e voci menu rimossi |
| DepartmentsPage/Dialog con K diretto | ✅ | TextBox K verde al posto di ComboBox |
| Eliminazione MarkupController | ✅ | Rimosso dal server + endpoint markup/{id} da ProjectCostingController |

---

## Blocco 5 — Analisi & Reporting

### 5a. Preventivo vs Consuntivo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| BudgetVsActualControl | ✅ | Views/BudgetVsCosting/ con SmoothExpander |
| 4 gruppi (GESTIONE/PRESCHIERAMENTO/INSTALLAZIONE/OPZIONE) | ✅ | Expander colorati |
| SX preventivo (risorse pianificate) | ✅ | Ore e costi da project_cost_resources |
| DX consuntivo (ore versate per dipendente) | ✅ | Dettaglio timbrature da timesheet_entries |
| Fix duplicati dipendenti (MIN department_id) | ✅ | Subquery in JOIN |

### 5b. Flusso di Cassa ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Griglia tipo Excel | ✅ | DataGrid con TextBox sempre visibili, editing diretto |
| Righe entrate/uscite/totali | ✅ | Differenza cumulativa progressiva |
| Grafico OxyPlot | ✅ | Barre + linea saldo + TextAnnotation |
| DB: 3 tabelle compatte | ✅ | project_cashflow, project_cashflow_categories, project_cashflow_data |

### 5c. Esplorazione DB Danea (Firebird) ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Endpoint explore/tables | ✅ | Lista tutte le 63 tabelle Danea |
| Endpoint explore/tables/{name}/columns | ✅ | Schema colonne con tipo, lunghezza, nullable |
| Endpoint explore/tables/{name}/data | ✅ | Primi N record (max 100) |
| Endpoint explore/tables/{name}/search | ✅ | Ricerca LIKE su colonna specifica (max 200) |
| Fix case-sensitivity Firebird | ✅ | Nomi tabella mixed-case senza ToUpper() |

### 5e. Codex — Sync DB Remoto ✅

| Funzionalità | Stato | Note |
|---|---|---|
| CodexSyncService + CodexPage | ✅ | 21 filtri, popup colonne, sync ogni 6h |
| Filtri wildcard (abc* / *abc) | ✅ | Su tutte le pagine con filtri di ricerca |
| Generazione codici Codex inline | ✅ | Pannello inline, prefissi 101/201/501/601/701 |
| Modifica/Elimina articoli Codex | ✅ | Pulsanti inline, solo admin, protezione se in composizione |
| Formato codice con punto singolo | ✅ | Getter DTO: rimuove tutti i punti, rimette uno solo |

### 5f. Catalogo Articoli ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Popup colonne + filtro Categoria | ✅ | Stesso pattern CodexPage |
| ComboBox filtro Fornitore/Produttore/Categoria | ✅ | Dropdown con valori distinti + "Tutti" |
| Sync fornitore da Easyfatt | ✅ | Match IDFornitore → TAnagrafica → supplier_id locale |

### 5g. Composizione Codex ✅

| Funzionalità | Stato | Note |
|---|---|---|
| CodexCompositionPage (layout split) | ✅ | Sinistra: articoli disponibili, Destra: TreeView composizione |
| Gerarchia a matrioska | ✅ | 501→1xx-4xx, 601→501, 701→601 |
| Drag & drop + doppio-click | ✅ | Con dialog quantità (drag) o inserimento diretto (doppio-click) |
| Colori sfondo per tipo codice | ✅ | 7 colori tenui per 101-701 |
| Sorgente Codex + Catalogo | ✅ | ComboBox sorgente, articoli catalogo con icona 🛒 |
| Sotto-nodi read-only | ✅ | In 601 non si modificano i 501 sotto |
| Protezione cancellazione | ✅ | Blocco delete su codex/catalogo se usati in composizione |
| Ricerca wildcard doppia (codice + descrizione) | ✅ | Due TextBox separate nel pannello sinistro |
| Riferimenti 201/401 su codici 101 | ✅ | Tabella codex_item_references + ComboBox con ricerca lazy |

---

## Blocco 6 — Sistema Notifiche & Dashboard ✅ COMPLETATO

### 6a-d. Notifiche + Dashboard PM + Grafici OxyPlot ✅

Tutti completati. Vedi dettaglio nella versione precedente del roadmap.

---

## Blocco 7 — Offerte Commerciali ✅ COMPLETATO

| Funzionalità | Stato | Note |
|---|---|---|
| OffersPage + OfferViewPage | ✅ | TreeView per cliente/anno, dettaglio con tab |
| Codice OF{anno}{progressivo 3 cifre} | ✅ | OF2026001 |
| Stati BOZZA→INVIATA→ACCETTATA→CONVERTITA | ✅ | + RIFIUTATA/PERSA/SUPERATA |
| Revisioni con copia completa costing | ✅ | Vecchia → SUPERATA, nuova copia |
| Conversione offerta → commessa | ✅ | Copia offer_* → project_*, crea fasi, notifica PM |
| OfferCostingController (mirror ProjectCosting) | ✅ | Tabelle offer_* |
| ProjectCostingControl riusato via _apiBasePath | ✅ | LoadForOffer(offerId) |
| ConvertOfferDialog | ✅ | Seleziona PM da endpoint /api/employees/pm-list |

---

## Blocco 8 — Modulo CMS Preventivi 🔧 IN CORSO

### 8a. Catalogo Template ✅

| Funzionalità | Stato | Note |
|---|---|---|
| QuoteDbService (DB separato dal modulo principale) | ✅ | 8 tabelle: quote_groups, quote_categories, quote_products, quote_product_variants, quotes, quote_items, quote_revisions, quote_documents, quote_status_log |
| QuoteCatalogController (API CRUD completo) | ✅ | Gruppi, categorie, prodotti con varianti, duplicazione, albero per TreeView |
| QuoteCatalogPage (UI WPF) | ✅ | TreeView Gruppi→Categorie + DataGrid prodotti con filtri, prezzi range, conteggio varianti |
| QuoteGroupDialog / QuoteCategoryDialog | ✅ | CRUD gruppi e categorie |
| QuoteProductDialog | ✅ | Editor prodotto con griglia varianti inline (codice, nome, costo, prezzo, sconto, IVA, UdM, qty), flag auto-include |
| Seed data catalogo | ✅ | 7 gruppi, 21 categorie, 36 prodotti, ~50 varianti con prezzi realistici |

### 8b. Gestione Preventivi ✅

| Funzionalità | Stato | Note |
|---|---|---|
| QuotesController (API CRUD completo) | ✅ | Codice PRV-2026-0001, auto-populate da template, gestione items, stati, duplicazione, ricalcolo totali, statistiche |
| QuotesHomePage (lista preventivi) | ✅ | DataGrid con filtri header, filtro stato, ricerca, colonne: numero, data, cliente, titolo, totale, utile, stato (badge colorato), agente |
| NewQuoteDialog | ✅ | Selezione cliente (ComboBox ricercabile), template, condizioni, pagamento |
| QuoteDetailPage (dettaglio completo) | ✅ | Header editabile, griglia voci, riepilogo economico (subtotale→IVA→sconto→imponibile→costi aziendali→UTILE), toggle PDF, note interne/preventivo, cambio stato |
| AddQuoteItemDialog | ✅ | Doppio-click da catalogo con duplicate detection (pattern DDP), aggiornamento real-time della lista items |
| Dirty tracking con snapshot JSON | ✅ | Confronto DTO serializzato, conferma uscita su navigazione |

### 8c. Generazione PDF ✅

| Funzionalità | Stato | Note |
|---|---|---|
| QuotePdfService (QuestPDF) | ✅ | Layout professionale: header ATEC, destinatario, tabella voci, riepilogo, condizioni, firma, footer con paginazione |
| Endpoint GET /api/quotes/{id}/pdf | ✅ | Ritorna byte[] PDF |
| Anteprima PDF (bottone viola) | ✅ | Salva in %TEMP%, apre con viewer di default |
| Scarica PDF (SaveFileDialog) | ✅ | Salva dove vuole l'utente |
| Toggle visibilità nel PDF | ✅ | ShowItemPrices, ShowSummary, ShowSummaryPrices |
| ApiClient.GetBytesAsync() | ✅ | Nuovo metodo per download binari |

### 8d. Livello Listino ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Tabella quote_price_lists | ✅ | id, name, currency, locale, is_active |
| FK price_list_id su quote_groups e quotes | ✅ | Migration automatica |
| CRUD Listini (API + UI) | ✅ | 4 endpoint + ComboBox filtro in QuoteCatalogPage |
| Filtro albero per listino | ✅ | GetTree(?priceListId=) + GetGroups(?priceListId=) |
| Selezione listino in NewQuoteDialog | ✅ | Filtra gruppi template per listino |

### 8e. Import Excel ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Endpoint POST /api/quote-catalog/import | ✅ | Transazione: listini→gruppi→categorie→prodotti→varianti |
| DTO import gerarchico | ✅ | 7 classi: QuoteCatalogImportDto → ...Listino/Group/Category/Product/Variant |
| Parser Excel (ClosedXML) | ✅ | Parsing struttura gerarchica con state machine |
| Pulsante "Importa Excel" nella CatalogPage | ✅ | OpenFileDialog + conferma conteggi + feedback |

### 8f. UI Varianti nel Preventivo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Colonne is_active, is_confirmed, parent_item_id | ✅ | Migration su quote_items |
| Toggle Attiva/Conferma nel DataGrid | ✅ | CheckBox con save immediato via API |
| RecalcTotals con is_active | ✅ | Solo items attivi con qty>0 contano nei totali |
| Endpoint AddProductWithAllVariants | ✅ | POST /{id}/items/product/{productId} — header + tutte le varianti |

### 8g. Rich Text Editor ✅

| Funzionalità | Stato | Note |
|---|---|---|
| UserControl HtmlEditor (WebView2) | ✅ | Riutilizzabile, comunicazione bidirezionale WPF↔JS |
| TinyMCE 5 self-hosted | ✅ | Installato localmente via npm, no API key, no CDN |
| Toolbar completa | ✅ | Bold/italic/underline, heading, colori, tabelle, immagini, link, code, fullscreen |
| Resize immagini nativo | ✅ | Handle drag angolari TinyMCE |
| Tabelle con resize colonne | ✅ | Plugin table nativo |
| Paste da Word/Office | ✅ | Plugin paste |
| Upload immagini inline (base64) | ✅ | File picker con blob cache |
| Upload allegato prodotto | ✅ | Pulsante + copia in uploads/products/ |

### 8h. QuoteDetailPage Redesign ✅

| Funzionalità | Stato | Note |
|---|---|---|
| ItemsControl custom al posto di DataGrid | ✅ | Layout gerarchico: prodotto padre → varianti figlie espandibili |
| Sezione "Contenuti automatici" separata | ✅ | ListBox con drag & drop per riordinamento, auto_include=1 |
| Varianti attivabili/disattivabili con checkbox | ✅ | Toggle inline, _suppressToggle per evitare eventi durante load |
| Editing inline varianti | ✅ | Qtà, prezzo, sconto modificabili direttamente nella riga |
| AddLocalVariantDialog | ✅ | Aggiunta varianti locali (non da catalogo) |
| Snapshot varianti locale | ✅ | Varianti copiate nel preventivo, indipendenti dal catalogo |
| AddQuoteItemDialog — solo prodotti padre | ✅ | Mostra un prodotto per riga (non le singole varianti), doppio-click aggiunge tutte le varianti |

### 8i. PDF Avanzato ✅

| Funzionalità | Stato | Note |
|---|---|---|
| description_rtf nel PDF | ✅ | Fix: _suppressToggle impedisce sovrascrittura vuota da checkbox |
| Tabelle HTML a 2 colonne | ✅ | TinyMCE produce `<table><tr><td>` — rendering QuestPDF con Row + RelativeItem |
| Auto-include sempre in fondo al PDF | ✅ | Sezione separata dopo il riepilogo |
| Nascondi dettagli (costo, qtà, sconto) | ✅ | Checkbox HideQuantities → rimuove 3 colonne dal riepilogo, lascia solo nome + totale |
| Riepilogo su pagina nuova | ✅ | Ultima pagina sempre separata con totali + firma |

### 8j. Gestione Stati Preventivo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| ComboBox cambio stato nella lista preventivi | ✅ | Stile dedicato con colore sfondo + testo per ogni stato |
| Pulsanti azione per riga (lista) | ✅ | Visualizza, Scarica PDF, Invia (placeholder), Duplica, Modifica, Elimina |
| Duplicazione completa con parent_item_id | ✅ | Clona quote + items preservando la gerarchia padre/figlio |
| Transizioni stato flessibili | ✅ | Tutti gli stati raggiungibili tranne converted (irreversibile) |

### 8k. Pulsanti azioni inline su riga prodotto catalogo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Modifica/Duplica/Elimina per riga | ✅ | Pulsanti inline nella DataGrid catalogo |

---

## Blocco 9 — Preventivi Unificati + Configurazione Sezioni 🔧 IN CORSO

### 9a. Configurazione Sezioni (pagina unificata) ✅

Sostituisce le vecchie pagine Fasi Template, Reparti e Sezioni Costo in un'unica interfaccia tree drag & drop.

| Funzionalità | Stato | Note |
|---|---|---|
| CostSectionsTreePage (tree unificato) | ✅ | Gruppi → Sezioni → Fasi Template + Reparti, tutto in drag & drop |
| Drag fasi template nel tree | ✅ | Collega fase → sezione costo |
| Drag reparti nelle fasi | ✅ | Assegna reparto → fase |
| Accordion (uno solo aperto per livello) | ✅ | Sia nel tree che nei pannelli fasi |
| Creazione inline fasi/reparti/sezioni/gruppi | ✅ | Bottoni +, doppio click per modifica |
| Eliminazione con verifica uso | ✅ | Mostra dove è usata la fase se in uso |
| Riordino fasi con frecce ▲▼ | ✅ | Aggiorna sort_order via API |
| DragDropAdorner (visual feedback) | ✅ | Oggetto segue il mouse durante drag |
| Vecchie pagine eliminate | ✅ | FasiTemplate/, Reparti/, CostSectionsPage rimossi |
| Menu aggiornato | ✅ | Un solo bottone "Configurazione Sezioni" |

### 9b. Preventivi Unificati (nuova pagina) 🔧

Nuova pagina con TreeView cliente/anno che sostituirà Offerte + CMS Preventivi. Due tipi: SERVICE (catalogo semplice) e IMPIANTO (catalogo + costing + conversione).

| Funzionalità | Stato | Note |
|---|---|---|
| PreventiviPage (TreeView + Detail) | ✅ | Stessa struttura OffersPage ma per quotes |
| NewPreventivoDialog | ✅ | Tipo SERVICE/IMPIANTO, cliente, listino, gruppo |
| ConvertPreventivoDialog | ✅ | Selezione PM per conversione IMPIANTO → Commessa |
| DB: quote_type su quotes | ✅ | ENUM SERVICE/IMPIANTO |
| DB: tabelle costing quote_cost_* | ✅ | Mirror di offer_cost_* per preventivi IMPIANTO |
| PreventiviController (API) | ✅ | List, Create (con init costing), Convert |
| PreventiviCostingController (API) | ✅ | Clone OfferCosting su tabelle quote_cost_* |
| CostingTreeControl.LoadForPreventivo | ✅ | Terzo mode: /api/preventivi/{id}/costing |
| Layout IMPIANTO (stile QuoteDetailPage) | ✅ | Info header + CostingTree + Contenuti Auto + Riepilogo + Note (card separate) |
| Status transitions + PDF | ✅ | Riusa QuotesController per items/status/pdf |
| CatalogPickerDialog (albero completo) | ✅ | Listino→Gruppo→Categoria→Prodotto con ricerca, carrello multi-selezione, varianti raggruppate |
| Materiali con gerarchia parent/varianti | ✅ | parent_item_id su quote_material_items, UI prodotto→varianti come QuoteDetailPage |
| AddMaterialVariantDialog | ✅ | Aggiunta variante locale: descrizione, costo, K, qtà, anteprima vendita |
| Layout materiali a colonne allineate | ✅ | Header colonne (DESCRIZIONE, QTA, COSTO UNIT., COSTO TOT., K, VENDITA) + grid allineata |
| Distribuzione prezzo materiali corretta | ✅ | Leaf items (varianti) nella distribuzione, non parent header |
| EnsureParentHasChildren (legacy conversion) | ✅ | Converte item flat in parent+figlio prima di aggiungere variante |
| Contenuti automatici (auto_include) | ✅ | Sezione separata nell'IMPIANTO, bottone "Ricarica dal catalogo" |
| API reload-auto-includes | ✅ | POST /api/quotes/{id}/reload-auto-includes — pulisce e ri-inserisce da catalogo |
| Pannello info editabile | ✅ | Contatti, pagamento, validità, opzioni PDF, note (card separate) |
| Materiali: is_active su varianti | ✅ | Checkbox attiva/disattiva, opacity 0.5, totali filtrati |
| Materiali: FK catalogo (product_id/variant_id) | ✅ | Sync bidirezionale con catalogo CMS |
| Materiali: 5 pulsanti azione prodotto | ✅ | Refresh da catalogo, push a catalogo, edit RTF, clona, elimina |
| Materiali: editor RTF (TinyMCE) | ✅ | MaterialRtfDialog con HtmlEditor, salva description_rtf |
| Materiali: clone prodotto + varianti | ✅ | POST clone con copia parent + figli |
| Materiali: delete cascade | ✅ | Elimina parent + tutte le varianti figlie |

### 9f. Gestione Permessi (VisiWin-style) ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Tabella auth_levels (livelli gerarchici) | ✅ | 5 livelli: Operatore → Developer, ereditarietà |
| Tabella auth_features (feature → min level) | ✅ | page_key + min_level configurabile |
| Attached property Auth.Feature | ✅ | Applicato direttamente su qualsiasi elemento XAML |
| PermissionEngine (cache + CanAccess) | ✅ | Caricamento all'avvio, auto-register feature mancanti |
| Pagina admin Gestione Permessi | ✅ | Griglia feature × livelli con ComboBox nomi livello |
| Converter RoleToVisibilityConverter | ✅ | Visibilità basata su livello utente corrente |

### 9e. Pulizia Varianti Catalogo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Rimozione colonne obsolete da quote_product_variants | ✅ | Rimossi: sell_price, discount_pct, vat_pct, unit, default_qty |
| SellPrice = CostPrice × MarkupValue (computed) | ✅ | Su DTO, dialog, catalogo — read-only ovunque |
| QuoteProductDialog semplificato | ✅ | Solo: Codice, Nome, Costo az., K, Prezzo cl. (computed) |
| QuoteCatalogPage varianti aggiornate | ✅ | Header + righe: Codice, Nome, Costo az., K, Prezzo cl. |
| Migration automatica DB | ✅ | ALTER TABLE + DROP COLUMN con try/catch |
| Controller aggiornati | ✅ | QuoteCatalog, Quotes, Preventivi — default qty=1, unit="nr.", disc=0, vat=22 |

### 9c. Catalogo CMS — Miglioramenti ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Categorie nidificabili (parent_id) | ✅ | Gerarchia ricorsiva senza limiti di livello |
| Sotto-categoria da context menu | ✅ | Tasto destro → "+ Sotto-categoria" |
| Drag & drop categorie nel tree | ✅ | Sposta categoria sotto altra categoria o gruppo |
| Drag & drop prodotti nel tree | ✅ | Sposta prodotto da lista o tree verso altra categoria |
| Prodotti come foglie nel tree | ✅ | Ogni categoria mostra i prodotti come nodi figlio |
| Click su prodotto → dettaglio singolo | ✅ | Mostra solo quel prodotto nella lista a destra |
| Click su categoria → tutti i prodotti ricorsivi | ✅ | Include prodotti delle sotto-categorie |
| Natural sort (IRB 120 prima di IRB 1200) | ✅ | NaturalStringComparer custom |
| Ricerca ricorsiva multi-termine con debounce | ✅ | Cerca in gruppi, categorie, prodotti a qualsiasi profondità |
| Tree con listini come radice (Tutti i listini) | ✅ | Listino → Gruppo → Categoria → Prodotto |
| Accordion (uno solo aperto per livello) | ✅ | Gruppi e categorie |
| Stato tree preservato dopo operazioni | ✅ | HashSet di chiavi espanse, ricorsivo |
| GridSplitter ridimensionabile | ✅ | Pannello tree allargabile a mano |
| Campo K (markup_value) sulle varianti | ✅ | DB + DTO + UI nel dialog prodotti |
| K calcolato da sell_price/cost_price | ✅ | Applicato in bulk su varianti esistenti |
| Cat. Materiali rimossa dal menu | ✅ | K gestito dal catalogo, non da pagina separata |
| Riorganizzazione listino Automation Technology | ✅ | Da ~35 gruppi sparsi a 8 gruppi ordinati |
| Fix encoding UTF-8 (€, ò, °) | ✅ | Correzione caratteri corrotti nel DB |
| Consolidamento codice | ✅ | Rimosso dead code, fix bug drag DataGrid, _jopt shared, debounce, ToLookup server |

### 9g. PDF Preventivo IMPIANTO ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Endpoint PDF detecta IMPIANTO | ✅ | Carica costing data + genera con GenerateImpianto |
| Copertina + contenuti + condizioni | ✅ | Stessa struttura SERVICE, senza prodotti catalogo CMS |
| Descrizioni materiali con RTF+immagini nel PDF | ✅ | RenderDescription per ogni parent materiale con description_rtf |
| Distribuzione come riepilogo commerciale | ✅ | Nome sezione + prezzo totale (niente dati interni al cliente) |
| Scheda prezzi (NET → OFFER → FINAL) | ✅ | Risorse + Materiali + Trasferte → percentuali → prezzo finale |
| Contenuti auto-include su pagina nuova | ✅ | PageBreak() prima di ogni contenuto automatico |
| Firma + footer | ✅ | Pagina finale con firma per accettazione |

### 9h. Materiali — Funzionalità Avanzate ✅

| Funzionalità | Stato | Note |
|---|---|---|
| is_active su varianti materiale | ✅ | Checkbox toggle, opacity 0.5 su disattivate, totali filtrati |
| Toggle switch iOS-style (ToggleSwitchStyle) | ✅ | Stile XAML puro con animazione pallino, VisualStateManager, hover glow |
| FK catalogo (product_id/variant_id) | ✅ | Sync bidirezionale con catalogo CMS |
| 5 pulsanti azione prodotto | ✅ | Refresh da catalogo, push a catalogo, edit RTF, clona, elimina |
| Editor RTF materiali (MaterialRtfDialog) | ✅ | TinyMCE/WebView2 con save description_rtf |
| Clone prodotto + varianti | ✅ | POST clone con copia parent + figli |
| Delete cascade | ✅ | Elimina parent + tutte le varianti figlie |
| Descrizioni varianti editabili inline | ✅ | TextBox LostFocus con save automatico |

### 9i. Revisioni e Duplicazioni Preventivi ✅

| Funzionalità | Stato | Note |
|---|---|---|
| DB: parent_quote_id + status superseded | ✅ | Migration automatica, ENUM aggiornato |
| Endpoint POST /{id}/revision | ✅ | Copia completa (items + costing), vecchia → SUPERATA, numero "PRV-XXXX Rev N" |
| Endpoint POST /{id}/duplicate | ✅ | Nuovo numero indipendente, nessun legame, titolo "(copia)" |
| Context menu tasto destro su TreeView | ✅ | Crea Revisione, Duplica, Riattiva, Elimina |
| TreeView con revisioni nidificate | ✅ | Revisioni come figli del master, card grigia per SUPERATA |
| Riattiva ultima revisione superata | ✅ | Solo sull'ultima Rev della catena, PUT status → draft |
| Elimina con riattivazione automatica | ✅ | Se elimini l'ultima Rev, la precedente torna a draft |

### 9j. Popolamento Catalogo da Web ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Ricerca web specifiche tecniche per codice | ✅ | WebSearch per part number → descrizione HTML strutturata |
| Template descrizione coerente | ✅ | h3 titolo, Part Number, Tipo, Specifiche, Compatibilità, descrizione funzionale IT |
| Batch update 32 schede DSQC | ✅ | 3 agenti paralleli, categorie S2/S3, S4/S4C/S4C+, IRC5 |

### 9k. Pannello SERVICE completo ✅

| Funzionalità | Stato | Note |
|---|---|---|
| ToggleSwitchStyle su varianti SERVICE | ✅ | Stesso stile IMPIANTO |
| Layout varianti a griglia allineata | ✅ | Toggle, Descrizione, QTA, COSTO UNIT., COSTO TOT., K, VENDITA, x |
| Expand/collapse varianti con ▼/▲ | ✅ | Bottone in header prodotto |
| 5 pulsanti azione prodotto | ✅ | +Variante, Refresh, Edit RTF, Clona, Elimina |
| Contenuti automatici con ricarica | ✅ | Pannello separato con bottone "Ricarica dal catalogo" |
| Sconto % editabile | ✅ | TextBox con PATCH quotes/{id}/field |
| 4 checkbox opzioni PDF | ✅ | ShowItemPrices, ShowSummary, ShowSummaryPrices, HideQuantities |
| Riepilogo completo (7 righe) | ✅ | TOTALE, IVA, SCONTO, IMPONIBILE, IVA INCLUSA, COSTI AZ., UTILE |
| Note (uso interno + preventivo) | ✅ | Due TextBox con save su LostFocus |
| Descrizioni editabili (parent + varianti) | ✅ | TextBox inline con save automatico |
| Endpoint PATCH quotes/{id}/field | ✅ | 15 campi consentiti (sconto, note, opzioni PDF, contatti, ecc.) |
| Endpoint POST items/{id}/clone | ✅ | Copia parent + varianti |
| Endpoint PATCH items/{id}/field | ✅ | Aggiornamento singolo campo item |

### 9l. Ristrutturazione Navigazione Preventivi ✅

QuotesHomePage (DataGrid CMS) diventa la pagina principale "Preventivi". PreventiviPage perde il TreeView e diventa il dettaglio full-screen.

| Funzionalità | Stato | Note |
|---|---|---|
| QuotesHomePage come main page "Preventivi" | ✅ | DataGrid full screen con filtri, ricerca, pulsanti rapidi |
| Colonna TIPO con badge IMP/SRV | ✅ | DataTrigger arancione/verde |
| Filtro tipo (IMP/SRV/Tutti) | ✅ | ComboBox filtro nella toolbar |
| Navigazione a PreventiviPage(quoteId) | ✅ | Doppio-click, Modifica, Nuovo → dettaglio full-screen |
| NewPreventivoDialog semplificato | ✅ | Solo Cliente (con +Nuovo), Titolo, Tipo. Rimossi Listino e Gruppo Template |
| Bottone + Nuovo Cliente nel dialog | ✅ | Apre CustomerDialog, ricarica lista, seleziona il nuovo |
| PreventiviPage senza TreeView | ✅ | Pannello dettaglio full width, riceve quoteId dal costruttore |
| Bottone ← Indietro | ✅ | NavigationService.GoBack() |
| Cartella Offerts eliminata | ✅ | OffersPage, OfferViewPage rimossi |
| Route MainWindow pulite | ✅ | Un solo bottone "Preventivi" → QuotesHomePage |

### 9m. Riorganizzazione Cartelle Views ✅

| Azione | Stato | Note |
|---|---|---|
| Merge cartelle Cms/ e Preventivi/ | ✅ | Tutti i file in Views/Quotes/ |
| Rinomina file italiani → inglese | ✅ | PreventiviPage→QuoteDetailPage, NewPreventivoDialog→NewQuoteDialog, ConvertPreventivoDialog→ConvertQuoteDialog |
| Eliminazione Offerts/ | ✅ | OffersPage, OfferViewPage, OfferCostingController rimossi |
| Eliminazione file obsoleti Cms/ | ✅ | Vecchi QuoteDetailPage, NewQuoteDialog (sostituiti) |

### 9n. Rinomina QuotesHomePage → QuotesHomePage ✅

| Azione | Stato | Note |
|---|---|---|
| Rinomina classe e file | ✅ | QuotesHomePage → QuotesHomePage ovunque (XAML, code-behind, MainWindow, roadmap) |

### 9o. Revisioni Preventivi — UI Lista e Read-Only ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Lista piatta con expand/collapse revisioni | ✅ | `QuoteDisplayRow` unificato (master + sub-row), toggle ▶/▼ inline nella DataGrid |
| Badge "Rev N" e conteggio revisioni | ✅ | Badge blu DBEAFE su sotto-righe, badge conteggio "N rev" su master |
| Stile visivo sotto-righe | ✅ | Background #F3F4F6, opacity 0.6 per SUPERATA, indent "↳" |
| Bottone "Crea Revisione" in riga | ✅ | Nascosto su sotto-righe e righe superseded |
| Crea revisione senza navigare al dettaglio | ✅ | Solo reload lista dopo POST /{id}/revision |
| Click su revisione superseded → read-only | ✅ | `QuoteDetailPage(id, readOnly: true)` |
| Read-only: guard `if (_readOnly) return;` su TUTTI gli handler | ✅ | 18 handler QuoteDetailPage + 18 handler CostingTreeControl |
| Read-only: `IsReadOnlyMode` DependencyProperty | ✅ | Su QuoteDetailPage e CostingTreeControl, binding XAML per nascondere bottoni |
| Read-only: converter `InverseBoolToVis` su bottoni azione | ✅ | Toolbar materiali, delete risorsa/variante, + Gruppo/Sezione, + Materiale, Ridistribuisci |
| Read-only: `ApplyReadOnlyMode()` su campi info | ✅ | TextBox.IsReadOnly, CheckBox.IsEnabled, ComboBox.IsEnabled |
| Read-only: badge "SUPERATA — SOLA LETTURA" | ✅ | Header status badge grigio |
| Read-only: `pnlActions` nascosto | ✅ | Barra azioni superiore (Inviato, Accettato, Elimina, ecc.) collapsed |

### 9p. Vista Raggruppata per Cliente ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Toggle "☰ Griglia / 👥 Per Cliente" nella toolbar | ✅ | Segmented control con bottone attivo blu #2563EB |
| DataGrid.GroupStyle con HeaderTemplate | ✅ | Header azzurro #EFF6FF per ogni cliente: nome bold + badge conteggio |
| CollectionViewSource con GroupDescription su CustomerName | ✅ | Ordinamento per cliente → data, filtri funzionano in entrambe le viste |
| Persistenza preferenza vista | ✅ | `UserPreferences` salva in `%AppData%/ATEC_PM/user_prefs.json` |

### 9q. UserPreferences Service ✅

| Funzionalità | Stato | Note |
|---|---|---|
| `UserPreferences.cs` (Services/) | ✅ | File JSON locale `%AppData%/ATEC_PM/user_prefs.json` |
| API: `GetString/GetBool/GetInt` + `Set(key, value)` | ✅ | Thread-safe, lazy-loaded, auto-save |
| Chiave `QuotesHomePage.ViewMode` | ✅ | `"grid"` o `"grouped"`, caricata al costruttore della pagina |
| Riutilizzabile per qualsiasi preferenza UI | ✅ | Colonne, filtri, dimensioni finestre, ecc. |

### 9d. Da completare

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Riorganizzazione listini Atec Service e LISTINO ATEC | ❌ | MEDIA | Come fatto per Automation Technology |
| Popolamento descrizioni DSQC rimanenti (~38 schede) | ❌ | BASSA | Serie 345/346, 266, 377, YB |
| Eliminazione OfferCostingController server | ❌ | BASSA | Non più referenziato da client |

---

## Blocco 10 — Sistema Permessi a Livelli (stile VisiWin7) ✅ COMPLETATO

### 10a. Architettura Permessi ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Tabelle DB auth_levels + auth_features | ✅ | 5 livelli gerarchici (TECH→RESP→PM→ADMIN→DEV), 24 feature con min_level |
| AuthLevelController (API CRUD) | ✅ | 6 endpoint: GET livelli, GET/POST/PUT/DELETE feature, GET /features/my |
| DTO AuthLevel_DTOs.cs | ✅ | AuthLevelDto, AuthFeatureDto, UpdateAuthFeatureRequest, CreateAuthFeatureRequest |
| PermissionEngine con cache a livelli | ✅ | LoadFeatures() al login, CanAccess(key), IsDisabledOnly(key), fallback retrocompatibile |
| Caricamento feature al login | ✅ | GET /api/auth-levels/features/my dopo autenticazione, ClearFeatures() al logout |

### 10b. Attached Property (stile VisiWin) ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Auth.Feature attached property | ✅ | `auth:Auth.Feature="nav.clienti"` direttamente sul bottone — zero code-behind |
| Auth.AutoHide attached property | ✅ | `auth:Auth.AutoHide="True"` su Expander — nasconde sezione se tutti i figli Collapsed |
| Supporto behavior HIDDEN/DISABLED | ✅ | HIDDEN=Collapsed, DISABLED=visibile grigio (opacity 0.4) non cliccabile |
| Rimosso ApplySidebarPermissions() | ✅ | Tutto dichiarativo nel XAML, nessuna lista centralizzata |
| Rimossi x:Name dai bottoni sidebar | ✅ | Non servono più, l'attached property lavora in autonomia |

### 10c. Pagina Admin Permessi ✅

| Funzionalità | Stato | Note |
|---|---|---|
| AuthLevelsPage (DataGrid interattiva) | ✅ | Griglia: feature key, nome, categoria, livello minimo (ComboBox con nomi da DB), checkmark per ruolo, modo H/D |
| Legenda livelli colorata | ✅ | Badge colorati: TECH(blu), RESP(giallo), PM(verde), ADMIN(rosso), DEV(viola) |
| Checkmark calcolate automaticamente | ✅ | Colonne T/R/PM/A/D con ✓ calcolato da min_level (ereditarietà gerarchica) |
| ComboBox livelli da DB | ✅ | Nomi livelli caricati da auth_levels, non hardcoded |
| Salvataggio immediato su modifica | ✅ | PUT su cambio ComboBox livello o behavior, guard anti-loop |
| AddFeatureDialog con livelli da DB | ✅ | Creazione nuove feature: chiave, nome, categoria, livello minimo |
| Eliminazione feature con conferma | ✅ | Pulsante X per riga |
| Bottone "Permessi" nella sidebar | ✅ | Sezione AMMINISTRAZIONE, visibile solo livello 3+ |

### 10d. Converter XAML ✅

| Funzionalità | Stato | Note |
|---|---|---|
| AuthFeatureToVisibilityConverter | ✅ | ConverterParameter="nav.clienti" → Visible/Collapsed (per uso fuori sidebar) |
| AuthFeatureToEnabledConverter | ✅ | Per behavior DISABLED in qualsiasi pagina |

---

## Blocco 11 — Prossimi Step Generali

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Notifica TIMESHEET_ANOMALY | ❌ | ALTA | Ore giornaliere > 10h, fase sfora budget > 150% |
| Autocomplete descrizioni materiali | ❌ | BASSA | SELECT DISTINCT da storico |
| Notifiche Mail (SMTP Aruba) | 🅿️ | BASSA | Alert su scadenze via email |
| Sicurezza (bcrypt, HTTPS, rate limiting) | ❌ | MEDIA | Migrazione SHA2→bcrypt |
| Integrazione fatture Danea (Firebird) | ❌ | MEDIA | Struttura DB mappata in roadmap_danea.md |
| Deploy produzione | 🅿️ | BASSA | Server aziendale o cloud |

---

## Struttura File

```
Services/
  ApiClient.cs                       ← HTTP client wrapper per API backend
  UserPreferences.cs                 ← Preferenze utente locali JSON (%AppData%/ATEC_PM/user_prefs.json)

Helpers/
  Auth.cs                            ← Attached property Auth.Feature + Auth.AutoHide (permessi VisiWin-style direttamente nel XAML)

Views/ConfigurazioneSezioni/
  CostSectionsTreePage.xaml/.cs     ← Pagina unificata: gruppi, sezioni, fasi, reparti (drag & drop)
  CostSectionTemplateDialog.xaml/.cs ← Dialog creazione sezione
  DepartmentDialog.xaml/.cs         ← Dialog creazione/modifica reparto

Views/Quotes/
  QuotesHomePage.xaml/.cs            ← MAIN PAGE: DataGrid preventivi con filtri, ricerca, badge TIPO, toggle vista Griglia/Per Cliente
  QuoteDetailPage.xaml/.cs          ← DETAIL PAGE: dettaglio full-screen (SERVICE o IMPIANTO), bottone ← Indietro
  CostingTreeControl.xaml/.cs       ← Control costing IMPIANTO: risorse + materiali (parent/varianti) + pricing + distribuzione
  CostingTreeModel.cs               ← Model: CostingTreeRow, MaterialTreeRow, MaterialProductGroup, PricingVM, DistributionRowVM
  AddMaterialVariantDialog.xaml/.cs ← Dialog aggiunta variante materiale locale
  MaterialRtfDialog.xaml/.cs        ← Dialog editor RTF (TinyMCE) per descrizione prodotto
  NewQuoteDialog.xaml/.cs           ← Dialog creazione: Cliente (+Nuovo), Titolo, Tipo SERVICE/IMPIANTO
  ConvertQuoteDialog.xaml/.cs       ← Dialog selezione PM per conversione IMPIANTO → Commessa
  QuoteCatalogPage.xaml/.cs         ← Catalogo: TreeView Listino→Gruppi→Categorie→Prodotti + drag & drop
  QuoteGroupDialog.xaml/.cs         ← CRUD gruppi catalogo
  QuoteCategoryDialog.xaml/.cs      ← CRUD categorie catalogo
  QuoteProductDialog.xaml/.cs       ← Editor prodotto con TinyMCE + griglia varianti + K markup

Resources/
  ToggleSwitchStyle.xaml             ← Toggle switch iOS-style per CheckBox (VisualStateManager, animazione, hover glow)

Views/Admin/
  AuthLevelsPage.xaml/.cs            ← Pagina gestione permessi: griglia feature × livelli, ComboBox livello minimo, checkmark ereditarietà
  AddFeatureDialog.xaml/.cs          ← Dialog creazione nuova feature (chiave, nome, categoria, livello)

Views/Costing/
  ProjectCostingControl.xaml/.cs    ← Control riusabile: Load() / LoadForOffer() / LoadForPreventivo()
  CatalogPickerDialog.xaml/.cs      ← Picker prodotti dal catalogo per sezioni materiali
  ViewModels/                       ← CostingViewModel, CostGroupVM, CostSectionVM, etc.

Server/Controllers/
  AuthLevelController.cs            ← API permessi a livelli (CRUD feature, GET /features/my per login)
  PreventiviController.cs           ← API preventivi unificati (list, create con costing init, convert)
  PreventiviCostingController.cs    ← API costing preventivi (mirror OfferCosting su quote_cost_*)
  QuoteCatalogController.cs         ← API catalogo + categorie nidificabili + move prodotti/categorie
  QuotesController.cs               ← API preventivi CMS (items, status, pdf)
  OffersController.cs               ← API offerte (vecchia, da addormentare)
  OfferCostingController.cs         ← API costing offerte (vecchia)

Server/Services/
  QuoteDbService.cs                 ← DB: quote_type, quote_cost_*, parent_id categorie, markup varianti
  QuotePdfService.cs                ← Generatore PDF con QuestPDF

Shared/DTOs/
  AuthLevel_DTOs.cs                 ← DTO livelli e feature permessi (AuthLevelDto, AuthFeatureDto, UpdateAuthFeatureRequest, CreateAuthFeatureRequest)
  Quote_DTOs.cs                     ← DTO con QuoteType, ParentId, MarkupValue, CategoryMoveRequest, ProductMoveRequest
```

---

## Note Tecniche

- **DockPanel ordering**: `Dock="Right"` prima degli elementi filler in XAML
- **MySQL + Dapper**: usare `System.Data.IDbConnection/IDbTransaction`, non tipi MySqlConnector
- **EPPlus 8+**: `ExcelPackage.License.SetNonCommercialOrganization()`
- **QuestPDF**: `QuestPDF.Settings.License = LicenseType.Community` — gratuito per fatturato < $1M
- **Cache WPF**: cancellare bin/obj/.vs e Rebuild quando il designer mostra errori namespace fantasma
- **Expander Content**: un solo figlio — usare StackPanel wrapper se servono più elementi
- **Naming conflicts**: `System.IO.File` vs `ControllerBase.File()` → fully qualified
- **DataGrid edit diretto**: TextBox nel CellTemplate con IsReadOnly bindato, niente CellEditingTemplate
- **DataGrid refresh senza flash**: `decimal[]` con `Items.Refresh()` via Dispatcher
- **OxyPlot**: RectangleBarSeries per barre verticali, TrackerFormatString su LineSeries, TextAnnotation per valori fissi
- **JWT Claims**: usare `ClaimTypes.NameIdentifier` (non custom "employeeId") per GetCurrentEmployeeId()
- **Codex encoding**: ConvertZeroDateTime=True + CharacterSet=latin1 nella connection string
- **Notifiche destinatari**: PM commessa + user_role IN ('ADMIN','PM') + reparto ACQ — Remove(currentEmpId)
- **Stili DataGrid TextBlock**: usare `DgHeaderText` e `DgCellText` (non ModernColumnHeader/ModernCell sui TextBlock)
- **Snapshot dirty tracking**: serializzare DTO in JSON al load, confrontare alla navigazione — niente eventi TextChanged
- **Views organizzate in sottocartelle**: Commesse/, Clienti/, Utenti/, Reparti/, FasiTemplate/, Materiali/, Easyfatt/, Catalogo/, Codex/, Cms/, Costing/, etc. Root: solo LoginWindow + MainWindow
- **Sidebar collassabile**: Expander con freccia ▼/▶, sezioni Principale/Gestione/Admin/Avanzata/Sessione
- **TinyMCE 5 self-hosted**: no API key, no CDN — npm install tinymce@5, copiato in Assets/tinymce/tinymce/
- **Permessi a livelli (VisiWin-style)**: attached property `auth:Auth.Feature="nav.xxx"` direttamente sul bottone XAML — zero code-behind. Tabelle auth_levels + auth_features, PermissionEngine.CanAccess("feature.key"), ereditarietà gerarchica automatica (livello N vede tutto ciò che è ≤ N), behavior HIDDEN/DISABLED. Per aggiungere un nuovo bottone con permessi: `<Button auth:Auth.Feature="nav.mia_pagina" .../>` + riga in auth_features dalla pagina admin
- **L'utente comunica in italiano**
