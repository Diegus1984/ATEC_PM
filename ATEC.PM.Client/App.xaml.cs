using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared.DTOs;
using Serilog;

namespace ATEC.PM.Client;

public partial class App : Application
{
    public static string ApiBaseUrl
    {
        get => AppSession.Instance.ApiBaseUrl;
        set => AppSession.Instance.ApiBaseUrl = value;
    }

    public static string Token
    {
        get => AppSession.Instance.Token;
        set => AppSession.Instance.Token = value;
    }

    public static string UserFullName
    {
        get => AppSession.Instance.UserFullName;
        set => AppSession.Instance.UserFullName = value;
    }

    public static string UserRole
    {
        get => AppSession.Instance.UserRole;
        set => AppSession.Instance.UserRole = value;
    }

    public static int UserId
    {
        get => AppSession.Instance.UserId;
        set => AppSession.Instance.UserId = value;
    }

    public static UserContext CurrentUser
    {
        get => AppSession.Instance.CurrentUser;
        set => AppSession.Instance.CurrentUser = value;
    }

    public static void SetCurrentUser(int id, string role,
        IEnumerable<string> deptCodes,
        IEnumerable<string> respCodes,
        IEnumerable<string> compCodes)
    {
        AppSession.Instance.SetCurrentUser(id, role, deptCodes, respCodes, compCodes);
    }

    private void App_Startup(object sender, StartupEventArgs e)
    {
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
        Log.Information("Log file cartella: {LogDir}", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATEC_PM", "Logs"));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        new LoginWindow().Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Eccezione non gestita sul thread UI");
        MessageBox.Show(
            $"Errore imprevisto (UI):\n{e.Exception.Message}\n\nDettaglio in:\n{GetLogHint()}",
            "ATEC PM — Debug crash",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Eccezione non gestita AppDomain (terminating={IsTerminating})", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Task exception non osservata");
        e.SetObserved();
    }

    private static string GetLogHint()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATEC_PM", "Logs");

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("ATEC PM Client chiuso");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}