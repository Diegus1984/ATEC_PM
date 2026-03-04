using System.Windows;
using System.Windows.Controls;

namespace ATEC.PM.Client.Views;

public partial class MainWindow : Window
{
    private Button? _activeNavButton;

    public MainWindow()
    {
        InitializeComponent();
        txtUserName.Text = App.UserFullName;
        txtRole.Text = App.UserRole;
        var parts = (App.UserFullName ?? "").Split(' ');
        txtInitials.Text = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : (App.UserFullName ?? "AT").Substring(0, Math.Min(2, (App.UserFullName ?? "").Length)).ToUpper();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        var tag = btn.Tag?.ToString() ?? "";
        txtPageTitle.Text = tag;

        // Reset precedente
        if (_activeNavButton != null)
            _activeNavButton.IsEnabled = true;

        // Segna attivo
        btn.IsEnabled = false;
        _activeNavButton = btn;

        switch (tag)
        {
            case "Dashboard":
                PageContent.Navigate(new DashboardPage());
                break;
            case "Commesse":
                PageContent.Navigate(new ProjectsPage());
                break;
            case "Timesheet":
                PageContent.Navigate(new TimesheetPage());
                break;
            case "Dipendenti":
                PageContent.Navigate(new EmployeesPage());
                break;
            case "Clienti":
                PageContent.Navigate(new CustomersPage());
                break;
            case "Fornitori":
                PageContent.Navigate(new SuppliersPage());
                break;
            case "Catalogo":
                PageContent.Navigate(new CatalogPage());
                break;
            default:
                PageContent.Content = null;
                break;
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

            new LoginWindow().Show();
            Close();
        }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Vuoi uscire dall'applicazione?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }
}