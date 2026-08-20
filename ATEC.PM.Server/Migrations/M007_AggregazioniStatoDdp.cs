using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v7: seed aggregazioni di stato DDP (matrice dall'Excel V53.1). Una sola volta: dopo, le modifiche
// dell'utente persistono (NON ri-seedato ad ogni avvio, a differenza di un INSERT IGNORE nella creazione tabelle).
public sealed class M007_AggregazioniStatoDdp : IMigrazione
{
    public int Versione => 7;

    public string Descrizione => "ddp aggregazioni stato";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO ddp_aggregations (code, name, description, kind, sort_order) VALUES
            ('A1','Conteggio per stato','Tutti gli stati conteggiati singolarmente (base di tutte le viste)','ALL',1),
            ('A2','Materiale Consegnato','CON+COS+DISP+ASS+MOD','SET',2),
            ('A3','Mat. Par. Cons.','Parzialmente consegnato/costruito (PAR)','SET',3),
            ('A4','Materiale in Consegna','Righe con Data prev. e stato NON consegnato (finestra/ritardo)','DATED',4),
            ('A5','Stati Avanzamento (7 card)','VER · CHEK · DO · RO · IO · DDP Stop(ANN+SOSP+RAM+SOST) · Sped-Mod(SPED+MOD)','SUBGROUPS',5),
            ('A6','Feedback Acquisti','VER+CHEK+DO+RO+PAR','SET',6),
            ('A7','Feedback Magazzino','CON+COS+DISP+PAR+MOD','SET',7),
            ('A8','Esclusione Dati Mancanti','Stati esclusi dall analisi di completezza','SET',8)");

        var seed = new Dictionary<string, string[]>
        {
            // A1 = «tutti gli stati»: DC e MIT vanno inclusi o le righe da costruire e
            // quelle in trattamento non compaiono in nessuna vista (difetto trovato
            // l'08/08/2026; sui DB già esistenti lo ripara la migrazione v76).
            ["A1"] = new[] { "VER", "DISP", "RO", "DO", "DC", "MIT", "IO", "PAR", "CON", "COS", "ASS", "CHEK", "SPED", "MOD", "ANN", "RAM", "SOSP", "SOST", "ND" },
            ["A2"] = new[] { "CON", "COS", "DISP", "ASS", "MOD" },
            ["A3"] = new[] { "PAR" },
            ["A4"] = new[] { "VER", "RO", "DO", "IO", "PAR", "CHEK", "SPED", "ANN", "RAM", "SOSP", "SOST", "ND" },
            ["A5"] = new[] { "VER", "CHEK", "DO", "RO", "IO", "ANN", "SOSP", "RAM", "SOST", "SPED", "MOD" },
            ["A6"] = new[] { "VER", "CHEK", "DO", "RO", "PAR" },
            ["A7"] = new[] { "CON", "COS", "DISP", "PAR", "MOD" },
            ["A8"] = new[] { "ANN", "SOSP", "RAM", "SOST", "DO", "CHEK", "IO", "RO" }
        };
        foreach (KeyValuePair<string, string[]> kv in seed)
        {
            int aggId = c.ExecuteScalar<int>("SELECT id FROM ddp_aggregations WHERE code=@C", new { C = kv.Key });
            foreach (string st in kv.Value)
                c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,@S)",
                    new { A = aggId, S = st });
        }

        log.LogInformation("[Migration v7] Seed aggregazioni di stato DDP (A1-A8)");
    }
}
