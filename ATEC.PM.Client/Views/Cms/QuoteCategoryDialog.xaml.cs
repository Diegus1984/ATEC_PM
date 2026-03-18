using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteCategoryDialog : Window
{
    private int? _editId;

    public QuoteCategoryDialog(List<QuoteGroupDto> groups, int? preselectedGroupId = null)
    {
        InitializeComponent();
        cmbGroup.ItemsSource = groups;
        if (preselectedGroupId.HasValue)
            cmbGroup.SelectedValue = preselectedGroupId.Value;
    }

    public QuoteCategoryDialog(List<QuoteGroupDto> groups, QuoteCategoryDto existing)
        : this(groups, existing.GroupId)
    {
        _editId = existing.Id;
        Title = "Modifica Categoria";
        txtName.Text = existing.Name;
        txtDescription.Text = existing.Description;
        txtSortOrder.Text = existing.SortOrder.ToString();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbGroup.SelectedValue is not int groupId)
        {
            MessageBox.Show("Seleziona un gruppo.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Il nome è obbligatorio.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dto = new QuoteCategorySaveDto
        {
            GroupId = groupId,
            Name = txtName.Text.Trim(),
            Description = txtDescription.Text.Trim(),
            SortOrder = int.TryParse(txtSortOrder.Text, out int so) ? so : 0,
            IsActive = true
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            if (_editId.HasValue)
                await ApiClient.PutAsync($"/api/quote-catalog/categories/{_editId}", body);
            else
                await ApiClient.PostAsync("/api/quote-catalog/categories", body);

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
