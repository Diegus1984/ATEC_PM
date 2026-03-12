using System.Windows.Threading;

namespace ATEC.PM.Client.UserControls;

public partial class DaneaSyncBar : UserControl
{
    private DispatcherTimer? _syncTimer;

    /// Evento scatenato quando la sync è completata (per ricaricare i dati nella pagina padre)
    public event Action? SyncCompleted;

    public DaneaSyncBar()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSyncStatus();
        Unloaded += (_, _) => _syncTimer?.Stop();
    }

    private async Task LoadSyncStatus()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/danea-sync/status");
            var doc = JsonDocument.Parse(json);
            bool isSyncing = doc.RootElement.GetProperty("isSyncing").GetBoolean();
            string? lastSync = doc.RootElement.GetProperty("lastSync").GetString();
            string? lastError = doc.RootElement.GetProperty("lastError").GetString();
            string? progress = doc.RootElement.GetProperty("progress").GetString();
            int suppliers = doc.RootElement.GetProperty("suppliers").GetInt32();
            int customers = doc.RootElement.GetProperty("customers").GetInt32();
            int articles = doc.RootElement.GetProperty("articles").GetInt32();

            if (isSyncing)
            {
                txtSyncStatus.Text = $"⟳ {progress}";
                txtSyncStatus.Foreground = Brush("#F79009");
                btnSync.IsEnabled = false;
                StartPolling();
            }
            else
            {
                btnSync.IsEnabled = true;
                _syncTimer?.Stop();

                if (!string.IsNullOrEmpty(lastError))
                {
                    txtSyncStatus.Text = $"✗ Errore: {lastError}";
                    txtSyncStatus.Foreground = Brush("#F04438");
                }
                else if (!string.IsNullOrEmpty(lastSync))
                {
                    txtSyncStatus.Text = $"✓ Sync: {lastSync} — {suppliers} forn. / {customers} cli. / {articles} art.";
                    txtSyncStatus.Foreground = Brush("#12B76A");
                }
                else
                {
                    txtSyncStatus.Text = "Mai sincronizzato";
                    txtSyncStatus.Foreground = Brush("#667085");
                }
            }
        }
        catch { txtSyncStatus.Text = "Sync non disponibile"; }
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            btnSync.IsEnabled = false;
            txtSyncStatus.Text = "⟳ Avvio sincronizzazione...";
            txtSyncStatus.Foreground = Brush("#F79009");
            await ApiClient.PostAsync("/api/danea-sync/run", "{}");
            StartPolling();
        }
        catch (Exception ex)
        {
            txtSyncStatus.Text = $"Errore: {ex.Message}";
            txtSyncStatus.Foreground = Brush("#F04438");
            btnSync.IsEnabled = true;
        }
    }

    private void StartPolling()
    {
        if (_syncTimer != null) return;
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _syncTimer.Tick += async (_, _) =>
        {
            await LoadSyncStatus();
            if (btnSync.IsEnabled) // sync terminata
            {
                _syncTimer.Stop();
                _syncTimer = null;
                SyncCompleted?.Invoke();
            }
        };
        _syncTimer.Start();
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
