using System.Windows;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class AddMaterialVariantDialog : Window
{
    public string Description { get; private set; } = "";
    public decimal UnitCost { get; private set; }
    public decimal MarkupValue { get; private set; } = 1.300m;
    public decimal Quantity { get; private set; } = 1;

    public AddMaterialVariantDialog(string parentName)
    {
        InitializeComponent();
        txtHeader.Text = $"Nuova variante per: {parentName}";
        UpdatePreview();
        txtUnitCost.TextChanged += (_, _) => UpdatePreview();
        txtMarkup.TextChanged += (_, _) => UpdatePreview();
        txtQty.TextChanged += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        decimal.TryParse(txtUnitCost.Text, out decimal cost);
        decimal.TryParse(txtMarkup.Text, out decimal k);
        decimal.TryParse(txtQty.Text, out decimal qty);
        if (qty <= 0) qty = 1;
        decimal totalSale = qty * cost * (k > 0 ? k : 1);
        txtPreview.Text = $"Vendita: {totalSale:N2} \u20ac";
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtDescription.Text))
        {
            MessageBox.Show("La descrizione è obbligatoria.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Description = txtDescription.Text.Trim();
        decimal.TryParse(txtUnitCost.Text, out decimal c); UnitCost = c;
        decimal.TryParse(txtMarkup.Text, out decimal k); MarkupValue = k > 0 ? k : 1.300m;
        decimal.TryParse(txtQty.Text, out decimal q); Quantity = q > 0 ? q : 1;

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
