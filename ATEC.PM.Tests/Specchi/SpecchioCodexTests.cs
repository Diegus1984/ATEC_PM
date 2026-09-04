using System.Text.RegularExpressions;
using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Specchi;

/// <summary>
/// Il sync Codex riscrive <b>solo le righe cambiate</b> (04/09/2026: prima erano 375.016
/// <c>UPDATE codex_items</c> in due giorni per 20.814 righe). Qui la parte senza database:
/// l'impronta della riga remota e la regola «va riscritta?» di <see cref="SpecchioCodex"/>.
/// </summary>
public class SpecchioCodexRegoleTests
{
    internal static Dictionary<string, object?> Remota(int id = 900001, string codice = "PRV-SPEC-1", string descr = "Vite M6", decimal prezzo = 1.5m) =>
        SpecchioCodex.Riga(new Dictionary<string, object?>
        {
            ["id"] = id, ["codice"] = codice, ["code_forn"] = "CF-" + id, ["fornitore"] = "ACME",
            ["prezzo_forn"] = prezzo, ["iva"] = "22", ["produttore"] = "Bosch", ["data"] = new DateTime(2026, 9, 4),
            ["descr"] = descr, ["note"] = "", ["categoria"] = "Viteria", ["barcode"] = "", ["tipologia"] = "",
            ["extra1"] = "", ["extra2"] = "", ["extra3"] = "", ["code_prod"] = "", ["spec"] = "", ["oper"] = 0,
            ["um"] = "pz", ["ubicazione"] = "", ["codexforn"] = "",
        });

    [Fact]
    public void L_impronta_e_stabile_e_sente_ogni_colonna_copiata()
    {
        string base_ = SpecchioCodex.Impronta(Remota());
        Assert.Equal(64, base_.Length);
        Assert.Equal(base_, SpecchioCodex.Impronta(Remota()));

        foreach (string colonna in SpecchioCodex.Colonne)
        {
            var modificata = Remota();
            modificata[colonna] = colonna switch
            {
                "id" => 900002,
                "prezzo_forn" => 9.99m,
                "data" => new DateTime(2025, 1, 1),
                "oper" => 7,
                _ => "CAMBIATO",
            };
            Assert.NotEqual(base_, SpecchioCodex.Impronta(modificata));
        }
    }

    [Fact]
    public void Null_vuoto_zeri_finali_e_maiuscole_delle_chiavi_non_contano()
    {
        string base_ = SpecchioCodex.Impronta(Remota());

        var conNull = Remota(); conNull["note"] = null;
        var conDbNull = Remota(); conDbNull["note"] = DBNull.Value;
        Assert.Equal(base_, SpecchioCodex.Impronta(conNull));
        Assert.Equal(base_, SpecchioCodex.Impronta(conDbNull));

        Assert.Equal(base_, SpecchioCodex.Impronta(Remota(prezzo: 1.50m)));       // 1.5 e 1.50: stesso prezzo
        Assert.Equal(base_, SpecchioCodex.Impronta(Remota(prezzo: 1.500000m)));

        // Una colonna del remoto che il sync non copia (es. un updated_at) non fa riscrivere.
        var conExtra = Remota(); conExtra["updated_at"] = DateTime.Now;
        Assert.Equal(base_, SpecchioCodex.Impronta(conExtra));

        // Le chiavi arrivano come le ha il remoto: maiuscole o no, è la stessa riga.
        var maiuscole = SpecchioCodex.Riga(Remota().ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value));
        Assert.Equal(base_, SpecchioCodex.Impronta(maiuscole));
        Assert.Equal(1.5m, SpecchioCodex.PrezzoRemoto(maiuscole));
    }

    [Fact]
    public void Si_riscrive_se_cambia_l_impronta_o_se_il_prezzo_locale_manca()
    {
        const string a = "aaaa", b = "bbbb";
        Assert.False(SpecchioCodex.VaRiscritta(a, a, 1.5m, 1.5m));           // uguale: si salta
        Assert.True(SpecchioCodex.VaRiscritta(a, b, 1.5m, 1.5m));            // cambiata
        Assert.True(SpecchioCodex.VaRiscritta(null, a, 1.5m, 1.5m));         // mai copiata (v121 appena applicata)
        Assert.True(SpecchioCodex.VaRiscritta(a, a, 0m, 1.5m));              // prezzo locale a zero, il remoto ce l'ha
        Assert.True(SpecchioCodex.VaRiscritta(a, a, null, 1.5m));
        Assert.False(SpecchioCodex.VaRiscritta(a, a, 0m, 0m));               // nessuno ha il prezzo: niente da fare
        Assert.False(SpecchioCodex.VaRiscritta(a, a, 0m, null));
        Assert.False(SpecchioCodex.VaRiscritta(a, a, 9m, 1.5m));             // il prezzo locale c'è: la CASE non lo toccherebbe
    }

    /// <summary>
    /// Guardiano: ogni colonna dell'impronta è nelle due query, e le due query scrivono
    /// l'impronta. Una colonna copiata ma fuori dall'impronta cambierebbe sul remoto senza
    /// che il sync se ne accorga: la riga resterebbe vecchia per sempre, senza errori.
    /// </summary>
    [Fact]
    public void Le_colonne_dell_impronta_sono_quelle_delle_query()
    {
        foreach (string sql in new[] { CodexSyncService.UpdateSql, CodexSyncService.InsertSql })
        {
            foreach (string colonna in SpecchioCodex.Colonne)
                Assert.True(Regex.IsMatch(sql, $@"@{colonna}\b"), $"@{colonna} manca nella query");
            Assert.Contains("@SyncHash", sql);
        }
        Assert.Contains("synced_at=NOW()", CodexSyncService.UpdateSql);
    }
}

/// <summary>Lo stesso, sul database: due giri uguali scrivono una volta sola.</summary>
[Collection(SchemaCondiviso.Nome)]
public class SpecchioCodexDatabaseTests
{
    private readonly SchemaCondiviso _schema;

    public SpecchioCodexDatabaseTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static Dictionary<string, object?> R(int n, string descr = "Vite M6", decimal prezzo = 1.5m) =>
        SpecchioCodexRegoleTests.Remota(900000 + n, $"PRV-SPEC-{n}", descr, prezzo);

    private static Task<CodexSyncService.Esito> Applica(MySqlConnection c, params Dictionary<string, object?>[] righe) =>
        CodexSyncService.ApplicaAsync(righe, c, null, NullLogger.Instance);

    [FactRichiedeMySql]
    public async Task Il_secondo_giro_uguale_non_riscrive_niente()
    {
        using MySqlConnection c = _schema.Apri();
        var righe = new[] { R(1), R(2), R(3) };

        CodexSyncService.Esito primo = await Applica(c, righe);
        Assert.Equal(new CodexSyncService.Esito(3, 0, 0, 0, 0), primo);
        Assert.Equal(3, c.ExecuteScalar<int>("SELECT COUNT(*) FROM codex_items WHERE remote_id > 900000 AND LENGTH(sync_hash) = 64"));

        CodexSyncService.Esito secondo = await Applica(c, righe);
        Assert.Equal(new CodexSyncService.Esito(0, 0, 0, 3, 0), secondo);
    }

    [FactRichiedeMySql]
    public async Task Solo_la_riga_cambiata_si_riscrive()
    {
        using MySqlConnection c = _schema.Apri();
        await Applica(c, R(1), R(2), R(3));

        CodexSyncService.Esito esito = await Applica(c, R(1), R(2, descr: "Vite M8"), R(3));
        Assert.Equal(new CodexSyncService.Esito(0, 1, 0, 2, 0), esito);
        Assert.Equal("Vite M8", c.ExecuteScalar<string>("SELECT descr FROM codex_items WHERE remote_id = 900002"));
    }

    [FactRichiedeMySql]
    public async Task Il_prezzo_azzerato_in_locale_si_riprende_dal_remoto_anche_se_il_remoto_non_e_cambiato()
    {
        using MySqlConnection c = _schema.Apri();
        var righe = new[] { R(1), R(2) };
        await Applica(c, righe);

        // Qualcuno azzera il prezzo in locale: il giro dopo lo rimette, come faceva la CASE a ogni giro.
        c.Execute("UPDATE codex_items SET prezzo_forn = 0 WHERE remote_id = 900001");
        Assert.Equal(new CodexSyncService.Esito(0, 1, 0, 1, 0), await Applica(c, righe));
        Assert.Equal(1.5m, c.ExecuteScalar<decimal>("SELECT prezzo_forn FROM codex_items WHERE remote_id = 900001"));

        // Un prezzo locale diverso da zero invece resta suo: il sync non lo tocca e non riscrive.
        c.Execute("UPDATE codex_items SET prezzo_forn = 9 WHERE remote_id = 900001");
        Assert.Equal(new CodexSyncService.Esito(0, 0, 0, 2, 0), await Applica(c, righe));
        Assert.Equal(9m, c.ExecuteScalar<decimal>("SELECT prezzo_forn FROM codex_items WHERE remote_id = 900001"));
    }

    [FactRichiedeMySql]
    public async Task Il_codice_nato_in_locale_si_riaggancia_e_si_riscrive_sempre()
    {
        using MySqlConnection c = _schema.Apri();
        c.Execute("INSERT INTO codex_items (remote_id, codice, descr) VALUES (NULL, 'PRV-SPEC-5', 'Nato in locale')");
        int idLocale = c.ExecuteScalar<int>("SELECT id FROM codex_items WHERE codice = 'PRV-SPEC-5'");

        Assert.Equal(new CodexSyncService.Esito(0, 0, 1, 0, 0), await Applica(c, R(5)));
        var riga = c.QuerySingle<(int Id, int? RemoteId, string Descr)>(
            "SELECT id, remote_id, descr FROM codex_items WHERE codice = 'PRV-SPEC-5'");
        Assert.Equal((idLocale, 900005, "Vite M6"), riga);        // stesso id locale, agganciato e riscritto
        Assert.Equal(new CodexSyncService.Esito(0, 0, 0, 1, 0), await Applica(c, R(5)));
    }

    [FactRichiedeMySql]
    public async Task Le_righe_sparite_dal_remoto_si_rimuovono_come_prima()
    {
        using MySqlConnection c = _schema.Apri();
        await Applica(c, R(1), R(2), R(3));

        Assert.Equal(new CodexSyncService.Esito(0, 0, 0, 2, 1), await Applica(c, R(1), R(2)));
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM codex_items WHERE remote_id = 900003"));
    }
}
