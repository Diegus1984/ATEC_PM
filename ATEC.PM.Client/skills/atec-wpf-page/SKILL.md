---
name: atec-wpf-page
description: >
  Genera pagine WPF complete (XAML + ViewModel + code-behind) per il gestionale ATEC PM,
  rispettando il design system e i pattern architetturali del progetto.
  Usa questa skill ogni volta che l'utente chiede di creare una nuova pagina, vista, schermata,
  finestra o dialog WPF per il gestionale ATEC. Anche quando dice cose come "aggiungi la pagina X",
  "crea la schermata per Y", "mi serve una vista per gestire Z", o semplicemente nomina un layout
  (griglia, treeview, dashboard, form). Se l'utente sta lavorando sul progetto ATEC PM e menziona
  UI, pagine, viste, o schermate WPF, questa skill è quasi certamente quella giusta.
---

# Skill: Genera Pagine WPF per ATEC PM

Questa skill genera pagine WPF complete e coerenti con il design system del gestionale ATEC PM.
Produce tre file: XAML (vista), ViewModel (logica), code-behind (cablaggio eventi e caricamento dati).

## Quando usarla

Ogni volta che serve creare una nuova pagina, dialog o controllo per il client WPF del gestionale ATEC PM. I layout supportati sono:

1. **DataGrid** — griglia dati con filtri in-header, toolbar azioni, status bar (es. Clienti, Dipendenti, Fornitori)
2. **TreeView + Dettaglio** — albero navigabile a sinistra, pannello dettagli con tab a destra (es. Commesse)
3. **Dashboard** — card KPI in griglia + tabelle riassuntive (es. Dashboard principale)
4. **Form dialog** — finestra modale per inserimento/modifica entità (es. CustomerDialog)

## Prima di iniziare

Leggi il file `references/templates.md` nella directory di questa skill per avere i template XAML, ViewModel e code-behind pronti per ciascun layout. Usali come punto di partenza e adattali all'entità richiesta.

## Design System ATEC PM

Questi valori sono fissi e vanno rispettati in ogni pagina generata.

### Palette colori
- **Sfondo pagina**: `#F7F8FA` (grigio freddo chiaro)
- **Pannelli/card**: `#FFFFFF` con bordo `#E4E7EC` (1px)
- **Accent primario**: `#2563EB` (blu), hover `#1D4ED8`, pressed `#1E40AF`
- **Sidebar**: `#1A1D26` con accent `#4F6EF7`
- **Danger**: `#DC2626` — Success: `#16A34A` — Warning: `#D97706`
- **Testo primario**: `#111827` — Secondario: `#6B7280` — Disabilitato: `#9CA3AF`

### Tipografia
Tutto **Segoe UI**:
- Titolo pagina: 20px Bold
- Titolo sezione: 14px SemiBold
- Corpo/DataGrid cells: 13px Regular
- Label/caption: 11px SemiBold, colore `#6B7280`
- Pulsanti: 13px Medium

### Bordi e spaziature
- Border radius: 6px (pulsanti, input), 4px (elementi piccoli)
- Padding standard: 16px (pannelli), 8px (celle), 12px (pulsanti)
- Margine tra sezioni: 16px
- Altezza input: 36px — Altezza input compatto (filtri header): 28px

### Stili da usare (già definiti in App.xaml)
Non ridefinire questi stili, usali direttamente:
- Pulsanti: `FlatBtn`, `PrimaryBtn`, `DangerBtn`, `SuccessBtn`, `GhostBtn`
- DataGrid: `ModernDataGrid`, `ModernColumnHeader`, `ModernCell`, `ModernRow`
- Input: `ModernTextBox`, `HeaderSearchBox`
- TreeView: `TreeViewStyle`
- Expander: `SmoothExpander`

## Architettura dei file

### Dove mettere i file
- Pagina semplice → `Views/[NomePagina]Page.xaml` + `.xaml.cs`
- Pagina con sotto-componenti → `Views/[Feature]/[NomePagina]Page.xaml` + sotto-cartella `ViewModels/`
- Dialog → `Views/[Entity]Dialog.xaml` + `.xaml.cs`
- ViewModel → stessa cartella della vista, oppure `ViewModels/` se più di uno

### Naming
- Pagine: `[Feature]Page.xaml` (es. `SuppliersPage.xaml`)
- Dialog: `[Entity]Dialog.xaml` (es. `SupplierDialog.xaml`)
- ViewModel: `[Entity]VM.cs` o `[Entity]ViewModel.cs`
- Elementi XAML: camelCase con prefisso → `btnNew`, `txtSearch`, `dgItems`, `treeItems`

## Pattern ViewModel

Ogni ViewModel implementa `INotifyPropertyChanged` con questo pattern:

```csharp
public class NomeEntityVM : INotifyPropertyChanged
{
    private string _campo = "";
    public string Campo
    {
        get => _campo;
        set { _campo = value; Notify(); }
    }

    // Per collezioni usa ObservableCollection<T>
    public ObservableCollection<ItemVM> Items { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

Punti chiave:
- Backing field privato + proprietà pubblica con `Notify()` nel setter
- `[CallerMemberName]` per evitare stringhe magiche
- `ObservableCollection<T>` per liste con binding
- Proprietà calcolate (solo getter) per valori derivati
- Metodo `RecalcTotals()` o simile per ricalcoli a cascata

## Pattern Code-Behind

Il code-behind gestisce: inizializzazione, caricamento dati via API, eventi UI, filtri.

### Caricamento dati
```csharp
private List<EntityDto> _allItems = new();

private async Task Load()
{
    txtStatus.Text = "Caricamento...";
    try
    {
        string json = await ApiClient.GetAsync("/api/endpoint");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var response = JsonSerializer.Deserialize<ApiResponse<List<EntityDto>>>(json, options);

        if (response?.Success == true)
        {
            _allItems = response.Data ?? new();
            ApplyFilter();
            txtStatus.Text = $"{_allItems.Count} elementi";
        }
    }
    catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
}
```

### Filtro con debounce (per DataGrid con filtri header)
```csharp
private CancellationTokenSource? _filterCts;

private async void FilterChanged(object sender, TextChangedEventArgs e)
{
    _filterCts?.Cancel();
    _filterCts = new CancellationTokenSource();
    try
    {
        await Task.Delay(300, _filterCts.Token);
        ApplyFilter();
    }
    catch (TaskCanceledException) { }
}

private void ApplyFilter()
{
    var filtered = _allItems.AsEnumerable();
    // applica filtri da TextBox con Tag
    foreach (var kvp in _filterBoxes)
    {
        string val = kvp.Value.Text.Trim().ToLower();
        if (!string.IsNullOrEmpty(val))
        {
            string prop = kvp.Key;
            filtered = filtered.Where(x =>
                (x.GetType().GetProperty(prop)?.GetValue(x)?.ToString() ?? "")
                .ToLower().Contains(val));
        }
    }
    dgItems.ItemsSource = filtered.ToList();
}
```

### Dialog (Nuovo/Modifica)
```csharp
private void BtnNew_Click(object sender, RoutedEventArgs e)
{
    var dlg = new EntityDialog { Owner = Window.GetWindow(this) };
    if (dlg.ShowDialog() == true) _ = Load();
}

private void BtnEdit_Click(object sender, RoutedEventArgs e)
{
    if (dgItems.SelectedItem is EntityDto item)
    {
        var dlg = new EntityDialog(item) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
```

### Navigazione e inizializzazione
```csharp
public NomePage()
{
    InitializeComponent();
    Loaded += async (_, _) => await Load();
}
```

## Istruzioni di generazione

Quando l'utente chiede una nuova pagina:

1. **Chiedi il layout** se non è chiaro (DataGrid, TreeView+Dettaglio, Dashboard, Dialog)
2. **Chiedi l'entità/endpoint** se non specificato
3. **Leggi `references/templates.md`** per il template del layout scelto
4. **Genera i tre file**: XAML, ViewModel (se serve), code-behind
5. **Adatta** nomi, colonne, campi all'entità richiesta
6. **Registra la navigazione** — indica all'utente dove aggiungere il case in `MainWindow.xaml.cs`

Ricorda: il codice deve essere **funzionante** e collegato alle API REST del server (`http://localhost:5100`). Usa sempre `ApiClient.GetAsync/PostAsync/PutAsync/DeleteAsync` per le chiamate.
