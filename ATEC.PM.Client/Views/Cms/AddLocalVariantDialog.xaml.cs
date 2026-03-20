using System.Windows;

namespace ATEC.PM.Client.Views.Quotes;

public partial class AddLocalVariantDialog : Window
{
    public string VariantName { get; private set; } = "";
    public string VariantCode { get; private set; } = "";
    public string VariantUnit { get; private set; } = "nr.";
    public decimal VariantPrice { get; private set; }
    public decimal VariantCost { get; private set; }
    public decimal VariantQty { get; private set; } = 1;

    public AddLocalVariantDialog(string productName)
    {
        InitializeComponent();
        DataContext = new { ProductName = $"Nuova variante per: {productName}" };
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Il nome è obbligatorio.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        VariantName = txtName.Text.Trim();
        VariantCode = txtCode.Text.Trim();
        VariantUnit = string.IsNullOrWhiteSpace(txtUnit.Text) ? "nr." : txtUnit.Text.Trim();
        decimal.TryParse(txtPrice.Text, out decimal p); VariantPrice = p;
        decimal.TryParse(txtCost.Text, out decimal c); VariantCost = c;
        decimal.TryParse(txtQty.Text, out decimal q); VariantQty = q > 0 ? q : 1;

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
