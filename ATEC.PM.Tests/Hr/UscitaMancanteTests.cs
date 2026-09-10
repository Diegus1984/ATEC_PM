using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// L'uscita finale dimenticata (09/09/2026, ordine di Diego sul «Controllo di ieri»: «questo non
/// deve essere stimato: se il dipendente ha dimenticato la timbratura, il sistema deve
/// segnalarlo come anomalia, e dobbiamo poter inviare il sollecito»).
///
/// <para>Il motore VB, con due entrate e una uscita, inventava l'uscita alle 17:00 con
/// l'asterisco e contava otto ore: una giornata a posto che a posto non era. Ora è INCOMPLETA
/// come quella con la sola entrata: anomalia rossa, zero ore, pulsante del sollecito, e la
/// rettifica del dialogo parte già su «Uscita». L'unica eccezione è OGGI: chi è rientrato dalla
/// pausa e non è ancora uscito è semplicemente al lavoro.</para>
/// </summary>
public class UscitaMancanteTests
{
    private static readonly DateTime Ieri = new(2026, 9, 8);
    private static readonly DateTime Oggi = new(2026, 9, 9);

    private static RawPunch T(string orario, string verso) => new(DateTime.Parse(orario), verso, null);

    private static TimesheetDay Calcola(DateTime giorno, DateTime oggi, params RawPunch[] timbrature) =>
        TimesheetEngine.Calcola(giorno, timbrature, oggi, new TimesheetEngine.EmployeeConfig(true));

    /// <summary>
    /// 10/09/2026, Diego: «controlla Vasile». Obreja il 02/09 aveva una sola strisciata,
    /// l'USCITA delle 17:03, e il cartellino diceva «Entrata 17:00, uscita non timbrata»:
    /// l'esatto contrario. Il motore metteva la timbratura unica nella casella dell'entrata
    /// senza guardarne il verso — nove giornate su venti erano così.
    /// </summary>
    [Fact]
    public void Chi_ha_timbrato_solo_l_uscita_non_e_entrato_a_quell_ora()
    {
        TimesheetDay c = Calcola(Ieri, Oggi, T("2026-09-08 17:03", "OUT"));

        Assert.Equal("⚠ INCOMPLETO: Solo uscita", c.Note);
        Assert.True(c.HasAnomaly);
        Assert.Equal("??:??", c.Entrata1);
        Assert.Equal("17:00", c.Uscita1);
        Assert.Equal("17:03", c.RawUscita1);
        Assert.Equal("--:--", c.RawEntrata1);
        // Niente ore: non si sa da che ora, e non si inventa.
        Assert.Equal("0h 0m", c.RegularHours);

        // Quella che manca è l'ENTRATA, ed è quella che HR scriverà su Ecos.
        Assert.Equal("IN", HrAttendanceService.VersoMancante(c.Note));
    }

    [Fact]
    public void Con_la_sola_entrata_resta_l_uscita_a_mancare()
    {
        TimesheetDay c = Calcola(Ieri, Oggi, T("2026-09-08 08:00", "IN"));

        Assert.Equal("⚠ INCOMPLETO: Solo entrata", c.Note);
        Assert.Equal("08:00", c.Entrata1);
        Assert.Equal("??:??", c.Uscita1);
        Assert.Equal("OUT", HrAttendanceService.VersoMancante(c.Note));
    }

    [Fact]
    public void Due_entrate_e_una_uscita_su_un_giorno_passato_sono_una_giornata_incompleta()
    {
        TimesheetDay c = Calcola(Ieri, Oggi,
            T("2026-09-08 07:55", "IN"), T("2026-09-08 12:33", "OUT"), T("2026-09-08 13:19", "IN"));

        Assert.Equal("⚠ INCOMPLETO: Uscita mancante", c.Note);
        Assert.True(c.HasAnomaly);
        // Le tre timbrature vere restano a video, l'uscita che manca lo dice in chiaro.
        Assert.Equal("08:00", c.Entrata1);
        Assert.Equal("12:30", c.Uscita1);
        Assert.Equal("13:30", c.Entrata2);
        Assert.Equal("??:??", c.Uscita2);
        // Niente ore inventate: si contano quando arriva l'uscita vera.
        Assert.Equal("0h 0m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
        Assert.DoesNotContain("*", c.Uscita2);
        Assert.DoesNotContain("Stimata", c.Note);
    }

    [Fact]
    public void Oggi_dopo_il_rientro_dalla_pausa_la_giornata_e_ancora_in_corso()
    {
        // Alle 15 del pomeriggio chi ha timbrato IN-OUT-IN è al lavoro, non ha dimenticato niente.
        TimesheetDay c = Calcola(Oggi, Oggi,
            T("2026-09-09 07:55", "IN"), T("2026-09-09 12:33", "OUT"), T("2026-09-09 13:19", "IN"));

        Assert.Equal("Giornata in corso", c.Note);
        Assert.False(c.HasAnomaly);
        Assert.Equal("07:55", c.RawEntrata1);
        Assert.Equal("13:19", c.RawEntrata2);
    }

    [Fact]
    public void La_giornata_incompleta_si_sollecita_ma_non_se_e_oggi()
    {
        Assert.True(HrDayReminder.Serve("⚠ INCOMPLETO: Uscita mancante", Ieri, Oggi));
        Assert.False(HrDayReminder.Serve("⚠ INCOMPLETO: Uscita mancante", Oggi, Oggi));
    }

    [Fact]
    public void L_email_chiede_l_orario_vero_e_non_parla_piu_di_stima()
    {
        var giornata = new HrDayDto
        {
            WorkDate = Ieri,
            HasData = true,
            Note = "⚠ INCOMPLETO: Uscita mancante",
            RegularHours = "0h 0m",
            Overtime = "0h 0m",
            Raw = new HrDayStageDto { ClockIn1 = "07:55", ClockOut1 = "12:33", ClockIn2 = "13:19", ClockOut2 = "--:--" },
        };

        string corpo = HrDayReminder.Corpo("Gabriele", Ieri, giornata, "");

        Assert.Contains("manca l'uscita di fine giornata", corpo);
        Assert.Contains("Comunica l'orario di uscita", corpo);
        Assert.DoesNotContain("stimato", corpo);
        // Non è il caso «solo entrata»: le tre timbrature ci sono e si dicono.
        Assert.DoesNotContain("Risulta registrata solo l'entrata", corpo);
    }

    [Fact]
    public void La_versione_delle_regole_e_salita_cosi_lo_storico_si_ricalcola()
    {
        // RepairDays ricalcola al primo import le giornate con rules_version più bassa: le
        // vecchie «Stimata 17:00» diventano incomplete da sole, senza reimportare niente.
        Assert.True(TimesheetRules.Version >= 5);
    }
}
