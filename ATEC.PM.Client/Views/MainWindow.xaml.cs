using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Shared;

namespace ATEC.PM.Client.Views;

public partial class MainWindow : Window
{
    private Button? _activeNavButton;

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
    }

    private void ApplySidebarPermissions()
    {
        var u = App.CurrentUser;

        // Sezione GESTIONE
        btnClienti.Visibility     = PermissionEngine.CanAccessClienti(u)      ? Visibility.Visible : Visibility.Collapsed;
        btnFornitori.Visibility   = PermissionEngine.CanAccessFornitori(u)    ? Visibility.Visible : Visibility.Collapsed;
        btnCatalogo.Visibility    = PermissionEngine.CanAccessCatalogo(u)     ? Visibility.Visible : Visibility.Collapsed;

        // Sezione REPORT / ADMIN
        btnReport.Visibility      = PermissionEngine.CanAccessReport(u)       ? Visibility.Visible : Visibility.Collapsed;
        btnImpostazioni.Visibility = PermissionEngine.CanAccessImpostazioni(u) ? Visibility.Visible : Visibility.Collapsed;
        btnUtenti.Visibility      = PermissionEngine.CanAccessUtenti(u)       ? Visibility.Visible : Visibility.Collapsed;

        // Nascondi label sezione se tutti i bottoni sono collassati
        bool anyGestione = btnClienti.Visibility    == Visibility.Visible ||
                           btnFornitori.Visibility  == Visibility.Visible ||
                           btnCatalogo.Visibility   == Visibility.Visible;
        lblGestione.Visibility = anyGestione ? Visibility.Visible : Visibility.Collapsed;

        bool anyAdmin = btnReport.Visibility      == Visibility.Visible ||
                        btnImpostazioni.Visibility == Visibility.Visible ||
                        btnUtenti.Visibility       == Visibility.Visible;
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
            case "Dashboard":     PageContent.Navigate(new DashboardPage()); break;
            case "Commesse":      PageContent.Navigate(new ProjectsPage()); break;
            case "Timesheet":     PageContent.Navigate(new TimesheetPage()); break;
            case "Clienti":       PageContent.Navigate(new CustomersPage()); break;
            case "Fornitori":     PageContent.Navigate(new SuppliersPage()); break;
            case "Catalogo":      PageContent.Navigate(new CatalogPage()); break;
            case "Utenti":        PageContent.Navigate(new UsersPage()); break;
            default:              PageContent.Content = null; break;
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
