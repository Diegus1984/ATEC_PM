using System.Reflection;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Controllers;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Segnalazione #93 — «visibili solo le proprie segnalazioni; la visualizzazione completa resta
/// solo per amministratore e Paolo Zanoni».
///
/// <para><b>Perché questi test esistono.</b> La regola vive in due funzioni statiche del
/// controller — <see cref="BugReportsController.FiltroVisibilitaSql"/> per le query di elenco e
/// conteggio, <see cref="BugReportsController.PuoVedereSegnalazione"/> per il singolo allegato —
/// e i test eseguono QUELLE, non una copia: una copia resterebbe verde mentre la pagina vera
/// mostra le righe sbagliate (stesso criterio di <c>LavorazioniOfficineTests</c> col filtro
/// delle viste DDP). Il guasto che tengono fermo è silenzioso: un filtro allentato non rompe
/// nessuna schermata, fa solo rivedere a tutti le segnalazioni altrui.</para>
/// </summary>
public class VisibilitaSegnalazioniTests
{
    // ── il filtro SQL dell'elenco e dei conteggi ──────────────────────────────

    /// <summary>Chi ha la vista completa non deve avere NESSUNA condizione sull'autore.</summary>
    [Fact]
    public void ConLaVistaCompleta_ilFiltroSparisce()
    {
        Assert.Equal("", BugReportsController.FiltroVisibilitaSql(vedeTutte: true));
    }

    /// <summary>
    /// Senza vista completa il filtro stringe su <c>created_by</c>. Il confronto diretto con
    /// <c>@Me</c> scarta da sé anche le righe con autore NULL (NULL = n non è mai vero):
    /// le segnalazioni orfane le vede solo chi vede tutto.
    /// </summary>
    [Fact]
    public void SenzaVistaCompleta_ilFiltroStringeSullAutore()
    {
        string filtro = BugReportsController.FiltroVisibilitaSql(vedeTutte: false);

        Assert.Contains("b.created_by = @Me", filtro);
        // Inizia con AND: si aggancia a un WHERE già aperto, in GetAll come in GetCounts.
        Assert.StartsWith(" AND ", filtro);
    }

    // ── la regola del singolo accesso (allegati) ──────────────────────────────

    /// <summary>
    /// Propria → sì; altrui → solo con la vista completa; autore NULL (utente rimosso) o utenza
    /// non riconosciuta (me = 0) → solo con la vista completa. L'ultimo caso è il valore del
    /// test: una claim mancante non deve mai aprire le segnalazioni orfane a chiunque.
    /// </summary>
    [Theory]
    [InlineData(false, 7, 7, true)]     // la propria si apre sempre
    [InlineData(false, 5, 7, false)]    // quella di un altro no
    [InlineData(true, 5, 7, true)]      // ... a meno di avere la vista completa
    [InlineData(false, null, 7, false)] // orfana: invisibile senza vista
    [InlineData(true, null, 7, true)]   // orfana con vista: visibile
    [InlineData(false, 7, 0, false)]    // senza utenza riconosciuta niente è "proprio"
    [InlineData(true, 7, 0, true)]      // la vista completa non dipende dalla claim autore
    public void UnaSegnalazione_siApreSoloSePropriaOConLaVista(
        bool vedeTutte, int? createdBy, int me, bool atteso)
    {
        Assert.Equal(atteso,
            BugReportsController.PuoVedereSegnalazione(vedeTutte, createdBy, me));
    }

    // ── chi può togliere un allegato ──────────────────────────────────────────

    /// <summary>
    /// Su una segnalazione convivono i file del segnalatore e quelli che chi gestisce allega
    /// rispondendo. Prima bastava poter modificare la segnalazione — cioè il segnalatore poteva
    /// cancellare anche le foto della risposta, in silenzio. Adesso il file lo toglie chi l'ha
    /// caricato, o chi gestisce il modulo.
    ///
    /// <para>Il caso che vale il test è l'ultimo: allegato <b>senza</b> autore (utenza rimossa)
    /// più utente qualsiasi = no. È volutamente più severo di prima: di un file di provenienza
    /// ignota decide chi ha in mano le segnalazioni.</para>
    /// </summary>
    [Theory]
    [InlineData(false, 7, 7, true)]     // il proprio si toglie
    [InlineData(false, 5, 7, false)]    // quello di un altro no (è il buco che si chiude)
    [InlineData(true, 5, 7, true)]      // ... a meno di gestire le segnalazioni
    [InlineData(true, null, 7, true)]   // orfano: lo toglie chi gestisce
    [InlineData(false, null, 7, false)] // orfano e non gestisco: no
    [InlineData(false, 7, 0, false)]    // senza utenza riconosciuta niente è "mio"
    public void UnAllegato_loTogliechiLhaCaricatoOChiGestisce(
        bool gestisce, int? caricatoDa, int me, bool atteso)
    {
        Assert.Equal(atteso,
            BugReportsController.PuoEliminareAllegato(gestisce, caricatoDa, me));
    }

    // ── il cancello del controller ────────────────────────────────────────────

    /// <summary>
    /// Il cancello di classe resta la SOLA <c>nav.bug_reports</c>. Metterci anche
    /// <c>data.bug_reports_all</c> sbaglierebbe in entrambi i modi possibili: dentro lo stesso
    /// attributo le chiavi sono in OR (aprirebbe la pagina a chi ha solo la vista), come secondo
    /// attributo si sommano in AND (la chiuderebbe a chi vede solo le proprie). La vista completa
    /// è un DI PIÙ dentro le azioni, non un requisito per entrare.
    /// </summary>
    [Fact]
    public void IlCancelloDiClasse_restaSoloLaChiaveDiNavigazione()
    {
        RequireFeatureAttribute? gate =
            typeof(BugReportsController).GetCustomAttribute<RequireFeatureAttribute>();

        Assert.NotNull(gate);
        string[] chiavi = Assert.IsType<string[]>(gate!.Arguments![0]);
        Assert.Equal(new[] { "nav.bug_reports" }, chiavi);
    }

    /// <summary>
    /// Le tre azioni che applicano il filtro devono esistere con questi nomi: se un refactoring
    /// le rinomina, chi lo fa deve passare di qui e ricontrollare che il filtro le segua.
    /// </summary>
    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetCounts")]
    [InlineData("DownloadAttachment")]
    [InlineData("DeleteAttachment")]
    public void LeAzioniFiltrate_esistonoSulController(string nome)
    {
        Assert.NotNull(typeof(BugReportsController)
            .GetMethod(nome, BindingFlags.Public | BindingFlags.Instance));
    }

    // ── archivio: chi lo apre e su cosa ────────────────────────────────────────

    /// <summary>
    /// Archiviare toglie la segnalazione dall'elenco di TUTTI, segnalatore compreso: è la fine
    /// del percorso, non un modo per nascondere l'arretrato. Perciò l'azione vuole la chiave di
    /// gestione — nascondere il pulsante nella pagina non è un permesso, l'endpoint resta
    /// raggiungibile lo stesso.
    /// </summary>
    [Theory]
    [InlineData("Archive")]
    [InlineData("Unarchive")]
    public void ArchivioERipristino_voglionoLaChiaveDiGestione(string nome)
    {
        MethodInfo? azione = typeof(BugReportsController)
            .GetMethod(nome, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(azione);

        RequireFeatureAttribute? gate = azione!.GetCustomAttribute<RequireFeatureAttribute>();
        Assert.NotNull(gate);
        string[] chiavi = Assert.IsType<string[]>(gate!.Arguments![0]);
        Assert.Equal(new[] { "action.manage_bug_reports" }, chiavi);
    }

    /// <summary>
    /// Stessa chiave sul cambio di stato: è l'azione che fa partire la campanella al segnalatore
    /// e scrive la build di risoluzione.
    /// </summary>
    [Fact]
    public void IlCambioDiStato_vuoleLaChiaveDiGestione()
    {
        MethodInfo? azione = typeof(BugReportsController)
            .GetMethod("UpdateStatus", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(azione);

        RequireFeatureAttribute? gate = azione!.GetCustomAttribute<RequireFeatureAttribute>();
        Assert.NotNull(gate);
        string[] chiavi = Assert.IsType<string[]>(gate!.Arguments![0]);
        Assert.Equal(new[] { "action.manage_bug_reports" }, chiavi);
    }
}
