using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v33: commessa di sistema INTERNA — contenitore per lavorazioni generiche
// (bozze staging non legate a una commessa reale). customer_id/pm_id sono NOT NULL.
public sealed class M033_CommessaInterna : IMigrazione
{
    public int Versione => 33;

    public string Descrizione => "progetto sistema INTERNA per lavorazioni generiche";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM projects WHERE code = @Code",
            new { Code = ATEC.PM.Shared.SystemProjects.InternaCode });
        if (exists == 0)
        {
            // Cliente di sistema (vat univoco) — riusa se già presente da un run precedente.
            const string systemVat = "__SYSTEM_INTERNA__";
            int customerId = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(id), 0) FROM customers WHERE vat_number = @Vat",
                new { Vat = systemVat });
            if (customerId == 0)
            {
                customerId = c.ExecuteScalar<int>(@"
                    INSERT INTO customers (company_name, vat_number, notes, is_active)
                    VALUES (@Name, @Vat, @Notes, 1);
                    SELECT LAST_INSERT_ID()", new
                {
                    Name = "ATEC — Sistema",
                    Vat = systemVat,
                    Notes = ATEC.PM.Shared.SystemProjects.InternaNotes,
                });
            }

            int pmId = c.ExecuteScalar<int>(
                "SELECT COALESCE(MIN(id), 0) FROM employees WHERE status = 'ACTIVE'");
            if (pmId == 0)
                pmId = c.ExecuteScalar<int>("SELECT COALESCE(MIN(id), 0) FROM employees");
            if (pmId == 0)
                throw new InvalidOperationException(
                    "Impossibile creare progetto INTERNA: nessun employee in anagrafica.");

            c.Execute(@"
                INSERT INTO projects
                    (code, title, customer_id, pm_id, description, status, priority, notes)
                VALUES
                    (@Code, @Title, @CustomerId, @PmId, @Description, 'ACTIVE', 'MEDIUM', @Notes)",
                new
                {
                    Code = ATEC.PM.Shared.SystemProjects.InternaCode,
                    Title = ATEC.PM.Shared.SystemProjects.InternaTitle,
                    CustomerId = customerId,
                    PmId = pmId,
                    Description = ATEC.PM.Shared.SystemProjects.InternaTitle,
                    Notes = ATEC.PM.Shared.SystemProjects.InternaNotes,
                });
        }

        log.LogInformation("[Migration v33] Progetto sistema INTERNA assicurato");
    }
}
