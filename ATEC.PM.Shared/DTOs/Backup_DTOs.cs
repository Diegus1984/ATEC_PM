namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Cambio della destinazione dei pacchetti di backup completo (pagina Backup).
/// Percorso vuoto = si torna alla destinazione del server (appsettings o predefinita).
/// La password è FACOLTATIVA anche con l'utente indicato: vuota = si tiene quella già
/// salvata (se l'utente non è cambiato) — così si può correggere il solo percorso
/// senza ridigitare niente.
/// </summary>
public class BackupDestinationSaveRequest
{
    public string Percorso { get; set; } = "";
    /// <summary>Utente per la share di rete (es. NAS\utente). Vuoto = catena di ripiego del server.</summary>
    public string ShareUser { get; set; } = "";
    /// <summary>Password dell'utente. Non viene mai restituita dalle letture.</summary>
    public string SharePassword { get; set; } = "";
}
