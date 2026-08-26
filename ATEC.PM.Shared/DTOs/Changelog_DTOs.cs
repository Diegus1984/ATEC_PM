namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una versione pubblicata in produzione: build, data del deploy e le modifiche
/// (prime righe dei commit) raccolte automaticamente da `aggiorna-server.ps1`.
/// Le segnalazioni chiuse arrivano invece da `bug_reports.fixed_in_build`.
/// </summary>
public class ChangelogVoce
{
    public string Build { get; set; } = "";
    /// <summary>Data/ora del deploy, ISO 8601 locale (es. 2026-08-26T12:02:00).</summary>
    public string Data { get; set; } = "";
    public List<string> Modifiche { get; set; } = new();
    public List<ChangelogSegnalazione> Segnalazioni { get; set; } = new();
}

public class ChangelogSegnalazione
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}
