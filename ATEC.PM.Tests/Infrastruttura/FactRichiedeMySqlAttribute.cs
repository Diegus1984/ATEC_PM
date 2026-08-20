namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Come <see cref="FactAttribute"/>, ma il test si <b>salta da solo</b> se non c'è un MySQL
/// raggiungibile, invece di fallire. Serve perché una parte dei test (le migrazioni) prova cose
/// che senza database non si possono provare, e un rosso lì significherebbe «manca MySQL su
/// questa macchina», non «il codice è rotto» — cioè il tipo di rosso che fa smettere di guardare
/// i test.
/// <para>La verifica si fa una volta sola per esecuzione (<see cref="Lazy{T}"/>): la discovery di
/// xUnit costruisce l'attributo per ogni test.</para>
/// </summary>
public sealed class FactRichiedeMySqlAttribute : FactAttribute
{
    private static readonly Lazy<bool> Disponibile = new(DatabaseDiProva.MySqlDisponibile);

    public FactRichiedeMySqlAttribute()
    {
        if (!Disponibile.Value)
            Skip = "MySQL non raggiungibile (imposta ATEC_PM_TEST_CS per indicarne uno): test saltato.";
    }
}
