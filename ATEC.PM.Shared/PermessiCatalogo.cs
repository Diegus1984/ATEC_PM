using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ATEC.PM.Shared;

/// <summary>
/// Una voce del catalogo unico dei permessi (<c>catalogo-permessi.json</c>, embedded in questo
/// assembly). Contratto polimorfico del PIANO-PERMESSI-REBUILD.md §12.1: ogni cosa governabile
/// è una voce con lo stesso contratto, distinta dal <see cref="Kind"/>.
/// </summary>
public sealed class VoceCatalogo
{
    public string Kind { get; set; } = "";
    public string? Chiave { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Micro dichiarati (oggi solo "prices"); si materializzano come chiavi figlie <c>&lt;chiave&gt;.prices</c> al passo 4.</summary>
    public List<string> Micros { get; set; } = new();
    /// <summary>La chiave non è usata da nessun endpoint: il gate vive solo nel client. Richiede <see cref="Motivo"/>.</summary>
    public bool SoloClient { get; set; }
    public string? Motivo { get; set; }
    /// <summary>Chiave globale da cui il micro prices eredita i grant alla nascita (semina fotografica, §12.8.1).</summary>
    public string? Eredita { get; set; }
    /// <summary>Vecchio nome della chiave: EnsureCatalogo (passo 2) migra le righe una volta sola (§12.8.2).</summary>
    public string? Alias { get; set; }
    /// <summary>Chiave morta: resta registrata per lo storico, esclusa dai controlli d'uso.</summary>
    public bool Ritirata { get; set; }
    /// <summary>Duplicato consapevole: stessa chiave usata da menu e albero commessa, da sdoppiare al passo 3 (§12.4).</summary>
    public bool ChiaveCondivisa { get; set; }
    public string? Nota { get; set; }
    public List<VoceCatalogo> Figli { get; set; } = new();
}

/// <summary>
/// Carica e valida il catalogo unico dei permessi. Unica fonte per: tipo TypeScript generato
/// (genera-catalogo.mjs), censimento (ATEC.PM.Tests) e — dal passo 2 — EnsureCatalogo.
/// </summary>
public static class PermessiCatalogo
{
    public const string NomeRisorsa = "ATEC.PM.Shared.catalogo-permessi.json";

    public static readonly string[] KindNoti = { "sezione", "voce", "sezione-commessa", "azione", "ambito" };
    public static readonly string[] MicroNoti = { "prices" };

    private static readonly Regex FormatoChiave = new(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<VoceCatalogo>> _albero = new(Carica);

    /// <summary>L'albero del catalogo, caricato una volta dall'embedded resource.</summary>
    public static IReadOnlyList<VoceCatalogo> Albero => _albero.Value;

    private static IReadOnlyList<VoceCatalogo> Carica()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(NomeRisorsa)
            ?? throw new InvalidOperationException(
                $"Risorsa '{NomeRisorsa}' non trovata: catalogo-permessi.json non è embedded in ATEC.PM.Shared.");

        using JsonDocument doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement radice = doc.RootElement.GetProperty("albero");
        var opzioni = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return radice.Deserialize<List<VoceCatalogo>>(opzioni)
            ?? throw new InvalidOperationException("catalogo-permessi.json: 'albero' vuoto o malformato.");
    }

    /// <summary>Tutte le voci in profondità, con il padre accanto (null per le radici).</summary>
    public static IEnumerable<(VoceCatalogo Voce, VoceCatalogo? Padre)> Piatte()
    {
        IEnumerable<(VoceCatalogo, VoceCatalogo?)> Scendi(VoceCatalogo voce, VoceCatalogo? padre)
        {
            yield return (voce, padre);
            foreach (VoceCatalogo figlio in voce.Figli)
                foreach (var coppia in Scendi(figlio, voce))
                    yield return coppia;
        }

        foreach (VoceCatalogo radice in Albero)
            foreach (var coppia in Scendi(radice, null))
                yield return coppia;
    }

    /// <summary>
    /// Le chiavi "primarie" del catalogo (senza i duplicati marcati <c>chiaveCondivisa</c>).
    /// </summary>
    public static IEnumerable<VoceCatalogo> VociPrimarie() =>
        Piatte().Select(c => c.Voce).Where(v => v.Chiave != null && !v.ChiaveCondivisa);

    /// <summary>
    /// Valida il catalogo e restituisce l'elenco degli errori (vuoto = valido).
    /// Stesse regole del generatore TypeScript: i due validatori devono restare allineati.
    /// </summary>
    public static IReadOnlyList<string> Valida()
    {
        var errori = new List<string>();
        var viste = Piatte().Select(c => c.Voce).ToList();

        foreach (VoceCatalogo v in viste)
        {
            string dove = v.Chiave ?? v.Label;

            if (!KindNoti.Contains(v.Kind))
                errori.Add($"[{dove}] kind sconosciuto: '{v.Kind}'");

            if (string.IsNullOrWhiteSpace(v.Label))
                errori.Add($"[{v.Chiave}] label mancante");

            bool chiaveRichiesta = v.Kind is "voce" or "azione" or "ambito";
            if (chiaveRichiesta && v.Chiave == null)
                errori.Add($"[{v.Label}] kind '{v.Kind}' senza chiave");
            if (v.Kind == "sezione" && v.Chiave != null)
                errori.Add($"[{v.Chiave}] una sezione (gruppo) non ha chiave propria");
            if (v.Kind == "sezione-commessa" && v.Chiave == null && string.IsNullOrWhiteSpace(v.Nota))
                errori.Add($"[{v.Label}] sezione-commessa senza chiave e senza nota che lo giustifichi");

            if (v.Chiave != null && !FormatoChiave.IsMatch(v.Chiave))
                errori.Add($"[{v.Chiave}] formato chiave non valido (atteso: prefisso.nome_minuscolo)");

            if (v.SoloClient && string.IsNullOrWhiteSpace(v.Motivo))
                errori.Add($"[{dove}] soloClient senza motivo: il motivo è obbligatorio (§12.8.6)");

            if (v.SoloClient && v.Ritirata)
                errori.Add($"[{dove}] soloClient e ritirata insieme non hanno senso");

            foreach (string micro in v.Micros.Where(m => !MicroNoti.Contains(m)))
                errori.Add($"[{dove}] micro sconosciuto: '{micro}'");

            if (v.Micros.Count > 0 && v.Chiave == null)
                errori.Add($"[{v.Label}] micros su una voce senza chiave");

            if (v.Kind is "azione" or "ambito" && v.Figli.Count > 0)
                errori.Add($"[{dove}] kind '{v.Kind}' non può avere figli");
        }

        // Duplicati: ammessi solo come coppia primaria + chiaveCondivisa (§12.4).
        foreach (var gruppo in viste.Where(v => v.Chiave != null).GroupBy(v => v.Chiave!))
        {
            int primarie = gruppo.Count(v => !v.ChiaveCondivisa);
            int condivise = gruppo.Count(v => v.ChiaveCondivisa);
            if (primarie == 0)
                errori.Add($"[{gruppo.Key}] solo occorrenze chiaveCondivisa: manca la voce primaria");
            else if (primarie > 1)
                errori.Add($"[{gruppo.Key}] chiave duplicata {primarie} volte senza chiaveCondivisa");
            if (condivise > 1)
                errori.Add($"[{gruppo.Key}] più di un duplicato chiaveCondivisa");
        }

        // eredita deve puntare a una chiave esistente del catalogo.
        var chiavi = viste.Where(v => v.Chiave != null).Select(v => v.Chiave!).ToHashSet();
        foreach (VoceCatalogo v in viste.Where(v => v.Eredita != null && !chiavi.Contains(v.Eredita!)))
            errori.Add($"[{v.Chiave}] eredita '{v.Eredita}' che non esiste a catalogo");

        return errori;
    }
}
