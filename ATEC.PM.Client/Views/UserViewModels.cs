using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.ViewModels;

/// <summary>
/// Riga utente per la DataGrid di UsersPage.
/// </summary>
public class UserRow
{
    public int Id { get; set; }
    public string BadgeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserRole { get; set; } = "";
    public string Status { get; set; } = "";
    public bool HasCredentials { get; set; }
    public string Username { get; set; } = "";
    public List<string> DepartmentCodes { get; set; } = new();
    public List<string> CompetenceCodes { get; set; } = new();
    public string DepartmentCodesDisplay => string.Join(", ", DepartmentCodes);
}

/// <summary>
/// Riga reparto con flag appartenenza/responsabile/primario.
/// Usata in EmployeeDialog e UsersPage.
/// </summary>
public class DeptMembershipRow : INotifyPropertyChanged
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";

    private bool _isMember;
    public bool IsMember
    {
        get => _isMember;
        set
        {
            _isMember = value;
            OnPropChanged();
            OnPropChanged(nameof(IsResponsible));
            OnPropChanged(nameof(IsPrimary));
        }
    }

    private bool _isResponsible;
    public bool IsResponsible
    {
        get => _isResponsible && _isMember;
        set { _isResponsible = value; OnPropChanged(); }
    }

    private bool _isPrimary;
    public bool IsPrimary
    {
        get => _isPrimary && _isMember;
        set { _isPrimary = value; OnPropChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Riga competenza tecnica con flag abilitazione e note.
/// Usata in EmployeeDialog e UsersPage.
/// </summary>
public class CompetenceRow : INotifyPropertyChanged
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropChanged(); }
    }

    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
