using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Regola dello specchio prezzi vecchio archivio Danea → Atec_PM (01/09/2026): quali
/// campi seguono il vecchio e quando due prezzi contano come diversi.
///
/// <para>Sta qui, fuori dal servizio, per essere provabile senza Firebird: la parte
/// fragile non e' la scrittura ma il confronto. Un NULL contato come «diverso da 0»
/// rifarebbe lo stesso UPDATE ogni 12 ore, sporcando il registro delle modifiche con
/// articoli che nessuno ha toccato.</para>
/// </summary>
public static class PrezziSpecchio
{
    /// <summary>
    /// I campi che seguono il vecchio archivio, nell'ordine in cui vengono letti.
    ///
    /// <para>Fuori restano di proposito: <c>Extra1</c> (il codice ATEC si codifica in
    /// Atec_PM, il vecchio lo cancellerebbe), <c>IDFornitore</c> (gli ID anagrafica
    /// vengono rimappati al trasferimento: copiarlo alla cieca lo farebbe puntare a
    /// un'altra ditta) e tutto il magazzino (giacenze e prezzi medi appartengono a
    /// questo archivio, non al vecchio).</para>
    /// </summary>
    public static readonly string[] Campi =
        { "PrezzoNettoForn", "PrezzoIvatoForn", "PrezzoNetto1", "PrezzoIvato1" };

    /// <summary>In Danea «prezzo assente» e «prezzo a zero» sono la stessa cosa.</summary>
    public static decimal Valore(decimal? prezzo) => prezzo ?? 0m;

    public static bool Diverso(decimal? vecchio, decimal? nuovo) => Valore(vecchio) != Valore(nuovo);

    /// <summary>
    /// I campi da riscrivere in Atec_PM per questo articolo. Lista vuota = gia'
    /// allineato: l'articolo non si tocca.
    /// </summary>
    public static List<DaneaMirrorChange> Differenze(
        string codArticolo, int idInAtecPm, IReadOnlyList<decimal?> vecchio, IReadOnlyList<decimal?> nuovo)
    {
        if (vecchio.Count != Campi.Length || nuovo.Count != Campi.Length)
            throw new ArgumentException($"Servono {Campi.Length} valori per articolo (i campi di PrezziSpecchio).");

        var differenze = new List<DaneaMirrorChange>();
        for (int i = 0; i < Campi.Length; i++)
        {
            if (!Diverso(vecchio[i], nuovo[i])) continue;
            differenze.Add(new DaneaMirrorChange
            {
                CodArticolo = codArticolo,
                IdInAtecPm = idInAtecPm,
                Campo = Campi[i],
                Prima = Valore(nuovo[i]),
                Dopo = Valore(vecchio[i]),
            });
        }
        return differenze;
    }
}
