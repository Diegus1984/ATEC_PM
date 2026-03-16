namespace ATEC.PM.Client.Views.Costing;

public partial class AddCostGroupDialog : Window
{
    private readonly int _projectId;
    private readonly string _apiBasePath;
    private List<CostSectionGroupDto> _availableGroups = new();
    private List<CostSectionTemplateDto> _availableTemplates = new();
    private const string CUSTOM_TAG = "__CUSTOM__";

    public string? SelectedGroupName { get; private set; }

    public AddCostGroupDialog(int projectId, List<CostSectionGroupDto> availableGroups, List<CostSectionTemplateDto> availableTemplates, string apiBasePath = "")
    {
        InitializeComponent();
        _projectId = projectId;
        _apiBasePath = string.IsNullOrEmpty(apiBasePath) ? $"/api/projects/{projectId}/costing" : apiBasePath;
        _availableGroups = availableGroups;
        _availableTemplates = availableTemplates;

        foreach (var grp in availableGroups)
        {
            int templateCount = availableTemplates.Count(t => t.GroupId == grp.Id);
            cmbGroup.Items.Add(new ComboBoxItem { Content = $"{grp.Name} ({templateCount} sezioni)", Tag = grp });
        }

        cmbGroup.Items.Add(new ComboBoxItem { Content = "✏ Nuovo gruppo...", Tag = CUSTOM_TAG });

        if (cmbGroup.Items.Count > 0)
            cmbGroup.SelectedIndex = 0;
    }

    private void CmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbGroup.SelectedItem is ComboBoxItem item)
        {
            bool isCustom = item.Tag is string s && s == CUSTOM_TAG;
            lblCustomName.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            txtCustomName.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            if (item.Tag is CostSectionGroupDto grp)
            {
                int count = _availableTemplates.Count(t => t.GroupId == grp.Id);
                int defaultCount = _availableTemplates.Count(t => t.GroupId == grp.Id && t.IsDefault);
                txtInfo.Text = $"Verranno aggiunte {defaultCount} sezioni default di questo gruppo alla commessa.";
            }
            else
            {
                txtInfo.Text = "Verrà creato un gruppo vuoto. Potrai aggiungere sezioni manualmente.";
            }
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbGroup.SelectedItem is not ComboBoxItem selected)
        {
            txtError.Text = "Seleziona un gruppo.";
            return;
        }

        try
        {
            if (selected.Tag is CostSectionGroupDto grp)
            {
                // Aggiungi gruppo da template: crea tutte le sezioni default
                var defaultTemplates = _availableTemplates
                    .Where(t => t.GroupId == grp.Id && t.IsDefault)
                    .OrderBy(t => t.SortOrder)
                    .ToList();

                foreach (var tmpl in defaultTemplates)
                {
                    var req = new
                    {
                        templateId = (int?)tmpl.Id,
                        name = tmpl.Name,
                        sectionType = tmpl.SectionType,
                        groupName = grp.Name,
                        sortOrder = tmpl.SortOrder,
                        isEnabled = true
                    };
                    string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    string result = await ApiClient.PostAsync($"{_apiBasePath}/sections", json);

                    // Copia reparti associati
                    JsonDocument doc = JsonDocument.Parse(result);
                    if (doc.RootElement.GetProperty("success").GetBoolean())
                    {
                        int newSectionId = doc.RootElement.GetProperty("data").GetInt32();
                        // Prendi i dept dal template
                        string tmplJson = await ApiClient.GetAsync("/api/cost-sections/templates");
                        JsonDocument tmplDoc = JsonDocument.Parse(tmplJson);
                        if (tmplDoc.RootElement.GetProperty("success").GetBoolean())
                        {
                            var allTemplates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                                tmplDoc.RootElement.GetProperty("data").GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                            var fullTmpl = allTemplates.FirstOrDefault(t => t.Id == tmpl.Id);
                            if (fullTmpl != null)
                            {
                                var deptReq = new { departmentIds = fullTmpl.DepartmentIds };
                                string deptJson = JsonSerializer.Serialize(deptReq, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                                await ApiClient.PutAsync($"{_apiBasePath}/sections/{newSectionId}/departments", deptJson);
                            }
                        }
                    }
                }

                SelectedGroupName = grp.Name;
            }
            else
            {
                // Nuovo gruppo personalizzato
                string name = txtCustomName.Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(name))
                {
                    txtError.Text = "Nome gruppo obbligatorio.";
                    return;
                }
                SelectedGroupName = name;
                // Non crea sezioni — il gruppo apparirà vuoto, il PM aggiungerà sezioni manualmente
                // Serve almeno una sezione placeholder per far apparire il gruppo
                // Creiamo una sezione vuota disabilitata? No, meglio non creare niente
                // Il gruppo appare solo se ha sezioni → creiamo una sezione vuota
                var req = new
                {
                    templateId = (int?)null,
                    name = "Nuova sezione",
                    sectionType = "IN_SEDE",
                    groupName = name,
                    sortOrder = 1,
                    isEnabled = true
                };
                string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await ApiClient.PostAsync($"{_apiBasePath}/sections", json);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
