using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Il rapporto dell'import dello storico: cosa è entrato, cosa c'era già, cosa è rimasto
/// scollegato. Si guarda una volta sola, ma è l'unica prova che l'import ha fatto quel che
/// doveva — quindi dice i numeri, non «fatto».
/// </summary>
public class SalesOfferImportReport
{
    public bool Simulazione { get; set; }

    // Rubrica
    public int ClientiLetti { get; set; }
    public int ClientiCreati { get; set; }
    public int ClientiArricchiti { get; set; }
    public int ClientiInvariati { get; set; }

    // Offerte
    public int OfferteLette { get; set; }
    public int OfferteInserite { get; set; }
    public int OfferteGiaPresenti { get; set; }
    public int ContattiInseriti { get; set; }

    // Collegamenti
    public int CollegateACliente { get; set; }
    public int CollegateACommessa { get; set; }
    /// <summary>Quante offerte restano senza cliente in rubrica.</summary>
    public int SenzaCliente { get; set; }
    /// <summary>I nomi distinti rimasti scollegati, dal più frequente (primi 50).</summary>
    public List<SalesOfferUnlinkedName> NomiNonCollegati { get; set; } = new();

    /// <summary>Cose andate storte ma non fatali: righe saltate, date impossibili, sigle ignote.</summary>
    public List<string> Avvisi { get; set; } = new();

    public DateTime EseguitoIl { get; set; } = DateTime.Now;
    public int DurataMs { get; set; }
}

/// <summary>Un nome cliente dell'offerta che la rubrica non ha saputo riconoscere.</summary>
public class SalesOfferUnlinkedName
{
    public string Nome { get; set; } = "";
    public int Offerte { get; set; }
}
