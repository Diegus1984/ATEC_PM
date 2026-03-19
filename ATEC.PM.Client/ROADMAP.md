# ATEC PM — Roadmap Aggiornata (Marzo 2026)

## Stack
- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token
- **Grafici**: OxyPlot.Wpf 2.2 (migrato da LiveCharts2)
- **PDF**: QuestPDF (Community License)
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
| Sidebar + Navigazione | ✅ | MainWindow con sidebar scura |

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

### 5c. Codex — Sync DB Remoto ✅

| Funzionalità | Stato | Note |
|---|---|---|
| CodexSyncService + CodexPage | ✅ | 21 filtri, popup colonne, sync ogni 6h |

### 5d. Catalogo Articoli ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Popup colonne + filtro Categoria | ✅ | Stesso pattern CodexPage |

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

### 8d. DA COMPLETARE — Funzionalità mancanti

| Funzionalità | Stato | Note |
|---|---|---|
| Rich text editor descrizione prodotto | ❌ | Extended.Wpf.Toolkit RichTextBoxFormatBar, toolbar bold/italic/liste |
| Upload allegato per prodotto | ❌ | Campo file associato al prodotto nel catalogo |
| Upload immagine per prodotto | ❌ | Immagine prodotto visibile nel catalogo e nel PDF |
| Link nella descrizione prodotto | ❌ | Inserimento URL nella descrizione rich text |

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
  QuoteCatalogPage.xaml/.cs         ← TreeView Gruppi→Categorie + DataGrid prodotti
  QuoteGroupDialog.xaml/.cs         ← CRUD gruppi
  QuoteCategoryDialog.xaml/.cs      ← CRUD categorie
  QuoteProductDialog.xaml/.cs       ← Editor prodotto con griglia varianti inline
  QuotesListPage.xaml/.cs           ← Lista preventivi con filtri e badge stato
  NewQuoteDialog.xaml/.cs           ← Dialog creazione con selezione cliente+template
  QuoteDetailPage.xaml/.cs          ← Dettaglio completo (header+voci+riepilogo+note+PDF)
  AddQuoteItemDialog.xaml/.cs       ← Aggiunta voci da catalogo con doppio-click
  Converters/
    QuoteCatalogConverters.cs       ← Tipo prodotto/contenuto badge
    QuoteStatusConverters.cs        ← Badge stato preventivo

Server/Services/
  QuoteDbService.cs                 ← DB separato per modulo preventivi (8 tabelle)
  QuotePdfService.cs                ← Generatore PDF con QuestPDF

Server/Controllers/
  QuoteCatalogController.cs         ← API catalogo (gruppi, categorie, prodotti, varianti)
  QuotesController.cs               ← API preventivi (CRUD, items, stati, PDF, stats)

Shared/DTOs/
  Quote_DTOs.cs                     ← Tutti i DTO del modulo
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
- **L'utente comunica in italiano**
