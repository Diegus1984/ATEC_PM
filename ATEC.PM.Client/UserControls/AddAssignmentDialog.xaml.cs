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

    public AddAssignmentDialog(IEnumerable<LookupItem> employees)
    {
        InitializeComponent();
        cmbEmployee.ItemsSource = employees.ToList();
        if (cmbEmployee.Items.Count > 0)
            cmbEmployee.SelectedIndex = 0;
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

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
