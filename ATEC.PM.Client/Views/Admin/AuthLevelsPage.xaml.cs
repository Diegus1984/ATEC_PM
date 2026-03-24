using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        try
        {
            // Carica livelli
            string levelsJson = await ApiClient.GetAsync("/api/auth-levels");
            var levelsDoc = JsonDocument.Parse(levelsJson);
            if (levelsDoc.RootElement.GetProperty("success").GetBoolean())
            {
                LevelOptions.Clear();
                foreach (var l in levelsDoc.RootElement.GetProperty("data").EnumerateArray())
                {
                    LevelOptions.Add(new LevelOption
                    {
                        Value = l.GetProperty("levelValue").GetInt32(),
                        Name = l.GetProperty("displayName").GetString() ?? ""
                    });
                }
            }

            // Carica feature
            string json = await ApiClient.GetAsync("/api/auth-levels/features");
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("success").GetBoolean()) return;

            _rows.Clear();
            foreach (var f in root.GetProperty("data").EnumerateArray())
            {
                _rows.Add(new FeatureRowVM
                {
                    Id = f.GetProperty("id").GetInt32(),
                    FeatureKey = f.GetProperty("featureKey").GetString() ?? "",
                    DisplayName = f.GetProperty("displayName").GetString() ?? "",
                    Category = f.GetProperty("category").GetString() ?? "",
                    MinLevel = f.GetProperty("minLevel").GetInt32(),
                    Behavior = f.GetProperty("behavior").GetString() ?? "HIDDEN",
                    LevelOptions = LevelOptions
                });
            }

            dgFeatures.ItemsSource = null;
            dgFeatures.ItemsSource = _rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Errore salvataggio: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshGrid()
    {
        _loading = true;
        dgFeatures.ItemsSource = null;
        dgFeatures.ItemsSource = _rows;
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
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadAsync();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Errore");
        }
    }

    private async void BtnDeleteFeature_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not FeatureRowVM row) return;

        if (MessageBox.Show($"Eliminare la feature '{row.DisplayName}'?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/auth-levels/features/{row.Id}");
            _rows.Remove(row);
            RefreshGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Errore");
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

    /// <summary>Lista livelli per il ComboBox (condivisa da tutte le righe)</summary>
    public List<LevelOption> LevelOptions { get; set; } = new();

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
