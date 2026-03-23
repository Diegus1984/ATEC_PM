using System.IO;
using System.Windows;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;
using Serilog;

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
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
            "Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXxcc3RTQ2ZeWU1xXURWYEo=");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ATEC_PM", "Logs", "client-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 15,
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("ATEC PM Client avviato — utente OS: {User}", Environment.UserName);

        new LoginWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("ATEC PM Client chiuso");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}