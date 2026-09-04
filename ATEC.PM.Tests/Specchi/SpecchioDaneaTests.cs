using System.Text.RegularExpressions;
using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Specchi;

/// <summary>
/// Lo specchio anagrafiche Danea → <c>suppliers</c>/<c>customers</c> scrive <b>solo ciò che
/// cambia</b> (04/09/2026: prima erano 40.032 scritture su <c>suppliers</c> in due giorni per
/// 2.214 righe). Qui la parte senza database: come si legge la riga di <c>TAnagrafica</c> e
/// quando una riga locale va riscritta.
/// </summary>
public class SpecchioDaneaRegoleTests
{
    private static Dictionary<string, object?> Riga(string? vat = "IT01234567890", string nome = " ACME srl ", string? note = "n")
        => new()
        {
            ["IDAnagr"] = 42, ["CodAnagr"] = "acme", ["Nome"] = nome, ["Referente"] = "Mario", ["Email"] = "a@b.it",
            ["Pec"] = "pec@b.it", ["Tel"] = "02 123", ["Cell"] = "333", ["Indirizzo"] = "Via Roma 1", ["Cap"] = "20100",
            ["Citta"] = "Milano", ["Prov"] = "MI", ["PartitaIva"] = vat, ["CodiceFiscale"] = "CF", ["PagamentoDefault"] = "RB 30",
            ["FE_CodUfficio"] = "ABCDEFG", ["Note"] = note,
        };

    private static SpecchioDanea.Fornitore Forn(string nome = "ACME", string email = "a@b.it", string note = "n", string cf = "CF") =>
        new("IT1", nome, "Mario", email, "02", "Via Roma 1, 20100 Milano (MI)", cf, note);

    private static SpecchioDanea.FornitoreLocale Loc(SpecchioDanea.Fornitore f) => SpecchioDanea.DopoScrittura(f);

    [Fact]
    public void La_riga_di_TAnagrafica_si_legge_come_prima()
    {
        SpecchioDanea.Fornitore? f = SpecchioDanea.DaFornitore(Riga());
        Assert.NotNull(f);
        Assert.Equal("ACME srl", f!.Nome);                                   // Trim ovunque
        Assert.Equal("Via Roma 1, 20100 Milano (MI)", f.Address);
        Assert.Equal("IT01234567890", f.Vat);

        SpecchioDanea.Cliente? c = SpecchioDanea.DaCliente(Riga());
        Assert.NotNull(c);
        Assert.Equal(42, c!.IDAnagr);
        Assert.Equal("ABCDEFG", c.Sdi);
        Assert.Equal("acme", c.CodAnagr);

        // Senza partita IVA la riga si salta, come da sempre.
        Assert.Null(SpecchioDanea.DaFornitore(Riga(vat: "  ")));
        Assert.Null(SpecchioDanea.DaCliente(Riga(vat: null)));

        // NULL dal database → stringa vuota; IDAnagr a DBNull → null.
        var r = Riga(note: null); r["IDAnagr"] = DBNull.Value;
        Assert.Equal("", SpecchioDanea.DaFornitore(r)!.Note);
        Assert.Null(SpecchioDanea.DaCliente(r)!.IDAnagr);
    }

    [Fact]
    public void L_indirizzo_vuoto_resta_come_da_sempre()
    {
        // Non è bello, ma è così da sempre: «aggiustarlo» riscriverebbe ogni riga senza indirizzo.
        Assert.Equal("()", SpecchioDanea.Indirizzo("", "", "", ""));
        Assert.Equal("()", SpecchioDanea.Indirizzo(null, null, null, null));
        Assert.Equal("Via Roma 1, 20100 Milano (MI)", SpecchioDanea.Indirizzo(" Via Roma 1 ", "20100", "Milano", "MI"));
    }

    [Fact]
    public void Il_fornitore_si_riscrive_solo_se_cambia_una_colonna_che_l_update_tocca()
    {
        SpecchioDanea.Fornitore f = Forn();
        Assert.True(SpecchioDanea.DaRiscrivere(null, f));                        // non c'è: si scrive
        Assert.False(SpecchioDanea.DaRiscrivere(Loc(f), f));                     // uguale: si salta
        Assert.True(SpecchioDanea.DaRiscrivere(Loc(f), Forn(nome: "ACME 2")));
        Assert.True(SpecchioDanea.DaRiscrivere(Loc(f), Forn(email: "x@y.it")));
        Assert.True(SpecchioDanea.DaRiscrivere(Loc(f), Forn(note: "altro")));
        // Il codice fiscale l'UPDATE non lo tocca: cambiarlo non deve far scrivere.
        Assert.False(SpecchioDanea.DaRiscrivere(Loc(f), Forn(cf: "ALTRO")));
        // NULL in locale e stringa vuota dal remoto NON sono uguali: si scrive (una volta).
        Assert.True(SpecchioDanea.DaRiscrivere(new SpecchioDanea.FornitoreLocale("ACME", "Mario", "a@b.it", "02", f.Address, null), Forn()));
    }

    [Fact]
    public void Il_cliente_si_riscrive_solo_se_cambia_una_colonna_che_l_update_tocca()
    {
        SpecchioDanea.Cliente c = SpecchioDanea.DaCliente(Riga())!;
        SpecchioDanea.ClienteLocale loc = SpecchioDanea.DopoScrittura(c);
        Assert.False(SpecchioDanea.DaRiscrivere(loc, c));
        Assert.True(SpecchioDanea.DaRiscrivere(null, c));
        Assert.True(SpecchioDanea.DaRiscrivere(loc, c with { IDAnagr = 43 }));
        Assert.True(SpecchioDanea.DaRiscrivere(loc, c with { Sdi = "XXXXXXX" }));
        // Cellulare e pagamento entrano solo alla nascita della riga: cambiarli non riscrive.
        Assert.False(SpecchioDanea.DaRiscrivere(loc, c with { Cell = "999" }));
        Assert.False(SpecchioDanea.DaRiscrivere(loc, c with { Pagamento = "BB 60" }));
    }

    /// <summary>
    /// Guardiano: le colonne dopo <c>ON DUPLICATE KEY UPDATE</c> sono esattamente quelle
    /// confrontate. Una colonna aggiunta all'UPDATE ma non al confronto cambierebbe in Danea
    /// senza mai arrivare qui — e nessuno se ne accorgerebbe.
    /// </summary>
    [Theory]
    [InlineData("fornitori")]
    [InlineData("clienti")]
    public void Le_colonne_riscritte_dall_update_sono_quelle_confrontate(string chi)
    {
        string sql = chi == "fornitori" ? DaneaSyncService.UpsertFornitoreSql : DaneaSyncService.UpsertClienteSql;
        string[] attese = chi == "fornitori" ? SpecchioDanea.ColonneConfrontateFornitori : SpecchioDanea.ColonneConfrontateClienti;

        string coda = sql[(sql.IndexOf("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal) + "ON DUPLICATE KEY UPDATE".Length)..];
        var colonne = Regex.Matches(coda, @"(\w+)\s*=\s*@\w+").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();

        Assert.Equal(attese.OrderBy(x => x).ToArray(), colonne);
    }
}

/// <summary>Lo stesso, sul database: due giri uguali scrivono una volta sola.</summary>
[Collection(SchemaCondiviso.Nome)]
public class SpecchioDaneaDatabaseTests
{
    private readonly SchemaCondiviso _schema;

    public SpecchioDaneaDatabaseTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static SpecchioDanea.Fornitore Forn(int n, string? nome = null, string email = "a@b.it") =>
        new($"PROVA-SPEC-{n}", nome ?? $"Fornitore {n}", "Mario", email, "02", "Via Roma 1, 20100 Milano (MI)", "CF", "");

    private static SpecchioDanea.Cliente Cli(int n, int idAnagr = 100) =>
        new($"PROVA-SPEC-C{n}", $"Cliente {n}", "Anna", "c@d.it", "pec@d.it", "02", "333", "Via Po 2, 10100 Torino (TO)", "CF", "RB 30", "ABCDEFG", $"cli{n}", idAnagr, "");

    [FactRichiedeMySql]
    public async Task Fornitori_il_secondo_giro_uguale_non_scrive_niente()
    {
        using MySqlConnection c = _schema.Apri();
        var remoti = new[] { Forn(1), Forn(2), Forn(3) };

        Assert.Equal((3, 0), await DaneaSyncService.ApplicaFornitori(remoti, c));
        Assert.Equal((0, 3), await DaneaSyncService.ApplicaFornitori(remoti, c));

        // Cambia una mail: si riscrive quella riga e basta.
        remoti[1] = Forn(2, email: "nuova@b.it");
        Assert.Equal((1, 2), await DaneaSyncService.ApplicaFornitori(remoti, c));
        Assert.Equal("nuova@b.it", c.ExecuteScalar<string>("SELECT email FROM suppliers WHERE vat_number = 'PROVA-SPEC-2'"));
        Assert.Equal((0, 3), await DaneaSyncService.ApplicaFornitori(remoti, c));
    }

    [FactRichiedeMySql]
    public async Task Con_la_stessa_partita_iva_vince_l_ultima_come_da_sempre()
    {
        using MySqlConnection c = _schema.Apri();
        var remoti = new[] { Forn(7, nome: "Prima"), Forn(7, nome: "Ultima") };

        Assert.Equal((2, 0), await DaneaSyncService.ApplicaFornitori(remoti, c));
        Assert.Equal("Ultima", c.ExecuteScalar<string>("SELECT company_name FROM suppliers WHERE vat_number = 'PROVA-SPEC-7'"));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM suppliers WHERE vat_number = 'PROVA-SPEC-7'"));
    }

    [FactRichiedeMySql]
    public async Task Una_colonna_locale_a_null_si_riscrive_una_volta_sola()
    {
        using MySqlConnection c = _schema.Apri();
        c.Execute("INSERT INTO suppliers (company_name, contact_name, email, phone, address, vat_number, fiscal_code, notes, is_active) VALUES ('Fornitore 9', 'Mario', 'a@b.it', '02', 'Via Roma 1, 20100 Milano (MI)', 'PROVA-SPEC-9', 'CF', NULL, 1)");

        var remoti = new[] { Forn(9) };
        Assert.Equal((1, 0), await DaneaSyncService.ApplicaFornitori(remoti, c));   // NULL ≠ '' → una scrittura
        Assert.Equal("", c.ExecuteScalar<string>("SELECT notes FROM suppliers WHERE vat_number = 'PROVA-SPEC-9'"));
        Assert.Equal((0, 1), await DaneaSyncService.ApplicaFornitori(remoti, c));   // da qui è uguale
    }

    [FactRichiedeMySql]
    public async Task Clienti_il_secondo_giro_uguale_non_scrive_niente()
    {
        using MySqlConnection c = _schema.Apri();
        var remoti = new[] { Cli(1), Cli(2) };

        Assert.Equal((2, 0), await DaneaSyncService.ApplicaClienti(remoti, c));
        Assert.Equal((0, 2), await DaneaSyncService.ApplicaClienti(remoti, c));

        remoti[0] = Cli(1, idAnagr: 101);
        Assert.Equal((1, 1), await DaneaSyncService.ApplicaClienti(remoti, c));
        Assert.Equal(101, c.ExecuteScalar<int>("SELECT easyfatt_id FROM customers WHERE vat_number = 'PROVA-SPEC-C1'"));
    }
}
