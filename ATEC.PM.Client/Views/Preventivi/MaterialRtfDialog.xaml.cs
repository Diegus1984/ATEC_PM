using System.Windows;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class MaterialRtfDialog : Window
{
    public string HtmlContent { get; private set; } = "";

    public MaterialRtfDialog(string productName, string? initialHtml)
    {
        InitializeComponent();
        txtTitle.Text = productName;
        htmlEditor.SetContent(initialHtml ?? "");
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HtmlContent = await htmlEditor.GetContentAsync();
        }
        catch
        {
            HtmlContent = "";
        }
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
