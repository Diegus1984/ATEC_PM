namespace ATEC.PM.Server.Services;

/// <summary>
/// Semaforo di una riga SAL — <b>una regola sola per tutto il gestionale</b> (segnalazioni
/// #114, #117, unificata il 24/08/2026).
///
/// <para>
/// La classificazione viveva scritta <b>due volte</b>, in due <c>CASE</c> quasi uguali dentro
/// <c>/api/sal/summary</c> e <c>/api/sal/prospetto</c>. Finché due copie di una regola restano
/// allineate va tutto bene; il punto è che nessuno se ne accorge quando smettono. È già
/// costato due segnalazioni: la card della Dashboard e poi il pallino del menu mostravano
/// numeri diversi da quelli scritti dentro la pagina SAL, perché li prendevano da una terza
/// parte ancora. Qui la regola è una, e le query la chiedono a lei.
/// </para>
///
/// <para><b>Le classi, in ordine di priorità</b> (mutuamente esclusive: una riga col saldo
/// scaduto conta SOLO come incasso):</para>
/// <list type="number">
/// <item><c>incasso</c> — fattura emessa e non incassata: la data prevista di saldo
/// (data fattura + gg saldo) è passata e il Pagamento non è «Pagata». Indipendente dallo
/// stato di fatturazione, come vuole la regola v10/D11.</item>
/// <item><c>warn</c> — da fatturare e in ritardo: non ancora emessa e la data ipotizzata è
/// arrivata (oggi compreso).</item>
/// <item><c>pre</c> — da fatturare e imminente: non ancora emessa e siamo dal <b>lunedì della
/// settimana precedente</b> a quella della data ipotizzata. Non è «entro 7 giorni»: la
/// differenza fra le due regole è esattamente ciò che faceva divergere i conteggi (#117).</item>
/// <item><c>attesa</c> — emessa e nei termini: non è un allarme, serve al Prospetto per
/// distinguerla da una riga senza semaforo.</item>
/// <item><c>''</c> — niente da segnalare.</item>
/// </list>
///
/// <para><b>Gemello lato client</b>: <c>salAlertState</c> / <c>salIncassoScaduto</c> in
/// <c>atec-pm-web/src/features/commesse/sal-utils.ts</c>, che colorano le righe del foglio SAL
/// di commessa. Se cambia una regola, cambiano tutte e due.</para>
/// </summary>
public static class SalAlert
{
    /// <summary>
    /// L'espressione <c>CASE … END</c> che classifica una riga SAL. Va bene sia dove le
    /// colonne sono nude (<c>FROM sal_rows</c>) sia dove hanno un alias.
    ///
    /// <para>I controlli <c>IS NOT NULL</c> ci sono anche dove la query ha già filtrato le
    /// righe senza data: costano niente, e rendono l'espressione <b>vera in qualunque
    /// contesto</b>, che è la ragione per cui adesso ce n'è una sola. Quello su
    /// <c>gg_saldo</c> in particolare non scatta mai coi dati di oggi — la colonna è
    /// <c>NOT NULL DEFAULT 0</c> — ma regge una LEFT JOIN o uno schema futuro, e toglierlo
    /// non farebbe guadagnare niente.</para>
    ///
    /// <para>🪤 Con <c>gg_saldo = 0</c> (il default) la data di saldo <b>coincide</b> con
    /// quella di fattura: una riga non ancora emessa e già scaduta finisce in <c>incasso</c>,
    /// non in <c>warn</c>, perché l'incasso ha la precedenza. Sorprende, ma è il comportamento
    /// di sempre — facevano così entrambi i CASE di prima, ed è congelato in un test.</para>
    /// </summary>
    /// <param name="alias">Alias della tabella/sottoquery con le colonne di <c>sal_rows</c>
    /// (<c>stato</c>, <c>pagamento</c>, <c>data_fatt</c>, <c>gg_saldo</c>); stringa vuota se
    /// la query le legge senza alias.</param>
    public static string CaseSql(string alias = "")
    {
        string p = string.IsNullOrWhiteSpace(alias) ? "" : $"{alias}.";
        string stato = $"{p}stato";
        string pagamento = $"{p}pagamento";
        string dataFatt = $"{p}data_fatt";
        string ggSaldo = $"{p}gg_saldo";

        // Data prevista di saldo: derivata, mai persistita. NULL se manca la data fattura o i
        // giorni di saldo — e NULL anche quando un gg_saldo abnorme manda DATE_ADD fuori
        // range, che è il caso in cui il confronto deve semplicemente non scattare.
        string dataSaldo = $"DATE_ADD({dataFatt}, INTERVAL {ggSaldo} DAY)";

        // Lunedì della settimana PRECEDENTE a quella della data ipotizzata: si torna al lunedì
        // della sua settimana (WEEKDAY = 0 il lunedì) e si tolgono altri 7 giorni.
        string lunediPrecedente =
            $"DATE_SUB(DATE_SUB({dataFatt}, INTERVAL WEEKDAY({dataFatt}) DAY), INTERVAL 7 DAY)";

        return $@"CASE
            WHEN {pagamento} <> 'Pagata' AND {dataFatt} IS NOT NULL AND {ggSaldo} IS NOT NULL
                 AND {dataSaldo} < CURDATE() THEN 'incasso'
            WHEN {stato} <> 'emessa' AND {dataFatt} IS NOT NULL
                 AND {dataFatt} <= CURDATE() THEN 'warn'
            WHEN {stato} <> 'emessa' AND {dataFatt} IS NOT NULL
                 AND CURDATE() >= {lunediPrecedente} THEN 'pre'
            WHEN {stato} = 'emessa' THEN 'attesa'
            ELSE ''
        END";
    }
}
