using System;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DdpDestinationDialog : Window
{
    private readonly int? _editId;
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    public DdpDestinationDialog(int? editId = null)
    {
        InitializeComponent();
        _editId = editId;
        if (_editId.HasValue)
        {
            Title = "Modifica Destinazione";
            btnDelete.Visibility = Visibility.Visible;
            Loaded += async (_, _) => await LoadItem();
        }
    }

    private async System.Threading.Tasks.Task LoadItem()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/ddp-destinations");
            var response = JsonSerializer.Deserialize<ApiResponse<System.Collections.Generic.List<DdpDestinationItem>>>(json, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                var item = response.Data.Find(d => d.Id == _editId);
                if (item != null)
                {
                    txtName.Text = item.Name;
                    txtSortOrder.Text = item.SortOrder.ToString();
                    chkActive.IsChecked = item.IsActive;
                }
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Inserisci un nome.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int sortOrder = int.TryParse(txtSortOrder.Text, out int s) ? s : 0;

        var req = new DdpDestinationSaveRequest
        {
            Name = name,
            SortOrder = sortOrder,
            IsActive = chkActive.IsChecked == true
        };

        try
        {
            string body = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (_editId.HasValue)
                await ApiClient.PutAsync($"/api/ddp-destinations/{_editId.Value}", body);
            else
                await ApiClient.PostAsync("/api/ddp-destinations", body);

            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editId == null) return;
        if (MessageBox.Show($"Eliminare la destinazione '{txtName.Text}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/ddp-destinations/{_editId.Value}");
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
