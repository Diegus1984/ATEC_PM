using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteProductDialog : Window
{
    private int? _editProductId;
    private int _categoryId;
    private ObservableCollection<VariantRow> _variants = new();

    // Costruttore per NUOVO prodotto (da categoria selezionata)
    // Costruttore per NUOVO prodotto (da categoria selezionata)
    public QuoteProductDialog(int categoryId, bool isNew = true)
    {
        InitializeComponent();
        _categoryId = categoryId;
        dgVariants.ItemsSource = _variants;
        _variants.Add(new VariantRow());
    }

    // Costruttore per MODIFICA prodotto (carica da API)
    public QuoteProductDialog(QuoteProductDto existing)
    {
        InitializeComponent();
        dgVariants.ItemsSource = _variants;
        _editProductId = existing.Id;
        _categoryId = existing.CategoryId;
        Title = "Modifica Prodotto";
        Loaded += async (_, _) => await LoadProduct(existing.Id);
    }

    private async System.Threading.Tasks.Task LoadProduct(int productId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/quote-catalog/products/{productId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var product = JsonSerializer.Deserialize<QuoteProductDto>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (product == null) return;

                _categoryId = product.CategoryId;
                txtName.Text = product.Name;
                txtCode.Text = product.Code;
                htmlEditor.SetContent(product.DescriptionRtf ?? "");
                chkAutoInclude.IsChecked = product.AutoInclude;

                // Tipo
                cmbItemType.SelectedIndex = product.ItemType == "content" ? 1 : 0;

                // Varianti
                _variants.Clear();
                foreach (var v in product.Variants)
                {
                    _variants.Add(new VariantRow
                    {
                        Id = v.Id,
                        Code = v.Code,
                        Name = v.Name,
                        CostPrice = v.CostPrice,
                        MarkupValue = v.MarkupValue,
                        SortOrder = v.SortOrder
                    });
                }
                if (_variants.Count == 0)
                    _variants.Add(new VariantRow());
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddVariant_Click(object sender, RoutedEventArgs e)
    {
        _variants.Add(new VariantRow { SortOrder = _variants.Count });
    }

    private void BtnRemoveVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VariantRow row)
        {
            _variants.Remove(row);
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Il nome è obbligatorio.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string itemType = "product";
        if (cmbItemType.SelectedItem is ComboBoxItem cbi && cbi.Tag is string t)
            itemType = t;

        string htmlContent = "";
        try { htmlContent = await htmlEditor.GetContentAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore lettura editor: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var dto = new QuoteProductSaveDto
        {
            CategoryId = _categoryId,
            ItemType = itemType,
            Code = txtCode.Text.Trim(),
            Name = txtName.Text.Trim(),
            DescriptionRtf = htmlContent,
            AttachmentPath = "",
            AutoInclude = chkAutoInclude.IsChecked == true,
            SortOrder = 0,
            IsActive = true,
            Variants = _variants.Select((v, i) => new QuoteProductVariantSaveDto
            {
                Id = v.Id,
                Code = v.Code ?? "",
                Name = v.Name ?? "",
                CostPrice = v.CostPrice,
                MarkupValue = v.MarkupValue,
                SortOrder = i
            }).ToList()
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            string json;
            if (_editProductId.HasValue)
                json = await ApiClient.PutAsync($"/api/quote-catalog/products/{_editProductId}", body);
            else
                json = await ApiClient.PostAsync("/api/quote-catalog/products", body);

            // Verifica risposta server
            var doc = JsonDocument.Parse(json);
            bool success = doc.RootElement.TryGetProperty("success", out var sp) && sp.GetBoolean();

            if (!success)
            {
                string msg = doc.RootElement.TryGetProperty("message", out var mp) ? mp.GetString() ?? "Errore sconosciuto" : "Errore sconosciuto";
                MessageBox.Show(msg, "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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

// ── Variant row ViewModel ──

public class VariantRow : INotifyPropertyChanged
{
    public int Id { get; set; }

    private string _code = "";
    public string Code { get => _code; set { _code = value; Notify(); } }

    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }

    private decimal _costPrice;
    public decimal CostPrice { get => _costPrice; set { _costPrice = value; Notify(); Notify(nameof(SellPrice)); } }

    private decimal _markupValue = 1;
    public decimal MarkupValue { get => _markupValue; set { _markupValue = value; Notify(); Notify(nameof(SellPrice)); } }

    public decimal SellPrice => CostPrice * MarkupValue;

    public int SortOrder { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
