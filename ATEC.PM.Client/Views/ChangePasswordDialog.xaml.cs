using System;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly bool _forced;
    private readonly bool _fromLogin;
    private readonly string _username;
    private readonly string _currentPassword;
    private bool _allowClose;

    /// <param name="forced">True al primo accesso con password iniziale (n.cognome).</param>
    /// <param name="currentPassword">Password attuale (precompilata dalla login).</param>
    /// <param name="username">Username per cambio da schermata login.</param>
    /// <param name="fromLogin">True se aperto dalla finestra di login (senza sessione).</param>
    public ChangePasswordDialog(
        bool forced = false,
        string currentPassword = "",
        string username = "",
        bool fromLogin = false)
    {
        _forced = forced;
        _fromLogin = fromLogin;
        _username = username.Trim();
        _currentPassword = currentPassword;
        InitializeComponent();
        ConfigureMode();
    }

    private void ConfigureMode()
    {
        if (_forced)
        {
            Title = "Cambio password obbligatorio";
            txtTitle.Text = "Cambio password obbligatorio";
            txtSubtitle.Text =
                "Stai usando la password iniziale (n.cognome). Scegli una nuova password " +
                $"di almeno {InitialPasswordHelper.MinPasswordLength} caratteri, diversa da quella attuale.";
            panelCurrent.Visibility = Visibility.Collapsed;
            btnCancel.Visibility = Visibility.Collapsed;
        }
        else
        {
            txtSubtitle.Text =
                $"La nuova password deve avere almeno {InitialPasswordHelper.MinPasswordLength} caratteri " +
                "e non può coincidere con quella attuale né con la password iniziale (n.cognome).";
            if (!string.IsNullOrEmpty(_currentPassword))
                txtCurrent.Password = _currentPassword;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Visibility = Visibility.Collapsed;
        txtError.Text = "";

        string current = _forced ? _currentPassword : txtCurrent.Password;
        string newPassword = txtNew.Password;
        string confirm = txtConfirm.Password;

        if (!_forced && string.IsNullOrEmpty(current))
        {
            ShowError("Inserisci la password attuale.");
            return;
        }

        if (newPassword.Length < InitialPasswordHelper.MinPasswordLength)
        {
            ShowError($"La nuova password deve avere almeno {InitialPasswordHelper.MinPasswordLength} caratteri.");
            return;
        }

        if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
        {
            ShowError("Le due password non coincidono.");
            return;
        }

        if (string.Equals(newPassword, current, StringComparison.Ordinal))
        {
            ShowError("La nuova password deve essere diversa da quella attuale.");
            return;
        }

        btnSave.IsEnabled = false;
        btnSave.Content = "Salvataggio...";

        try
        {
            ChangePasswordRequest request = new ChangePasswordRequest
            {
                Username = _fromLogin ? _username : "",
                CurrentPassword = current,
                NewPassword = newPassword,
                ConfirmNewPassword = confirm
            };
            string body = JsonSerializer.Serialize(request);
            string json = _fromLogin
                ? await ApiClient.PostAnonymousAsync("/api/auth/change-password-login", body)
                : await ApiClient.PostAsync("/api/auth/change-password", body);
            if (!ApiClient.IsApiSuccess(json, out string msg))
            {
                ShowError(msg);
                return;
            }

            _allowClose = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            btnSave.IsEnabled = true;
            btnSave.Content = "Salva password";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = false;
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnSave_Click(sender, e);
    }

    private void ShowError(string message)
    {
        txtError.Text = message;
        txtError.Visibility = Visibility.Visible;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forced && !_allowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
