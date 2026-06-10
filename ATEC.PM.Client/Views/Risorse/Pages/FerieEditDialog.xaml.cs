using System;
using System.Windows;
using ATEC.PM.Client.Controls;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Form dedicato alla modifica delle SOLE date (+ descrizione) di un periodo di ferie.
public partial class FerieEditDialog : Window
{
    public DateTime NewStart { get; private set; }
    public DateTime NewEnd { get; private set; }
    public string? NewDescription { get; private set; }
    public bool DeleteRequested { get; private set; }

    public FerieEditDialog(ResAssignmentDto a)
    {
        InitializeComponent();
        txtResource.Text = $"{a.EmployeeName} · ferie / permesso";
        dpInizio.SelectedDate = a.DataInizio;
        dpFine.SelectedDate = a.DataFine;
        txtDescrizione.Text = a.Descrizione ?? "";
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (dpInizio.SelectedDate == null || dpFine.SelectedDate == null)
        {
            Warn("Indica data inizio e data fine.");
            return;
        }
        if (dpFine.SelectedDate.Value.Date < dpInizio.SelectedDate.Value.Date)
        {
            Warn("La data fine non può precedere la data inizio.");
            return;
        }
        NewStart = dpInizio.SelectedDate.Value.Date;
        NewEnd = dpFine.SelectedDate.Value.Date;
        NewDescription = string.IsNullOrWhiteSpace(txtDescrizione.Text) ? null : txtDescrizione.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ShadcnMessageBox.Show("Eliminare questo periodo di ferie?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        DeleteRequested = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void Warn(string msg) =>
        ShadcnMessageBox.Show(msg, "Ferie", MessageBoxButton.OK, MessageBoxImage.Warning);
}
