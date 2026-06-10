using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Verifica che l'utente loggato sia ancora attivo; in caso contrario forza il re-login.
/// </summary>
public static class SessionGuard
{
    private static int _loggingOut;

    /// <returns>False se l'utente è stato disconnesso.</returns>
    public static async Task<bool> EnsureActiveOrLogoutAsync()
    {
        if (string.IsNullOrEmpty(App.Token))
            return false;

        SessionStatusDto? status;
        try
        {
            status = await ApiClient.GetDataAsync<SessionStatusDto>("/api/auth/session");
        }
        catch
        {
            return true;
        }

        if (status == null || status.IsActive)
            return true;

        ForceLogout("Il tuo account è stato disattivato. Contatta l'amministratore.");
        return false;
    }

    public static void ForceLogout(string message)
    {
        if (Interlocked.CompareExchange(ref _loggingOut, 1, 0) != 0)
            return;

        Dispatcher dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            ShowLogoutUi(message);
        else
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ShowLogoutUi(message)));
    }

    private static void ShowLogoutUi(string message)
    {
        try
        {
            App.Token = "";
            App.UserFullName = "";
            App.UserRole = "";
            App.UserId = 0;
            App.CurrentUser = new UserContext();
            PermissionEngine.ClearFeatures();

            new LoginWindow().Show();
            foreach (Window w in Application.Current.Windows)
            {
                if (w is not LoginWindow)
                    w.Close();
            }

            ShadcnMessageBox.Show(message, "Accesso terminato", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref _loggingOut, 0);
        }
    }
}
