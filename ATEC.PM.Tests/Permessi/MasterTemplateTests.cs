using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Passo 6 del rebuild (§3.5, §3.6, §5.4): il template si edita dall'app SENZA muovere nessuno,
/// «Applica a…» può portare un template esplicito, e la copia produce un CLONE — origin
/// compresi, perché marcare tutto MANO rendeva inerte ogni «Applica template» futuro (§12.8.5).
/// </summary>
public sealed class SchemaMasterFixture : IDisposable
{
    private readonly Lazy<DatabaseDiProva> _db = new(() =>
    {
        var d = new DatabaseDiProva("master6");
        d.CreaSchemaCompleto();
        // L'invariante «non ci si chiude fuori» vuole ALMENO un amministratore dei permessi:
        // sul database di prova nessuno ha righe, quindi lo si nomina qui (il jolly all'utente
        // admin del bootstrap), o ogni scrittura verrebbe rifiutata con un 409.
        d.Esegui(@"INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
                   SELECT MIN(id), '*', 'FULL', 'MANO' FROM employees");
        return d;
    });

    public DatabaseDiProva Db => _db.Value;

    public void Dispose()
    {
        if (_db.IsValueCreated) _db.Value.Dispose();
    }
}

public class MasterTemplateTests : IClassFixture<SchemaMasterFixture>
{
    private readonly SchemaMasterFixture _schema;

    public MasterTemplateTests(SchemaMasterFixture schema) => _schema = schema;

    private PermissionAdminService Servizio()
    {
        DbService db = _schema.Db.Servizio();
        var access = new FeatureAccessService(db);
        var changes = new PermissionChangeService(db, access, new HubFinto(), NullLogger<PermissionChangeService>.Instance);
        return new PermissionAdminService(db, access, changes);
    }

    /// <summary>SignalR finto: la propagazione qui non ha nessuno da avvisare.</summary>
    private sealed class HubFinto : Microsoft.AspNetCore.SignalR.IHubContext<ATEC.PM.Server.Hubs.ProjectHub>
    {
        public Microsoft.AspNetCore.SignalR.IHubClients Clients { get; } = new ClientiFinti();
        public Microsoft.AspNetCore.SignalR.IGroupManager Groups { get; } = new GruppiFinti();

        private sealed class ClientiFinti : Microsoft.AspNetCore.SignalR.IHubClients
        {
            private static readonly Microsoft.AspNetCore.SignalR.IClientProxy Proxy = new ProxyFinto();
            public Microsoft.AspNetCore.SignalR.IClientProxy All => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy Client(string connectionId) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy Group(string groupName) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy User(string userId) => Proxy;
            public Microsoft.AspNetCore.SignalR.IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
        }

        private sealed class ProxyFinto : Microsoft.AspNetCore.SignalR.IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class GruppiFinti : Microsoft.AspNetCore.SignalR.IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }

    private int CreaPersona(MySqlConnection c, string username, string ruolo)
    {
        c.Execute(@"INSERT INTO employees (first_name, last_name, email, username, password_hash, user_role, status)
                    VALUES ('Test', @U, CONCAT(@U,'@test.it'), @U, 'x', @R, 'ACTIVE')",
            new { U = username, R = ruolo });
        return c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = @U", new { U = username });
    }

    [FactRichiedeMySql]
    public void Il_template_si_edita_senza_muovere_nessuno_e_spenta_vuol_dire_assenza()
    {
        PermissionAdminService servizio = Servizio();
        using MySqlConnection c = _schema.Db.Apri();
        int persona = CreaPersona(c, "m6.fermo", "TECH");
        long righePrima = c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM employee_feature_access WHERE employee_id = @Id", new { Id = persona });

        // Accendi, cambia livello, spegni: solo il pacchetto si muove.
        servizio.ImpostaPacchetto(new ImpostaPacchettoRequest { Classe = "TECH", FeatureKey = "nav.scadenze", Stato = "FULL" });
        Assert.Equal("FULL", c.ExecuteScalar<string>(
            "SELECT access FROM auth_class_features WHERE class_name = 'TECH' AND feature_key = 'nav.scadenze'"));

        servizio.ImpostaPacchetto(new ImpostaPacchettoRequest { Classe = "TECH", FeatureKey = "nav.scadenze", Stato = "READ" });
        Assert.Equal("READ", c.ExecuteScalar<string>(
            "SELECT access FROM auth_class_features WHERE class_name = 'TECH' AND feature_key = 'nav.scadenze'"));

        // §3.7: nel master «spenta» è un'ASSENZA dal pacchetto, non un diniego.
        servizio.ImpostaPacchetto(new ImpostaPacchettoRequest { Classe = "TECH", FeatureKey = "nav.scadenze", Stato = "NO" });
        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM auth_class_features WHERE class_name = 'TECH' AND feature_key = 'nav.scadenze'"));

        // Nessun utente è cambiato: salvare il master non muove nessuno (§5.8.6).
        Assert.Equal(righePrima, c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM employee_feature_access WHERE employee_id = @Id", new { Id = persona }));

        // Il jolly di un template non si tocca da qui; un template inesistente si rifiuta.
        Assert.Throws<ArgumentException>(() => servizio.ImpostaPacchetto(
            new ImpostaPacchettoRequest { Classe = "ADMIN", FeatureKey = "*", Stato = "FULL" }));
        Assert.Throws<KeyNotFoundException>(() => servizio.ImpostaPacchetto(
            new ImpostaPacchettoRequest { Classe = "INESISTENTE", FeatureKey = "nav.scadenze", Stato = "FULL" }));
    }

    [FactRichiedeMySql]
    public void Applica_con_template_esplicito_usa_quel_pacchetto_e_rispetta_le_eccezioni()
    {
        PermissionAdminService servizio = Servizio();
        using MySqlConnection c = _schema.Db.Apri();
        int persona = CreaPersona(c, "m6.override", "TECH");

        // Un'eccezione a mano che l'applicazione deve lasciare dov'è.
        c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
                    VALUES (@Id, 'nav.timesheet', 'NO', 'MANO')", new { Id = persona });

        // Anteprima col template del RESP_REPARTO (non della sua classe): non scrive niente.
        EsitoApplicaClasseDto anteprima = servizio.ApplicaClasse(new ApplicaClasseRequest
        {
            EmployeeIds = new List<int> { persona },
            Anteprima = true,
            Classe = "RESP_REPARTO",
        }, changedBy: null);
        Assert.True(anteprima.Combo > 0);
        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employee_feature_access WHERE employee_id = @Id AND origin = 'CLASSE'",
            new { Id = persona }));

        // Applicazione vera: le righe CLASSE arrivano dal pacchetto RESP, l'eccezione resta.
        EsitoApplicaClasseDto esito = servizio.ApplicaClasse(new ApplicaClasseRequest
        {
            EmployeeIds = new List<int> { persona },
            Anteprima = false,
            Classe = "RESP_REPARTO",
        }, changedBy: null);
        Assert.Equal(anteprima.Combo, esito.Combo);
        Assert.True(esito.RispettateAMano >= 1);

        var pacchettoResp = c.Query<string>(
            "SELECT feature_key FROM auth_class_features WHERE class_name = 'RESP_REPARTO'").ToHashSet();
        var sueClasse = c.Query<string>(
            "SELECT feature_key FROM employee_feature_access WHERE employee_id = @Id AND origin = 'CLASSE'",
            new { Id = persona }).ToHashSet();
        Assert.True(sueClasse.SetEquals(pacchettoResp.Where(k => k != "nav.timesheet")),
            "le righe CLASSE devono essere il pacchetto RESP (meno l'eccezione a mano)");
        Assert.Equal(("NO", "MANO"), c.QuerySingle<(string, string)>(
            "SELECT access, origin FROM employee_feature_access WHERE employee_id = @Id AND feature_key = 'nav.timesheet'",
            new { Id = persona }));

        Assert.Throws<KeyNotFoundException>(() => servizio.ApplicaClasse(new ApplicaClasseRequest
        {
            EmployeeIds = new List<int> { persona },
            Anteprima = true,
            Classe = "REFUSO",
        }, changedBy: null));
    }

    [FactRichiedeMySql]
    public void La_copia_produce_un_clone_con_gli_origin_del_sorgente()
    {
        PermissionAdminService servizio = Servizio();
        using MySqlConnection c = _schema.Db.Apri();
        int sorgente = CreaPersona(c, "m6.sorgente", "PM");
        int destinatario = CreaPersona(c, "m6.dest", "TECH");

        c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin) VALUES
                    (@S, 'nav.commesse', 'FULL', 'CLASSE'),
                    (@S, 'nav.bilancio', 'READ', 'MANO'),
                    (@S, 'nav.timesheet', 'NO', 'MANO'),
                    (@D, 'nav.catalogo', 'FULL', 'MANO')",
            new { S = sorgente, D = destinatario });

        // Anteprima: elenco dei cambi, zero scritture.
        EsitoApplicaClasseDto anteprima = servizio.CopiaDa(new CopiaPermessiRequest
        { DaEmployeeId = sorgente, AEmployeeId = destinatario, Anteprima = true }, changedBy: null);
        // 2 voci che arrivano + 1 che se ne va. Il diniego del sorgente NON è un cambio (per il
        // destinatario «non abilitato» resta «non abilitato»), ma la copia lo materializza lo
        // stesso come riga NO: è un'eccezione del clone, e si vede nell'assert qui sotto.
        Assert.Equal(3, anteprima.Combo);
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employee_feature_access WHERE employee_id = @Id", new { Id = destinatario }));

        // Copia vera: clone esatto — accessi E origin; la riga in più è SPARITA (non un diniego).
        servizio.CopiaDa(new CopiaPermessiRequest
        { DaEmployeeId = sorgente, AEmployeeId = destinatario, Anteprima = false }, changedBy: null);

        var righe = c.Query<(string Chiave, string Access, string Origin)>(
                "SELECT feature_key, access, origin FROM employee_feature_access WHERE employee_id = @Id",
                new { Id = destinatario })
            .OrderBy(r => r.Chiave)
            .ToList();
        Assert.Equal(new[]
        {
            ("nav.bilancio", "READ", "MANO"),
            ("nav.commesse", "FULL", "CLASSE"),
            ("nav.timesheet", "NO", "MANO"),
        }, righe);

        // Il senso della falla §12.8.5: sul CLONE un futuro «Applica template» lavora ancora —
        // la riga CLASSE non è diventata un'eccezione.
        Assert.Equal("CLASSE", c.ExecuteScalar<string>(
            "SELECT origin FROM employee_feature_access WHERE employee_id = @Id AND feature_key = 'nav.commesse'",
            new { Id = destinatario }));
    }
}
