using ATEC.PM.Server.Services.Hr;
using Xunit;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// 10/09/2026, Diego guarda il «Controllo di ieri» del 09/09 e chiede cosa non torna oltre
/// alle righe rosse. Due cose, tutte e due qui dentro:
///
/// <para><b>1.</b> Cimmino aveva timbrato quattro volte (07:48, 13:38, 14:07, 18:14) e il
/// motore ne usava tre: il rientro delle 14:07, a 29 minuti dall'uscita, se lo mangiava il
/// raggruppamento a 30 minuti. La giornata diventava «1 entrata / 2 uscite», con un rientro
/// dedotto alle 14:00 che nessuno aveva timbrato e un'ora piena di pausa al posto della
/// mezz'ora vera — mezz'ora di lavoro persa in silenzio.</para>
///
/// <para><b>2.</b> Chi timbra solo entrata e uscita (nove persone quel giorno) si vedeva
/// l'uscita della sera scritta sotto la colonna della pausa pranzo appena dedotta, mentre
/// sotto l'uscita vera non c'era niente.</para>
/// </summary>
public class PausaBreveTests
{
    private static readonly DateTime Giorno = new(2026, 9, 9);
    private static readonly DateTime Domani = new(2026, 9, 10);

    private static RawPunch T(int ora, int minuto, string verso) =>
        new(Giorno.AddHours(ora).AddMinutes(minuto), verso);

    private static TimesheetDay Calcola(params RawPunch[] timbrature) =>
        TimesheetEngine.Calcola(Giorno, timbrature, Domani);

    [Fact]
    public void Il_rientro_a_meno_di_mezz_ora_dall_uscita_non_si_perde_piu()
    {
        // La giornata vera di Cimmino del 09/09/2026.
        TimesheetDay c = Calcola(
            T(7, 48, "IN"), T(13, 38, "OUT"), T(14, 7, "IN"), T(18, 14, "OUT"));

        // Quattro caselle piene con le timbrature vere: niente più orari con l'asterisco.
        Assert.Equal(("08:00", "13:30", "14:00", "18:00"), (c.Entrata1, c.Uscita1, c.Entrata2, c.Uscita2));
        Assert.Equal("OK", c.Note);
        Assert.False(c.HasAnomaly);

        // E sotto ognuna l'orario da cui viene, il rientro delle 14:07 compreso.
        Assert.Equal(("07:48", "13:38", "14:07", "18:14"),
            (c.RawEntrata1, c.RawUscita1, c.RawEntrata2, c.RawUscita2));

        // La pausa è quella timbrata (13:30-14:00), non l'ora piena inventata: la mezz'ora
        // che mancava torna nel conto delle ore.
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("1h 30m", c.Overtime);
    }

    [Fact]
    public void La_stessa_strisciata_ripetuta_si_scarta_ancora()
    {
        // Due uscite a dieci minuti: è lo stesso gesto, vale la prima. Passa lo stadio dei
        // doppioni (5 minuti) ed è il raggruppamento a doverla togliere.
        TimesheetDay c = Calcola(
            T(8, 0, "IN"), T(12, 0, "OUT"), T(12, 10, "OUT"), T(13, 0, "IN"), T(17, 0, "OUT"));

        Assert.Equal(("08:00", "12:00", "13:00", "17:00"), (c.Entrata1, c.Uscita1, c.Entrata2, c.Uscita2));
        Assert.Equal("OK", c.Note);
        Assert.Equal("8h 0m", c.RegularHours);
    }

    [Fact]
    public void Chi_timbra_solo_entrata_e_uscita_vede_l_uscita_sotto_l_ultima_colonna()
    {
        // La giornata vera di Di Monte del 09/09/2026: due sole timbrature.
        TimesheetDay c = Calcola(T(8, 1, "IN"), T(17, 1, "OUT"));

        Assert.Equal("AUTO_P: Pausa 1h detratta", c.Note);
        Assert.Equal(("08:00", "12:30*", "13:30*", "17:00"), (c.Entrata1, c.Uscita1, c.Entrata2, c.Uscita2));

        // L'uscita timbrata sta sotto l'uscita di fine giornata; sotto la pausa dedotta,
        // che nessuno ha timbrato, non c'è niente.
        Assert.Equal("17:01", c.RawUscita2);
        Assert.Equal("--:--", c.RawUscita1);
        Assert.Equal("08:01", c.RawEntrata1);
        Assert.Equal("--:--", c.RawEntrata2);
        Assert.Equal("8h 0m", c.RegularHours);
    }
}
