# ATEC PM — Roadmap Aggiornata (Marzo 2026)

## Stack
- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token
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
| Timesheet Settimanale | ✅ | Inserimento ore per fase, validazioni |
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

### 3c. Materiali

| Funzionalità | Stato | Note |
|---|---|---|
| Vista materiali (categorie collassabili) | ✅ | Stessa pagina risorse, sotto |
| K per riga materiale | ✅ | Default da categoria, sovrascrivibile |
| Tipo MATERIAL / COMMISSION | ✅ | Badge provvigione, K provvigione separato |
| Spese trasferta calcolate | ✅ | Auto da risorse DA_CLIENTE, K editabile |
| Indennità trasferta calcolate | ✅ | Auto da risorse DA_CLIENTE, K editabile |
| Autocomplete descrizioni da storico | ❌ | SELECT DISTINCT description FROM project_material_items |

### 3d. Scheda Prezzi / Riepilogo

| Funzionalità | Stato | Note |
|---|---|---|
| Totale risorse + materiali + trasferte | ✅ | Barre nere + barra blu TOTALE GENERALE |
| Tab Scheda Prezzi (NET → OFFER → FINAL) | ❌ | Struttura%, contingency%, rischi%, margine% |
| Confronto preventivo vs consuntivo | ❌ | Via phase_template → cost_section_template |
| Export Excel/PDF preventivo | ❌ | EPPlus per Excel |

---

## Blocco 4 — Pulizia / Refactoring

| Funzionalità | Stato | Note |
|---|---|---|
| Eliminazione tabella markup_coefficients | ✅ | K ora su departments.default_markup |
| Eliminazione project_markup_values | ✅ | Non più usata |
| Rimozione markup_code da departments | ✅ | Sostituito da default_markup |
| Rimozione markup_value da sezioni costo | ✅ | K sulla riga risorsa, non sulla sezione |
| Eliminazione MarkupPage/MarkupDialog | ✅ | Voce menu rimossa |
| DepartmentsPage/Dialog con K diretto | ✅ | TextBox K verde al posto di ComboBox |
| Eliminazione MarkupController | ❌ | File ancora presente nel server |

---

## Blocco 5 — Prossimi Step

| Funzionalità | Stato | Priorità | Note |
|---|---|---|---|
| Scheda Prezzi (NET → FINAL OFFER) | ❌ | ALTA | In fondo alla pagina costing o tab dedicato |
| Confronto preventivo vs consuntivo | ❌ | ALTA | Report per sezione costo: budget vs ore reali |
| Dashboard principale | ❌ | MEDIA | KPI commesse, ore, costi, stato avanzamento |
| Export Excel preventivo | ❌ | MEDIA | EPPlus, formato standard ATEC |
| Export PDF offerta | ❌ | MEDIA | Documento offerta cliente |
| Separazione ruoli ADMIN/PM vs TECH | ❌ | MEDIA | Menu/pagine visibili per ruolo |
| Notifiche Mail (SMTP Aruba) | 🅿️ | BASSA | Alert su scadenze, ore eccessive |
| Deploy produzione | 🅿️ | BASSA | Server aziendale o cloud |

---

## Architettura Relazioni Chiave

### Reparti (centro costo)
- `departments` → `hourly_cost` + `default_markup`
- Quando selezioni dipendente nella commessa → precompila €/h e K dal suo reparto
- Niente più tabelle K separate (markup_coefficients eliminata)

### Sezioni Costo → Reparti
- `cost_section_template_departments` = quali reparti possono lavorare in quella sezione
- Filtra i dipendenti visibili nella ComboBox della commessa

### Fasi Template → Sezioni Costo
- `phase_templates.cost_section_template_id` (many-to-one)
- Permette confronto preventivo vs consuntivo raggruppando ore timesheet per sezione costo
- Es: "Prog. schema elettrico" + "Prog. quadri" + "Cablaggio" → tutte puntano a "PROGETTAZIONE ELETTRICA"

### In Commessa
- `project_cost_sections` = copia locale delle sezioni (indipendente dal template)
- `project_cost_resources` = risorse con ore, €/h, K per riga, campi trasferta
- `project_material_sections` / `project_material_items` = materiali con K per riga
- `project_pricing` = percentuali scheda prezzi + K trasferta/indennità
- Trasferte: costo ore nella sezione risorse (K risorsa), spese viaggio/alloggio/indennità nella sezione materiali (K dedicato)

### Calcolo Prezzo
```
Vendita Risorse = Σ (ore × €/h × K) per riga
Vendita Materiali = Σ (qtà × costo × K) per riga
Vendita Trasferte = (viaggi + alloggio) × K_trasferta
Vendita Indennità = indennità × K_indennità
─────────────────
NET PRICE = Σ tutto
+ Costi struttura (2%)
+ Contingency (5%)
+ Rischi & Garanzie (5%)
= OFFER PRICE
+ Margine trattativa (10%)
= FINAL OFFER PRICE
```

---

## Struttura File Costing (Views/Costing/)

```
Views/Costing/
  ProjectCostingControl.xaml          ← XAML completo risorse + materiali + trasferte + totali
  ProjectCostingControl.xaml.cs       ← Code-behind snello (handler eventi)
  AddCostSectionDialog.xaml/.cs       ← Dialog aggiungi sezione (da template o custom)
  AddCostGroupDialog.xaml/.cs         ← Dialog aggiungi gruppo (da template o custom)
  Converters/
    CostingConverters.cs              ← HexToBrush, BoolToAngle, MarkupToString, ItemTypeToBadge, ecc.
  ViewModels/
    CostingViewModel.cs               ← Root VM, FromData(), WireAllChanges(), totali generali
    CostGroupVM.cs                    ← Gruppo (GESTIONE, INSTALLAZIONE...), colore, totali
    CostSectionVM.cs                  ← Sezione, tipo IN_SEDE/DA_CLIENTE, totali ore + trasferte
    CostResourceVM.cs                 ← Riga risorsa con €/h, K, ore, trasferta, TotalSale
    MaterialSectionVM.cs              ← Categoria materiale, DefaultMarkup, DefaultCommissionMarkup
    MaterialItemVM.cs                 ← Riga materiale, ItemType MATERIAL/COMMISSION, TotalSale
```

---

## Note Tecniche

- **DockPanel ordering**: `Dock="Right"` prima degli elementi filler in XAML
- **MySQL + Dapper**: usare `System.Data.IDbConnection/IDbTransaction`, non tipi MySqlConnector
- **EPPlus 8+**: `ExcelPackage.License.SetNonCommercialOrganization()`
- **Cache WPF**: cancellare bin/obj/.vs e Rebuild quando il designer mostra errori namespace fantasma
- **Expander Content**: un solo figlio — usare StackPanel wrapper se servono più elementi
- **Naming conflicts**: `System.IO.File` vs `ControllerBase.File()` → fully qualified
- **L'utente comunica in italiano**
