using System.Windows;

namespace ATEC.PM.Client.Views.Templates;

public partial class InputDialog : Window
{
    public string InputText => txtInput.Text.Trim();

    public InputDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        txtLabel.Text = label;
        txtInput.Text = defaultValue;
        Loaded += (_, _) =>
        {
            txtInput.Focus();
            txtInput.SelectAll();
        };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
