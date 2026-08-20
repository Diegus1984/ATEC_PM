using ATEC.PM.Shared;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>
/// EnsureCatalogo — rebuild permessi, passo 2 (PIANO-PERMESSI-REBUILD.md §12.6).
///
/// <para>Allinea <c>auth_features</c> al catalogo unico (<c>catalogo-permessi.json</c>, embedded
/// in Shared) a ogni avvio, dentro il lock delle migrazioni: <b>registra</b> le chiavi nuove
/// (min_level 3 = solo Admin sotto il motore VECCHIO: una chiave appena nata non apre niente a
/// nessuno nemmeno in un rollback), <b>materializza</b> i micro come chiavi figlie
/// (<c>&lt;chiave&gt;.prices</c>, §12.8.3), <b>migra</b> gli alias una volta sola (§12.8.2),
/// <b>marca</b> le ritirate con <c>retired_at</c> e <b>ripesca</b> chi torna in catalogo.</para>
///
/// <para>Le righe orfane (in tabella ma fuori catalogo) si segnalano e basta: possono essere una
/// chiave tolta per errore — cancellarle butterebbe i grant — o storia da pulire con una
/// migrazione <c>Facoltativa</c> (§12.8.10). I grant non si toccano MAI da qui: registrare una
/// chiave è automatico, concederla è una decisione.</para>
/// </summary>
public static class CatalogoPermessiSync
{
    /// <summary>Livello minimo delle chiavi appena registrate: solo Admin sotto il motore vecchio.</summary>
    private const int LivelloNuoveChiavi = 3;

    /// <summary>Le tabelle di grant in cui un alias va rinominato (il log NON c'è: è storia).</summary>
    private static readonly string[] TabelleGrant =
        { "employee_feature_access", "auth_class_features", "auth_role_features" };

    public sealed record Esito(
        int Nuove, int Rinominate, int Ritirate, int Ripescate, int EtichetteAggiornate,
        IReadOnlyList<string> Orfane)
    {
        public bool NienteDaFare =>
            Nuove == 0 && Rinominate == 0 && Ritirate == 0 && Ripescate == 0 && EtichetteAggiornate == 0;
    }

    public static Esito Allinea(MySqlConnection c, ILogger log) =>
        Allinea(c, log, PermessiCatalogo.Albero);

    public static Esito Allinea(MySqlConnection c, ILogger log, IReadOnlyList<VoceCatalogo> albero)
    {
        // Catalogo malformato = avvio fermo, come una migrazione fallita (§12.8.7). In sviluppo
        // lo stesso errore l'ha già dato la build web (genera-catalogo.mjs) o il censimento.
        IReadOnlyList<string> errori = PermessiCatalogo.Valida(albero);
        if (errori.Count > 0)
            throw new InvalidOperationException(
                "catalogo-permessi.json non valido:\n - " + string.Join("\n - ", errori));

        // ── Il bersaglio: chiave → (etichetta, categoria, ritirata) — micro compresi ─────
        var target = new Dictionary<string, (string Label, string Categoria, bool Ritirata)>(StringComparer.OrdinalIgnoreCase);
        var alias = new List<(string Vecchia, string Nuova)>();

        foreach (VoceCatalogo v in PermessiCatalogo.VociPrimarie(albero))
        {
            target[v.Chiave!] = (v.Label, CategoriaDi(v), v.Ritirata);
            if (v.Alias != null) alias.Add((v.Alias, v.Chiave!));

            // I micro sono chiavi come le altre (§12.1): senza la riga qui, il jolly non li
            // espanderebbe e la scheda admin non li vedrebbe (§12.8.3).
            foreach (string micro in v.Micros)
                target[$"{v.Chiave}.{micro}"] = (EtichettaMicro(v.Label, micro), "data", v.Ritirata);
        }

        int rinominate = MigraAlias(c, log, alias);

        var esistenti = c.Query<RigaFeature>(
                "SELECT feature_key AS Chiave, display_name AS Label, retired_at AS RitirataIl FROM auth_features")
            .ToDictionary(r => r.Chiave, StringComparer.OrdinalIgnoreCase);

        // ── Chiavi nuove: si REGISTRANO (nessun grant: sotto «default nega» restano spente) ──
        int nuove = 0;
        foreach ((string chiave, (string label, string categoria, bool ritirata)) in target)
        {
            if (esistenti.ContainsKey(chiave)) continue;
            nuove += c.Execute(@"
                INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior, retired_at)
                VALUES (@Chiave, @Label, @Categoria, @MinLevel, 'HIDDEN', @RitirataIl)",
                new { Chiave = chiave, Label = label, Categoria = categoria, MinLevel = LivelloNuoveChiavi, RitirataIl = ritirata ? DateTime.Now : (DateTime?)null });
        }

        // ── Etichette: la scheda mostra il nome del catalogo, non quello di anni fa ──────
        int etichette = 0;
        foreach ((string chiave, (string label, _, _)) in target)
        {
            if (!esistenti.TryGetValue(chiave, out RigaFeature? riga) || riga.Label == label) continue;
            etichette += c.Execute(
                "UPDATE auth_features SET display_name = @Label WHERE feature_key = @Chiave",
                new { Label = label, Chiave = chiave });
        }

        // ── Ritirate e ripescate ─────────────────────────────────────────────────────────
        var daRitirare = target.Where(kv => kv.Value.Ritirata)
            .Select(kv => kv.Key)
            .Where(k => esistenti.TryGetValue(k, out RigaFeature? r) && r.RitirataIl == null)
            .ToList();
        int ritirate = daRitirare.Count == 0 ? 0 : c.Execute(
            "UPDATE auth_features SET retired_at = NOW() WHERE feature_key IN @Chiavi",
            new { Chiavi = daRitirare });

        var daRipescare = target.Where(kv => !kv.Value.Ritirata)
            .Select(kv => kv.Key)
            .Where(k => esistenti.TryGetValue(k, out RigaFeature? r) && r.RitirataIl != null)
            .ToList();
        int ripescate = daRipescare.Count == 0 ? 0 : c.Execute(
            "UPDATE auth_features SET retired_at = NULL WHERE feature_key IN @Chiavi",
            new { Chiavi = daRipescare });

        // ── Orfane: in tabella, vive, ma fuori catalogo — si segnalano, non si toccano ───
        List<string> orfane = esistenti.Values
            .Where(r => r.RitirataIl == null && !target.ContainsKey(r.Chiave))
            .Select(r => r.Chiave)
            .OrderBy(k => k)
            .ToList();
        if (orfane.Count > 0)
            log.LogWarning(
                "[EnsureCatalogo] {Quante} chiavi in auth_features ma FUORI dal catalogo (tolte senza alias né ritirata? §12.8.2): {Chiavi}",
                orfane.Count, string.Join(", ", orfane));

        return new Esito(nuove, rinominate, ritirate, ripescate, etichette, orfane);
    }

    /// <summary>
    /// Rinomina una chiave (vecchia → nuova) in <c>auth_features</c> e nelle tabelle di grant,
    /// una volta sola: al giro dopo la vecchia non esiste più e non c'è niente da fare.
    /// <para>Nei grant si usa <c>UPDATE IGNORE</c> + pulizia dei resti: se una persona ha già
    /// la chiave nuova, la sua riga vecchia è ridondante e si elimina — il grant c'è già.</para>
    /// </summary>
    private static int MigraAlias(MySqlConnection c, ILogger log, List<(string Vecchia, string Nuova)> alias)
    {
        int migrate = 0;
        foreach ((string vecchia, string nuova) in alias)
        {
            bool vecchiaEsiste = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM auth_features WHERE feature_key = @K", new { K = vecchia }) > 0;
            if (!vecchiaEsiste) continue; // già migrata in un avvio precedente

            bool nuovaEsiste = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM auth_features WHERE feature_key = @K", new { K = nuova }) > 0;
            if (nuovaEsiste)
            {
                // Convivono: qualcuno ha già registrato la nuova. La vecchia si ritira e basta;
                // i suoi grant si migrano comunque qui sotto.
                c.Execute("UPDATE auth_features SET retired_at = NOW() WHERE feature_key = @K AND retired_at IS NULL",
                    new { K = vecchia });
                log.LogWarning("[EnsureCatalogo] alias {Vecchia}→{Nuova}: esistevano entrambe, la vecchia è stata ritirata.",
                    vecchia, nuova);
            }
            else
            {
                c.Execute("UPDATE auth_features SET feature_key = @Nuova WHERE feature_key = @Vecchia",
                    new { Nuova = nuova, Vecchia = vecchia });
            }

            foreach (string tabella in TabelleGrant)
            {
                c.Execute($"UPDATE IGNORE `{tabella}` SET feature_key = @Nuova WHERE feature_key = @Vecchia",
                    new { Nuova = nuova, Vecchia = vecchia });
                c.Execute($"DELETE FROM `{tabella}` WHERE feature_key = @Vecchia", new { Vecchia = vecchia });
            }

            migrate++;
            log.LogInformation("[EnsureCatalogo] chiave rinominata: {Vecchia} → {Nuova} (grant migrati, log intatto).",
                vecchia, nuova);
        }
        return migrate;
    }

    private static string CategoriaDi(VoceCatalogo v) => v.Kind switch
    {
        "voce" => "navigation",
        "sezione-commessa" => "project",
        "ambito" => "scope",
        _ => v.Chiave!.StartsWith("data.", StringComparison.OrdinalIgnoreCase) ? "data" : "action",
    };

    private static string EtichettaMicro(string labelVoce, string micro) => micro switch
    {
        "prices" => $"{labelVoce} — vede prezzi",
        _ => $"{labelVoce} — {micro}",
    };

    private sealed class RigaFeature
    {
        public string Chiave { get; set; } = "";
        public string Label { get; set; } = "";
        public DateTime? RitirataIl { get; set; }
    }
}
