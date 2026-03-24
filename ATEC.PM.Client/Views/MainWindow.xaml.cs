using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared;

namespace ATEC.PM.Client.Views;

public partial class MainWindow : Window
{
    private Button? _activeNavButton;
    private DispatcherTimer? _badgeTimer;

    public MainWindow()
    {
        InitializeComponent();
        txtUserName.Text = App.UserFullName;
        txtRole.Text = App.UserRole;
        string[] parts = (App.UserFullName ?? "").Split(' ');
        txtInitials.Text = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : (App.UserFullName ?? "AT").Substring(0, Math.Min(2, (App.UserFullName ?? "").Length)).ToUpper();

        // I permessi sono applicati direttamente nel XAML via auth:Auth.Feature
        StartBadgePolling();
    }

    private void StartBadgePolling()
    {
        _ = UpdateBadge();
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _badgeTimer.Tick += async (_, _) => await UpdateBadge();
        _badgeTimer.Start();
    }

    private async Task UpdateBadge()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/notifications/badge");
            var response = JsonSerializer.Deserialize<ApiResponse<NotificationBadge>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response?.Success == true && response.Data != null)
            {
                int count = response.Data.UnreadCount;
                if (count > 0)
                {
                    txtBadgeCount.Text = count > 99 ? "99+" : count.ToString();
                    badgeNotif.Visibility = Visibility.Visible;
                }
                else
                {
                    badgeNotif.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch { /* silenzioso — il server potrebbe non essere raggiungibile */ }
    }

    public void NavigateToProject(int projectId, string referenceType = "")
    {
        txtPageTitle.Text = "Commesse";

        if (_activeNavButton != null)
            _activeNavButton.IsEnabled = true;
        _activeNavButton = null;

        string section = referenceType switch
        {
            "BOM" => "ddp_commercial",
            "PHASE" => "phases",
            "TIMESHEET" => "details",
            _ => "details"
        };

        var projectsPage = new ProjectsPage();
        PageContent.Navigate(projectsPage);
        projectsPage.NavigateToSection(projectId, section);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        Button btn = (Button)sender;
        string tag = btn.Tag?.ToString() ?? "";
        txtPageTitle.Text = tag;

        if (_activeNavButton != null)
            _activeNavButton.IsEnabled = true;

        btn.IsEnabled = false;
        _activeNavButton = btn;

        switch (tag)
        {
            case "Dashboard": PageContent.Navigate(new DashboardPage()); break;
            case "Commesse": PageContent.Navigate(new ProjectsPage()); break;
            case "Offerte": PageContent.Navigate(new OffersPage()); break;
            case "Timesheet": PageContent.Navigate(new TimesheetPage()); break;
            case "Clienti": PageContent.Navigate(new CustomersPage()); break;
            case "Fornitori": PageContent.Navigate(new SuppliersPage()); break;
            case "Catalogo": PageContent.Navigate(new CatalogPage()); break;
            case "Utenti": PageContent.Navigate(new UsersPage()); break;
            case "ConfigurazioneSezioni": PageContent.Navigate(new CostSectionsTreePage()); break;
            case "CategorieMateriali": PageContent.Navigate(new MaterialCategoriesPage()); break;
            case "Codex": PageContent.Navigate(new CodexPage()); break;
            case "CodexComposizione": PageContent.Navigate(new CodexCompositionPage()); break;
            case "DestinazioniDdp": PageContent.Navigate(new DdpDestinationsPage()); break;
            case "Backup": PageContent.Navigate(new BackupPage()); break;
            case "Permessi": PageContent.Navigate(new Admin.AuthLevelsPage()); break;
            case "CatalogoPreventivi": PageContent.Navigate(new Quotes.QuoteCatalogPage()); break;
            case "Preventivi": PageContent.Navigate(new Quotes.QuotesListPage()); break;
            case "PreventiviUnificati": PageContent.Navigate(new Preventivi.PreventiviPage()); break;
            default: PageContent.Content = null; break;
        }
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Vuoi disconnetterti?", "Logout", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Token = "";
            App.UserFullName = "";
            App.UserRole = "";
            App.UserId = 0;
            App.CurrentUser = new();
            PermissionEngine.ClearFeatures();

            new LoginWindow().Show();
            Close();
        }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Vuoi uscire dall'applicazione?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }
}
