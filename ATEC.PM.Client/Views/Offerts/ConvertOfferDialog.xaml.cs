using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class ConvertOfferDialog : Window
{
    public int SelectedPmId { get; private set; }

    public ConvertOfferDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadEmployees();
    }

    private async Task LoadEmployees()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/employees/pm-list");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Select(e => new
                    {
                        Id = e.GetProperty("id").GetInt32(),
                        Name = e.GetProperty("name").GetString() ?? ""
                    })
                    .OrderBy(x => x.Name)
                    .ToList();
                cmbPm.ItemsSource = items;
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (cmbPm.SelectedValue is not int pmId)
        {
            MessageBox.Show("Seleziona un Project Manager", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
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
}
