using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Admin;

public partial class AuthLevelsPage : Page
{
    private readonly List<FeatureRowVM> _rows = new();
    private bool _loading;

    /// <summary>Dizionario livello → nome (caricato dal DB)</summary>
    public List<LevelOption> LevelOptions { get; private set; } = new();

    public AuthLevelsPage()
    {
        DataContext = this;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        txtStatus.Text = "Caricamento...";
        try
        {
            // Carica livelli
            List<AuthLevelDto> levels = await ApiClient.GetListAsync<AuthLevelDto>("/api/auth-levels");
            LevelOptions.Clear();
            foreach (AuthLevelDto l in levels)
            {
                LevelOptions.Add(new LevelOption
                {
                    Value = l.LevelValue,
                    Name = AuthFeatureLabels.LevelDisplay(l.LevelValue, l.DisplayName)
                });
            }

            List<AuthFeatureDto> features = await ApiClient.GetListAsync<AuthFeatureDto>("/api/auth-levels/features");

            _rows.Clear();
            foreach (AuthFeatureDto f in features)
            {
                _rows.Add(new FeatureRowVM
                {
                    Id = f.Id,
                    FeatureKey = f.FeatureKey,
                    DisplayName = f.DisplayName,
                    Category = f.Category,
                    MinLevel = f.MinLevel,
                    Behavior = f.Behavior,
                    LevelOptions = LevelOptions,
                    BehaviorOptions = AuthFeatureLabels.BehaviorOptions.ToList()
                });
            }

            ApplyFilter();
            txtStatus.Text = BuildStatusText();
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
            ShadcnMessageBox.Show($"Errore caricamento: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void CboLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is not ComboBox cbo) return;
        if (cbo.DataContext is not FeatureRowVM row) return;
        if (cbo.SelectedValue is not int newLevel) return;
        if (row.MinLevel == newLevel) return; // nessun cambio reale

        row.MinLevel = newLevel;
        // Le checkmark si aggiornano automaticamente via INotifyPropertyChanged
        await SaveFeatureAsync(row);
    }

    private async void CboBehavior_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is not ComboBox cbo) return;
        if (cbo.DataContext is not FeatureRowVM row) return;
        if (cbo.SelectedValue is not string newBehavior) return;
        if (row.Behavior == newBehavior) return; // nessun cambio reale

        row.Behavior = newBehavior;
        await SaveFeatureAsync(row);
    }

    private static readonly JsonSerializerOptions _jopt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task SaveFeatureAsync(FeatureRowVM row)
    {
        try
        {
            var payload = new { MinLevel = row.MinLevel, Behavior = row.Behavior };
            await ApiClient.PutAsync($"/api/auth-levels/features/{row.Id}", JsonSerializer.Serialize(payload, _jopt));
        }
        catch (Exception ex)
        {
            ShadcnMessageBox.Show($"Errore salvataggio: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildStatusText()
    {
        string filter = txtSearch.Text.Trim();
        if (string.IsNullOrEmpty(filter))
            return $"{_rows.Count} funzioni";

        int visible = dgFeatures.ItemsSource is IEnumerable<FeatureRowVM> items ? items.Count() : _rows.Count;
        return $"{visible} di {_rows.Count} funzioni";
    }

    private void ApplyFilter()
    {
        string filter = txtSearch.Text.Trim().ToLower();
        List<FeatureRowVM> filtered = string.IsNullOrEmpty(filter)
            ? _rows
            : _rows.Where(r =>
                r.DisplayNameReadable.ToLower().Contains(filter) ||
                r.CategoryDisplay.ToLower().Contains(filter) ||
                r.FeatureKeyDisplay.ToLower().Contains(filter) ||
                r.FeatureKey.ToLower().Contains(filter)).ToList();
        dgFeatures.ItemsSource = filtered;
        txtStatus.Text = BuildStatusText();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void BtnRowOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void MenuItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is FeatureRowVM row)
        {
            dgFeatures.SelectedItem = row;
            await ExecuteDeleteAsync(row);
        }
    }

    private async Task ExecuteDeleteAsync(FeatureRowVM row)
    {
        if (ShadcnMessageBox.Show($"Eliminare la feature '{row.DisplayNameReadable}'?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/auth-levels/features/{row.Id}");
            _rows.Remove(row);
            RefreshGrid();
            txtStatus.Text = BuildStatusText();
        }
        catch (Exception ex)
        {
            ShadcnMessageBox.Show(ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshGrid()
    {
        _loading = true;
        dgFeatures.ItemsSource = null;
        ApplyFilter();
        // Ritarda il reset di _loading: WPF processa i binding in modo asincrono,
        // i ComboBox scatenano SelectionChanged DOPO il re-bind
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => _loading = false);
    }

    private async void BtnAddFeature_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddFeatureDialog(LevelOptions) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var payload = new
            {
                FeatureKey = dlg.FeatureKey,
                DisplayName = dlg.FeatureDisplayName,
                Category = dlg.FeatureCategory,
                MinLevel = dlg.FeatureMinLevel,
                Behavior = "HIDDEN"
            };
            string result = await ApiClient.PostAsync("/api/auth-levels/features", JsonSerializer.Serialize(payload, _jopt));
            if (ApiClient.IsApiSuccess(result, out string msg))
                await LoadAsync();
            else
                ShadcnMessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            ShadcnMessageBox.Show(ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

/// <summary>Opzione livello per ComboBox (dal DB)</summary>
public class LevelOption
{
    public int Value { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>ViewModel per riga nella DataGrid permessi</summary>
public class FeatureRowVM : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";

    public string CategoryDisplay => AuthFeatureLabels.CategoryDisplay(Category);
    public string FeatureKeyDisplay => AuthFeatureLabels.FeatureKeyDisplay(FeatureKey);
    public string DisplayNameReadable => AuthFeatureLabels.ImproveDisplayName(FeatureKey, DisplayName);

    public List<LevelOption> LevelOptions { get; set; } = new();
    public List<BehaviorOption> BehaviorOptions { get; set; } = new();

    private int _minLevel;
    public int MinLevel
    {
        get => _minLevel;
        set { _minLevel = value; OnPropertyChanged(nameof(MinLevel)); OnPropertyChanged(nameof(CheckTech)); OnPropertyChanged(nameof(CheckResp)); OnPropertyChanged(nameof(CheckPm)); OnPropertyChanged(nameof(CheckAdmin)); OnPropertyChanged(nameof(CheckDev)); }
    }

    private string _behavior = "HIDDEN";
    public string Behavior
    {
        get => _behavior;
        set { _behavior = value; OnPropertyChanged(nameof(Behavior)); }
    }

    // Checkmark calcolate: livello >= min_level → accesso
    public string CheckTech  => MinLevel <= 0 ? "\u2713" : "";
    public string CheckResp  => MinLevel <= 1 ? "\u2713" : "";
    public string CheckPm    => MinLevel <= 2 ? "\u2713" : "";
    public string CheckAdmin => MinLevel <= 3 ? "\u2713" : "";
    public string CheckDev   => MinLevel <= 4 ? "\u2713" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
