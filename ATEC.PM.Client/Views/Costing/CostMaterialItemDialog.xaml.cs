using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.UserControls;

public partial class CostMaterialItemDialog : Window
{
    private readonly int _projectId;
    private readonly int _sectionId;
    private bool _suppressAutocomplete;

    public CostMaterialItemDialog(int projectId, int sectionId)
    {
        InitializeComponent();
        _projectId = projectId;
        _sectionId = sectionId;
    }

    // ── AUTOCOMPLETE ────────────────────────────────────────────────

    private async void TxtDescription_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAutocomplete) return;
        string query = txtDescription.Text.Trim();
        if (query.Length < 2) { popSuggestions.IsOpen = false; return; }

        try
        {
            string json = await ApiClient.GetAsync(
                $"/api/projects/{_projectId}/costing/material-items/suggestions?q={Uri.EscapeDataString(query)}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            List<string> items = JsonSerializer.Deserialize<List<string>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (items.Count > 0)
            {
                lstSuggestions.ItemsSource = items;
                popSuggestions.IsOpen = true;
            }
            else
            {
                popSuggestions.IsOpen = false;
            }
        }
        catch { popSuggestions.IsOpen = false; }
    }

    private void TxtDescription_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && popSuggestions.IsOpen && lstSuggestions.Items.Count > 0)
        {
            lstSuggestions.SelectedIndex = 0;
            lstSuggestions.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popSuggestions.IsOpen = false;
        }
    }

    private void LstSuggestions_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && lstSuggestions.SelectedItem is string selected)
        {
            ApplySuggestion(selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popSuggestions.IsOpen = false;
            txtDescription.Focus();
        }
    }

    private void LstSuggestions_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (lstSuggestions.SelectedItem is string selected)
            ApplySuggestion(selected);
    }

    private void ApplySuggestion(string text)
    {
        _suppressAutocomplete = true;
        txtDescription.Text = text;
        txtDescription.CaretIndex = text.Length;
        popSuggestions.IsOpen = false;
        txtDescription.Focus();
        _suppressAutocomplete = false;
    }

    // ── SAVE / CANCEL ───────────────────────────────────────────────

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtDescription.Text))
        {
            txtError.Text = "Descrizione obbligatoria.";
            return;
        }

        decimal.TryParse(txtQuantity.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal qty);
        decimal.TryParse(txtUnitCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cost);

        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                sectionId = _sectionId,
                description = txtDescription.Text.Trim(),
                quantity = qty > 0 ? qty : 1,
                unitCost = cost,
                sortOrder = 0
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/material-items", json);
            var doc = System.Text.Json.JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
