using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M116 — Segnalazione #135: un particolare a disegno (101) può essere ricavato da un
/// articolo commerciale (201), il <b>grezzo</b> da comprare. Finché la derivazione restava
/// solo in <c>codex_item_references</c> non la leggeva nessuno: il 101 finiva tutto e solo
/// in DDP Officina e il grezzo non lo ordinava nessuno, senza nessun errore a dirlo.
///
/// <para>Da qui in poi il pezzo è <b>uno solo visto da due griglie</b>: il 101 resta in DDP
/// Officina (lavorazione, disegno, ore) e in DDP Commerciale compare la riga del 201 da
/// ordinare. Servono due colonne su <c>bom_items</c>:</para>
///
/// <list type="bullet">
/// <item><c>raw_codex_code</c> — il codice Codex del 201 di derivazione, <b>senza punti</b>.
/// Valorizzato = questa riga è un grezzo generato dalla derivazione, e quel codice è la sua
/// identità. 🪤 Non si può ritrovare la riga dal <c>part_number</c>: quando il 201 ha
/// esattamente un articolo Danea, <c>part_number</c> diventa il <b>codice del fornitore</b>
/// (stessa regola di <c>RisolviArticoloCommerciale</c>), quindi non somiglia più al Codex.</item>
/// <item><c>raw_auto_qty</c> — la quantità calcolata all'ultimo ricalcolo. Serve a capire se
/// <c>quantity</c> l'ha corretta una persona: da una barra escono più pezzi, e chi compra
/// deve poter scrivere «1» dove il conto 1:1 dice «4». Se le due divergono la quantità è
/// stata corretta a mano e il ricalcolo <b>non la sovrascrive</b>.</item>
/// <item><c>raw_sources</c> — i codici dei 101 che alimentano quel grezzo, per far leggere in
/// griglia «grezzo di 101…» senza rifare i join a ogni GET. È uno snapshot riscritto a ogni
/// ricalcolo. 🪤 Non usare <c>notes</c> per questo: quel campo è di chi compra e riscriverlo
/// a ogni ricalcolo cancellerebbe quello che ci ha messo.</item>
/// <item><c>raw_internal_share</c> — <b>quanta parte del grezzo pesa nel Bilancio</b>, da 0 a 1.
/// In officina il costo di un 101 è <b>un campo solo</b>: se la lavorazione è <i>esterna</i>
/// quel numero è già il prezzo del pezzo <b>finito</b>, materiale compreso, e sommarci anche il
/// grezzo conterebbe il materiale due volte; se è <i>in casa</i> (Internal/Print3D) il costo
/// sono le ore per la tariffa, e il materiale non lo conta nessuno — lì il grezzo è un costo
/// vero che mancava. La quota è la frazione dei 101 in casa fra quelli che chiedono quel
/// grezzo: 1 = tutti in casa, 0 = tutti fuori, in mezzo il caso misto. Il Bilancio moltiplica
/// per questa quota, così <b>correggere a mano la quantità non falsa il conto</b>.</item>
/// </list>
///
/// <para>🪤 <b>La riga del grezzo NON è una riga figlia di composizione</b>:
/// <c>parent_bom_item_id</c> e <c>composition_qty</c> restano NULL. Se avesse
/// <c>composition_qty</c> valorizzata, «comanda il padre» (<see cref="Services.ComposizioneDdp"/>)
/// la moltiplicherebbe una seconda volta a ogni cambio di quantità dell'intestazione del
/// gruppo, dopo che il ricalcolo l'ha già contata attraverso i suoi 101. La quantità del
/// grezzo ha un solo padrone: la somma dei 101 che lo usano.</para>
///
/// <para>Nessun dato da convertire: al 28/08/2026 <c>codex_item_references</c> è vuota in
/// produzione, quindi non esiste ancora nessun grezzo da generare. Le righe si creeranno via
/// via che l'ufficio tecnico compila la derivazione sui 101.</para>
/// </summary>
public sealed class M116_GrezzoDerivazione101 : IMigrazione
{
    public int Versione => 116;

    public string Descrizione =>
        "#135: bom_items.raw_codex_code + raw_auto_qty (grezzo 201 derivato da un 101)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // La tabella della derivazione nasce col bootstrap dello schema, che però su un
        // database già popolato non gira: da qui in avanti ci si appoggia del codice, quindi
        // se ne assicura l'esistenza dove la migrazione arriva davvero. Verificata presente in
        // produzione il 28/08/2026 (0 righe): questa è una rete, non una riparazione.
        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_item_references (
            id INT AUTO_INCREMENT PRIMARY KEY,
            source_codex_id INT NOT NULL,
            ref_codex_id INT NOT NULL,
            ref_type VARCHAR(10) NOT NULL COMMENT '201 o 401',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (source_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (ref_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            UNIQUE KEY uq_source_ref (source_codex_id, ref_type),
            INDEX idx_source (source_codex_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", commandTimeout: 600);

        bool codice = AddColumnIfMissing(
            c, "bom_items", "raw_codex_code", "VARCHAR(20) NULL AFTER atec_code");
        bool quantita = AddColumnIfMissing(
            c, "bom_items", "raw_auto_qty", "DECIMAL(10,3) NULL AFTER raw_codex_code");
        AddColumnIfMissing(
            c, "bom_items", "raw_sources", "VARCHAR(300) NULL AFTER raw_auto_qty");
        AddColumnIfMissing(
            c, "bom_items", "raw_internal_share", "DECIMAL(6,4) NULL AFTER raw_sources");

        // Il ricalcolo cerca i grezzi di UNA commessa: senza indice sarebbe una scansione
        // della distinta di tutte le commesse a ogni salvataggio di riga d'officina.
        CreaIndiceSeManca(c, "bom_items", "idx_bom_raw_codex", "project_id, raw_codex_code");

        log.LogInformation(
            "[v116] Grezzo da derivazione: raw_codex_code {C}, raw_auto_qty {Q}.",
            codice ? "aggiunta" : "già presente",
            quantita ? "aggiunta" : "già presente");
    }
}
