using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.ViewModels;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class EmployeeDialog : Window
{
    private readonly int _employeeId;
    private List<DepartmentDto> _departments = new();
    private ObservableCollection<DeptMembershipRow> _deptRows = new();
    private ObservableCollection<CompetenceRow> _compRows = new();

    public int SavedEmployeeId { get; private set; }

    public EmployeeDialog(int employeeId = 0)
    {
        InitializeComponent();
        _employeeId = employeeId;
        Title = employeeId == 0 ? "Nuovo Utente" : "Modifica Utente";
        lstDeptMembership.ItemsSource = _deptRows;
        lstCompetences.ItemsSource = _compRows;

        txtPasswordHint.Text = employeeId == 0
            ? "Lascia vuoto per non impostare credenziali ora"
            : "Lascia vuoto per non modificare la password";

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadDepartments();
        if (_employeeId > 0)
            await LoadEmployee();
    }

    private async Task LoadDepartments()
    {
        try
        {
            _departments = await ApiClient.GetListAsync<DepartmentDto>("/api/departments");
            if (_departments.Count == 0) return;

            _deptRows.Clear();
            _compRows.Clear();
            foreach (DepartmentDto dept in _departments)
            {
                _deptRows.Add(new DeptMembershipRow
                {
                    DepartmentId = dept.Id,
                    DepartmentName = $"{dept.Code} — {dept.Name}"
                });
                _compRows.Add(new CompetenceRow
                {
                    DepartmentId = dept.Id,
                    DepartmentName = $"{dept.Code} — {dept.Name}"
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading departments: {ex.Message}");
        }
    }

    private async Task LoadEmployee()
    {
        try
        {
            EmployeeSaveRequest? emp = await ApiClient.GetDataAsync<EmployeeSaveRequest>($"/api/employees/{_employeeId}");
            if (emp != null)
            {
                txtFirstName.Text = emp.FirstName;
                txtLastName.Text = emp.LastName;
                txtEmail.Text = emp.Email;
                SelectComboItem(cmbType, emp.EmpType);
                SelectComboItem(cmbStatus, emp.Status);
            }

            UserDetailDto? user = await ApiClient.GetDataAsync<UserDetailDto>($"/api/users/{_employeeId}");
            if (user == null) return;

            string role = user.UserRole;
            rbAdmin.IsChecked = role == "ADMIN";
            rbPm.IsChecked = role == "PM";
            rbResp.IsChecked = role == "RESP_REPARTO";
            rbTech.IsChecked = !new[] { "ADMIN", "PM", "RESP_REPARTO" }.Contains(role);

            txtUsername.Text = user.Username;

            List<EmployeeDepartmentDto> depts = user.Departments;

            foreach (DeptMembershipRow row in _deptRows)
            {
                EmployeeDepartmentDto? existing = depts.FirstOrDefault(d => d.DepartmentId == row.DepartmentId);
                if (existing != null)
                {
                    row.IsMember = true;
                    row.IsResponsible = existing.IsResponsible;
                    row.IsPrimary = existing.IsPrimary;
                }
            }

            List<EmployeeCompetenceDto> comps = user.Competences;

            foreach (CompetenceRow row in _compRows)
            {
                EmployeeCompetenceDto? existing = comps.FirstOrDefault(c => c.DepartmentId == row.DepartmentId);
                if (existing != null)
                {
                    row.IsEnabled = true;
                    row.Notes = existing.Notes;
                }
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore caricamento: {ex.Message}";
        }
    }

    private void SelectComboItem(ComboBox cmb, string value)
    {
        foreach (ComboBoxItem item in cmb.Items)
            if (item.Content?.ToString() == value) { item.IsSelected = true; break; }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (string.IsNullOrWhiteSpace(txtFirstName.Text) || string.IsNullOrWhiteSpace(txtLastName.Text))
        {
            txtError.Text = "Nome e cognome sono obbligatori.";
            return;
        }

        bool hasPassword = txtPassword.Password.Length > 0;
        if (hasPassword)
        {
            if (txtPassword.Password.Length < 4)
            {
                txtError.Text = "Password minimo 4 caratteri.";
                return;
            }
            if (txtPassword.Password != txtPasswordConfirm.Password)
            {
                txtError.Text = "Le password non coincidono.";
                return;
            }
        }

        btnSave.IsEnabled = false;
        btnSave.Content = "Salvataggio...";

        try
        {
            // 1. Anagrafica
            object empObj = new
            {
                badgeNumber = "",
                firstName = txtFirstName.Text.Trim(),
                lastName = txtLastName.Text.Trim(),
                email = txtEmail.Text.Trim(),
                phone = "",
                empType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INTERNAL",
                hourlyCost = 0m,
                weeklyHours = 40m,
                hireDate = (string?)null,
                status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ACTIVE",
                notes = ""
            };

            string empJson = JsonSerializer.Serialize(empObj,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            int employeeId = _employeeId;
            if (_employeeId == 0)
            {
                string res = await ApiClient.PostAsync("/api/employees", empJson);
                if (!ApiClient.TryGetApiData<int>(res, out int newEmpId, out string errMsg))
                {
                    txtError.Text = errMsg;
                    return;
                }
                employeeId = newEmpId;
            }
            else
            {
                await ApiClient.PutAsync($"/api/employees/{_employeeId}", empJson);
            }

            // 2. Ruolo
            string role = rbAdmin.IsChecked == true ? "ADMIN"
                        : rbPm.IsChecked == true ? "PM"
                        : rbResp.IsChecked == true ? "RESP_REPARTO"
                        : "TECH";

            await ApiClient.PutAsync("/api/users/role",
                JsonSerializer.Serialize(new { employeeId, userRole = role },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // 3. Credenziali (solo se entrambi compilati)
            if (!string.IsNullOrWhiteSpace(txtUsername.Text) && hasPassword)
            {
                await ApiClient.PostAsync("/api/auth/set-credentials",
                    JsonSerializer.Serialize(
                        new { employeeId, username = txtUsername.Text.Trim(), password = txtPassword.Password },
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }

            // 4. Reparti
            List<EmployeeDepartmentDto> depts = _deptRows
                .Where(r => r.IsMember)
                .Select(r => new EmployeeDepartmentDto
                {
                    DepartmentId = r.DepartmentId,
                    IsResponsible = r.IsResponsible,
                    IsPrimary = r.IsPrimary
                }).ToList();

            await ApiClient.PutAsync("/api/users/departments",
                JsonSerializer.Serialize(new { employeeId, departments = depts },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // 5. Competenze
            List<EmployeeCompetenceDto> comps = _compRows
                .Where(r => r.IsEnabled)
                .Select(r => new EmployeeCompetenceDto
                {
                    DepartmentId = r.DepartmentId,
                    Notes = r.Notes
                }).ToList();

            await ApiClient.PutAsync("/api/users/competences",
                JsonSerializer.Serialize(new { employeeId, competences = comps },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            SavedEmployeeId = employeeId;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            btnSave.IsEnabled = true;
            btnSave.Content = "Salva";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
