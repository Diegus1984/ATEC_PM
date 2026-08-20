using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

// Matrice degli avanzamenti di stato DDP (v7, per tipo di distinta): valida i cambi di stato
// delle righe (bom_items = COMMERCIAL, ddp_officina_items = OFFICINA) contro ddp_status_transitions.
// Regole:
//  - stato corrente vuoto (riga nuova) → si valuta la riga speciale 'INIZIO' del tipo
//    (finestra di partenza: sulla commerciale esclude DC — il materiale commerciale si acquista);
//  - stato invariato → ammesso (non è una transizione);
//  - (tipo, stato) SENZA record in matrice → non governato → tutto ammesso
//    (retro-compatibilità con stati custom creati da Conf. DDP e non ancora mappati);
//  - (tipo, stato) governato → ammessi solo i to_key in matrice (il sentinella to_key=''
//    non conta: marca i terminali ANN/SOST).
public static class DdpTransitionService
{
    public const string TypeCommercial = "COMMERCIAL";
    public const string TypeOfficina = "OFFICINA";
    private const string StartKey = "INIZIO";

    /// <summary>
    /// Null se la transizione è ammessa, altrimenti il messaggio d'errore da mostrare all'utente.
    /// </summary>
    /// <param name="cache">
    /// Se c'è, la matrice (poche decine di righe) viene letta una volta sola e tenuta in memoria
    /// finché qualcuno non la modifica da «Conf. DDP». Serve dove questo controllo gira <b>una
    /// volta per riga</b>: aggiudicare una RDO da 200 righe faceva 200 letture identiche.
    /// Ometterla non è un errore: si rilegge dal database.
    /// </param>
    public static string? Validate(IDbConnection c, string ddpType, string? fromStatus, string? toStatus,
        AnagraficheCache? cache = null)
    {
        string from = (fromStatus ?? "").Trim().ToUpperInvariant();
        string to = (toStatus ?? "").Trim().ToUpperInvariant();
        if (to.Length == 0 || from == to) return null;

        string lookup = from.Length == 0 ? StartKey : from;
        List<string> rows = cache == null
            ? LeggiDestinazioni(c, ddpType, lookup)
            : cache.Leggi(Anagrafica.TransizioniDdp, () => LeggiMatrice(c))
                   .TryGetValue(ChiaveMatrice(ddpType, lookup), out List<string>? dalCache)
                        ? dalCache
                        : new List<string>();

        if (rows.Count == 0) return null;   // (tipo, stato) non governato dalla matrice

        var allowed = rows.Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Contains(to)) return null;

        if (from.Length == 0)
            return $"Stato iniziale non ammesso per questa distinta: {DdpStatusMap.ToLabel(to)}.";
        return allowed.Count == 0
            ? $"Lo stato {DdpStatusMap.ToLabel(from)} è terminale: nessun avanzamento ammesso."
            : $"Transizione non ammessa dalla matrice stati: {DdpStatusMap.ToLabel(from)} → {DdpStatusMap.ToLabel(to)}.";
    }

    private static string ChiaveMatrice(string ddpType, string fromKey) =>
        $"{ddpType.ToUpperInvariant()}|{fromKey.ToUpperInvariant()}";

    private static List<string> LeggiDestinazioni(IDbConnection c, string ddpType, string fromKey) =>
        c.Query<string>(
            "SELECT to_key FROM ddp_status_transitions WHERE ddp_type = @Type AND from_key = @From",
            new { Type = ddpType, From = fromKey }).ToList();

    /// <summary>Tutta la matrice in un colpo: "TIPO|DA" → stati raggiungibili.</summary>
    private static IReadOnlyDictionary<string, List<string>> LeggiMatrice(IDbConnection c) =>
        c.Query<(string DdpType, string FromKey, string ToKey)>(
            "SELECT ddp_type AS DdpType, from_key AS FromKey, to_key AS ToKey FROM ddp_status_transitions")
        .GroupBy(r => ChiaveMatrice(r.DdpType, r.FromKey))
        .ToDictionary(g => g.Key, g => g.Select(r => r.ToKey).ToList(), StringComparer.OrdinalIgnoreCase);
}
