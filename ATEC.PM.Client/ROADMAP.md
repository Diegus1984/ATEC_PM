# ATEC PM — Roadmap Aggiornata (Marzo 2026)

## Stack
- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token
- **Grafici**: OxyPlot.Wpf 2.2 (migrato da LiveCharts2)
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
| Righe entrate (PAGAMENTO, %, ENTRATE, Aggiustamento) | ✅ | % distribuite per mese |
| Righe uscite (categorie fornitore dinamiche CRUD) | ✅ | 8 default + aggiungi/rimuovi |
| Righe totali (USCITE MESE, DIFFERENZA cumulativa, BANCA) | ✅ | Differenza cumulativa progressiva |
| Colonne frozen A+B | ✅ | Etichetta + Importo fisse, mesi scrollabili |
| Colori celle (verde editabile #92D050, giallo calcolato #FFE699) | ✅ | Via CellColor + RowTypeToBgConverter |
| Rosso valori negativi | ✅ | CellForegroundConverter |
| Cap % a 100% | ✅ | Automatico nel Recalculate |
| Grafico OxyPlot | ✅ | Barre entrate/uscite (RectangleBarSeries) + linea saldo (LineSeries) |
| Annotazioni valori sulle barre | ✅ | TextAnnotation sopra/sotto barre con importi |
| DB: 3 tabelle compatte | ✅ | project_cashflow, project_cashflow_categories, project_cashflow_data |
| Endpoint unico PUT data (upsert generico) | ✅ | data_type: INCOME_PCT, ADJUSTMENT, CAT_PCT, BANK, SCHEDULE |

### 5c. Codex — Sync DB Remoto ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Tabella `codex_items` (clone locale) | ✅ | 22 colonne, indici su codice/fornitore/categoria |
| `CodexSyncService` (BackgroundService) | ✅ | Sync all'avvio + schedulato ogni 6h + manuale |
| `CodexController` | ✅ | GET lista, GET dettaglio, POST sync, GET sync-status |
| `CodexPage` WPF | ✅ | DataGrid 21 filtri, popup "Seleziona colonne", preferenze JSON locale |
| Encoding fix | ✅ | ConvertZeroDateTime + CharacterSet latin1/utf8mb4 |
| Connessione remota | ✅ | SERVER-CODEX:3306, ConnectionTimeout=60 |

### 5d. Catalogo Articoli — Miglioramenti ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Popup "Seleziona colonne" | ✅ | Stesso pattern CodexPage, preferenze in catalog_columns.json |
| Filtro su Categoria aggiunto | ✅ | TextBox header come le altre colonne |

---

## Blocco 6 — Sistema Notifiche & Dashboard ✅ COMPLETATO

### 6a. Infrastruttura Notifiche ✅

| Funzionalità | Stato | Note |
|---|---|---|
| Tabella `notifications` + `notification_recipients` | ✅ | Approccio B: 1 messaggio + N destinatari con is_read/read_at |
| `NotificationsController` | ✅ | GET unread, GET all, GET badge, PUT read, PUT read-all, DELETE |
| `NotificationService` (helper creazione) | ✅ | Create(), GetProjectPmIds(), GetAcqEmployeeIds() |
| `NotificationBackgroundService` | ✅ | Check scadenze DDP ogni 6h + pulizia retention |
| Trigger DDP_STATUS_CHANGED | ✅ | PUT ddp/{id} → confronta vecchio/nuovo stato → notifica PM+ACQ |
| Trigger PHASE_ASSIGNED | ✅ | AddAssignment + SaveAssignments → notifica tecnico assegnato |
| DdpStatusMap condiviso | ✅ | Classe statica in Bom_DTOs.cs, usata da server e client |
| Claim fix ClaimTypes.NameIdentifier | ✅ | Corretto in NotificationsController, ProjectsController, PhasesController, ChatController |
| Retention configurabile | ✅ | appsettings.json → Notifications:RetentionReadDays/RetentionUnreadDays |

### 6b. Dashboard PM con Notifiche ✅

| Funzionalità | Stato | Note |
|---|---|---|
| KPI cards | ✅ | Commesse attive, ore settimana/mese, ricavo totale |
| DataGrid alarm list stile VisiWin | ✅ | 4 severità colorate: ALARM rosso, WARNING arancio, INFO blu, SUCCESS verde |
| Colonne: Icona, Data/Ora, Operatore, Commessa, Tipo, Titolo, Messaggio, Articolo, Letto | ✅ | Messaggi in italiano |
| Righe lette → opacity 55% | ✅ | DataTrigger su IsRead |
| Checkbox "Solo non lette" | ✅ | Default attivo |
| Pulsante "Segna tutte lette" | ✅ | PUT read-all + refresh |
| Bottone "→ Vai" per navigazione | ✅ | Click → naviga alla DDP della commessa nel TreeView |
| Polling notifiche ogni 30s | ✅ | DispatcherTimer nella DashboardPage |
| Badge contatore sidebar | ✅ | Cerchio rosso su voce Dashboard, polling 60s |
| Commesse recenti | ✅ | DataGrid con codice, titolo, cliente, stato, ore |

### 6c. Tipi Notifica implementati

| Tipo | Severità | Destinatario | Trigger |
|---|---|---|---|
| `DDP_STATUS_CHANGED` | INFO/SUCCESS/WARNING | PM + ACQ (escluso chi modifica) | Cambio stato articolo DDP |
| `DDP_OVERDUE` | ALARM | PM + ACQ | Background: date_needed < oggi, stato ≠ DELIVERED/CANCELLED |
| `PHASE_ASSIGNED` | INFO | Tecnico assegnato | Assegnazione fase (singola e bulk) |

### 6d. Migrazione Grafici ✅

| Funzionalità | Stato | Note |
|---|---|---|
| LiveCharts2 → OxyPlot.Wpf 2.2 | ✅ | Rimosso SkiaSharp, rendering molto più veloce |
| RectangleBarSeries (barre verticali) | ✅ | Entrate verde + Uscite rosso |
| LineSeries (saldo cumulativo) | ✅ | Linea blu con marker |
| TextAnnotation valori sulle barre | ✅ | Importi visibili sopra/sotto barre |
| Tracker tooltip su linea | ✅ | "Saldo cumulativo: N €" |

---

## Blocco 7 — Prossimi Step

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Notifica TIMESHEET_ANOMALY | ❌ | ALTA | Ore giornaliere > 10h, fase sfora budget > 150% |
| Export Excel preventivo | ❌ | MEDIA | EPPlus, formato standard ATEC |
| Export PDF offerta | ❌ | MEDIA | Documento offerta cliente |
| Autocomplete descrizioni materiali | ❌ | BASSA | SELECT DISTINCT da storico |
| Notifiche Mail (SMTP Aruba) | 🅿️ | BASSA | Alert su scadenze via email |
| Deploy produzione | 🅿️ | BASSA | Server aziendale o cloud |

---

## Struttura Navigazione TreeView Commessa

```
📁 AT2026001 - Cliente
  ├── Dettagli                    → ProjectDashboardControl (KPI + ultime registrazioni con note)
  ├── ⚙ Configura Commessa       → ProjectCostingControl (risorse + materiali + scheda prezzi)
  ├── Fasi e Avanzamento          → PhasesManagementControl
  ├── 📊 Preventivo vs Consuntivo → BudgetVsActualControl
  ├── 💰 Flusso di Cassa          → CashFlowControl (griglia Excel + grafico OxyPlot)
  ├── 💬 Chat                     → ProjectChatControl
  ├── 📋 DDP Commerciali          → DdpCommercialControl
  └── 📁 Documenti                → DocumentManagerControl (lazy-load, preview)
```

---

## Architettura Relazioni Chiave

### Reparti (centro costo)
- `departments` → `hourly_cost` + `default_markup`
- Quando selezioni dipendente nella commessa → precompila €/h e K dal suo reparto

### Sezioni Costo → Reparti
- `cost_section_template_departments` = quali reparti possono lavorare in quella sezione
- Filtra i dipendenti visibili nella ComboBox della commessa

### Fasi Template → Sezioni Costo
- `phase_templates.cost_section_template_id` (many-to-one)
- Permette confronto preventivo vs consuntivo raggruppando ore timesheet per sezione costo

### In Commessa
- `project_cost_sections` = copia locale delle sezioni (indipendente dal template)
- `project_cost_resources` = risorse con ore, €/h, K per riga, campi trasferta
- `project_material_sections` / `project_material_items` = materiali con K per riga
- `project_pricing` = percentuali scheda prezzi + K trasferta/indennità
- Trasferte: costo ore nella sezione risorse (K risorsa), spese viaggio/alloggio/indennità nella sezione materiali (K dedicato)

### Notifiche
- `notifications` = messaggio unico (type, severity, title, message, reference_type/id, project_id, created_by)
- `notification_recipients` = N destinatari per notifica (employee_id, is_read, read_at)
- Retention: lette dopo 5gg, non lette dopo 30gg (configurabile in appsettings.json)
- Destinatari: PM commessa + ruoli ADMIN/PM + reparto ACQ (escluso chi genera la notifica)

### Flusso Cassa
- `project_cashflow` = testata (payment_amount, month_count)
- `project_cashflow_categories` = categorie fornitore CRUD
- `project_cashflow_data` = unica tabella per tutti i valori mensili (data_type + ref_id + month_number)
- Catena: timesheet_entries → project_phases → projects (no project_id diretto su timbrature)

### Codex
- `codex_items` = clone locale di SERVER-CODEX:codex.codici
- Sync: all'avvio (delay 5s) + ogni 6h + manuale via POST /api/codex/sync
- Encoding: latin1/utf8mb4 + ConvertZeroDateTime

### Calcolo Prezzo
```
Vendita Risorse = Σ (ore × €/h × K) per riga
Vendita Materiali = Σ (qtà × costo × K) per riga
Vendita Trasferte = (viaggi + alloggio) × K_trasferta
Vendita Indennità = indennità × K_indennità
─────────────────
NET PRICE = Σ tutto
+ Costi struttura (%)
+ Contingency (%)
+ Rischi & Garanzie (%)
= OFFER PRICE
+ Margine trattativa (%)
= FINAL OFFER PRICE
```

---

## Struttura File

```
Views/Costing/
  ProjectCostingControl.xaml/.cs    ← Risorse + materiali + scheda prezzi
  AddCostSectionDialog.xaml/.cs     ← Dialog aggiungi sezione
  AddCostGroupDialog.xaml/.cs       ← Dialog aggiungi gruppo
  Converters/CostingConverters.cs
  ViewModels/CostingViewModel.cs, CostGroupVM.cs, CostSectionVM.cs,
             CostResourceVM.cs, MaterialSectionVM.cs, MaterialItemVM.cs

Views/CashFlow/
  CashFlowControl.xaml/.cs          ← Griglia tipo Excel + grafico OxyPlot
  VM/CashFlowViewModel.cs           ← CfGridRow, CfRowType, Recalculate(), BuildChart()
  Converters/CashFlowConverters.cs  ← NegativeToBrush, RowTypeToBg, SepValue, InvertBool, IntAmount, CellForeground

Views/BudgetVsCosting/
  BudgetVsActualControl.xaml/.cs
  ViewModels/BvaCostingVM.cs

Views/Codex/
  CodexPage.xaml/.cs                ← DataGrid 21 filtri + popup colonne + sync status

Views/DashboardPage.xaml/.cs        ← KPI + alarm list notifiche + commesse recenti

Services/
  NotificationService.cs            ← Create() + NotificationBackgroundService (scadenze + retention)
  CodexSyncService.cs               ← Sync DB remoto SERVER-CODEX

Shared/DTOs/
  DdpStatusMap                      ← Mappa stati DDP condivisa server/client (in Bom_DTOs.cs)
```

---

## Note Tecniche

- **DockPanel ordering**: `Dock="Right"` prima degli elementi filler in XAML
- **MySQL + Dapper**: usare `System.Data.IDbConnection/IDbTransaction`, non tipi MySqlConnector
- **EPPlus 8+**: `ExcelPackage.License.SetNonCommercialOrganization()`
- **Cache WPF**: cancellare bin/obj/.vs e Rebuild quando il designer mostra errori namespace fantasma
- **Expander Content**: un solo figlio — usare StackPanel wrapper se servono più elementi
- **Naming conflicts**: `System.IO.File` vs `ControllerBase.File()` → fully qualified
- **DataGrid edit diretto**: TextBox nel CellTemplate con IsReadOnly bindato, niente CellEditingTemplate
- **DataGrid refresh senza flash**: `decimal[]` con `Items.Refresh()` via Dispatcher
- **OxyPlot**: RectangleBarSeries per barre verticali, TrackerFormatString su LineSeries, TextAnnotation per valori fissi
- **OxyPlot tracker**: RectangleBarSeries non supporta tracker standard — usare TextAnnotation
- **JWT Claims**: usare `ClaimTypes.NameIdentifier` (non custom "employeeId") per GetCurrentEmployeeId()
- **Codex encoding**: ConvertZeroDateTime=True + CharacterSet=latin1 nella connection string
- **Column selector**: popup ToggleButton + Popup con CheckBox, preferenze in %AppData%/ATEC_PM/*.json
- **Notifiche destinatari**: PM commessa + user_role IN ('ADMIN','PM') + reparto ACQ — Remove(currentEmpId)
- **L'utente comunica in italiano**
