using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Abbinamento fra il nome cliente <b>scritto sull'offerta</b> e la rubrica.
///
/// <para><b>Perché serve.</b> Nel vecchio Excel il cliente era testo libero: «ABB», «A.B.B.
/// Robotics», «Abb Robotics Italy S.p.A.» sono la stessa azienda scritta in tre modi. Il registro
/// tiene quel testo com'è (è quello che c'è sull'offerta cartacea) e ci affianca un collegamento
/// alla rubrica, che è ciò su cui poi si raggruppa nell'Andamento.</para>
///
/// <para>Port fedele di <c>keyName</c> e <c>matchClient</c> di <c>mutations.js</c>: stessa
/// normalizzazione, stesse due regole (uguaglianza esatta della chiave, poi contenimento a parole
/// intere <b>solo se il candidato è unico</b>). Sui dati reali collega circa 1.170 offerte su
/// 1.770; il resto si assegna a mano.</para>
/// </summary>
public static class SalesOfferMatching
{
    /// <summary>
    /// Le forme giuridiche che non distinguono un'azienda da un'altra, quindi si buttano via.
    /// Gli spazi facoltativi servono perché la punteggiatura è già diventata spazio: «S.p.A.»
    /// arriva qui come «s p a ».
    /// </summary>
    private static readonly Regex FormeGiuridiche = new(
        @"\b(s\s?p\s?a|s\s?r\s?l\s?s?|s\s?a\s?s|s\s?n\s?c|gmbh|ltd|llc|inc|srl|spa|sas|snc)\b",
        RegexOptions.Compiled);

    private static readonly Regex Punteggiatura = new(@"[&/.,\-()'""+_]", RegexOptions.Compiled);
    private static readonly Regex SpaziMultipli = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// La chiave con cui due nomi si confrontano: minuscolo, senza accenti, punteggiatura
    /// trasformata in spazio, forme giuridiche rimosse, spazi compressi.
    /// <para>Esempio: <c>«ADLEREVO S.p.A. – Stabilimento Pesaro»</c> → <c>«adlerevo stabilimento pesaro»</c>.</para>
    /// </summary>
    public static string KeyName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        // NFD + scarto dei segni diacritici: «Società» → «societa».
        string senzaAccenti = new string(s.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        string t = senzaAccenti.ToLowerInvariant();
        t = Punteggiatura.Replace(t, " ");
        t = FormeGiuridiche.Replace(t, " ");
        return SpaziMultipli.Replace(t, " ").Trim();
    }

    /// <summary>
    /// Cerca in <paramref name="rubrica"/> l'unico cliente che corrisponde a
    /// <paramref name="nome"/>, o <c>null</c>.
    ///
    /// <para>Due regole, in quest'ordine:</para>
    /// <list type="number">
    /// <item>chiave <b>identica</b>: è la stessa azienda scritta in modo diverso;</item>
    /// <item>chiave <b>contenuta a parole intere</b>, nei due versi («abb» dentro «abb robotics
    /// italy», «adlerevo stabilimento pesaro» che contiene «adlerevo») — ma <b>solo se il
    /// candidato è uno</b>. Con due o più candidati non si collega niente: meglio un nome
    /// scollegato, che si assegna a mano, di un'offerta attribuita al cliente sbagliato.</item>
    /// </list>
    ///
    /// <para>Sotto i tre caratteri la seconda regola non si applica: «AB» finirebbe dentro
    /// mezza rubrica.</para>
    /// </summary>
    /// <param name="rubrica">Coppie (id, nome) della rubrica, già caricate una volta sola.</param>
    public static int? MatchClient(IReadOnlyList<(int Id, string Nome)> rubrica, string? nome) =>
        MatchClientPreNormalizzato(rubrica.Select(r => (r.Id, KeyName(r.Nome))).ToList(), nome);

    /// <summary>
    /// Come <see cref="MatchClient"/>, ma con le chiavi della rubrica <b>già calcolate</b>.
    /// <para>Serve all'import: normalizzare 422 nomi per ciascuno dei ~600 nomi distinti delle
    /// offerte vuol dire un quarto di milione di passaggi di espressioni regolari per niente.</para>
    /// </summary>
    public static int? MatchClientPreNormalizzato(IReadOnlyList<(int Id, string Key)> rubrica, string? nome)
    {
        string k = KeyName(nome);
        if (k.Length == 0) return null;

        foreach ((int id, string e0) in rubrica)
            if (e0 == k) return id;

        if (k.Length < 3) return null;

        int? unico = null;
        foreach ((int id, string e) in rubrica)
        {
            if (e == k || e.Length < 3) continue;
            bool contenuto = $" {e} ".Contains($" {k} ", StringComparison.Ordinal)
                          || $" {k} ".Contains($" {e} ", StringComparison.Ordinal);
            if (!contenuto) continue;
            if (unico != null) return null; // più di un candidato: non si indovina
            unico = id;
        }
        return unico;
    }

    /// <summary>
    /// Normalizza un codice commessa del vecchio Excel per confrontarlo con
    /// <c>projects.code</c>: maiuscolo, senza spazi e col punto ricondotto al trattino basso
    /// (nello storico convivono <c>C221221.001</c> e <c>C221111_093</c>, in ATEC PM vince il
    /// secondo).
    /// </summary>
    public static string KeyProjectCode(string? code) =>
        (code ?? "").Trim().ToUpperInvariant().Replace('.', '_').Replace(" ", "");
}
