using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Il nome del database di prova deve stare dentro al <b>lock delle migrazioni</b>.
///
/// <para><c>DbService.NomeLockMigrazioni</c> compone <c>atec_pm_migrate:</c> + nome del database
/// e <b>tronca a 64</b>, perché oltre quella lunghezza MySQL rifiuta il nome del lock. La coda
/// del nome è l'orario coi millisecondi: è l'unica cosa che distingue due database dello stesso
/// test. Se il nome cresce, il troncamento si mangia proprio quella, e due corse parallele
/// finiscono sullo stesso lock: una delle due non riesce a prendere il lock e <b>si ferma
/// invece di migrare</b>.</para>
///
/// <para>Sfiorato il 24/08/2026 aggiungendo la data al nome per rendere leggibili i residui:
/// si passava da 61 a 70 caratteri. La data è stata tolta e l'età dei residui si legge da
/// <c>information_schema.tables.create_time</c>. Questo test è qui perché la prossima volta
/// il conto lo faccia la suite, non chi rilegge.</para>
/// </summary>
public class NomeDatabaseDiProvaTests
{
    /// <summary>Il suffisso più lungo in uso nella suite: è lui a decidere il caso peggiore.</summary>
    private const string SuffissoPiuLungo = "ordine_commesse_chiusa";

    [Fact]
    public void IlNomePiuLungo_nonFaTroncareIlLockDelleMigrazioni()
    {
        string nome = DatabaseDiProva.ComponiNome(SuffissoPiuLungo, new DateTime(2026, 8, 24, 23, 59, 59, 999));
        string lockName = DbService.NomeLockMigrazioni(nome);

        Assert.Equal($"atec_pm_migrate:{nome}", lockName);   // cioè: NON troncato
        Assert.True(lockName.Length <= 64, $"lock di {lockName.Length} caratteri: MySQL si ferma a 64");
    }

    /// <summary>
    /// La coda che distingue due database dello stesso test (l'orario coi millisecondi) deve
    /// sopravvivere al lock: è la garanzia che due corse parallele non si contendano lo stesso.
    /// </summary>
    [Fact]
    public void DueDatabaseDelloStessoTest_hannoLockDiversi()
    {
        var t1 = new DateTime(2026, 8, 24, 12, 0, 0, 100);
        var t2 = new DateTime(2026, 8, 24, 12, 0, 0, 101);

        string l1 = DbService.NomeLockMigrazioni(DatabaseDiProva.ComponiNome(SuffissoPiuLungo, t1));
        string l2 = DbService.NomeLockMigrazioni(DatabaseDiProva.ComponiNome(SuffissoPiuLungo, t2));

        Assert.NotEqual(l1, l2);
    }

    /// <summary>Il prefisso è quello che lo spazzino cerca: se cambia, i residui non li trova più.</summary>
    [Fact]
    public void IlPrefisso_restaQuelloCheCercaLoSpazzino()
    {
        Assert.StartsWith("atec_pm_test_", DatabaseDiProva.ComponiNome("qualsiasi", DateTime.UtcNow));
    }
}
