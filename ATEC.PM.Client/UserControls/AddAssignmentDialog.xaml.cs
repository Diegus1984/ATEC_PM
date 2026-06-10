using System.Globalization;
using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class AddAssignmentDialog : Window
{
    // ── Risultato ─────────────────────────────────────────────────────
    public int    SelectedEmployeeId { get; private set; }
    public string SelectedEmployeeName { get; private set; } = "";
    public decimal PlannedHours       { get; private set; }
    public string  AssignRole         { get; private set; } = "MEMBER";
    public bool    ApplyToAllPhases   { get; private set; }

    public AddAssignmentDialog(IEnumerable<LookupItem> employees, bool isSectionLevel = false)
    {
        InitializeComponent();
        
        // Esclude le risorse generiche/wildcard (nome tipo "[UTM] Generico"): in assegnazione
        // servono solo tecnici reali. Difesa lato dialog, oltre al filtro del chiamante.
        var sorted = employees.Where(e => !e.Name.StartsWith("["))
                              .OrderBy(e => e.Name)
                              .ToList();
                              
        cmbEmployee.ItemsSource = sorted;
        if (cmbEmployee.Items.Count > 0)
            cmbEmployee.SelectedIndex = 0;

        if (isSectionLevel)
        {
            chkAllPhases.IsChecked = true;
            chkAllPhases.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (cmbEmployee.SelectedItem is not LookupItem emp)
        {
            txtError.Text = "Selezionare un tecnico.";
            return;
        }

        if (!decimal.TryParse(txtHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hours) || hours <= 0)
        {
            txtError.Text = "Inserire ore pianificate > 0.";
            return;
        }

        SelectedEmployeeId   = emp.Id;
        SelectedEmployeeName = emp.Name;
        PlannedHours         = hours;
        AssignRole           = "MEMBER";
        ApplyToAllPhases     = chkAllPhases.IsChecked == true;

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
