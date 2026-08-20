using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Genera il codice commessa nel formato <c>C{aammgg}.{progressivo}</c> — es. <c>C260731.001</c>:
/// "C" + la data di creazione (aammgg, 2 cifre anno) + un progressivo a 3 cifre che riparte ogni giorno.
/// <para>
/// Il progressivo guarda TUTTE le commesse dello stesso giorno (anche annullate/completate),
/// così un codice non viene mai riusato. Il vecchio formato annuale <c>AT{anno}{NNN}</c> resta
/// valido sullo storico: il LIKE è ancorato al prefisso della giornata e non lo incrocia.
/// </para>
/// </summary>
public static class ProjectCodeGenerator
{
    /// <summary>Prefisso della giornata, punto incluso (es. <c>C260731.</c>).</summary>
    public static string PrefixFor(DateTime day) => $"C{day:yyMMdd}.";

    /// <summary>Prossimo codice libero per la giornata indicata (default: oggi).</summary>
    public static string Next(IDbConnection c, IDbTransaction? tx = null, DateTime? day = null)
    {
        string prefix = PrefixFor(day ?? DateTime.Now);
        // CAST ... AS UNSIGNED: un eventuale suffisso non numerico vale 0 e non blocca il calcolo.
        int maxNum = c.ExecuteScalar<int>(
            @"SELECT COALESCE(MAX(CAST(SUBSTRING(code, @Len + 1) AS UNSIGNED)), 0)
              FROM projects WHERE code LIKE @Pref",
            new { Len = prefix.Length, Pref = prefix + "%" }, tx);
        return $"{prefix}{maxNum + 1:D3}";
    }
}
