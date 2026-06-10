using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.GammaRobot;

// Dialog create/edit anagrafica quadro (configurazione di un robot). Usato dall'editor Composizione (solo ADMIN).
public partial class GammaQuadroDialog : Window
{
    public GammaQuadroSaveRequest Result { get; private set; } = new();

    public GammaQuadroDialog(GammaQuadroSaveRequest? existing = null)
    {
        InitializeComponent();

        if (existing != null)
        {
            txtTitle.Text = "Modifica quadro";
            txtControllore.Text = existing.Controllore ?? "";
            txtGenerazione.Text = existing.Generazione ?? "";
            txtPayload.Text = existing.Payload ?? "";
            txtArea.Text = existing.AreaLavoro ?? "";
            txtOsVersion.Text = existing.OsVersion ?? "";
            txtSystemKey.Text = existing.SystemKey ?? "";
            txtNote.Text = existing.Note ?? "";
        }

        Loaded += (_, _) => { txtControllore.Focus(); txtControllore.SelectAll(); };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = new GammaQuadroSaveRequest
        {
            Controllore = Nullify(txtControllore.Text),
            Generazione = Nullify(txtGenerazione.Text),
            Payload = Nullify(txtPayload.Text),
            AreaLavoro = Nullify(txtArea.Text),
            OsVersion = Nullify(txtOsVersion.Text),
            SystemKey = Nullify(txtSystemKey.Text),
            Note = Nullify(txtNote.Text)
        };
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string? Nullify(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
