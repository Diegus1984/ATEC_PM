namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Stato di una combo: <c>NO</c> = non abilitato (nessuna riga), <c>READ</c> = sola lettura,
/// <c>FULL</c> = lettura e scrittura. Sono le tre voci che l'Excel di Paolo chiama
/// «Non abilitato / solo lettura / lettura e scrittura».
/// </summary>
public static class StatoCombo
{
    public const string No = "NO";
    public const string Read = "READ";
    public const string Full = "FULL";
}

/// <summary>
/// Una delle 9 aree della maschera di Paolo (PIANO-PERMESSI.md §6). Un'area è UNA combo che
/// può comandare PIÙ chiavi: «DDP Comm. + Officine» ne governa due, «Risorse» ne governa una
/// in lettura e una in più in scrittura.
///
/// <para>Le aree stanno sul SERVER e non nel client apposta: sono la stessa cosa che i
/// pacchetti-classe scrivono, e due elenchi da tenere allineati divergono al primo che si
/// dimentica — con l'effetto di una combo che dice una cosa e concede l'altra.</para>
/// </summary>
public class AreaPermessoDto
{
    public string Id { get; set; } = "";
    public string Titolo { get; set; } = "";

    /// <summary>Chiavi che la combo scrive a ogni livello (READ e FULL).</summary>
    public List<string> Chiavi { get; set; } = new();

    /// <summary>
    /// Chiavi concesse SOLO quando la combo è su «scrittura»: è il caso di
    /// <c>resources.edit</c>, che è la chiave di modifica del planner e non ha senso in lettura.
    /// </summary>
    public List<string> ChiaviSoloScrittura { get; set; } = new();

    /// <summary>
    /// <c>true</c> se la combo ha solo due stati (no / sì): Commesse, TimeSheet, Chat e
    /// Documenti non hanno una via di mezzo — o si usano o no.
    /// </summary>
    public bool DueStati { get; set; }

    /// <summary>Come si chiama lo stato «acceso» in questa area: «vede elenco», «carica ore»…</summary>
    public string EtichettaAcceso { get; set; } = "lettura e scrittura";

    /// <summary>Stato attuale della persona su quest'area: NO / READ / FULL.</summary>
    public string Stato { get; set; } = StatoCombo.No;

    /// <summary>Stato che avrebbe applicando la sua classe. Serve alla colonna «diverso dalla classe».</summary>
    public string StatoClasse { get; set; } = StatoCombo.No;

    /// <summary>Almeno una delle chiavi è stata decisa a mano (<c>origin = MANO</c>).</summary>
    public bool AMano { get; set; }

    /// <summary>
    /// Le chiavi dell'area non sono tutte allo stesso stato (può succedere toccandole una per
    /// una da «Funzioni avanzate»). La combo mostra il minimo, ma va detto.
    /// </summary>
    public bool Incoerente { get; set; }
}

/// <summary>Una singola funzione del catalogo, come si vede sulla scheda della persona.</summary>
public class FunzionePermessoDto
{
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Categoria { get; set; } = "navigation";
    public string Stato { get; set; } = StatoCombo.No;
    public string StatoClasse { get; set; } = StatoCombo.No;
    public string Origin { get; set; } = "";
    /// <summary>La funzione è governata da una delle 9 aree: sulla scheda si mostra lì, non fra le avanzate.</summary>
    public string? AreaId { get; set; }
}

/// <summary>Una riga del registro delle modifiche ai permessi di una persona.</summary>
public class StoricoPermessoDto
{
    public int Id { get; set; }
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AccessBefore { get; set; }
    public string? AccessAfter { get; set; }
    public string Origin { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

/// <summary>La scheda permessi di una persona: le 9 aree, il catalogo intero e lo storico.</summary>
public class SchedaPermessiDto
{
    public int EmployeeId { get; set; }
    public string Nome { get; set; } = "";
    public string Username { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Classe = <c>employees.user_role</c>. Da sola non concede niente: riempie le combo.</summary>
    public string Classe { get; set; } = "";
    public string ClasseDisplay { get; set; } = "";

    /// <summary>Reparti di appartenenza (anagrafica): non concedono permessi, servono a capire chi è.</summary>
    public List<string> Reparti { get; set; } = new();

    /// <summary>Ha la riga jolly <c>*</c>: vede tutto, anche le funzioni che non esistono ancora.</summary>
    public bool Jolly { get; set; }

    public List<AreaPermessoDto> Aree { get; set; } = new();
    public List<FunzionePermessoDto> Funzioni { get; set; } = new();
    public List<StoricoPermessoDto> Storico { get; set; } = new();

    /// <summary>Quante funzioni sono diverse da quello che direbbe la sua classe.</summary>
    public int DiverseDallaClasse { get; set; }
}

/// <summary>Riga di elenco della pagina «Permessi»: una persona.</summary>
public class RigaPermessiDto
{
    public int EmployeeId { get; set; }
    public string Nome { get; set; } = "";
    public string Username { get; set; } = "";
    public string Classe { get; set; } = "";
    public string ClasseDisplay { get; set; } = "";
    public List<string> Reparti { get; set; } = new();
    public int Funzioni { get; set; }
    public int DiverseDallaClasse { get; set; }
    /// <summary>Righe decise a mano (<c>origin = MANO</c>): le eccezioni che «Applica template» rispetta (§5.9 rebuild).</summary>
    public int AMano { get; set; }
    public bool Jolly { get; set; }

    /// <summary>
    /// Utenza <b>segnaposto</b> di reparto (<c>[ACQ] Generico</c>, <c>[UTE] Generico</c>…): serve
    /// a imputare ore a nome di un ufficio, non è una persona.
    ///
    /// <para>Ha i suoi permessi e resta nell'elenco, ma non va offerta come modello nel
    /// «Copia da»: lì si cerca un collega vero. Si riconosce dall'utenza, perché in anagrafica
    /// non c'è nessun campo che la distingua da un dipendente (<c>emp_type</c> è
    /// <c>INTERNAL</c> come per tutti).</para>
    /// </summary>
    public bool Segnaposto { get; set; }
}

/// <summary>Cambia una combo (area) o una singola funzione. <c>Stato</c>: NO / READ / FULL.</summary>
public class ImpostaPermessoRequest
{
    public int EmployeeId { get; set; }
    /// <summary>Id dell'area (le 9 combo). Alternativo a <see cref="FeatureKey"/>.</summary>
    public string? AreaId { get; set; }
    /// <summary>Chiave singola (sezione «Funzioni avanzate»). Alternativo a <see cref="AreaId"/>.</summary>
    public string? FeatureKey { get; set; }
    public string Stato { get; set; } = StatoCombo.No;
}

/// <summary>Applica il pacchetto della classe a una o più persone.</summary>
public class ApplicaClasseRequest
{
    public List<int> EmployeeIds { get; set; } = new();
    /// <summary>true = non scrive niente, torna solo l'anteprima di cosa cambierebbe.</summary>
    public bool Anteprima { get; set; }
    /// <summary>
    /// Template da applicare al posto della classe di ciascuno (pagina Master, §5.4 rebuild):
    /// vuoto = ognuno riceve il pacchetto della PROPRIA gerarchia (comportamento storico).
    /// </summary>
    public string? Classe { get; set; }
}

/// <summary>Un profilo/template della pagina Master (§5.4 rebuild).</summary>
public class ClasseDto
{
    public string Classe { get; set; } = "";
    public string Display { get; set; } = "";
    /// <summary>Il pacchetto è la riga jolly <c>*</c>: si applica com'è, non si configura voce per voce.</summary>
    public bool Jolly { get; set; }
    /// <summary>Voci che il pacchetto concede (jolly escluso).</summary>
    public int Voci { get; set; }
}

/// <summary>
/// Scrive una voce del template (pagina Master). <c>Stato = NO</c> = la voce ESCE dal
/// pacchetto (§3.7: nel master «spenta» è un'assenza, il diniego serve alle persone).
/// Salvare il template non cambia nessuno: i grant si muovono solo con «Applica template».
/// </summary>
public class ImpostaPacchettoRequest
{
    public string Classe { get; set; } = "";
    public string FeatureKey { get; set; } = "";
    public string Stato { get; set; } = StatoCombo.No;
}

/// <summary>Una modifica che «Applica classe» farebbe (o ha fatto) su una persona.</summary>
public class CambioPrevistoDto
{
    public int EmployeeId { get; set; }
    public string Nome { get; set; } = "";
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Da { get; set; } = StatoCombo.No;
    public string A { get; set; } = StatoCombo.No;
}

/// <summary>Esito di «Applica classe»: quante persone, quante combo, e l'elenco esatto.</summary>
public class EsitoApplicaClasseDto
{
    public int Persone { get; set; }
    public int Combo { get; set; }
    public List<CambioPrevistoDto> Cambi { get; set; } = new();
    /// <summary>Combo lasciate stare perché decise a mano (<c>origin = MANO</c>).</summary>
    public int RispettateAMano { get; set; }
}

/// <summary>
/// Copia tutta la scheda di un collega: il destinatario diventa un CLONE (§3.6 rebuild) —
/// stesse righe, stessi accessi e <b>stessi origin</b> (riga da template resta CLASSE,
/// eccezione resta MANO: così i futuri «Applica template» sul clonato funzionano come
/// sull'originale). Le righe in più del destinatario vengono tolte.
/// </summary>
public class CopiaPermessiRequest
{
    public int DaEmployeeId { get; set; }
    public int AEmployeeId { get; set; }
    /// <summary>true = non scrive niente, torna l'elenco esatto di cosa cambierebbe (§3.6: anteprima obbligatoria).</summary>
    public bool Anteprima { get; set; }
}

/// <summary>Riporta una funzione al valore della classe (toglie la decisione a mano).</summary>
public class RiallineaPermessoRequest
{
    public int EmployeeId { get; set; }
    /// <summary>Vuoto = riallinea TUTTE le funzioni della persona.</summary>
    public string? FeatureKey { get; set; }
}
