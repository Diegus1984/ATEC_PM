namespace ATEC.PM.Client.UserControls;

public partial class NewChatDialog : Window
{
    private readonly int _projectId;
    private readonly List<CheckBox> _checkBoxes = new();

    public int CreatedChatId { get; private set; }
    public string ChatTitle { get; private set; } = "";

    public NewChatDialog(int projectId)
    {
        InitializeComponent();
        _projectId = projectId;
        Loaded += async (_, _) => await LoadEmployees();
    }

    private async Task LoadEmployees()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/employees");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var employees = JsonSerializer.Deserialize<List<EmployeeListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            foreach (EmployeeListItem emp in employees)
            {
                bool isMe = emp.Id == App.UserId;
                CheckBox cb = new()
                {
                    Content = isMe ? $"{emp.FullName} (tu)" : emp.FullName,
                    Tag = emp.Id,
                    IsChecked = isMe,
                    IsEnabled = !isMe,
                    FontSize = 13,
                    Margin = new Thickness(4, 2, 0, 2)
                };
                _checkBoxes.Add(cb);
                pnlEmployees.Children.Add(cb);
            }
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        string title = txtTitle.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            txtError.Text = "Il titolo è obbligatorio.";
            return;
        }

        List<int> participantIds = _checkBoxes
            .Where(cb => cb.IsChecked == true && cb.Tag is int)
            .Select(cb => (int)cb.Tag)
            .ToList();

        try
        {
            string jsonBody = JsonSerializer.Serialize(new
            {
                projectId = _projectId,
                title,
                participantIds
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/chat", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                CreatedChatId = doc.RootElement.GetProperty("data").GetInt32();
                ChatTitle = title;
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
