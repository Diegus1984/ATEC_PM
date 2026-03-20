# ATEC PM — Roadmap Aggiornata (Marzo 2026)

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
| QuotesListPage (lista preventivi) | ✅ | DataGrid con filtri header, filtro stato, ricerca, colonne: numero, data, cliente, titolo, totale, utile, stato (badge colorato), agente |
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

## Blocco 9 — Fusione Preventivi + Offerte ❌ DA FARE

**Preventivi e offerte sono la stessa cosa.** Il Blocco 9 fonde i due moduli in uno solo, prendendo le funzionalità migliori di ciascuno.

### Decisioni aperte (da definire prima di procedere)

| # | Decisione | Opzioni | Stato |
|---|---|---|---|
| D1 | Strategia fusione | **A**: Offerte come base + features CMS, **B**: CMS come base + conversione | ☐ |
| D2 | Costing | Avanzato (struttura/contingency/margine) vs Semplice (costo/vendita/utile) | ☐ |
| D3 | Mapping verso commessa | Come le voci catalogo diventano fasi/BOM/budget ore | ☐ |
| D4 | Codice unico | OF2026xxx o PRV-2026-xxxx o altro formato | ☐ |
| D5 | UI lista | TreeView (per cliente/anno) o DataGrid (con filtri) | ☐ |
| D6 | Offerte vecchie | Migrare o lasciare com'è sono | ☐ |

### Roadmap fusione (post-decisioni)

| Fase | Descrizione | Sessioni | Dipende da |
|---|---|---|---|
| F1 — Decisioni | Definire risposte alle 6 decisioni | 1 discussione | — |
| F2 — Fusione DB | Unificare tabelle o creare ponte tra schemi | 1-2 | F1 |
| F3 — Fusione UI | Una sola coppia di pagine con features migliori + rich text editor + upload allegati/immagini | 2-3 | F2 |
| F4 — Conversione commessa | Adattare conversione al nuovo schema. Mapping voci catalogo → fasi/BOM | 1-2 | F3 |
| F5 — PDF aggiornato | Adattare QuotePdfService al schema fuso + logo + layout personalizzabile | 1 | F3 |
| F6 — Revisioni | Sistema revisioni con snapshot JSON + storico completo | 1 | F3 |
| F7 — Dashboard CMC | KPI: preventivi emessi, tasso conversione, pipeline, utile medio + grafici OxyPlot | 1-2 | F3 |
| F8 — Ruolo CMC | CMC nel RBAC. Menu/pagine visibili per ruolo | 1 | F7 |
| F9 — Cleanup | Rimuovere modulo non più usato. Pulizia codice e DB | 1 | F4 |

**Totale stimato: 10-14 sessioni**

### Inventario — cosa abbiamo da entrambi i moduli

**Dal modulo Offerte (Blocco 7):**
- Conversione in commessa funzionante
- Costing avanzato (sezioni/risorse/materiali/pricing)
- ProjectCostingControl riusabile
- Revisioni con copia completa
- Notifiche OFFER_CONVERTED

**Dal modulo Preventivi CMS (Blocco 8):**
- Catalogo template (Gruppi→Categorie→Prodotti→Varianti)
- Auto-populate dal template
- Aggiunta rapida con doppio-click + duplicate detection
- Generazione PDF professionale
- Dirty tracking con snapshot
- Doppio prezzo costo/vendita con utile per riga

**Da costruire nella fusione:**
- Ponte tra voci catalogo e sezioni costo
- UI unificata (una sola lista + un solo dettaglio)
- Mapping conversione: voci catalogo → fasi progetto + BOM
- Rich text editor per descrizione prodotto
- Upload allegati/immagini per prodotto
- Dashboard CMC con KPI
- Ruolo CMC nel RBAC

---

## Blocco 10 — Prossimi Step Generali

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Notifica TIMESHEET_ANOMALY | ❌ | ALTA | Ore giornaliere > 10h, fase sfora budget > 150% |
| Autocomplete descrizioni materiali | ❌ | BASSA | SELECT DISTINCT da storico |
| Notifiche Mail (SMTP Aruba) | 🅿️ | BASSA | Alert su scadenze via email |
| Separazione ruoli (menu per ruolo) | ❌ | MEDIA | ADMIN/PM/CMC/TECH vedono pagine diverse |
| Sicurezza (bcrypt, HTTPS, rate limiting) | ❌ | MEDIA | Migrazione SHA2→bcrypt |
| Deploy produzione | 🅿️ | BASSA | Server aziendale o cloud |

---

## Struttura File Modulo CMS

```
Views/Cms/
  QuoteCatalogPage.xaml/.cs         ← TreeView Gruppi→Categorie + DataGrid prodotti + Import Excel
  QuoteGroupDialog.xaml/.cs         ← CRUD gruppi
  QuoteCategoryDialog.xaml/.cs      ← CRUD categorie
  QuoteProductDialog.xaml/.cs       ← Editor prodotto con TinyMCE + griglia varianti + allegato
  QuotesListPage.xaml/.cs           ← Lista preventivi con filtri e badge stato
  NewQuoteDialog.xaml/.cs           ← Dialog creazione con selezione listino+cliente+template
  QuoteDetailPage.xaml/.cs          ← Dettaglio completo + toggle Attiva/Conferma varianti
  AddQuoteItemDialog.xaml/.cs       ← Aggiunta voci da catalogo con doppio-click
  Converters/
    QuoteCatalogConverters.cs       ← Tipo prodotto/contenuto badge
    QuoteStatusConverters.cs        ← Badge stato preventivo

UserControls/
  HtmlEditor.xaml/.cs               ← WebView2 + TinyMCE 5 (riutilizzabile)

Assets/tinymce/
  editor.html                       ← HTML host per TinyMCE
  tinymce/                          ← TinyMCE 5 self-hosted (npm)

Views/Codex/
  CodexPage.xaml/.cs                ← Lista articoli codex con filtri
  CodexCompositionPage.xaml/.cs     ← Composizione 501/601/701 con drag&drop
  QuantityDialog.xaml/.cs           ← Dialog quantità

Server/Services/
  QuoteDbService.cs                 ← DB modulo preventivi (10 tabelle incl. quote_price_lists)
  QuotePdfService.cs                ← Generatore PDF con QuestPDF

Server/Controllers/
  QuoteCatalogController.cs         ← API catalogo + listini + import Excel
  QuotesController.cs               ← API preventivi + AddProductWithAllVariants

Shared/DTOs/
  Quote_DTOs.cs                     ← Tutti i DTO (incl. PriceList, Import, varianti attive)
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
- **L'utente comunica in italiano**
