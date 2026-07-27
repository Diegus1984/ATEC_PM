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
    public static string? Validate(IDbConnection c, string ddpType, string? fromStatus, string? toStatus)
    {
        string from = (fromStatus ?? "").Trim().ToUpperInvariant();
        string to = (toStatus ?? "").Trim().ToUpperInvariant();
        if (to.Length == 0 || from == to) return null;

        string lookup = from.Length == 0 ? StartKey : from;
        var rows = c.Query<string>(
            "SELECT to_key FROM ddp_status_transitions WHERE ddp_type = @Type AND from_key = @From",
            new { Type = ddpType, From = lookup }).ToList();
        if (rows.Count == 0) return null;   // (tipo, stato) non governato dalla matrice

        var allowed = rows.Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Contains(to)) return null;

        if (from.Length == 0)
            return $"Stato iniziale non ammesso per questa distinta: {DdpStatusMap.ToLabel(to)}.";
        return allowed.Count == 0
            ? $"Lo stato {DdpStatusMap.ToLabel(from)} è terminale: nessun avanzamento ammesso."
            : $"Transizione non ammessa dalla matrice stati: {DdpStatusMap.ToLabel(from)} → {DdpStatusMap.ToLabel(to)}.";
    }
}
