namespace ATEC.PM.Server.Services;

/// <summary>Traccia quali dipendenti hanno almeno un client Gantt connesso (SignalR).</summary>
public sealed class UserPresenceService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _connectionToEmployee = new();
    private readonly Dictionary<int, int> _employeeConnections = new();

    public void Register(string connectionId, int employeeId)
    {
        lock (_lock)
        {
            if (_connectionToEmployee.ContainsKey(connectionId))
                return;
            _connectionToEmployee[connectionId] = employeeId;
            _employeeConnections.TryGetValue(employeeId, out int count);
            _employeeConnections[employeeId] = count + 1;
        }
    }

    public void Unregister(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToEmployee.TryGetValue(connectionId, out int employeeId))
                return;
            _connectionToEmployee.Remove(connectionId);
            int count = _employeeConnections[employeeId] - 1;
            if (count <= 0)
                _employeeConnections.Remove(employeeId);
            else
                _employeeConnections[employeeId] = count;
        }
    }

    public List<int> GetOnlineEmployeeIds()
    {
        lock (_lock)
            return _employeeConnections.Keys.ToList();
    }
}
