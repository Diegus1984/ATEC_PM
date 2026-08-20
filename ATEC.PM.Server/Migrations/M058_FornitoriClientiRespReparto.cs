using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M058_FornitoriClientiRespReparto : IMigrazione
{
    public int Versione => 58;

    public string Descrizione => "Fornitori e Clienti a livello Resp. Reparto";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Revisione livelli del 31/07/2026 (fatta con l'utente prima di portare i
        // permessi in produzione): le anagrafiche Fornitori e Clienti scendono a
        // Resp. Reparto, così l'ufficio acquisti può crearle e correggerle senza
        // passare da un PM. Tutto il resto resta com'era.
        c.Execute(@"
            UPDATE auth_features SET min_level = 1
            WHERE feature_key IN ('nav.fornitori', 'nav.clienti')");

        log.LogInformation("[Migration v58] Anagrafiche Fornitori e Clienti portate al livello 1 (Resp. Reparto)");
    }
}
