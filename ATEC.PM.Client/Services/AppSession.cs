using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Services;

public sealed class AppSession
{
    public static AppSession Instance { get; } = new();
    private AppSession() { }

    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    public string Token { get; set; } = "";
    public string UserFullName { get; set; } = "";
    public string UserRole { get; set; } = "";
    public int UserId { get; set; }
    public UserContext CurrentUser { get; set; } = new();

    public void SetCurrentUser(int id, string role,
        IEnumerable<string> deptCodes,
        IEnumerable<string> respCodes,
        IEnumerable<string> compCodes)
    {
        CurrentUser = PermissionEngine.BuildContext(id, role, deptCodes, respCodes, compCodes);
    }
}
