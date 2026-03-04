using System.Windows;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client;

public partial class App : Application
{
    public static string ApiBaseUrl { get; set; } = "http://localhost:5100";
    public static string Token { get; set; } = "";
    public static string UserFullName { get; set; } = "";
    public static string UserRole { get; set; } = "";
    public static int UserId { get; set; }

    // Contesto permessi — popolato dopo il login
    public static UserContext CurrentUser { get; set; } = new();

    public static void SetCurrentUser(int id, string role,
        IEnumerable<string> deptCodes,
        IEnumerable<string> respCodes,
        IEnumerable<string> compCodes)
    {
        CurrentUser = PermissionEngine.BuildContext(id, role, deptCodes, respCodes, compCodes);
    }

    private void App_Startup(object sender, StartupEventArgs e)
    {
        new LoginWindow().Show();
    }
}
