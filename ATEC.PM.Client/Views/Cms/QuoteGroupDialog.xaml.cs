using System;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteGroupDialog : Window
{
    private int? _editId;

    public QuoteGroupDialog()
    {
        InitializeComponent();
    }

    public QuoteGroupDialog(QuoteGroupDto existing) : this()
    {
        _editId = existing.Id;
        Title = "Modifica Gruppo";
        txtName.Text = existing.Name;
        txtDescription.Text = existing.Description;
        txtSortOrder.Text = existing.SortOrder.ToString();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Il nome è obbligatorio.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dto = new QuoteGroupSaveDto
        {
            Name = txtName.Text.Trim(),
            Description = txtDescription.Text.Trim(),
            SortOrder = int.TryParse(txtSortOrder.Text, out int so) ? so : 0,
            IsActive = true
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            if (_editId.HasValue)
                await ApiClient.PutAsync($"/api/quote-catalog/groups/{_editId}", body);
            else
                await ApiClient.PostAsync("/api/quote-catalog/groups", body);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
