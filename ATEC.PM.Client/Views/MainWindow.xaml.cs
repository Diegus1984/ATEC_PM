using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ATEC.PM.Client.Controls;
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
    private NotificationPollingService? _pollingService;
    private bool _drawerOpen;
    private const double DrawerWidth = 280;
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(200));

    /// <summary>Pagine menu riusate — evita ricreazione e reload API a ogni click.</summary>
    private readonly Dictionary<string, Page> _pageCache = new();
    private string? _currentNavTag;

    // Tag → bottone nav, per gestire stato attivo
    private readonly Dictionary<string, Button> _navButtons = new();
    // Tag → display name leggibile
    private readonly Dictionary<string, string> _tagToDisplayName = new();
    private Button? _activeNavBtn;

    public ObservableCollection<NavMenuItem> NavItems { get; set; } = new();

    public string UserFullName => App.UserFullName ?? "Utente";
    public string UserRole => App.UserRole ?? "Ruolo";
    public string UserInitials
    {
        get
        {
            string fullName = UserFullName;
            string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
                : fullName.Substring(0, Math.Min(2, fullName.Length)).ToUpper();
        }
    }

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

        Activated += (_, _) => _pollingService?.OnWindowActivated();
        Deactivated += (_, _) => _pollingService?.OnWindowDeactivated();
    }

    private ObservableCollection<NavMenuItem> BuildNavMenu()
    {
        ObservableCollection<NavMenuItem> menu = new();

        NavMenuItem principale = new() { Name = "PRINCIPALE" };
        AddIfAllowed(principale, "Dashboard", "Dashboard", "nav.dashboard");
        AddIfAllowed(principale, "Commesse", "Commesse", "nav.commesse");
        AddIfAllowed(principale, "Timesheet", "Timesheet", "nav.timesheet");
        AddIfAllowed(principale, "Risorse", "Risorse", "nav.risorse");
        menu.Add(principale);

        NavMenuItem gestionale = new() { Name = "PM" };
        AddIfAllowed(gestionale, "Verbali (MoM)", "MoM", "nav.mom");
        AddIfAllowed(gestionale, "Gestore DDP", "GestoreDdp", "nav.gestore_ddp");
        if (gestionale.SubItems.Count > 0) menu.Add(gestionale);

        NavMenuItem commerciale = new() { Name = "COMMERCIALE" };
        AddIfAllowed(commerciale, "Preventivi", "Preventivi", "nav.preventivi");
        AddIfAllowed(commerciale, "Cat. Preventivi", "CatalogoPreventivi", "nav.cat_preventivi");
        AddIfAllowed(commerciale, "Gamma Robot", "GammaRobot", "nav.gamma_robot");
        if (commerciale.SubItems.Count > 0) menu.Add(commerciale);

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
        AddIfAllowed(avanzata, "Conf. DDP", "DestinazioniDdp", "nav.ddp_destinazioni");
        AddIfAllowed(avanzata, "Aggregazioni DDP", "AggregazioniDdp", "nav.ddp_aggregazioni");
        AddIfAllowed(avanzata, "Backup DB", "Backup", "nav.backup");
        AddIfAllowed(avanzata, "Template Commesse", "TemplateCommesse", "nav.project_templates");
        if (avanzata.SubItems.Count > 0) menu.Add(avanzata);

        return menu;
    }

    private static void AddIfAllowed(NavMenuItem group, string name, string tag, string featureKey)
    {
        if (PermissionEngine.CanAccess(featureKey))
            group.SubItems.Add(new NavMenuItem { Name = name, Tag = tag });
    }

    private void BuildNavUI()
    {
        navMenuPanel.Children.Clear();
        _navButtons.Clear();
        _tagToDisplayName.Clear();

        foreach (NavMenuItem group in NavItems)
        {
            string iconChar = group.Name switch
            {
                "PRINCIPALE" => "\uE80F",       // Home
                "COMMERCIALE" => "\uE8CB",      // Receipt / vendite
                "GESTIONE" => "\uEAE7",         // Database
                "AMMINISTRAZIONE" => "\uE716",   // People
                "GESTIONE AVANZATA" => "\uE713", // Settings
                "SESSIONE" => "\uE77B",          // User/Contact
                _ => "\uE71B"                    // Folder
            };

            Expander expander = new()
            {
                Header = group.Name,
                Tag = iconChar,
                IsExpanded = group.Name != "GESTIONE AVANZATA" && group.Name != "SESSIONE",
                Style = (Style)FindResource("NavExpander")
            };

            StackPanel itemsPanel = new();
            foreach (NavMenuItem item in group.SubItems)
            {
                Button btn = new()
                {
                    Content = item.Name,
                    Tag = item.Tag,
                    Style = (Style)FindResource("NavBtn")
                };
                btn.Click += NavBtn_Click;
                itemsPanel.Children.Add(btn);

                if (item.Tag is not ("" or "Logout" or "Exit"))
                {
                    _navButtons[item.Tag] = btn;
                    _tagToDisplayName[item.Tag] = item.Name;
                }
            }

            expander.Content = itemsPanel;
            navMenuPanel.Children.Add(expander);
        }
    }

    private void SetActiveNav(string tag)
    {
        if (_activeNavBtn != null)
            _activeNavBtn.IsEnabled = true;

        if (_navButtons.TryGetValue(tag, out Button? btn))
        {
            btn.IsEnabled = false;
            _activeNavBtn = btn;
        }
        else
        {
            _activeNavBtn = null;
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

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleDrawer();
    }

    private AnimationProxy? _animProxy;

    private void ToggleDrawer()
    {
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

    private Page GetOrCreatePage(string cacheKey, Func<Page> factory)
    {
        if (!_pageCache.TryGetValue(cacheKey, out Page? page))
        {
            page = factory();
            _pageCache[cacheKey] = page;
        }
        return page;
    }

    private async void NavigateToTag(string tag)
    {
        if (_currentNavTag == "MoM" && tag != "MoM"
            && _pageCache.TryGetValue("MoM", out Page? leavingMom) && leavingMom is MoM.MoMPage momPageLeaving)
            await momPageLeaving.FlushSavesAsync();

        txtPageTitle.Text = _tagToDisplayName.TryGetValue(tag, out string? friendly) ? friendly : tag;
        SetActiveNav(tag);

        bool momWasCached = _pageCache.ContainsKey("MoM");
        Page? page = tag switch
        {
            "Dashboard" => GetOrCreatePage(tag, () => new DashboardPage()),
            "Commesse" => GetOrCreatePage(tag, () => new ProjectsPage()),
            "Timesheet" => GetOrCreatePage(tag, () => new TimesheetPage()),
            "Clienti" => GetOrCreatePage(tag, () => new CustomersPage()),
            "Fornitori" => GetOrCreatePage(tag, () => new SuppliersPage()),
            "Catalogo" => GetOrCreatePage(tag, () => new CatalogPage()),
            "Utenti" => GetOrCreatePage(tag, () => new UsersPage()),
            "ConfigurazioneSezioni" => GetOrCreatePage(tag, () => new CostSectionsTreePage()),
            "CategorieMateriali" => GetOrCreatePage(tag, () => new MaterialCategoriesPage()),
            "Codex" => GetOrCreatePage(tag, () => new CodexPage()),
            "CodexComposizione" => GetOrCreatePage(tag, () => new CodexCompositionPage()),
            "DestinazioniDdp" => GetOrCreatePage(tag, () => new DdpDestinationsPage()),
            "AggregazioniDdp" => GetOrCreatePage(tag, () => new AggregazioniDdpPage()),
            "Backup" => GetOrCreatePage(tag, () => new BackupPage()),
            "Permessi" => GetOrCreatePage(tag, () => new Admin.AuthLevelsPage()),
            "CatalogoPreventivi" => GetOrCreatePage(tag, () => new Commerciale.QuoteCatalog.QuoteCatalogPage()),
            "GammaRobot" => GetOrCreatePage(tag, () => new Commerciale.GammaRobot.GammaRobotPage()),
            "Preventivi" => GetOrCreatePage(tag, () => new Commerciale.Preventivi.QuotesHomePage()),
            "TemplateCommesse" => GetOrCreatePage(tag, () => new Templates.ProjectTemplatePage()),
            "MoM" => GetOrCreatePage(tag, () => new MoM.MoMPage()),
            "GestoreDdp" => GetOrCreatePage(tag, () => new GestoreDdp.GestoreDdpPage()),
            "Risorse" => GetOrCreatePage(tag, () => new Risorse.ResourcePlannerPage()),
            _ => null
        };

        if (page != null)
            PageContent.Navigate(page);
        else
            PageContent.Content = null;

        if (tag == "MoM" && page is MoM.MoMPage momPage && momWasCached)
            await momPage.RefreshFromServerAsync();

        _currentNavTag = tag;
    }

    public void NavigateToProject(int projectId, string referenceType = "", bool reloadTree = false)
    {
        txtPageTitle.Text = "Commesse";
        SetActiveNav("Commesse");

        string section = referenceType switch
        {
            "BOM" => "ddp_commercial",
            "PHASE" => "budget_vs_actual",
            "TIMESHEET" => "details",
            _ => "details"
        };

        ProjectsPage projectsPage = (ProjectsPage)GetOrCreatePage("Commesse", () => new ProjectsPage());
        PageContent.Navigate(projectsPage);
        if (reloadTree)
            _ = projectsPage.RefreshTreeAndNavigateToSection(projectId, section);
        else
            projectsPage.NavigateToSection(projectId, section);
    }

    private void StartBadgePolling()
    {
        _pollingService = new NotificationPollingService(FetchUnreadCount);
        _pollingService.BadgeUpdated += OnBadgeUpdated;
        _pollingService.Start();
    }

    private static async Task<int> FetchUnreadCount()
    {
        NotificationBadge? badge = await ApiClient.GetDataAsync<NotificationBadge>("/api/notifications/badge");
        return badge?.UnreadCount ?? 0;
    }

    private void OnBadgeUpdated(int count)
    {
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

    private void DoLogout()
    {
        if (ShadcnMessageBox.Show("Vuoi disconnetterti?", "Logout", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Token = "";
            App.UserFullName = "";
            App.UserRole = "";
            App.UserId = 0;
            App.CurrentUser = new();
            PermissionEngine.ClearFeatures();
            _pageCache.Clear();

            new LoginWindow().Show();
            Close();
        }
    }

    private void DoExit()
    {
        if (ShadcnMessageBox.Show("Vuoi uscire dall'applicazione?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }

    private void UserProfileBox_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            
            fe.ContextMenu.HorizontalOffset = 8; // Beautiful floating gap to the right of the sidebar
            
            // Self-unsubscribing Opened event to adjust position once measured
            RoutedEventHandler? openedHandler = null;
            openedHandler = (s, args) =>
            {
                if (s is ContextMenu menu)
                {
                    menu.Opened -= openedHandler;
                    double actualMenuHeight = menu.ActualHeight;
                    menu.VerticalOffset = fe.ActualHeight - actualMenuHeight;
                }
            };
            fe.ContextMenu.Opened += openedHandler;
            
            // Initial positioning with high-precision fallback height (236px)
            double menuHeightEstimate = fe.ContextMenu.ActualHeight;
            if (menuHeightEstimate <= 0) menuHeightEstimate = 236; // Exact default height based on components
            fe.ContextMenu.VerticalOffset = fe.ActualHeight - menuHeightEstimate;
            
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void LogoutMenu_Click(object sender, RoutedEventArgs e)
    {
        DoLogout();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        DoExit();
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
