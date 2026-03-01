using System.Windows;
using System.Windows.Controls;

namespace ATEC.PM.Client.Views;

public partial class MainWindow : Window
{
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
        var tag = ((Button)sender).Tag?.ToString() ?? "";
        txtPageTitle.Text = tag;

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
            default:
                PageContent.Content = null;
                break;
        }
    }
}