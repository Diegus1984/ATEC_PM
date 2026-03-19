using System.Windows;

namespace ATEC.PM.Client.Views;

public partial class QuantityDialog : Window
{
    public int Quantity { get; private set; } = 1;

    public QuantityDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            txtQuantity.Focus();
            txtQuantity.SelectAll();
        };
        txtQuantity.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                BtnOk_Click(this, new RoutedEventArgs());
            else if (e.Key == System.Windows.Input.Key.Escape)
                BtnCancel_Click(this, new RoutedEventArgs());
        };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(txtQuantity.Text.Trim(), out int qty) && qty >= 1)
        {
            Quantity = qty;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Inserire un numero intero >= 1", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
