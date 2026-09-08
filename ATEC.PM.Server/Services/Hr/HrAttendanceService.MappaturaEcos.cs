using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

// Parte «mappatura per EmplID» di HrAttendanceService (08/09/2026). Le persone si collegano
// per EmplCode (badge), ma non tutte le API lo mandano: PeopleAbsenceRequestGetAll dà solo
// l'EmplID. L'id si impara dagli scarichi che portano tutti e due i campi e si tiene in
// employees.ecos_empl_id (M126). Manuale Ecos §6.3 e §9.8.
public partial class HrAttendanceService
{
    /// <summary>
    /// Impara le coppie (EmplCode, EmplID) viste in uno scarico: chi ha quel codice riceve
    /// quell'id. Idempotente e silenzioso; le coppie incomplete si ignorano.
    /// </summary>
    internal static int ImparaEmplId(MySqlConnection c, IEnumerable<(string EmplCode, string EmplId)> coppie)
    {
        var viste = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach ((string codice, string id) in coppie)
        {
            if (string.IsNullOrWhiteSpace(codice) || !int.TryParse(id?.Trim(), out int emplId) || emplId <= 0) continue;
            viste[codice.Trim()] = emplId;
        }
        if (viste.Count == 0) return 0;

        int aggiornati = 0;
        foreach ((string codice, int emplId) in viste)
        {
            aggiornati += c.Execute(@"
                UPDATE employees SET ecos_empl_id = @Id
                WHERE ecos_empl_code = @Codice AND (ecos_empl_id IS NULL OR ecos_empl_id <> @Id)",
                new { Id = emplId, Codice = codice });
        }
        return aggiornati;
    }

    /// <summary>C'è qualcuno collegato per codice ma ancora senza EmplID?</summary>
    internal static bool MancanoEmplId(MySqlConnection c) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM employees
            WHERE ecos_empl_code IS NOT NULL AND ecos_empl_code <> '' AND ecos_empl_id IS NULL") > 0;

    /// <summary>
    /// Impara gli EmplID dai badge: <c>PeopleBadgeGetAll</c> porta EmplCode ed EmplID insieme e
    /// non ha criteri impliciti, quindi copre anche chi non timbra e non ha giorni di assenza
    /// nella finestra — altrimenti le richieste di quella persona resterebbero «non
    /// riconosciute» per sempre (Carretta, 08/09/2026: EmplID 5399 a log per due import).
    /// </summary>
    public int ImparaEmplIdDaiBadge(IEnumerable<EcosBadge> badges)
    {
        using MySqlConnection c = _db.Open();
        return ImparaEmplId(c, badges.Select(b => (b.EmplCode, b.EmplId)));
    }

    /// <summary>EmplID di Ecos → <c>employees.id</c>, per le API che non mandano l'EmplCode.</summary>
    private static Dictionary<string, int> MappaEcosPerId(MySqlConnection c)
    {
        var mappa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var riga in c.Query<(int Id, int EmplId)>(
            "SELECT id AS Id, ecos_empl_id AS EmplId FROM employees WHERE ecos_empl_id IS NOT NULL"))
        {
            mappa[riga.EmplId.ToString()] = riga.Id;
        }
        return mappa;
    }

    /// <summary>
    /// Trova il dipendente di una riga Ecos: prima per EmplID (stabile), poi per EmplCode.
    /// </summary>
    private static bool TrovaDipendente(
        Dictionary<string, int> perId, Dictionary<string, int> perCodice,
        string? emplId, string? emplCode, out int employeeId)
    {
        if (!string.IsNullOrWhiteSpace(emplId) && perId.TryGetValue(emplId.Trim(), out employeeId)) return true;
        if (!string.IsNullOrWhiteSpace(emplCode) && perCodice.TryGetValue(emplCode.Trim(), out employeeId)) return true;
        employeeId = 0;
        return false;
    }
}
