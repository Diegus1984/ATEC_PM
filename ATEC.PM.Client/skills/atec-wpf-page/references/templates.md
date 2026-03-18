# Template per Layout WPF - ATEC PM

Questo file contiene i template XAML, ViewModel e code-behind per ciascun tipo di layout.
Usali come base e adatta nomi, colonne e campi all'entità richiesta.

---

## 1. Layout DataGrid (Griglia con filtri + toolbar)

Adatto per: elenchi entità con ricerca, CRUD, esportazione.
Esempi esistenti: CustomersPage, EmployeesPage, SuppliersPage.

### XAML — `[Entity]Page.xaml`

```xml
<Page x:Class="ATEC.PM.Client.Views.[Entity]Page"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Background="#F7F8FA">

    <DockPanel>
        <!-- TOOLBAR -->
        <Border DockPanel.Dock="Top" Background="White"
                BorderBrush="#E4E7EC" BorderThickness="0,0,0,1" Padding="16,8">
            <StackPanel Orientation="Horizontal">
                <Button x:Name="btnNew" Content="＋ Nuovo"
                        Style="{StaticResource PrimaryBtn}" Margin="0,0,8,0"
                        Click="BtnNew_Click"/>
                <Button x:Name="btnEdit" Content="Modifica"
                        Style="{StaticResource FlatBtn}" Margin="0,0,8,0"
                        IsEnabled="False" Click="BtnEdit_Click"/>
                <Button x:Name="btnDelete" Content="Elimina"
                        Style="{StaticResource FlatBtn}" Margin="0,0,8,0"
                        IsEnabled="False" Foreground="#B42318"
                        Click="BtnDelete_Click"/>
                <Rectangle Width="1" Fill="#E4E7EC" Margin="8,2"/>
                <Button x:Name="btnRefresh" Content="Aggiorna"
                        Style="{StaticResource GhostBtn}" Margin="8,0,0,0"
                        Click="BtnRefresh_Click"/>
            </StackPanel>
        </Border>

        <!-- STATUS BAR -->
        <Border DockPanel.Dock="Bottom" Background="White"
                BorderBrush="#E4E7EC" BorderThickness="0,1,0,0" Padding="16,6">
            <TextBlock x:Name="txtStatus" Text="Pronto"
                       FontSize="11" Foreground="#6B7280"/>
        </Border>

        <!-- DATAGRID -->
        <DataGrid x:Name="dgItems"
                  Style="{StaticResource ModernDataGrid}"
                  AutoGenerateColumns="False"
                  IsReadOnly="True"
                  SelectionMode="Single"
                  SelectionChanged="DgItems_SelectionChanged"
                  MouseDoubleClick="DgItems_DoubleClick">
            <DataGrid.Columns>
                <!-- COLONNA CON FILTRO HEADER -->
                <DataGridTemplateColumn Width="*" SortMemberPath="NomeCampo">
                    <DataGridTemplateColumn.Header>
                        <StackPanel>
                            <TextBlock Text="NOME COLONNA"
                                       Style="{StaticResource ModernColumnHeader}"/>
                            <TextBox Tag="NomeCampo"
                                     Style="{StaticResource HeaderSearchBox}"
                                     TextChanged="FilterChanged"/>
                        </StackPanel>
                    </DataGridTemplateColumn.Header>
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding NomeCampo}"
                                       Style="{StaticResource ModernCell}"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>

                <!-- Ripeti per ogni colonna, cambiando NomeCampo e intestazione -->
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</Page>
```

### Code-Behind — `[Entity]Page.xaml.cs`

```csharp
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs; // o il namespace corretto del DTO

namespace ATEC.PM.Client.Views;

public partial class [Entity]Page : Page
{
    private List<[Entity]Dto> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;

    public [Entity]Page()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/[entities]");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resp = JsonSerializer.Deserialize<ApiResponse<List<[Entity]Dto>>>(json, options);

            if (resp?.Success == true)
            {
                _allItems = resp.Data ?? new();
                CollectFilterBoxes();
                ApplyFilter();
                txtStatus.Text = $"{_allItems.Count} elementi";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void CollectFilterBoxes()
    {
        if (_filterBoxes.Count > 0) return;
        foreach (var col in dgItems.Columns)
        {
            if (col.Header is StackPanel sp)
            {
                var tb = sp.Children.OfType<TextBox>().FirstOrDefault();
                if (tb?.Tag is string tag)
                    _filterBoxes[tag] = tb;
            }
        }
    }

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

    private void DgItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = dgItems.SelectedItem != null;
        btnEdit.IsEnabled = hasSelection;
        btnDelete.IsEnabled = hasSelection;
    }

    private void DgItems_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnEdit_Click(sender, e);
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new [Entity]Dialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgItems.SelectedItem is [Entity]Dto item)
        {
            var dlg = new [Entity]Dialog(item) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgItems.SelectedItem is not [Entity]Dto item) return;

        var result = MessageBox.Show(
            "Sei sicuro di voler eliminare questo elemento?",
            "Conferma eliminazione",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await ApiClient.DeleteAsync($"/api/[entities]/{item.Id}");
                await Load();
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await Load();
    }
}
```

---

## 2. Layout TreeView + Pannello Dettagli

Adatto per: entità gerarchiche con navigazione ad albero e dettagli contestuali.
Esempio esistente: ProjectsPage (Commesse).

### XAML — `[Feature]Page.xaml`

```xml
<Page x:Class="ATEC.PM.Client.Views.[Feature]Page"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Background="#F7F8FA">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="260"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- PANNELLO SINISTRO: ALBERO -->
        <Border Grid.Column="0" Background="White"
                BorderBrush="#E4E7EC" BorderThickness="0,0,1,0">
            <DockPanel>
                <!-- Toolbar albero -->
                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal"
                            Margin="12,12,12,8">
                    <Button Content="＋ Nuovo" Style="{StaticResource PrimaryBtn}"
                            Margin="0,0,8,0" Click="BtnNew_Click"/>
                    <Button Content="Aggiorna" Style="{StaticResource GhostBtn}"
                            Click="BtnRefresh_Click"/>
                </StackPanel>

                <!-- Ricerca -->
                <Border DockPanel.Dock="Top" Margin="12,0,12,8">
                    <Grid>
                        <TextBox x:Name="txtSearch"
                                 Style="{StaticResource ModernTextBox}"
                                 TextChanged="TxtSearch_TextChanged"/>
                        <TextBlock Text="Cerca..." IsHitTestVisible="False"
                                   Foreground="#9CA3AF" FontSize="13"
                                   VerticalAlignment="Center" Margin="12,0"
                                   Visibility="{Binding Text.Length, ElementName=txtSearch,
                                   Converter={StaticResource InverseBoolToVisibility}}"/>
                    </Grid>
                </Border>

                <!-- Status -->
                <TextBlock DockPanel.Dock="Bottom" x:Name="txtTreeStatus"
                           Text="0 elementi" FontSize="11" Foreground="#6B7280"
                           Margin="12,8"/>

                <!-- TreeView -->
                <TreeView x:Name="treeItems"
                          Style="{StaticResource TreeViewStyle}"
                          SelectedItemChanged="TreeItems_SelectedChanged"
                          Margin="0,0,0,0"/>
            </DockPanel>
        </Border>

        <!-- PANNELLO DESTRO: DETTAGLIO -->
        <Border Grid.Column="1">
            <DockPanel>
                <!-- Header dettaglio -->
                <Border DockPanel.Dock="Top" Background="White"
                        BorderBrush="#E4E7EC" BorderThickness="0,0,0,1"
                        Padding="20,12">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="txtTitle" Text="Seleziona un elemento"
                                   FontSize="20" FontWeight="Bold"
                                   Foreground="#111827"
                                   VerticalAlignment="Center"/>
                        <!-- Pulsanti azione dettaglio -->
                        <Button x:Name="btnEditDetail" Content="Modifica"
                                Style="{StaticResource FlatBtn}"
                                Margin="16,0,0,0" Visibility="Collapsed"
                                Click="BtnEditDetail_Click"/>
                    </StackPanel>
                </Border>

                <!-- Contenuto dettaglio (Tab o pannello singolo) -->
                <TabControl x:Name="tabDetail" Margin="16"
                            Visibility="Collapsed">
                    <TabItem Header="Dettagli">
                        <ScrollViewer VerticalScrollBarVisibility="Auto"
                                      Padding="16">
                            <!-- Form campi dettaglio qui -->
                            <StackPanel x:Name="pnlDetails"/>
                        </ScrollViewer>
                    </TabItem>
                    <TabItem Header="Documenti">
                        <StackPanel x:Name="pnlDocuments"/>
                    </TabItem>
                </TabControl>
            </DockPanel>
        </Border>
    </Grid>
</Page>
```

### Code-Behind — `[Feature]Page.xaml.cs`

```csharp
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class [Feature]Page : Page
{
    private List<[Entity]Dto> _allItems = new();

    public [Feature]Page()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtTreeStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/[entities]");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resp = JsonSerializer.Deserialize<ApiResponse<List<[Entity]Dto>>>(json, options);

            if (resp?.Success == true)
            {
                _allItems = resp.Data ?? new();
                BuildTree();
                txtTreeStatus.Text = $"{_allItems.Count} elementi";
            }
        }
        catch (Exception ex) { txtTreeStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void BuildTree()
    {
        treeItems.Items.Clear();
        // Raggruppa per anno o categoria
        var groups = _allItems.GroupBy(x => x.GroupKey);
        foreach (var group in groups.OrderByDescending(g => g.Key))
        {
            var node = new TreeViewItem
            {
                Header = group.Key,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111827"))
            };
            foreach (var item in group)
            {
                node.Items.Add(new TreeViewItem
                {
                    Header = item.DisplayName,
                    Tag = item,
                    FontSize = 13,
                    FontWeight = FontWeights.Normal
                });
            }
            node.IsExpanded = true;
            treeItems.Items.Add(node);
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string search = txtSearch.Text.Trim().ToLower();
        foreach (TreeViewItem group in treeItems.Items)
        {
            bool anyVisible = false;
            foreach (TreeViewItem child in group.Items)
            {
                bool match = string.IsNullOrEmpty(search) ||
                             child.Header.ToString()!.ToLower().Contains(search);
                child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) anyVisible = true;
            }
            group.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void TreeItems_SelectedChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (treeItems.SelectedItem is TreeViewItem tvi && tvi.Tag is [Entity]Dto item)
        {
            txtTitle.Text = item.DisplayName;
            tabDetail.Visibility = Visibility.Visible;
            btnEditDetail.Visibility = Visibility.Visible;
            LoadDetail(item);
        }
    }

    private void LoadDetail([Entity]Dto item)
    {
        // Popola i campi del pannello dettaglio
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new [Entity]Dialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEditDetail_Click(object sender, RoutedEventArgs e)
    {
        if (treeItems.SelectedItem is TreeViewItem tvi && tvi.Tag is [Entity]Dto item)
        {
            var dlg = new [Entity]Dialog(item) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await Load();
    }
}
```

---

## 3. Layout Dashboard (Card KPI + Tabelle)

Adatto per: panoramiche, riepiloghi, metriche rapide.
Esempio esistente: DashboardPage.

### XAML — `[Feature]DashboardPage.xaml`

```xml
<Page x:Class="ATEC.PM.Client.Views.[Feature]DashboardPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Background="#F7F8FA">

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel>
            <!-- TITOLO -->
            <TextBlock Text="Dashboard [Feature]" FontSize="20" FontWeight="Bold"
                       Foreground="#111827" Margin="0,0,0,20"/>

            <!-- KPI CARDS -->
            <UniformGrid Columns="4" Margin="0,0,0,20">
                <!-- Card singola — ripeti per ogni KPI -->
                <Border Background="White" BorderBrush="#E4E7EC"
                        BorderThickness="1" CornerRadius="6"
                        Padding="16" Margin="0,0,12,0">
                    <StackPanel>
                        <TextBlock Text="LABEL KPI"
                                   FontSize="11" FontWeight="SemiBold"
                                   Foreground="#6B7280"/>
                        <TextBlock x:Name="txtKpi1" Text="—"
                                   FontSize="28" FontWeight="Bold"
                                   Foreground="#111827" Margin="0,4,0,0"/>
                    </StackPanel>
                </Border>

                <!-- Altre card KPI... -->
            </UniformGrid>

            <!-- SEZIONE TABELLA -->
            <TextBlock Text="Dettaglio" FontSize="14" FontWeight="SemiBold"
                       Foreground="#111827" Margin="0,0,0,12"/>

            <Border Background="White" BorderBrush="#E4E7EC"
                    BorderThickness="1" CornerRadius="6" Padding="0">
                <DataGrid x:Name="dgSummary"
                          Style="{StaticResource ModernDataGrid}"
                          AutoGenerateColumns="False"
                          IsReadOnly="True"
                          MaxHeight="400">
                    <DataGrid.Columns>
                        <!-- Colonne senza filtro header -->
                        <DataGridTextColumn Header="Nome" Binding="{Binding Nome}"
                                            Width="*"
                                            HeaderStyle="{StaticResource ModernColumnHeader}"
                                            CellStyle="{StaticResource ModernCell}"/>
                        <!-- Altre colonne... -->
                    </DataGrid.Columns>
                </DataGrid>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Page>
```

### Code-Behind — `[Feature]DashboardPage.xaml.cs`

```csharp
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class [Feature]DashboardPage : Page
{
    private DispatcherTimer? _timer;

    public [Feature]DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await Load();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += async (_, _) => await Load();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer?.Stop();
    }

    private async Task Load()
    {
        try
        {
            // Carica KPI
            string json = await ApiClient.GetAsync("/api/[feature]/stats");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resp = JsonSerializer.Deserialize<ApiResponse<[Feature]StatsDto>>(json, options);

            if (resp?.Success == true && resp.Data != null)
            {
                txtKpi1.Text = resp.Data.Valore1.ToString("N0");
                // Aggiorna altri KPI...
            }

            // Carica tabella dettaglio
            string json2 = await ApiClient.GetAsync("/api/[feature]/summary");
            var resp2 = JsonSerializer.Deserialize<ApiResponse<List<[Feature]SummaryDto>>>(json2, options);
            if (resp2?.Success == true)
            {
                dgSummary.ItemsSource = resp2.Data;
            }
        }
        catch { /* silenzioso per auto-refresh */ }
    }
}
```

---

## 4. Layout Dialog (Form modale)

Adatto per: inserimento/modifica singola entità.
Esempio esistente: CustomerDialog.

### XAML — `[Entity]Dialog.xaml`

```xml
<Window x:Class="ATEC.PM.Client.Views.[Entity]Dialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Nuovo [Entity]"
        Width="500" SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        Background="#F7F8FA">

    <Border Margin="24" Background="White" BorderBrush="#E4E7EC"
            BorderThickness="1" CornerRadius="6" Padding="24">
        <StackPanel>
            <!-- CAMPO -->
            <TextBlock Text="Nome campo" FontSize="11" FontWeight="SemiBold"
                       Foreground="#6B7280" Margin="0,0,0,4"/>
            <TextBox x:Name="txtCampo1"
                     Style="{StaticResource ModernTextBox}"
                     Margin="0,0,0,16"/>

            <!-- Ripeti per ogni campo... -->

            <!-- PULSANTI -->
            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Right" Margin="0,24,0,0">
                <Button Content="Annulla" Style="{StaticResource FlatBtn}"
                        Width="100" Margin="0,0,8,0"
                        Click="BtnCancel_Click"/>
                <Button Content="Salva" Style="{StaticResource PrimaryBtn}"
                        Width="100"
                        Click="BtnSave_Click"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

### Code-Behind — `[Entity]Dialog.xaml.cs`

```csharp
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class [Entity]Dialog : Window
{
    private int? _editId;

    // Costruttore per nuovo
    public [Entity]Dialog()
    {
        InitializeComponent();
    }

    // Costruttore per modifica
    public [Entity]Dialog([Entity]Dto existing) : this()
    {
        _editId = existing.Id;
        Title = $"Modifica [Entity]";
        txtCampo1.Text = existing.Campo1;
        // Popola altri campi...
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validazione
        if (string.IsNullOrWhiteSpace(txtCampo1.Text))
        {
            MessageBox.Show("Compilare tutti i campi obbligatori.",
                "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dto = new [Entity]Dto
        {
            Campo1 = txtCampo1.Text.Trim(),
            // Altri campi...
        };

        try
        {
            string jsonBody = JsonSerializer.Serialize(dto);
            if (_editId.HasValue)
                await ApiClient.PutAsync($"/api/[entities]/{_editId}", jsonBody);
            else
                await ApiClient.PostAsync("/api/[entities]", jsonBody);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore nel salvataggio: {ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

---

## Note generali

- Ogni pagina deve avere `Background="#F7F8FA"` sul tag Page
- I pannelli/card usano `Background="White"` con `BorderBrush="#E4E7EC"` e `BorderThickness="1"`
- Non ridefinire stili già presenti in App.xaml (FlatBtn, PrimaryBtn, ModernDataGrid, ecc.)
- L'ApiClient è statico: `ApiClient.GetAsync("/api/...")` — non serve istanziarlo
- Le risposte API sono wrappate in `ApiResponse<T>` con proprietà `Success` e `Data`
- La serializzazione JSON usa sempre `PropertyNameCaseInsensitive = true`
- Per navigare da MainWindow, aggiungere un case nel `Nav_Click` switch:
  ```csharp
  case "[Feature]": PageContent.Navigate(new [Feature]Page()); break;
  ```
