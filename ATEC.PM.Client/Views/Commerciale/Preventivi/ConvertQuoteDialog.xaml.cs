using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.Preventivi;

public partial class ConvertQuoteDialog : Window
{
    public int SelectedPmId { get; private set; }

    public ConvertQuoteDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadPmList();
    }

    private async Task LoadPmList()
    {
        try
        {
            List<LookupItem>? items = await ApiClient.GetDataAsync<List<LookupItem>>("/api/employees/pm-list");
            if (items != null)
            {
                cmbPm.ItemsSource = items
                    .OrderBy(x => x.Name)
                    .Select(x => new { Id = x.Id, Name = x.Name })
                    .ToList();
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (cmbPm.SelectedValue is not int pmId)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Seleziona un Project Manager", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedPmId = pmId;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Legge projectId dalla risposta POST /api/quotes/{id}/convert.</summary>
    public static bool TryParseConvertResponse(string json, out int projectId, out string message)
    {
        projectId = 0;
        message = "";
        if (!ApiClient.TryGetApiData<int>(json, out int id, out message))
            return false;
        projectId = id;
        return projectId > 0;
    }
}
