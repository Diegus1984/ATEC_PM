using System.Text.RegularExpressions;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Lo schema è scritto due volte: il bootstrap di <c>DbService</c> (i <c>CREATE TABLE</c> che
/// costruiscono un database nuovo) e le migrazioni (che portano avanti quelli esistenti). Su un
/// database nuovo le migrazioni storiche NON girano — vengono solo registrate — quindi se una
/// migrazione aggiunge una colonna o un indice e nessuno lo riporta nel bootstrap, i database
/// nuovi nascono senza. Il 04/09/2026 il confronto con la produzione ha trovato esattamente
/// questo: <c>travel_steps.idx_ts_phase</c> (M079) mancava nel bootstrap.
///
/// <para>Questo test legge il DDL delle migrazioni dai sorgenti (colonne aggiunte, tabelle
/// create, tolte o rinominate, indici creati o tolti, in ordine di versione) e pretende che il database di prova —
/// costruito dal bootstrap — sia nello stato finale che le migrazioni descrivono.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class BootstrapAllineatoTests
{
    private readonly SchemaCondiviso _schema;

    public BootstrapAllineatoTests(SchemaCondiviso schema) => _schema = schema;

    private sealed record Attesa(int Versione, string Cosa, string Tabella, string Nome, bool Presente);

    [FactRichiedeMySql]
    public void Il_bootstrap_contiene_tutto_cio_che_le_migrazioni_aggiungono()
    {
        List<Attesa> attese = LeggiDdlDelleMigrazioni();
        Assert.True(attese.Count >= 60, $"lette solo {attese.Count} istruzioni DDL dalle migrazioni: le regex non vedono più i sorgenti?");

        using MySqlConnection c = _schema.Apri();
        var tabelle = c.Query<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var colonne = c.Query<(string T, string C)>("SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = DATABASE()")
            .Select(x => $"{x.T}.{x.C}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indici = c.Query<(string T, string I)>("SELECT DISTINCT table_name, index_name FROM information_schema.statistics WHERE table_schema = DATABASE()")
            .Select(x => $"{x.T}.{x.I}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Stato finale per oggetto: l'ultima migrazione (in ordine di versione) che lo tocca decide.
        var finale = new Dictionary<string, Attesa>(StringComparer.OrdinalIgnoreCase);
        foreach (Attesa a in attese.OrderBy(a => a.Versione))
            finale[$"{a.Cosa}:{a.Tabella}.{a.Nome}"] = a;

        var difformi = new List<string>();
        foreach (Attesa a in finale.Values.OrderBy(a => a.Versione))
        {
            // Una tabella tolta da una migrazione porta via colonne e indici: non si giudicano.
            if (a.Cosa != "tabella" && finale.TryGetValue($"tabella:{a.Tabella}.", out Attesa? t) && !t.Presente) continue;

            bool c_ = a.Cosa switch
            {
                "tabella" => tabelle.Contains(a.Tabella),
                "colonna" => colonne.Contains($"{a.Tabella}.{a.Nome}"),
                "indice" => indici.Contains($"{a.Tabella}.{a.Nome}"),
                _ => throw new InvalidOperationException(a.Cosa),
            };
            if (c_ != a.Presente)
                difformi.Add($"M{a.Versione:000}: {a.Cosa} {a.Tabella}{(a.Nome.Length > 0 ? "." + a.Nome : "")} dovrebbe {(a.Presente ? "esserci" : "NON esserci")} nel bootstrap");
        }

        Assert.True(difformi.Count == 0,
            "Bootstrap (DbService) e migrazioni non raccontano lo stesso schema — un database nuovo nascerebbe diverso da quelli aggiornati:\n - "
            + string.Join("\n - ", difformi));
    }

    // ── Lettura del DDL dai sorgenti ────────────────────────────────────────────────

    private static readonly Regex Versione = new(@"M(\d{3})_", RegexOptions.Compiled);
    private static readonly Regex AddColumn = new(@"AddColumnIfMissing\(\s*c\s*,\s*""(?<t>\w+)""\s*,\s*""(?<n>\w+)""", RegexOptions.Compiled);
    private static readonly Regex AlterAdd = new(@"ALTER\s+TABLE\s+`?(?<t>\w+)`?\s+ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(?<n>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DropColumn = new(@"ALTER\s+TABLE\s+`?(?<t>\w+)`?\s+DROP\s+COLUMN\s+`?(?<n>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreateTable = new(@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(?<t>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DropTable = new(@"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?`?(?<t>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RenameTable = new(@"(?:RENAME\s+TABLE\s+`?(?<a>\w+)`?\s+TO\s+`?(?<b>\w+)`?|ALTER\s+TABLE\s+`?(?<a2>\w+)`?\s+RENAME\s+(?:TO\s+)?`?(?<b2>\w+)`?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreaIndice = new(@"CreaIndiceSeManca\(\s*c\s*,\s*""(?<t>\w+)""\s*,\s*""(?<n>\w+)""", RegexOptions.Compiled);
    private static readonly Regex EliminaIndice = new(@"EliminaIndiceSePresente\(\s*c\s*,\s*""(?<t>\w+)""\s*,\s*""(?<n>\w+)""", RegexOptions.Compiled);
    private static readonly Regex AlterAddIndex = new(@"ALTER\s+TABLE\s+`?(?<t>\w+)`?\s+ADD\s+(?:UNIQUE\s+)?(?:KEY|INDEX)\s+`?(?<n>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreateIndex = new(@"CREATE\s+(?:UNIQUE\s+)?INDEX\s+`?(?<n>\w+)`?\s+ON\s+`?(?<t>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DropIndex = new(@"ALTER\s+TABLE\s+`?(?<t>\w+)`?\s+DROP\s+(?:KEY|INDEX)\s+`?(?<n>\w+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static List<Attesa> LeggiDdlDelleMigrazioni()
    {
        string cartella = Path.Combine(CartellaServer(), "Migrations");
        var attese = new List<Attesa>();
        foreach (string file in Directory.EnumerateFiles(cartella, "M*_*.cs"))
        {
            Match v = Versione.Match(Path.GetFileName(file));
            if (!v.Success) continue;
            int versione = int.Parse(v.Groups[1].Value);

            // Via i commenti (di riga e XML): il DDL descritto a parole non conta.
            string testo = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//")));
            // Nomi interpolati (`{tabella}`) non si possono verificare: si saltano.
            static bool Statico(Match m) => !m.Value.Contains('{');

            foreach (Match m in AddColumn.Matches(testo).Where(Statico)) attese.Add(new(versione, "colonna", m.Groups["t"].Value, m.Groups["n"].Value, true));
            foreach (Match m in AlterAdd.Matches(testo).Where(Statico)) attese.Add(new(versione, "colonna", m.Groups["t"].Value, m.Groups["n"].Value, true));
            foreach (Match m in DropColumn.Matches(testo).Where(Statico)) attese.Add(new(versione, "colonna", m.Groups["t"].Value, m.Groups["n"].Value, false));
            foreach (Match m in CreateTable.Matches(testo).Where(Statico)) attese.Add(new(versione, "tabella", m.Groups["t"].Value, "", true));
            foreach (Match m in DropTable.Matches(testo).Where(Statico)) attese.Add(new(versione, "tabella", m.Groups["t"].Value, "", false));
            foreach (Match m in RenameTable.Matches(testo).Where(Statico))
            {
                string a = m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["a2"].Value;
                string b = m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value;
                attese.Add(new(versione, "tabella", a, "", false));
                attese.Add(new(versione, "tabella", b, "", true));
            }
            foreach (Match m in CreaIndice.Matches(testo).Where(Statico)) attese.Add(new(versione, "indice", m.Groups["t"].Value, m.Groups["n"].Value, true));
            foreach (Match m in AlterAddIndex.Matches(testo).Where(Statico)) attese.Add(new(versione, "indice", m.Groups["t"].Value, m.Groups["n"].Value, true));
            foreach (Match m in CreateIndex.Matches(testo).Where(Statico)) attese.Add(new(versione, "indice", m.Groups["t"].Value, m.Groups["n"].Value, true));
            foreach (Match m in EliminaIndice.Matches(testo).Where(Statico)) attese.Add(new(versione, "indice", m.Groups["t"].Value, m.Groups["n"].Value, false));
            foreach (Match m in DropIndex.Matches(testo).Where(Statico)) attese.Add(new(versione, "indice", m.Groups["t"].Value, m.Groups["n"].Value, false));
        }
        return attese;
    }

    private static string CartellaServer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, "ATEC.PM.Server");
            if (Directory.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cartella ATEC.PM.Server non trovata risalendo da " + AppContext.BaseDirectory);
    }
}
