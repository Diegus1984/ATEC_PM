namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Stato di una voce: <c>NO</c> = non abilitato (nessuna riga, o un diniego esplicito),
/// <c>READ</c> = sola lettura, <c>FULL</c> = lettura e scrittura.
/// </summary>
public static class StatoCombo
{
    public const string No = "NO";
    public const string Read = "READ";
    public const string Full = "FULL";
}

/// <summary>
/// Una singola funzione del catalogo, come si vede sulla scheda della persona.
///
/// <para><b>È lo stato EFFETTIVO</b> (jolly già espanso), non «cosa direbbe la classe»: la
/// matrioska rende l'albero del catalogo e per ogni chiave chiede due cose sole — a che
/// livello è, e se qualcuno l'ha decisa a mano (<c>Origin</c>). Il vecchio «diverso dalla
/// classe» è uscito col rebuild §6: raccontava una differenza al posto di uno stato.</para>
/// </summary>
public class FunzionePermessoDto
{
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Categoria { get; set; } = "navigation";
    public string Stato { get; set; } = StatoCombo.No;
    /// <summary><c>MANO</c> = eccezione decisa a mano, che «Applica template» rispetta.</summary>
    public string Origin { get; set; } = "";
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

/// <summary>La scheda permessi di una persona: il catalogo intero con lo stato, e lo storico.</summary>
public class SchedaPermessiDto
{
    public int EmployeeId { get; set; }
    public string Nome { get; set; } = "";
    public string Username { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Classe = <c>employees.user_role</c>. Da sola non concede niente: sceglie il template.</summary>
    public string Classe { get; set; } = "";
    public string ClasseDisplay { get; set; } = "";

    /// <summary>Reparti di appartenenza (anagrafica): non concedono permessi, servono a capire chi è.</summary>
    public List<string> Reparti { get; set; } = new();

    /// <summary>Ha la riga jolly <c>*</c>: vede tutto, anche le funzioni che non esistono ancora.</summary>
    public bool Jolly { get; set; }

    public List<FunzionePermessoDto> Funzioni { get; set; } = new();
    public List<StoricoPermessoDto> Storico { get; set; } = new();
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

/// <summary>
/// Cambia UNA voce del catalogo sulla persona. <c>Stato</c>: NO / READ / FULL — e <c>NO</c>
/// scrive un diniego, non cancella la riga (§3.7: spegnere non è cancellare).
/// <para>Una chiave alla volta: il vecchio <c>AreaId</c> («una delle 9 aree» che ne comandava
/// due o tre) è uscito col rebuild §6 — la matrioska accende una sezione mandando l'elenco
/// delle sue chiavi, e resta un solo modo di scrivere un permesso.</para>
/// </summary>
public class ImpostaPermessoRequest
{
    public int EmployeeId { get; set; }
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

/// <summary>Esito di «Applica template»: quante persone, quante voci, e l'elenco esatto.</summary>
public class EsitoApplicaClasseDto
{
    public int Persone { get; set; }
    /// <summary>Quante voci cambierebbero (o sono cambiate) in tutto.</summary>
    public int Voci { get; set; }
    public List<CambioPrevistoDto> Cambi { get; set; } = new();
    /// <summary>Voci lasciate stare perché decise a mano (<c>origin = MANO</c>).</summary>
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

/// <summary>«Torna al template»: toglie la decisione a mano e rimette la voce sotto il pacchetto.</summary>
public class RiallineaPermessoRequest
{
    public int EmployeeId { get; set; }
    /// <summary>Vuoto = riporta al template TUTTE le voci della persona.</summary>
    public string? FeatureKey { get; set; }
}
