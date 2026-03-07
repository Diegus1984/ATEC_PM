namespace ATEC.PM.Client.Views;

public partial class DepartmentDialog : Window
{
    private readonly DepartmentDto? _existing;

    // Nuovo reparto
    public DepartmentDialog(List<MarkupCoefficientDto> markups)
    {
        InitializeComponent();
        _existing = null;
        txtDialogTitle.Text = "Nuovo Reparto";
        PopulateMarkupCombo(markups, "");
    }

    // Modifica reparto esistente
    public DepartmentDialog(DepartmentDto dept, List<MarkupCoefficientDto> markups)
    {
        InitializeComponent();
        _existing = dept;
        txtDialogTitle.Text = $"Modifica Reparto — {dept.Code}";
        txtCode.Text = dept.Code;
        txtName.Text = dept.Name;
        txtHourlyCost.Text = dept.HourlyCost.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        txtSortOrder.Text = dept.SortOrder.ToString();
        PopulateMarkupCombo(markups, dept.MarkupCode);
    }

    private void PopulateMarkupCombo(List<MarkupCoefficientDto> markups, string currentCode)
    {
        cmbMarkup.Items.Add(new ComboBoxItem { Content = "— nessuno —", Tag = "" });
        int selectedIdx = 0;
        int idx = 1;
        foreach (MarkupCoefficientDto mk in markups.Where(m => m.CoefficientType == "RESOURCE").OrderBy(m => m.SortOrder))
        {
            cmbMarkup.Items.Add(new ComboBoxItem { Content = $"{mk.Code} — {mk.Description}", Tag = mk.Code });
            if (mk.Code == currentCode) selectedIdx = idx;
            idx++;
        }
        cmbMarkup.SelectedIndex = selectedIdx;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string code = txtCode.Text.Trim().ToUpper();
        string name = txtName.Text.Trim();

        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("Codice obbligatorio.", "Attenzione");
            return;
        }
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Nome obbligatorio.", "Attenzione");
            return;
        }
        if (!decimal.TryParse(txtHourlyCost.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out decimal hourlyCost))
        {
            MessageBox.Show("Costo orario non valido.", "Attenzione");
            return;
        }
        if (!int.TryParse(txtSortOrder.Text, out int sortOrder))
            sortOrder = 0;

        string markupCode = (cmbMarkup.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        DepartmentSaveRequest req = new()
        {
            Code = code,
            Name = name,
            HourlyCost = hourlyCost,
            MarkupCode = markupCode,
            SortOrder = sortOrder,
            IsActive = true
        };

        try
        {
            string json = JsonSerializer.Serialize(req);
            string result;

            if (_existing == null)
                result = await ApiClient.PostAsync("/api/departments", json);
            else
                result = await ApiClient.PutAsync($"/api/departments/{_existing.Id}", json);

            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                DialogResult = true;
                Close();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Errore");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore");
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
