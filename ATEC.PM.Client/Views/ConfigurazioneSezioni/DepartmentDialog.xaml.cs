using System.Globalization;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DepartmentDialog : Window
{
    private readonly DepartmentDto? _existing;

    public DepartmentDialog()
    {
        InitializeComponent();
        _existing = null;
        txtDialogTitle.Text = "Nuovo reparto";
    }

    public DepartmentDialog(DepartmentDto dept)
    {
        InitializeComponent();
        _existing = dept;
        txtDialogTitle.Text = $"Modifica reparto — {dept.Code}";
        txtCode.Text = dept.Code;
        txtName.Text = dept.Name;
        txtHourlyCost.Text = dept.HourlyCost.ToString("F2", CultureInfo.InvariantCulture);
        txtDefaultMarkup.Text = dept.DefaultMarkup.ToString("F3", CultureInfo.InvariantCulture);
        txtSortOrder.Text = dept.SortOrder.ToString();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";
        string code = txtCode.Text.Trim().ToUpper();
        string name = txtName.Text.Trim();

        if (string.IsNullOrEmpty(code))
        {
            txtError.Text = "Codice obbligatorio.";
            return;
        }
        if (string.IsNullOrEmpty(name))
        {
            txtError.Text = "Nome obbligatorio.";
            return;
        }
        if (!decimal.TryParse(txtHourlyCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hourlyCost))
        {
            txtError.Text = "Costo orario non valido.";
            return;
        }
        if (!decimal.TryParse(txtDefaultMarkup.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal defaultMarkup))
        {
            txtError.Text = "K ricarico non valido.";
            return;
        }
        if (!int.TryParse(txtSortOrder.Text, out int sortOrder))
            sortOrder = 0;

        DepartmentSaveRequest req = new()
        {
            Code = code,
            Name = name,
            HourlyCost = hourlyCost,
            DefaultMarkup = defaultMarkup,
            SortOrder = sortOrder,
            IsActive = true
        };

        try
        {
            string json = JsonSerializer.Serialize(req);
            string result = _existing == null
                ? await ApiClient.PostAsync("/api/departments", json)
                : await ApiClient.PutAsync($"/api/departments/{_existing.Id}", json);

            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = msg ?? "Errore";
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
