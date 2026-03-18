using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

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

        ApplySidebarPermissions();
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

    private void ApplySidebarPermissions()
    {
        var u = App.CurrentUser;

        // Sezione GESTIONE
        btnClienti.Visibility = PermissionEngine.CanAccessClienti(u) ? Visibility.Visible : Visibility.Collapsed;
        btnFornitori.Visibility = PermissionEngine.CanAccessFornitori(u) ? Visibility.Visible : Visibility.Collapsed;
        btnCatalogo.Visibility = PermissionEngine.CanAccessCatalogo(u) ? Visibility.Visible : Visibility.Collapsed;
        btnCodex.Visibility = PermissionEngine.CanAccessCatalogo(u) ? Visibility.Visible : Visibility.Collapsed;
        btnBackup.Visibility = u.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        // Sezione GESTIONE AVANZATA
        btnFasiTemplate.Visibility = u.IsPm ? Visibility.Visible : Visibility.Collapsed;
        btnReparti.Visibility = u.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        lblAvanzata.Visibility = (btnFasiTemplate.Visibility == Visibility.Visible ||
                                  btnReparti.Visibility == Visibility.Visible ||
                                  btnMaterialCat.Visibility == Visibility.Visible ||
                                  btnCostSections.Visibility == Visibility.Visible ||
                                  btnDdpDest.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;

        // Sezione REPORT / ADMIN
        btnUtenti.Visibility = PermissionEngine.CanAccessUtenti(u) ? Visibility.Visible : Visibility.Collapsed;
        btnCostSections.Visibility = u.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        btnDdpDest.Visibility = (u.IsPm || u.IsResponsible) ? Visibility.Visible : Visibility.Collapsed;


        // Nascondi label sezione se tutti i bottoni sono collassati
        bool anyGestione = btnClienti.Visibility == Visibility.Visible ||
                   btnFornitori.Visibility == Visibility.Visible ||
                   btnCatalogo.Visibility == Visibility.Visible ||
                   btnCodex.Visibility == Visibility.Visible;
        lblGestione.Visibility = anyGestione ? Visibility.Visible : Visibility.Collapsed;

        bool anyAdmin = btnUtenti.Visibility == Visibility.Visible || btnBackup.Visibility == Visibility.Visible;
        lblAdmin.Visibility = anyAdmin ? Visibility.Visible : Visibility.Collapsed;
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
            case "FasiTemplate": PageContent.Navigate(new PhaseTemplatesPage()); break;
            case "Reparti": PageContent.Navigate(new DepartmentsPage()); break;
            case "SezioniCosto": PageContent.Navigate(new CostSectionsPage()); break;
            case "CategorieMateriali": PageContent.Navigate(new MaterialCategoriesPage()); break;
            case "Codex": PageContent.Navigate(new CodexPage()); break;
            case "DestinazioniDdp": PageContent.Navigate(new DdpDestinationsPage()); break;
            case "Backup": PageContent.Navigate(new BackupPage()); break;
            case "CatalogoPreventivi": PageContent.Navigate(new Quotes.QuoteCatalogPage()); break;
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
