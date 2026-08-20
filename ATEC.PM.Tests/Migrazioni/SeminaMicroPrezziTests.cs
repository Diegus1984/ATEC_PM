using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// M104 (rebuild §12, passo 4): i micro «vede prezzi» nascono per FOTOGRAFIA — chi oggi vede la
/// voce tiene i suoi numeri, dinieghi compresi. E la migrazione si può rieseguire senza danni.
/// </summary>
public class SeminaMicroPrezziTests
{
    [FactRichiedeMySql]
    public void La_semina_fotografa_le_voci_ed_e_idempotente()
    {
        using var db = new DatabaseDiProva("semina104");
        db.CreaSchemaCompleto(); // la M104 vera è già passata (a vuoto: i dipendenti finti non c'erano)
        using MySqlConnection c = db.Apri();

        c.Execute(@"INSERT INTO employees (first_name, last_name, email, username, password_hash, user_role, status)
                    VALUES ('Semina', 'Uno', 'semina.uno@test.it', 'semina.uno', 'x', 'TECH', 'ACTIVE')");
        int uno = c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = 'semina.uno'");

        c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin) VALUES
                    (@Uno, 'project.ddp_commerciale', 'READ', 'CLASSE'),
                    (@Uno, 'nav.gestore_ddp', 'NO', 'MANO')", new { Uno = uno });
        c.Execute("INSERT IGNORE INTO auth_class_features (class_name, feature_key, access) VALUES ('RESP_REPARTO', 'nav.acquisti_inbox', 'FULL')");

        new M104_SeminaMicroPrezzi().Applica(c, NullLogger.Instance);

        // Fotografia: stesso accesso, stessa origine — il diniego resta diniego anche sul micro.
        Assert.Equal(("READ", "CLASSE"), Riga(c, uno, "project.ddp_commerciale.prices"));
        Assert.Equal(("NO", "MANO"), Riga(c, uno, "nav.gestore_ddp.prices"));
        Assert.Equal("FULL", c.ExecuteScalar<string>(
            "SELECT access FROM auth_class_features WHERE class_name = 'RESP_REPARTO' AND feature_key = 'nav.acquisti_inbox.prices'"));

        // Il micro è registrato col min_level della voce (rollback al motore vecchio fedele).
        var livelli = c.QuerySingle<(int Micro, int Voce)>(@"
            SELECT (SELECT min_level FROM auth_features WHERE feature_key = 'project.ddp_commerciale.prices'),
                   (SELECT min_level FROM auth_features WHERE feature_key = 'project.ddp_commerciale')");
        Assert.Equal(livelli.Voce, livelli.Micro);

        // Secondo giro: niente righe in più, e un ritocco fatto nel frattempo non si sovrascrive.
        long prima = c.ExecuteScalar<long>("SELECT COUNT(*) FROM employee_feature_access");
        c.Execute("UPDATE employee_feature_access SET access = 'FULL' WHERE employee_id = @Uno AND feature_key = 'project.ddp_commerciale.prices'",
            new { Uno = uno });
        new M104_SeminaMicroPrezzi().Applica(c, NullLogger.Instance);
        Assert.Equal(prima, c.ExecuteScalar<long>("SELECT COUNT(*) FROM employee_feature_access"));
        Assert.Equal(("FULL", "CLASSE"), Riga(c, uno, "project.ddp_commerciale.prices"));
    }

    private static (string Access, string Origin) Riga(MySqlConnection c, int employeeId, string chiave) =>
        c.QuerySingle<(string, string)>(
            "SELECT access, origin FROM employee_feature_access WHERE employee_id = @Id AND feature_key = @Chiave",
            new { Id = employeeId, Chiave = chiave });
}
