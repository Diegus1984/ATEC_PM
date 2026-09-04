using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════════════════════
// ANDAMENTO — la sezione con cui il venditore riferisce alla proprietà.
// Questa metà è quella delle OFFERTE; le commesse arrivano dal Bilancio e dal SAL.
// Vedi docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Tutto quello che la pagina Andamento mostra sulle offerte di un anno.</summary>
public class AndamentoOfferteDto
{
    public int Anno { get; set; }

    public AndamentoKpiDto Kpi { get; set; } = new();

    /// <summary>Dodici mesi dell'anno scelto: emesse, prese e aperte a valore.</summary>
    public List<AndamentoMeseDto> Mensile { get; set; } = new();

    /// <summary>Valore emesso per mese negli ultimi cinque anni (una serie per anno).</summary>
    public List<AndamentoSerieAnnoDto> MensileMultiAnno { get; set; } = new();

    public List<AndamentoGruppoDto> PerVenditore { get; set; } = new();
    public List<AndamentoGruppoDto> PerTipo { get; set; } = new();
    /// <summary>Analisi di mercato: Impianto / Intervento / Ricambio / Altro.</summary>
    public List<AndamentoGruppoDto> PerCategoria { get; set; } = new();
    public List<AndamentoGruppoDto> TopClienti { get; set; } = new();

    /// <summary>Le aperte raggruppate per livello di chance (il primo gruppo è «non valutata»).</summary>
    public List<AndamentoChanceDto> PerChance { get; set; } = new();

    /// <summary>Le quindici offerte aperte più grandi: è la lista che si guarda per prima.</summary>
    public List<AndamentoOffertaDto> TopAperte { get; set; } = new();

    /// <summary>
    /// Portafoglio aperto ricostruito mese per mese dal registro modifiche, ultimi 12 mesi.
    /// <b>Vuoto quando il registro non copre il periodo</b> — sullo storico importato non c'è
    /// nessuna riga di log, e disegnare una linea piatta sarebbe una bugia.
    /// </summary>
    public List<AndamentoPortafoglioDto> Portafoglio { get; set; } = new();
    /// <summary>Perché il portafoglio nel tempo è vuoto, quando lo è.</summary>
    public string PortafoglioNota { get; set; } = "";
}

/// <summary>
/// I numeri dell'anno. Le regole ricalcano <c>APP.stats</c> del vecchio applicativo, comprese
/// le sue esclusioni: le <b>bozze</b> non sono offerte emesse, e le righe <b>senza importo</b>
/// non entrano nei valori.
/// </summary>
public class AndamentoKpiDto
{
    /// <summary>Quante righe ci sono in tutto nell'anno, bozze e righe senza importo comprese.</summary>
    public int RigheTotali { get; set; }
    public int Bozze { get; set; }
    /// <summary>Righe escluse dai valori perché senza importo: si dice, non si nasconde.</summary>
    public int SenzaImporto { get; set; }

    public int Emesse { get; set; }
    public decimal ValoreEmesse { get; set; }
    /// <summary>Somma con l'estremo alto della forbice, dove c'è.</summary>
    public decimal ValoreEmesseMax { get; set; }
    /// <summary>Almeno un'offerta ha una forbice di prezzo: solo allora il «fino a» ha senso.</summary>
    public bool HaForbice { get; set; }

    public int Prese { get; set; }
    public decimal ValorePrese { get; set; }
    /// <summary>Somma degli importi d'ordine delle prese a corpo.</summary>
    public decimal ValoreOrdini { get; set; }
    public int PreseACorpo { get; set; }
    public int PreseAConsuntivo { get; set; }
    /// <summary>
    /// Prese senza importo d'ordine e senza «a consuntivo»: sullo storico ce ne sono, ed è un
    /// debito dei dati — la scheda le rifiuta in salvataggio finché non si compila uno dei due.
    /// </summary>
    public int PreseIncomplete { get; set; }

    public int Aperte { get; set; }
    public decimal ValoreAperte { get; set; }
    public decimal ValoreAperteMax { get; set; }

    public int Perse { get; set; }
    public decimal ValorePerse { get; set; }
    public int Sospese { get; set; }
    public decimal ValoreSospese { get; set; }

    /// <summary>Prese su (prese + perse), in percentuale. NULL se non si è ancora chiuso niente.</summary>
    public decimal? ConversionePct { get; set; }

    /// <summary>Σ importo × chance/100 sulle aperte che una chance ce l'hanno.</summary>
    public decimal PortafoglioPonderato { get; set; }
    /// <summary>Quante aperte hanno una chance: senza questo il ponderato non si sa quanto vale.</summary>
    public int ApertConChance { get; set; }
}

public class AndamentoMeseDto
{
    /// <summary>1-12.</summary>
    public int Mese { get; set; }
    public string Etichetta { get; set; } = "";
    public decimal Emesse { get; set; }
    public decimal Prese { get; set; }
    public decimal Aperte { get; set; }
    public int NumeroEmesse { get; set; }
}

public class AndamentoSerieAnnoDto
{
    public int Anno { get; set; }
    /// <summary>Dodici valori, da gennaio a dicembre.</summary>
    public List<decimal> Mesi { get; set; } = new();
}

/// <summary>Un raggruppamento (venditore, tipo, categoria, cliente) con i suoi numeri.</summary>
public class AndamentoGruppoDto
{
    public string Chiave { get; set; } = "";
    public string Etichetta { get; set; } = "";
    public int Emesse { get; set; }
    public decimal ValoreEmesse { get; set; }
    public int Prese { get; set; }
    public decimal ValorePrese { get; set; }
    public int Aperte { get; set; }
    public decimal ValoreAperte { get; set; }
    public decimal? ConversionePct { get; set; }
}

public class AndamentoChanceDto
{
    /// <summary>0-100, oppure -1 per «non valutata».</summary>
    public int Livello { get; set; }
    public string Etichetta { get; set; } = "";
    public int Offerte { get; set; }
    public decimal Valore { get; set; }
    /// <summary>Valore × chance/100: quanto di questo gruppo entra nel portafoglio ponderato.</summary>
    public decimal ValorePonderato { get; set; }
}

/// <summary>Una riga nell'elenco delle aperte più grandi.</summary>
public class AndamentoOffertaDto
{
    public int Id { get; set; }
    public string Numero { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public DateTime? Data { get; set; }
    public decimal? Importo { get; set; }
    public int? Chance { get; set; }
    public string Venditore { get; set; } = "";
}

public class AndamentoPortafoglioDto
{
    /// <summary>Primo giorno del mese.</summary>
    public DateTime Mese { get; set; }
    public string Etichetta { get; set; } = "";
    public decimal Valore { get; set; }
    public int Offerte { get; set; }
}
