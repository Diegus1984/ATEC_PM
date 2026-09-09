using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M129 — <c>employees.payroll_code</c>: la matricola del libro paga, accanto al codice Ecos.
///
/// <para>In azienda una persona ha DUE numeri (Diego, 09/09/2026). Il codice badge Ecos
/// (<c>ecos_empl_code</c>, es. 1027) serve a noi per riconoscere timbrature e richieste. La
/// matricola del consulente paghe (es. 001) è quella che deve comparire sotto il nome nel
/// calendario «Tutti, mese per mese» e nel file Excel delle presenze, come nel foglio
/// «PRESENZE MM MESE AA.xlsx» che l'ufficio manda ogni mese. Finché c'era un numero solo il
/// calendario mostrava quello di Ecos, che sul foglio paghe non vuol dire niente.</para>
///
/// <para>Unica come il codice Ecos: due persone con la stessa matricola sono un errore di
/// battitura. Il precarico prende le 26 matricole dal foglio di agosto 2026, abbinate per
/// cognome e nome: chi non si trova (o si trova due volte) resta vuoto e lo dice nel log, la
/// matricola si mette poi dall'anagrafica. Il foglio scrive «Carretta Gedeone Marco»: da noi
/// è «Carretta Marco».</para>
/// </summary>
public sealed class M129_EmployeesMatricolaPaghe : IMigrazione
{
    public int Versione => 129;

    public string Descrizione =>
        "HR: employees.payroll_code, la matricola del libro paga (calendario ed Excel), con precarico dal foglio di agosto 2026";

    // (cognome, nome, matricola) come sul foglio «PRESENZE 08 AGOSTO 26.xlsx», nomi come in anagrafica.
    private static readonly (string Cognome, string Nome, string Matricola)[] Precarico =
    [
        ("Frattini", "Diego", "001"), ("Cimmino", "Paolo", "004"), ("Carretta", "Maria", "008"),
        ("Vinardi", "Gianpiero", "014"), ("Larganà", "Gionatan", "020"), ("Tomasi", "Rossano", "034"),
        ("Cassano", "Mario", "051"), ("Buda", "Giuseppe", "052"), ("Castagneri", "Federico", "060"),
        ("Chiantia", "Rocco", "062"), ("Saffioti", "Simone", "070"), ("Castellano", "Paolo", "075"),
        ("Spinello", "Luca", "078"), ("Scabbia", "Omar", "081"), ("Zanoni", "Paolo", "082"),
        ("Corrado", "Angelo", "086"), ("Carretta", "Marco", "091"), ("Monge", "Matteo Paolo", "102"),
        ("Cesi", "Gabriele", "103"), ("Maracich", "Giorgio", "106"), ("Sinapi", "Emanuele", "109"),
        ("Vottero", "Gabriele", "118"), ("Abatangelo", "Alessandra", "121"), ("Carretta", "Edoardo", "122"),
        ("Di Monte", "Daniel", "123"), ("Obreja", "Vasile Ovidiu", "124"),
    ];

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool colonna = AddColumnIfMissing(c, "employees", "payroll_code",
            "VARCHAR(20) NULL COMMENT 'Matricola del libro paga: compare nel calendario presenze e nel file Excel (non e il codice Ecos)' AFTER ecos_empl_id");

        bool indice = false;
        if (c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM information_schema.statistics
                WHERE table_schema = DATABASE() AND table_name = 'employees' AND index_name = 'uq_employees_payroll'") == 0)
        {
            c.Execute("CREATE UNIQUE INDEX `uq_employees_payroll` ON `employees` (`payroll_code`)", commandTimeout: 600);
            indice = true;
        }

        // Precarico: solo dove la colonna è ancora vuota e la persona è una sola. Un
        // precarico non deve mai fermare l'avvio: ogni riga per conto suo, l'errore va nel log.
        int messe = 0, saltate = 0;
        foreach (var (cognome, nome, matricola) in Precarico)
        {
            try
            {
                var ids = c.Query<int>(@"
                    SELECT id FROM employees
                    WHERE last_name = @Cognome AND first_name = @Nome
                      AND emp_type = 'INTERNAL' AND status <> 'TERMINATED'",
                    new { Cognome = cognome, Nome = nome }).ToList();
                if (ids.Count != 1)
                {
                    saltate++;
                    if (ids.Count > 1)
                        log.LogWarning("[M129] {Cognome} {Nome}: {N} omonimi, matricola {M} non assegnata.", cognome, nome, ids.Count, matricola);
                    continue;
                }
                messe += c.Execute(
                    "UPDATE employees SET payroll_code = @M WHERE id = @Id AND payroll_code IS NULL",
                    new { M = matricola, Id = ids[0] });
            }
            catch (MySqlException ex)
            {
                saltate++;
                log.LogWarning("[M129] {Cognome} {Nome}: matricola {M} non assegnata ({Msg}).", cognome, nome, matricola, ex.Message);
            }
        }

        log.LogInformation("[M129] employees.payroll_code: colonna={C}, indice unico={I}; precarico: {Messe} matricole messe, {Saltate} non abbinate.",
            colonna, indice, messe, saltate);
    }
}
