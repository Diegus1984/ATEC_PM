using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v75 — SEGNALAZIONE #54: stati della DDP OFFICINE.
// Paolo chiede tre cose insieme, e sono la stessa cosa vista da tre lati:
//  1) CON (consegnato) e COS (costruito) tornano stati veri. La v39 li aveva assorbiti
//     dentro DISP perché sul commerciale la distinzione non serviva; in officina sì:
//     un pezzo comprato fuori ARRIVA, un pezzo fatto in casa VIENE COSTRUITO.
//  2) DISP esce dalla DDP Officine (deciso l'08/08/2026 da Diego: con CON e COS separati
//     «disponibile/consegnato» diventa ambiguo). **Resta intatto sul commerciale**, dove
//     è in uso e dove «disponibile a magazzino» è un'informazione vera.
//  3) i passaggi di stato dell'officina diventano LIBERI: ognuno dei 12 stati va verso
//     tutti gli altri. La matrice non viene svuotata ma riempita — così l'editor di
//     Conf. DDP continua a funzionare e un domani si può rimettere una regola.
// Come conseguenza di (3), RAM/CHEK/VER spariscono dalla tendina dell'officina senza
// essere cancellati: non sono fra le destinazioni ammesse. Sul commerciale restano.
// ⚠️ Trappola del meccanismo (ddp-config.ts): uno stato che non compare MAI come
// `from_key` non è governato e riapre la tendina completa. Per questo la matrice va
// scritta per tutti e 12 gli stati + la riga speciale INIZIO, senza buchi.
public sealed class M075_StatiOfficinaLiberi : IMigrazione
{
    public int Versione => 75;

    public string Descrizione => "DDP Officine: CON e COS separati da DISP, passaggi di stato liberi, DISP e RAM/CHEK/VER fuori dalla lista officina";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // (1) I due stati tornano. ON DUPLICATE: se un DB li avesse ancora spenti dalla
        // v39, li riaccende invece di lasciarli invisibili.
        c.Execute(@"INSERT INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order, is_active) VALUES
            ('CON', 'CONSEGNATO', '#00B050', '#FFFFFF', 6, 1),
            ('COS', 'COSTRUITO',  '#2E7D32', '#FFFFFF', 7, 1)
            ON DUPLICATE KEY UPDATE label = VALUES(label), sort_order = VALUES(sort_order), is_active = 1");

        // Ordine di flusso della #54 (vale per tutte e due le distinte: la tabella è una
        // sola). Gli stati fuori dalla lista officina vanno in coda, non spariscono.
        c.Execute(@"UPDATE ddp_statuses SET sort_order = CASE status_key
                WHEN 'RO' THEN 1 WHEN 'DO' THEN 2  WHEN 'DC' THEN 3  WHEN 'IO' THEN 4
                WHEN 'PAR' THEN 5 WHEN 'CON' THEN 6 WHEN 'COS' THEN 7 WHEN 'DISP' THEN 8
                WHEN 'MIT' THEN 9 WHEN 'ANN' THEN 10 WHEN 'SOSP' THEN 11 WHEN 'SOST' THEN 12
                WHEN 'ASS' THEN 13 WHEN 'RAM' THEN 14 WHEN 'CHEK' THEN 15 WHEN 'VER' THEN 16
                ELSE sort_order END
            WHERE status_key IN ('RO','DO','DC','IO','PAR','CON','COS','DISP','MIT','ANN','SOSP','SOST','ASS','RAM','CHEK','VER')");

        // (2)+(3) Matrice OFFICINA riscritta da zero: 12 stati, tutti verso tutti.
        // I 4 stati che restano fuori (DISP, RAM, CHEK, VER) non compaiono né come
        // partenza né come destinazione: la tendina dell'officina si riduce ai 12.
        string[] officina = { "RO", "DO", "DC", "IO", "PAR", "CON", "COS", "MIT", "ANN", "SOSP", "SOST", "ASS" };
        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type = 'OFFICINA'");

        var archi = new List<object>();
        foreach (string from in officina)
        {
            foreach (string to in officina)
            {
                if (from != to) archi.Add(new { Type = "OFFICINA", From = from, To = to });
            }
        }
        // Riga senza stato: da lì si può andare ovunque, come per gli altri.
        foreach (string to in officina) archi.Add(new { Type = "OFFICINA", From = "INIZIO", To = to });

        c.Execute("INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key) VALUES (@Type, @From, @To)", archi);

        // Le righe officina che erano a DISP diventano COS o CON secondo la natura della
        // lavorazione (interna = costruita, esterna = consegnata): DISP non è più
        // selezionabile lì, e una riga bloccata su uno stato non più in lista non si
        // potrebbe più muovere. Al 08/08/2026 in produzione sono ZERO righe.
        // Si legge SOLO `work_type` (il tipo congelato sulla riga da WorkRequestDdpSync):
        // risalire alla lavorazione collegata costringerebbe a confrontare
        // `project_work_requests.type`, che ha una COLLATION DIVERSA (utf8mb4_unicode_ci
        // contro utf8mb4_0900_ai_ci) e fa fallire il confronto con «Illegal mix of
        // collations». Senza tipo si chiude come CONSEGNATO, che è il caso più comune.
        int rimappate = c.Execute(@"
            UPDATE ddp_officina_items
            SET item_status = CASE
                    WHEN UPPER(TRIM(COALESCE(work_type, ''))) = 'INTERNAL' THEN 'COS'
                    ELSE 'CON' END,
                updated_at = NOW()
            WHERE UPPER(TRIM(COALESCE(item_status, ''))) = 'DISP'");

        // Le aggregazioni sono insiemi di stati: senza questo passo CON e COS non
        // sarebbero contati da nessuna vista, e «Materiale Consegnato» direbbe zero su
        // righe consegnate davvero. A9 (escluso dai totali) NON li tocca: sono costi veri.
        c.Execute(@"INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key)
            SELECT a.id, k.status_key
            FROM ddp_aggregations a
            JOIN (SELECT 'CON' AS status_key UNION ALL SELECT 'COS') k
            WHERE a.code IN ('A1','A2','A7')");

        log.LogInformation(
            "[Migration v75] Stati officina: CON/COS creati, matrice OFFICINA libera su 12 stati, {Rimappate} righe da DISP spostate su CON/COS.",
            rimappate);
    }
}
