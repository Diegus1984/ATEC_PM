using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CodexGenerateDialog : Window
{
    public string GeneratedCode { get; private set; } = "";
    public int GeneratedId { get; private set; } = 0;

    private List<(string Codice, string Descrizione)> _prefixes = new();
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
            var response = JsonSerializer.Deserialize<ApiResponse<List<dynamic>>>(json, _jsonOpt);

            if (response?.Success == true && response.Data != null)
            {
                var prefixes = new List<(string, string)>();
                foreach (var item in response.Data)
                {
                    string codice = item.GetProperty("codice").GetString() ?? "";
                    string descrizione = item.GetProperty("descrizione").GetString() ?? "";
                    prefixes.Add((codice, descrizione));
                }

                _prefixes = prefixes;
                cmbPrefisso.ItemsSource = prefixes;
                cmbPrefisso.DisplayMemberPath = "Item2"; // Mostra descrizione
                cmbPrefisso.SelectedValuePath = "Item1";  // Valore è il codice
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore caricamento prefissi: {ex.Message}";
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (cmbPrefisso.SelectedValue == null || string.IsNullOrWhiteSpace(cmbPrefisso.SelectedValue.ToString()))
        {
            txtError.Text = "Seleziona un prefisso";
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
            string prefisso = cmbPrefisso.SelectedValue.ToString() ?? "";
            string descrizione = txtDescrizione.Text.Trim();

            var req = new CodexNewItemRequest
            {
                Prefisso = prefisso,
                Descrizione = descrizione
            };

            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/codex/generate", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<CodexGeneratedCode>>(result, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                GeneratedCode = response.Data.Codice;
                GeneratedId = response.Data.Id;
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = response?.Message ?? "Errore nella generazione";
                btnSave.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
            btnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
