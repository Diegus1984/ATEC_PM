using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// M103 (rebuild §12, passo 3): lo split menu/albero è una FOTOGRAFIA — <c>project.X</c> nasce
/// identica a <c>nav.X</c> per ogni persona (accesso e origine, dinieghi compresi), classe e
/// ruolo, col <c>min_level</c> del motore vecchio. E si può rieseguire senza toccare niente.
/// </summary>
public class SplitChiaviAlberoTests
{
    [FactRichiedeMySql]
    public void La_fotografia_copia_accessi_e_origini_ed_e_idempotente()
    {
        using var db = new DatabaseDiProva("split103");
        db.CreaSchemaCompleto(); // la M103 vera è già passata (a vuoto: i dipendenti finti non c'erano)
        using MySqlConnection c = db.Apri();

        c.Execute(@"INSERT INTO employees (first_name, last_name, email, username, password_hash, user_role, status)
                    VALUES ('Split', 'Uno', 'split.uno@test.it', 'split.uno', 'x', 'TECH', 'ACTIVE'),
                           ('Split', 'Due', 'split.due@test.it', 'split.due', 'x', 'RESP_REPARTO', 'ACTIVE')");
        int uno = c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = 'split.uno'");
        int due = c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = 'split.due'");

        // Lo spettro dei casi: concessione a mano, DINIEGO a mano, riga di classe; più un
        // pacchetto di classe e una lista del motore vecchio.
        c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin) VALUES
                    (@Uno, 'nav.mom',  'READ', 'MANO'),
                    (@Uno, 'nav.sal',  'NO',   'MANO'),
                    (@Due, 'nav.checklist', 'FULL', 'CLASSE')",
            new { Uno = uno, Due = due });
        c.Execute("INSERT IGNORE INTO auth_class_features (class_name, feature_key, access) VALUES ('TECH', 'nav.milestones', 'READ')");
        c.Execute("INSERT IGNORE INTO auth_role_features (role_name, feature_key, access) VALUES ('AMM', 'nav.work_requests', 'READ')");

        // La migrazione è idempotente per costruzione: si riesegue direttamente sui dati veri.
        new M103_SplitChiaviAlbero().Applica(c, NullLogger.Instance);

        // Fotografia riga per riga: accesso E origine, diniego compreso.
        Assert.Equal(("READ", "MANO"), Riga(c, uno, "project.mom"));
        Assert.Equal(("NO", "MANO"), Riga(c, uno, "project.sal"));
        Assert.Equal(("FULL", "CLASSE"), Riga(c, due, "project.checklist"));
        Assert.Equal("READ", c.ExecuteScalar<string>(
            "SELECT access FROM auth_class_features WHERE class_name = 'TECH' AND feature_key = 'project.milestones'"));
        Assert.Equal("READ", c.ExecuteScalar<string>(
            "SELECT access FROM auth_role_features WHERE role_name = 'AMM' AND feature_key = 'project.work_requests'"));

        // min_level fotografato dal motore vecchio, non il default «solo Admin» delle chiavi nuove.
        var livelli = c.QuerySingle<(int Nuova, int Vecchia)>(@"
            SELECT (SELECT min_level FROM auth_features WHERE feature_key = 'project.mom'),
                   (SELECT min_level FROM auth_features WHERE feature_key = 'nav.mom')");
        Assert.Equal(livelli.Vecchia, livelli.Nuova);

        // Secondo giro: nessuna riga in più, e la riga esistente non viene sovrascritta.
        long prima = c.ExecuteScalar<long>("SELECT COUNT(*) FROM employee_feature_access");
        c.Execute("UPDATE employee_feature_access SET access = 'FULL' WHERE employee_id = @Uno AND feature_key = 'project.mom'",
            new { Uno = uno });
        new M103_SplitChiaviAlbero().Applica(c, NullLogger.Instance);
        Assert.Equal(prima, c.ExecuteScalar<long>("SELECT COUNT(*) FROM employee_feature_access"));
        Assert.Equal(("FULL", "MANO"), Riga(c, uno, "project.mom")); // il ritocco fatto nel frattempo resta
    }

    private static (string Access, string Origin) Riga(MySqlConnection c, int employeeId, string chiave) =>
        c.QuerySingle<(string, string)>(
            "SELECT access, origin FROM employee_feature_access WHERE employee_id = @Id AND feature_key = @Chiave",
            new { Id = employeeId, Chiave = chiave });
}
