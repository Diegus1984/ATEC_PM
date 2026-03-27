using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared;

namespace ATEC.PM.Client.Views;

// ═══════════════════════════════════════════════════════════════
// Modello voce menu navigazione
// ═══════════════════════════════════════════════════════════════

public class NavMenuItem
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public ObservableCollection<NavMenuItem> SubItems { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// MainWindow
// ═══════════════════════════════════════════════════════════════

public partial class MainWindow : Window
{
    private DispatcherTimer? _badgeTimer;
    private bool _drawerOpen;
    private const double DrawerWidth = 280;
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(200));

    public ObservableCollection<NavMenuItem> NavItems { get; set; } = new();

    public MainWindow()
    {
        DataContext = this;
        NavItems = BuildNavMenu();
        InitializeComponent();
        BuildNavUI();

        txtUserName.Text = App.UserFullName;
        txtRole.Text = App.UserRole;
        string[] parts = (App.UserFullName ?? "").Split(' ');
        txtInitials.Text = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : (App.UserFullName ?? "AT").Substring(0, Math.Min(2, (App.UserFullName ?? "").Length)).ToUpper();

        StartBadgePolling();
    }

    // ══════════════════════════════════════════════════════════
    // COSTRUZIONE MENU CON PERMESSI
    // ══════════════════════════════════════════════════════════

    private ObservableCollection<NavMenuItem> BuildNavMenu()
    {
        ObservableCollection<NavMenuItem> menu = new();

        NavMenuItem principale = new() { Name = "PRINCIPALE" };
        principale.SubItems.Add(new() { Name = "Dashboard", Tag = "Dashboard" });
        AddIfAllowed(principale, "Commesse", "Commesse", "nav.commesse");
        AddIfAllowed(principale, "Cat. Preventivi", "CatalogoPreventivi", "nav.cat_preventivi");
        AddIfAllowed(principale, "Preventivi", "Preventivi", "nav.preventivi");
        AddIfAllowed(principale, "Timesheet", "Timesheet", "nav.timesheet");
        menu.Add(principale);

        NavMenuItem gestione = new() { Name = "GESTIONE" };
        AddIfAllowed(gestione, "Clienti", "Clienti", "nav.clienti");
        AddIfAllowed(gestione, "Fornitori", "Fornitori", "nav.fornitori");
        AddIfAllowed(gestione, "Catalogo Articoli", "Catalogo", "nav.catalogo");
        AddIfAllowed(gestione, "Codex Articoli", "Codex", "nav.codex");
        AddIfAllowed(gestione, "Composizione Codex", "CodexComposizione", "nav.codex_composizione");
        if (gestione.SubItems.Count > 0) menu.Add(gestione);

        NavMenuItem admin = new() { Name = "AMMINISTRAZIONE" };
        AddIfAllowed(admin, "Utenti", "Utenti", "nav.utenti");
        AddIfAllowed(admin, "Permessi", "Permessi", "nav.permessi");
        if (admin.SubItems.Count > 0) menu.Add(admin);

        NavMenuItem avanzata = new() { Name = "GESTIONE AVANZATA" };
        AddIfAllowed(avanzata, "Config. Sezioni", "ConfigurazioneSezioni", "nav.config_sezioni");
        AddIfAllowed(avanzata, "Destinazioni DDP", "DestinazioniDdp", "nav.ddp_destinazioni");
        AddIfAllowed(avanzata, "Backup DB", "Backup", "nav.backup");
        if (avanzata.SubItems.Count > 0) menu.Add(avanzata);

        NavMenuItem sessione = new() { Name = "SESSIONE" };
        sessione.SubItems.Add(new() { Name = "Logout", Tag = "Logout" });
        sessione.SubItems.Add(new() { Name = "Esci", Tag = "Exit" });
        menu.Add(sessione);

        return menu;
    }

    private static void AddIfAllowed(NavMenuItem group, string name, string tag, string featureKey)
    {
        if (PermissionEngine.CanAccess(featureKey))
            group.SubItems.Add(new NavMenuItem { Name = name, Tag = tag });
    }

    // ══════════════════════════════════════════════════════════
    // GENERAZIONE UI MENU
    // ══════════════════════════════════════════════════════════

    private void BuildNavUI()
    {
        navMenuPanel.Children.Clear();

        foreach (NavMenuItem group in NavItems)
        {
            Expander expander = new()
            {
                Header = group.Name,
                IsExpanded = group.Name != "GESTIONE AVANZATA" && group.Name != "SESSIONE",
                Style = (Style)FindResource("NavExpander")
            };

            StackPanel itemsPanel = new();
            foreach (NavMenuItem item in group.SubItems)
            {
                Button btn = new()
                {
                    Content = "  " + item.Name,
                    Tag = item.Tag,
                    Style = (Style)FindResource("NavBtn")
                };
                btn.Click += NavBtn_Click;
                itemsPanel.Children.Add(btn);
            }

            expander.Content = itemsPanel;
            navMenuPanel.Children.Add(expander);
        }
    }

    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string tag = btn.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(tag)) return;

        if (tag == "Logout") { DoLogout(); return; }
        if (tag == "Exit") { DoExit(); return; }

        NavigateToTag(tag);
    }

    // ══════════════════════════════════════════════════════════
    // DRAWER TOGGLE (push — anima larghezza colonna)
    // ══════════════════════════════════════════════════════════

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleDrawer();
    }

    private AnimationProxy? _animProxy;

    private void ToggleDrawer()
    {
        // Cancella animazione in corso
        _animProxy?.BeginAnimation(AnimationProxy.ValueProperty, null);

        double from = _drawerOpen ? DrawerWidth : 0;
        double to = _drawerOpen ? 0 : DrawerWidth;
        _drawerOpen = !_drawerOpen;

        DoubleAnimation anim = new()
        {
            From = from,
            To = to,
            Duration = AnimDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        anim.Completed += (_, _) => colDrawer.Width = new GridLength(to);

        _animProxy = new AnimationProxy { Value = from };
        _animProxy.ValueChanged += v => colDrawer.Width = new GridLength(v);
        _animProxy.BeginAnimation(AnimationProxy.ValueProperty, anim);
    }

    // ══════════════════════════════════════════════════════════
    // NAVIGAZIONE
    // ══════════════════════════════════════════════════════════

    private void NavigateToTag(string tag)
    {
        txtPageTitle.Text = tag;

        switch (tag)
        {
            case "Dashboard": PageContent.Navigate(new DashboardPage()); break;
            case "Commesse": PageContent.Navigate(new ProjectsPage()); break;
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
            case "Preventivi": PageContent.Navigate(new Preventivi.QuotesHomePage()); break;
            default: PageContent.Content = null; break;
        }
    }

    public void NavigateToProject(int projectId, string referenceType = "")
    {
        txtPageTitle.Text = "Commesse";

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

    // ══════════════════════════════════════════════════════════
    // BADGE NOTIFICHE
    // ══════════════════════════════════════════════════════════

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
        catch { /* silenzioso */ }
    }

    // ══════════════════════════════════════════════════════════
    // SESSIONE
    // ══════════════════════════════════════════════════════════

    private void DoLogout()
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

    private void DoExit()
    {
        if (MessageBox.Show("Vuoi uscire dall'applicazione?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }
}

// ═══════════════════════════════════════════════════════════════
// Helper: proxy per animare GridLength via DoubleAnimation
// ═══════════════════════════════════════════════════════════════

public class AnimationProxy : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(AnimationProxy),
            new PropertyMetadata(0.0, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public event Action<double>? ValueChanged;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimationProxy proxy)
            proxy.ValueChanged?.Invoke((double)e.NewValue);
    }
}
