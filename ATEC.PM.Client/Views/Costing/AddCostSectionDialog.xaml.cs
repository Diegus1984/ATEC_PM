namespace ATEC.PM.Client.Views.Costing;

public partial class AddCostSectionDialog : Window
{
    private readonly int _projectId;
    private readonly string _groupName;
    private readonly string _apiBasePath;
    private List<CostSectionTemplateDto> _templates = new();
    private const string CUSTOM_TAG = "__CUSTOM__";

    /// <summary>
    /// groupName: il gruppo in cui aggiungere la sezione
    /// templates: lista template disponibili (non ancora nella commessa) per questo gruppo
    /// </summary>
    public AddCostSectionDialog(int projectId, string groupName, List<CostSectionTemplateDto> templates, string apiBasePath = "")
    {
        InitializeComponent();
        _projectId = projectId;
        _groupName = groupName;
        _apiBasePath = string.IsNullOrEmpty(apiBasePath) ? $"/api/projects/{projectId}/costing" : apiBasePath;
        _templates = templates;
        txtGroupName.Text = groupName;

        // Popola combo
        foreach (var tmpl in templates)
            cmbTemplate.Items.Add(new ComboBoxItem { Content = tmpl.Name, Tag = tmpl });

        cmbTemplate.Items.Add(new ComboBoxItem { Content = "✏ Personalizzata...", Tag = CUSTOM_TAG });

        if (cmbTemplate.Items.Count > 0)
            cmbTemplate.SelectedIndex = 0;
    }

    private void CmbTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbTemplate.SelectedItem is ComboBoxItem item)
        {
            bool isCustom = item.Tag is string s && s == CUSTOM_TAG;
            lblCustomName.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            txtCustomName.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            // Se da template, precompila il tipo
            if (item.Tag is CostSectionTemplateDto tmpl)
            {
                for (int i = 0; i < cmbType.Items.Count; i++)
                {
                    if (cmbType.Items[i] is ComboBoxItem typeItem && typeItem.Tag?.ToString() == tmpl.SectionType)
                    {
                        cmbType.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbTemplate.SelectedItem is not ComboBoxItem selected)
        {
            txtError.Text = "Seleziona una sezione.";
            return;
        }

        string sectionType = (cmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "IN_SEDE";
        int? templateId = null;
        string name;
        int sortOrder = 99;

        if (selected.Tag is CostSectionTemplateDto tmpl)
        {
            templateId = tmpl.Id;
            name = tmpl.Name;
            sectionType = tmpl.SectionType;
            sortOrder = tmpl.SortOrder;
        }
        else
        {
            // Personalizzata
            name = txtCustomName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                txtError.Text = "Nome sezione obbligatorio.";
                return;
            }
        }

        try
        {
            // Crea la sezione nella commessa
            var req = new
            {
                templateId,
                name,
                sectionType,
                groupName = _groupName,
                sortOrder,
                isEnabled = true
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync($"{_apiBasePath}/sections", json);
            JsonDocument doc = JsonDocument.Parse(result);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                int newSectionId = doc.RootElement.GetProperty("data").GetInt32();

                // Se da template, copia anche i reparti associati
                if (templateId.HasValue)
                {
                    var tmplData = _templates.FirstOrDefault(t => t.Id == templateId.Value);
                    if (tmplData != null)
                    {
                        foreach (int deptId in tmplData.DepartmentIds)
                        {
                            var deptReq = new { projectCostSectionId = newSectionId, departmentId = deptId };
                            string deptJson = JsonSerializer.Serialize(deptReq);
                            await ApiClient.PostAsync($"{_apiBasePath}/sections/{newSectionId}/departments", deptJson);
                        }
                    }
                }

                DialogResult = true;
                Close();
            }
            else
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
