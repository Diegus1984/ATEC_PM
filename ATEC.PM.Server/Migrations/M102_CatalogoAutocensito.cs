using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Rebuild permessi, passo 2 (PIANO-PERMESSI-REBUILD.md §12.6) — la colonna delle chiavi ritirate.
///
/// <para>Da qui in poi <c>auth_features</c> è una PROIEZIONE del catalogo unico
/// (<c>ATEC.PM.Shared/catalogo-permessi.json</c>): la allinea <c>EnsureCatalogo</c> a ogni avvio,
/// come <c>EnsureViews</c> fa con le viste. Le migrazioni <b>non registrano più chiavi</b> —
/// restano per i grant (chi riceve cosa), che sono decisioni.</para>
///
/// <para>Una chiave ritirata non si cancella (le righe storiche e il log la nominano): si marca
/// con <c>retired_at</c> ed esce da <c>/features/my</c> e dall'espansione del jolly (§12.8.10).</para>
/// </summary>
public sealed class M102_CatalogoAutocensito : IMigrazione
{
    public int Versione => 102;

    public string Descrizione =>
        "auth_features.retired_at: chiavi ritirate dal catalogo unico autocensito (rebuild §12, passo 2)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool aggiunta = AiutiMigrazione.AddColumnIfMissing(c, "auth_features", "retired_at", "DATETIME NULL");

        log.LogInformation(
            "[Migration v102] auth_features.retired_at {Stato}. L'allineamento delle chiavi lo fa EnsureCatalogo a ogni avvio.",
            aggiunta ? "aggiunta" : "già presente");
    }
}
