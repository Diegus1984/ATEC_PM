using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.GammaRobot;

// Dialog create/edit anagrafica robot (modello). Usato dall'editor Composizione (solo ADMIN).
public partial class GammaRobotDialog : Window
{
    public GammaRobotSaveRequest Result { get; private set; } = new();

    public GammaRobotDialog(GammaRobotSaveRequest? existing = null)
    {
        InitializeComponent();

        if (existing != null)
        {
            txtTitle.Text = "Modifica robot";
            txtModello.Text = existing.Modello;
            txtSerie.Text = existing.Serie ?? "";
            txtBrand.Text = string.IsNullOrWhiteSpace(existing.Brand) ? "ABB" : existing.Brand;
            txtNote.Text = existing.Note ?? "";
        }

        Loaded += (_, _) => { txtModello.Focus(); txtModello.SelectAll(); };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string modello = txtModello.Text.Trim();
        if (string.IsNullOrWhiteSpace(modello))
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Il modello è obbligatorio.", "Robot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new GammaRobotSaveRequest
        {
            Modello = modello,
            Serie = Nullify(txtSerie.Text),
            Brand = string.IsNullOrWhiteSpace(txtBrand.Text) ? "ABB" : txtBrand.Text.Trim(),
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
