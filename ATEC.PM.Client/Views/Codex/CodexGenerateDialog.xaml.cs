using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CodexGenerateDialog : Window
{
    public string GeneratedCode { get; private set; } = "";
    public int GeneratedId { get; private set; } = 0;

    private int? _currentReservationId;
    private string _currentReservedCode = "";
    private bool _confirmed = false;
    private bool _closing = false;

    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    public CodexGenerateDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadPrefixes();
    }

    private async System.Threading.Tasks.Task LoadPrefixes()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/codex/prefixes");
            var response = JsonSerializer.Deserialize<ApiResponse<List<CodexPrefix>>>(json, _jsonOpt);

            if (response?.Success == true && response.Data != null)
            {
                cmbPrefisso.ItemsSource = response.Data;
                cmbPrefisso.DisplayMemberPath = "Display";
                cmbPrefisso.SelectedValuePath = "Codice";
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore caricamento prefissi: {ex.Message}";
        }
    }

    private async void CmbPrefisso_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_closing) return;
        txtError.Text = "";

        await ReleaseCurrentReservation();

        if (cmbPrefisso.SelectedValue == null)
        {
            brdPreview.Visibility = Visibility.Collapsed;
            btnSave.IsEnabled = false;
            return;
        }

        try
        {
            string prefisso = cmbPrefisso.SelectedValue.ToString() ?? "";
            var req = new CodexReserveRequest { Prefisso = prefisso };
            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/codex/reserve", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<CodexReservationResult>>(result, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                _currentReservationId = response.Data.ReservationId;
                _currentReservedCode = response.Data.Codice;
                txtPreviewCode.Text = _currentReservedCode;
                brdPreview.Visibility = Visibility.Visible;
                btnSave.IsEnabled = true;
            }
            else
            {
                txtError.Text = response?.Message ?? "Errore prenotazione";
                brdPreview.Visibility = Visibility.Collapsed;
                btnSave.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
            brdPreview.Visibility = Visibility.Collapsed;
            btnSave.IsEnabled = false;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (_currentReservationId == null)
        {
            txtError.Text = "Nessun codice prenotato";
            return;
        }

        if (string.IsNullOrWhiteSpace(txtDescrizione.Text))
        {
            txtError.Text = "Inserisci una descrizione";
            return;
        }

        btnSave.IsEnabled = false;
        try
        {
            var req = new CodexConfirmRequest
            {
                ReservationId = _currentReservationId.Value,
                Descrizione = txtDescrizione.Text.Trim()
            };

            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/codex/confirm", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<CodexGeneratedCode>>(result, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                GeneratedCode = response.Data.Codice;
                GeneratedId = response.Data.Id;
                _confirmed = true;
                _currentReservationId = null;
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = response?.Message ?? "Errore nella conferma";
                btnSave.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
            btnSave.IsEnabled = true;
        }
    }

    private async void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        btnCancel.IsEnabled = false;
        await ReleaseCurrentReservation();
        _confirmed = true;
        DialogResult = false;
        Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_confirmed || _closing) return;

        if (_currentReservationId != null)
        {
            e.Cancel = true;
            _closing = true;
            await ReleaseCurrentReservation();
            DialogResult = false;
            Close();
        }
    }

    private async System.Threading.Tasks.Task ReleaseCurrentReservation()
    {
        if (_currentReservationId == null) return;

        try
        {
            await ApiClient.PostAsync($"/api/codex/release/{_currentReservationId.Value}", "{}");
        }
        catch { }

        _currentReservationId = null;
        _currentReservedCode = "";
    }
}