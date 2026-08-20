using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Il cuore della decisione «questa persona può vedere / può scrivere»:
/// <see cref="FeatureAccessService.ConcedeAccesso"/> e
/// <see cref="FeatureAccessService.ConcedeScrittura"/>.
///
/// <para><b>Perché proprio questi due metodi.</b> Sono statici e puri — decidono a partire dalle
/// sole righe di permesso, senza database — e sono la <b>fonte di verità unica</b>: li usa il
/// motore a ogni richiesta, e li usa l'invariante «non ci si chiude fuori» di
/// <c>PermissionAdminService</c>. Se la regola qui cambia significato, cambia per tutti e due
/// insieme, ed è esattamente ciò che deve succedere: una seconda copia della regola scritta in
/// SQL, la prima volta che diverge, farebbe dire all'invariante «resta un amministratore» mentre
/// il motore ne nega l'accesso.</para>
///
/// <para><b>Nota per chi cerca il motore dei permessi.</b> Non è
/// <c>ATEC.PM.Shared/PermissionEngine.cs</c>: quello è il motore del client WPF ritirato il
/// 20/07/2026 e oggi non è referenziato da nessun file di codice (solo da documenti). Ha un
/// fallback opposto a quello attuale — <c>feature non registrata → accesso libero</c> — quindi
/// testarlo darebbe sicurezza su regole che non governano più niente.</para>
/// </summary>
public class RegoleAccessoTests
{
    private const string Funzione = "nav.sal";
    private const string Jolly = FeatureAccessService.JollyKey;   // "*"
    private const string Pieno = FeatureAccessService.AccessFull;  // "FULL"
    private const string Lettura = FeatureAccessService.AccessRead; // "READ"
    private const string Diniego = FeatureAccessService.AccessNegato; // "NO"

    private static Dictionary<string, string> Righe(params (string Chiave, string Accesso)[] righe)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string k, string a) in righe) d[k] = a;
        return d;
    }

    // ── la riga della singola funzione ────────────────────────────────────────

    [Fact]
    public void RigaPiena_concedeVedereEScrivere()
    {
        var grants = Righe((Funzione, Pieno));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.True(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    /// <summary>READ è la sola lettura: si entra nella pagina, non si salva.</summary>
    [Fact]
    public void RigaInLettura_concedeVedereMaNonScrivere()
    {
        var grants = Righe((Funzione, Lettura));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    [Fact]
    public void RigaDiDiniego_nonConcedeNiente()
    {
        var grants = Righe((Funzione, Diniego));

        Assert.False(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    [Fact]
    public void NessunaRigaENessunJolly_nonConcedeNiente()
    {
        var grants = Righe();

        Assert.False(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    // ── il jolly ──────────────────────────────────────────────────────────────

    [Fact]
    public void SoloIlJolly_valeAnchePerUnaFunzioneSenzaRiga()
    {
        var grants = Righe((Jolly, Pieno));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.True(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    /// <summary>
    /// Il jolly vale anche per le funzioni che <b>non esistono ancora</b>: chi amministra non
    /// deve perdere l'accesso a ogni pagina nuova che viene aggiunta.
    /// </summary>
    [Fact]
    public void IlJolly_valeAnchePerUnaFunzioneMaiVista()
    {
        var grants = Righe((Jolly, Pieno));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, "nav.funzione_inventata_domani"));
    }

    /// <summary>
    /// <b>La regola che rende possibili le eccezioni</b>: chi vede tutto, meno una cosa.
    /// Se si rompe, il diniego per persona sparisce e qualcuno vede quello che non deve —
    /// senza nessun errore da nessuna parte. È il caso degli Acquisti col Timesheet spento.
    /// </summary>
    [Fact]
    public void IlDiniegoSullaSingolaFunzione_vinceSulJolly()
    {
        var grants = Righe((Jolly, Pieno), (Funzione, Diniego));

        Assert.False(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
        // …ma su tutto il resto il jolly continua a valere.
        Assert.True(FeatureAccessService.ConcedeAccesso(grants, "nav.commesse"));
    }

    /// <summary>Anche al contrario: la riga specifica in lettura limita un jolly pieno.</summary>
    [Fact]
    public void LaRigaInLettura_limitaIlJollyPieno()
    {
        var grants = Righe((Jolly, Pieno), (Funzione, Lettura));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    /// <summary>E una riga piena vale anche se il jolly è un diniego.</summary>
    [Fact]
    public void LaRigaPiena_vinceSulJollyNegato()
    {
        var grants = Righe((Jolly, Diniego), (Funzione, Pieno));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.True(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    [Fact]
    public void JollyInSolaLettura_faVedereMaNonScrivereOvunque()
    {
        var grants = Righe((Jolly, Lettura));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.False(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    // ── forma dei dati ────────────────────────────────────────────────────────

    /// <summary>
    /// Le righe arrivano dal database e passano per l'interfaccia: maiuscole e minuscole non
    /// devono contare, né sul nome della funzione né sul valore.
    /// </summary>
    [Theory]
    [InlineData("full")]
    [InlineData("Full")]
    [InlineData("FULL")]
    public void IlValoreDellAccesso_nonGuardaMaiuscoleEMinuscole(string valore)
    {
        var grants = Righe((Funzione, valore));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, Funzione));
        Assert.True(FeatureAccessService.ConcedeScrittura(grants, Funzione));
    }

    [Theory]
    [InlineData("no")]
    [InlineData("No")]
    [InlineData("NO")]
    public void IlDiniego_nonGuardaMaiuscoleEMinuscole(string valore)
    {
        Assert.True(FeatureAccessService.Negato(valore));
        Assert.False(FeatureAccessService.ConcedeAccesso(Righe((Funzione, valore)), Funzione));
    }

    [Fact]
    public void IlNomeDellaFunzione_nonGuardaMaiuscoleEMinuscole()
    {
        var grants = Righe(("NAV.SAL", Pieno));

        Assert.True(FeatureAccessService.ConcedeAccesso(grants, "nav.sal"));
    }

    /// <summary>Un valore sconosciuto non è un diniego: solo <c>NO</c> nega.</summary>
    [Fact]
    public void UnValoreSconosciuto_nonVieneLettoComeDiniego()
    {
        Assert.False(FeatureAccessService.Negato(""));
        Assert.False(FeatureAccessService.Negato(null));
        Assert.False(FeatureAccessService.Negato("BOH"));
    }
}
