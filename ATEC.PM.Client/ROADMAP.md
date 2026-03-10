# ATEC PM — Roadmap Aggiornata (Marzo 2026)

## Stack
- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token
- **Grafici**: LiveChartsCore.SkiaSharpView.WPF (LiveCharts2)
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
| Grafico LiveCharts2 | ✅ | Barre entrate/uscite + linea saldo cumulativo |
| Grafico allineato a colonne griglia | ✅ | Margin calcolato da LayoutUpdated |
| Scala Y dinamica | ✅ | MinStep auto-calcolato dai dati |
| DB: 3 tabelle compatte | ✅ | project_cashflow, project_cashflow_categories, project_cashflow_data |
| Endpoint unico PUT data (upsert generico) | ✅ | data_type: INCOME_PCT, ADJUSTMENT, CAT_PCT, BANK, SCHEDULE |

---

## Blocco 6 — Prossimi Step

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Dashboard principale | ❌ | MEDIA | KPI commesse, ore, costi, stato avanzamento |
| Export Excel preventivo | ❌ | MEDIA | EPPlus, formato standard ATEC |
| Export PDF offerta | ❌ | MEDIA | Documento offerta cliente |
| Separazione ruoli ADMIN/PM vs TECH | ❌ | MEDIA | Menu/pagine visibili per ruolo |
| Autocomplete descrizioni materiali | ❌ | BASSA | SELECT DISTINCT da storico |
| Notifiche Mail (SMTP Aruba) | 🅿️ | BASSA | Alert su scadenze, ore eccessive |
| Deploy produzione | 🅿️ | BASSA | Server aziendale o cloud |

---

## Struttura Navigazione TreeView Commessa

```
📁 AT2026001 - Cliente
  ├── Dettagli                    → ProjectDashboardControl (KPI + ultime registrazioni con note)
  ├── ⚙ Configura Commessa       → ProjectCostingControl (risorse + materiali + scheda prezzi)
  ├── Fasi e Avanzamento          → PhasesManagementControl
  ├── 📊 Preventivo vs Consuntivo → BudgetVsActualControl
  ├── 💰 Flusso di Cassa          → CashFlowControl (griglia Excel + grafico)
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

### Flusso Cassa
- `project_cashflow` = testata (payment_amount, month_count)
- `project_cashflow_categories` = categorie fornitore CRUD
- `project_cashflow_data` = unica tabella per tutti i valori mensili (data_type + ref_id + month_number)
- Catena: timesheet_entries → project_phases → projects (no project_id diretto su timbrature)

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
  CashFlowControl.xaml/.cs          ← Griglia tipo Excel + grafico LiveCharts2
  VM/CashFlowViewModel.cs           ← CfGridRow, CfRowType, Recalculate(), BuildChart()
  Converters/CashFlowConverters.cs  ← NegativeToBrush, RowTypeToBg, SepValue, InvertBool, IntAmount, CellForeground

Views/BudgetVsCosting/
  BudgetVsActualControl.xaml/.cs
  ViewModels/BvaCostingVM.cs
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
- **DataGrid refresh senza flash**: `decimal[]` con `Items.Refresh()` via Dispatcher, oppure senza Refresh se binding sufficiente
- **LiveCharts2 YAxis**: MinStep, MinLimit, MaxLimit settabili solo da codice C#, non da XAML
- **Grafico allineamento**: Margin calcolato da `LayoutUpdated` della DataGrid, con DrawMargin per asse Y
- **StringFormat + ConvertBack**: StringFormat=N0 impedisce ConvertBack su TextBox — usare Converter dedicato (IntegerAmountConverter)
- **L'utente comunica in italiano**
