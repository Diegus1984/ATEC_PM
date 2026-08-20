using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Controllers;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Segnalazione #88 — «una commessa in stand-by o chiusa si consulta ma non si modifica».
///
/// <para><b>Perché questo test esiste.</b> La regola non vive in un posto solo: vive in ogni
/// azione di scrittura che tocca una commessa, e sono più di cento sparse in una quindicina di
/// controller. Una di esse lasciata scoperta non produce nessun errore, nessuna schermata rotta,
/// nessuna riga di log: produce una commessa chiusa che si lascia modificare da chi non dovrebbe,
/// e lo si scopre — se lo si scopre — mesi dopo guardando un numero che non torna. È lo stesso
/// tipo di guasto silenzioso del backfill della #83, che infatti fu un test a prenderlo.</para>
///
/// <para>Il test enumera <b>per riflessione</b> tutte le azioni di scrittura dei controller che
/// toccano dati di commessa e pretende che ognuna dichiari cosa fa: passa dal cancello
/// (<see cref="RequireProjectWritableAttribute"/>), oppure lo chiama a mano
/// (<see cref="ScritturaControllataAManoAttribute"/>), oppure dichiara di non essere roba di
/// commessa (<see cref="ScritturaNonDiCommessaAttribute"/>). <b>Un'azione nuova senza nessuna
/// delle tre fa fallire il test</b>: è l'unico modo perché chi aggiungerà un endpoint fra sei
/// mesi si accorga che quella decisione esiste.</para>
/// </summary>
public class CancelloCommessaTests
{
    /// <summary>
    /// I controller che toccano i dati di una commessa. Chi non è in elenco non viene
    /// controllato: anagrafiche (Codex, catalogo, clienti), preventivi, amministrazione.
    /// <para>⚠️ Aggiungendo un controller «di commessa» va aggiunto anche qui, altrimenti il
    /// test non lo guarda. È l'unico punto in cui questa lista sta scritta.</para>
    /// </summary>
    private static readonly Type[] ControllerDiCommessa =
    {
        typeof(ProjectsController),
        typeof(ProjectCostingController),
        typeof(ProjectHoursController),
        typeof(TravelController),
        typeof(BudgetVsActualController),
        typeof(CashFlowController),
        typeof(SalController),
        typeof(MilestonesController),
        typeof(CheckListController),
        typeof(MoMController),
        typeof(PhasesController),
        typeof(TimesheetController),
        typeof(WorkRequestsController),
        typeof(ChatController),
        typeof(DdpFeedbackController),
        typeof(DdpRowOffController),
        typeof(PurchaseRfqController),
        typeof(DashboardController),
        typeof(BilancioController),
    };

    [Fact]
    public void OgniScritturaDiCommessa_dichiaraCosaFaDelCancello()
    {
        var scoperte = new List<string>();

        foreach (Type controller in ControllerDiCommessa)
        {
            // Il cancello può stare sulla classe: allora vale per tutte le sue azioni.
            bool cancelloSullaClasse =
                controller.GetCustomAttribute<RequireProjectWritableAttribute>() != null;

            foreach (MethodInfo azione in AzioniDiScrittura(controller))
            {
                if (cancelloSullaClasse) continue;
                if (azione.GetCustomAttribute<RequireProjectWritableAttribute>() != null) continue;
                if (azione.GetCustomAttribute<ScritturaControllataAManoAttribute>() != null) continue;
                if (azione.GetCustomAttribute<ScritturaNonDiCommessaAttribute>() != null) continue;

                scoperte.Add($"{controller.Name}.{azione.Name}");
            }
        }

        Assert.True(scoperte.Count == 0,
            "Queste scritture non dicono cosa fanno del cancello della commessa (#88). Aggiungi "
            + "[RequireProjectWritable], oppure [ScritturaControllataAMano] se il controllo lo fai "
            + "dentro l'azione, oppure [ScritturaNonDiCommessa] se davvero non tocca una commessa:\n  "
            + string.Join("\n  ", scoperte));
    }

    /// <summary>
    /// Le dichiarazioni «non è roba di commessa» devono avere un motivo scritto: è quella frase
    /// che permette, quando qualcosa non torna, di distinguere una scelta da una svista.
    /// </summary>
    [Fact]
    public void LeEsclusioni_hannoTutteUnMotivoScritto()
    {
        var mute = new List<string>();

        foreach (Type controller in ControllerDiCommessa)
        foreach (MethodInfo azione in AzioniDiScrittura(controller))
        {
            string? motivo =
                azione.GetCustomAttribute<ScritturaNonDiCommessaAttribute>()?.Motivo
                ?? azione.GetCustomAttribute<ScritturaControllataAManoAttribute>()?.Motivo;
            if (motivo != null && motivo.Trim().Length < 20)
                mute.Add($"{controller.Name}.{azione.Name}");
        }

        Assert.True(mute.Count == 0,
            "Queste dichiarazioni non spiegano niente:\n  " + string.Join("\n  ", mute));
    }

    /// <summary>
    /// La regola del cancello, presa da sola: <b>solo ATTIVA</b> lascia scrivere senza permesso.
    /// Il valore di questo test è nel caso «stato sconosciuto»: se domani nascesse uno stato
    /// nuovo, deve nascere <i>chiuso</i>, non aperto per distrazione.
    /// </summary>
    [Theory]
    [InlineData(ProjectStatuses.Active, true)]
    [InlineData(ProjectStatuses.Draft, false)]
    [InlineData(ProjectStatuses.OnHold, false)]
    [InlineData(ProjectStatuses.Completed, false)]
    [InlineData(ProjectStatuses.Cancelled, false)]
    [InlineData("", false)]
    [InlineData("STATO_CHE_NON_ESISTE_ANCORA", false)]
    public void SoloLaCommessaAttiva_siLasciaModificareDaTutti(string stato, bool atteso)
    {
        Assert.Equal(atteso, ProjectWriteGuard.StatoLiberoATutti(stato));
    }

    /// <summary>Lo stato arriva dal database: maiuscole, spazi e <c>null</c> non devono ingannare.</summary>
    [Theory]
    [InlineData("active")]
    [InlineData("  ACTIVE  ")]
    public void LoStatoAttivo_siRiconosceComunqueSiaScritto(string stato)
    {
        Assert.True(ProjectWriteGuard.StatoLiberoATutti(stato));
    }

    [Fact]
    public void UnoStatoNullo_nonEUnaCommessaAttiva()
    {
        Assert.False(ProjectWriteGuard.StatoLiberoATutti(null));
    }

    private static IEnumerable<MethodInfo> AzioniDiScrittura(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m =>
                m.GetCustomAttributes<HttpPostAttribute>().Any()
                || m.GetCustomAttributes<HttpPutAttribute>().Any()
                || m.GetCustomAttributes<HttpPatchAttribute>().Any()
                || m.GetCustomAttributes<HttpDeleteAttribute>().Any());
}
