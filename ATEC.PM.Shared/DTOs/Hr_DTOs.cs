namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// DTO del modulo HR presenze (PIANO-HR-PRESENZE.md, Fase 1): cartellino mensile,
/// import da EcosAgile, mappatura dipendenti ↔ codici Ecos e rettifiche.
/// </summary>
public class HrCartellinoMeseDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int Anno { get; set; }
    public int Mese { get; set; }

    /// <summary>false = il dipendente non ha ancora <c>ecos_empl_code</c>: le sue timbrature
    /// non possono arrivare, e la pagina lo dice invece di mostrare un mese vuoto.</summary>
    public bool EcosCollegato { get; set; }

    /// <summary>Una riga per OGNI giorno del mese, anche vuoto: la griglia del cartellino
    /// è il calendario, non l'elenco dei giorni lavorati.</summary>
    public List<HrGiornataDto> Giornate { get; set; } = new();
}

public class HrGiornataDto
{
    public DateTime Giorno { get; set; }

    /// <summary>Domenica, festività nazionale, patrono o Lunedì dell'Angelo
    /// (la regola vive in RegoleCartellino.EFestivo, copia unica lato server).</summary>
    public bool Festivo { get; set; }

    /// <summary>true = esiste un cartellino calcolato per questo giorno.</summary>
    public bool HaDati { get; set; }

    /// <summary>Orari a video; l'asterisco segnala un orario messo dal sistema.</summary>
    public string Entrata1 { get; set; } = "";
    public string Uscita1 { get; set; } = "";
    public string Entrata2 { get; set; } = "";
    public string Uscita2 { get; set; } = "";

    /// <summary>«8h 0m», oppure «---» quando la giornata non è calcolabile.</summary>
    public string OreOrdinarie { get; set; } = "";
    public string Straordinario { get; set; } = "";
    public string Pausa { get; set; } = "";

    /// <summary>Solo le fasce CCNL diverse da zero (chiave = lettera della circolare).</summary>
    public Dictionary<string, string> Fasce { get; set; } = new();

    public string Nota { get; set; } = "";
    public bool Anomalia { get; set; }

    /// <summary>Le timbrature grezze del giorno (Ecos + rettifiche), per il dettaglio.</summary>
    public List<HrTimbraturaDto> Timbrature { get; set; } = new();
}

public class HrTimbraturaDto
{
    public long Id { get; set; }
    public DateTime Orario { get; set; }
    public string Verso { get; set; } = "";
    public string Origine { get; set; } = "";
    public string? Motivo { get; set; }
    public string? CreataDa { get; set; }
}

public class HrImportEsitoDto
{
    public bool Successo { get; set; }
    public string Messaggio { get; set; } = "";
    public int TimbratureNuove { get; set; }
    public int TimbratureAggiornate { get; set; }
    public int GiornateRicalcolate { get; set; }

    /// <summary>Codici Ecos visti nell'import ma senza dipendente collegato
    /// («EmplCode — Nome»): si risolvono dalla mappatura.</summary>
    public List<string> NonAbbinati { get; set; } = new();
}

public class HrStatoDto
{
    /// <summary>false = credenziali Ecos assenti in configurazione: niente import.</summary>
    public bool Configurato { get; set; }
    public bool ImportInCorso { get; set; }
    public DateTime? UltimoImport { get; set; }
    public string UltimoEsito { get; set; } = "";
    public long TimbratureTotali { get; set; }
    public long GiornateTotali { get; set; }
    public int DipendentiCollegati { get; set; }
    public int DipendentiAttivi { get; set; }
}

public class HrMappaturaRigaDto
{
    public int EmployeeId { get; set; }
    public string Nome { get; set; } = "";
    public string? EcosEmplCode { get; set; }
}

public class HrBadgeDto
{
    public string EmplCode { get; set; } = "";
    public string Nome { get; set; } = "";
    public bool InForza { get; set; }
}

public class HrBadgesDto
{
    public bool Configurato { get; set; }
    public List<HrBadgeDto> Badges { get; set; } = new();
}

public class HrMappaturaRequest
{
    /// <summary>Vuoto o null = scollega il dipendente da Ecos.</summary>
    public string? EcosEmplCode { get; set; }
}

public class HrRettificaRequest
{
    public int EmployeeId { get; set; }
    public DateTime Orario { get; set; }

    /// <summary>«IN» oppure «OUT», come i VersusCode di Ecos.</summary>
    public string Verso { get; set; } = "";

    /// <summary>Obbligatorio: una rettifica senza motivo non è una cronistoria.</summary>
    public string Motivo { get; set; } = "";
}
