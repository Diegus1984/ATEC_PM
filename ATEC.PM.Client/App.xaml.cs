using System.Windows;
using ATEC.PM.Client.Views;

namespace ATEC.PM.Client;

public partial class App : Application
{
    public static string ApiBaseUrl { get; set; } = "http://localhost:5100";
    public static string Token { get; set; } = "";
    public static string UserFullName { get; set; } = "";
    public static string UserRole { get; set; } = "";
    public static int UserId { get; set; }

    private void App_Startup(object sender, StartupEventArgs e)
    {
        new LoginWindow().Show();
    }
}
