using System.Windows;

namespace ATEC.PM.Client.Views.Commerciale.Preventivi;

public partial class MaterialRtfDialog : Window
{
    private readonly string _initialHtml;

    public string HtmlContent { get; private set; } = "";

    public MaterialRtfDialog(string productName, string? initialHtml)
    {
        InitializeComponent();
        txtTitle.Text = productName;
        _initialHtml = initialHtml ?? "";
        Loaded += OnDialogLoaded;
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        htmlEditor.SetContent(_initialHtml);
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
