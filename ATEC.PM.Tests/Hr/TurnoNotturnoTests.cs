using System.Globalization;
using ATEC.PM.Server.Services.Hr;
using Xunit;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il <b>terzo turno</b>: la casistica che il motore VB non aveva mai visto.
///
/// <para><b>La regola aziendale</b> (CCNL metalmeccanici PMI, decisa da Diego il
/// 01/09/2026): il turno di notte vale per il giorno in cui <b>comincia</b>. Chi entra alle
/// 22 di mercoledì ed esce alle 6 di giovedì ha fatto la giornata di mercoledì — otto ore —
/// e lo straordinario si conta su quelle otto, non su due mezze giornate che da sole non
/// superano mai la soglia.</para>
///
/// <para>I casi non sono inventati: sono le due notti di <b>Monge</b> (19→20 e 20→21 agosto
/// 2026) e le tre di <b>Sinapi</b> (16→17, 17→18, 18→19), come stanno nei grezzi di
/// produzione. Prima il 19 valeva zero ore con l'anomalia «solo entrata», e il 20 — letto
/// come 06:00→22:30 — regalava sette ore e mezza di straordinario che nessuno aveva
/// lavorato.</para>
///
/// <para>L'altra metà di questi test è il contrario: <b>quando NON è una notte</b>. Una
/// strisciata dimenticata non deve diventare un turno, e senza contesto il motore deve
/// comportarsi esattamente come prima — è quello che tiene in piedi il banco di prova delle
/// 379 giornate.</para>
/// </summary>
public class TurnoNotturnoTests
{
    /// <summary>Un giorno lontano da quelli calcolati: nessuna «giornata in corso» di mezzo.</summary>
    private static readonly DateTime Oggi = new(2026, 9, 1);

    private static DateTime Ora(string valore) =>
        DateTime.Parse(valore, CultureInfo.InvariantCulture);

    private static RawPunch T(string orario, string verso) => new(Ora(orario), verso);

    private static TimesheetDay Calcola(string giorno, RawPunch[] timbrature, NightContext? notte = null) =>
        TimesheetEngine.Calcola(Ora(giorno), timbrature, Oggi, null, notte);

    // ── Il turno di notte vale per il giorno in cui comincia ──────────────────

    [Fact]
    public void Il_giorno_che_apre_la_notte_si_prende_il_turno_intero()
    {
        // Monge, mercoledì 19/08: entra alle 19:46 ed esce alle 06:03 del 20. Dieci ore
        // filate, tutte sul mercoledì — e le due oltre le otto sono straordinario.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 19:46", "IN") },
            new NightContext(Tomorrow: new[] { T("2026-08-20 06:03", "OUT") }));

        Assert.Equal("20:00", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("06:00", c.Uscita2);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("2h 0m", c.Overtime);
        Assert.Equal("0h 0m", c.BreakTime);
        Assert.False(c.HasAnomaly);
        Assert.StartsWith("Turno notturno", c.Note);

        // Le due ore di straordinario sono in fondo alla notte: fascia g, non a.
        Assert.Equal("2h 0m", c.Fasce["G"]);
        Assert.Equal("0h 0m", c.Fasce["A"]);
    }

    [Fact]
    public void Il_giorno_dopo_non_conta_quelle_ore_ma_conta_il_suo_turno()
    {
        // Monge, giovedì 20/08: le sei ore fino alle 06:03 sono di mercoledì; il giovedì
        // comincia alle 22:20 e finisce alle 06:30 di venerdì. Otto ore tonde.
        TimesheetDay c = Calcola(
            "2026-08-20",
            new[] { T("2026-08-20 06:03", "OUT"), T("2026-08-20 22:20", "IN") },
            new NightContext(
                Yesterday: new[] { T("2026-08-19 19:46", "IN") },
                Tomorrow: new[] { T("2026-08-21 06:30", "OUT"), T("2026-08-21 13:36", "IN") }));

        Assert.Equal("22:30", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("06:30", c.Uscita2);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
        Assert.False(c.HasAnomaly);
        Assert.Contains("le prime ore contano sul giorno prima", c.Note);
    }

    [Fact]
    public void Chi_ha_lavorato_di_notte_e_rientra_nel_pomeriggio_conta_solo_il_pomeriggio()
    {
        // Monge, venerdì 21/08: la mattina è la coda della notte di giovedì (e conta a
        // giovedì), il venerdì è il turno delle 13:36.
        TimesheetDay c = Calcola(
            "2026-08-21",
            new[]
            {
                T("2026-08-21 06:30", "OUT"),
                T("2026-08-21 13:36", "IN"),
                T("2026-08-21 18:10", "OUT"),
            },
            new NightContext(
                Yesterday: new[] { T("2026-08-20 06:03", "OUT"), T("2026-08-20 22:20", "IN") },
                Tomorrow: new[] { T("2026-08-22 07:59", "IN") }));

        Assert.Equal("13:30", c.Entrata1);
        Assert.Equal("18:00", c.Uscita1);
        Assert.Equal("4h 30m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
        Assert.Contains("le prime ore contano sul giorno prima", c.Note);
    }

    [Fact]
    public void La_giornata_che_e_solo_la_coda_di_una_notte_lo_dice()
    {
        // Sinapi, mercoledì 19/08: l'unica timbratura è l'uscita dalla notte di martedì.
        // Zero ore, ma con scritto perché — non è una giornata dimenticata.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 06:58", "OUT") },
            new NightContext(Yesterday: new[] { T("2026-08-18 19:04", "IN") }));

        Assert.Equal("0h 0m", c.RegularHours);
        Assert.False(c.HasAnomaly);
        Assert.Contains("contano sul giorno prima", c.Note);
    }

    [Fact]
    public void La_notte_di_domenica_e_tutta_festiva_e_quasi_tutta_notturna()
    {
        // Sinapi, domenica 16/08: entra alle 20:59 ed esce alle 07:28 del lunedì. Dieci ore
        // e mezza, tutte di domenica perché è lì che il turno comincia.
        TimesheetDay c = Calcola(
            "2026-08-16",
            new[] { T("2026-08-16 20:59", "IN") },
            new NightContext(Tomorrow: new[] { T("2026-08-17 07:28", "OUT") }));

        Assert.Equal("0h 0m", c.RegularHours);      // festivo: è tutto straordinario
        Assert.Equal("10h 30m", c.Overtime);
        Assert.Equal("8h 0m", c.Fasce["H"]);        // h. notturno e festivo
        Assert.Equal("2h 30m", c.Fasce["E"]);       // e. straordinario festivo oltre le 8h
    }

    [Fact]
    public void Fra_due_notti_la_giornata_conta_solo_quella_che_apre()
    {
        // Sinapi, lunedì 17/08: esce alle 07:28 dalla notte di domenica (che conta a
        // domenica) e alle 19:00 apre la sua, che finisce alle 07:30 di martedì.
        TimesheetDay c = Calcola(
            "2026-08-17",
            new[] { T("2026-08-17 07:28", "OUT"), T("2026-08-17 19:00", "IN") },
            new NightContext(
                Yesterday: new[] { T("2026-08-16 20:59", "IN") },
                Tomorrow: new[] { T("2026-08-18 07:30", "OUT"), T("2026-08-18 19:04", "IN") }));

        Assert.Equal("19:00", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("07:30", c.Uscita2);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("4h 30m", c.Overtime);

        // Lo straordinario matura in fondo al turno: tre ore prima delle 6, notturne.
        Assert.Equal("3h 0m", c.Fasce["G"]);
        Assert.Equal("1h 30m", c.Fasce["A"]);
    }

    [Fact]
    public void La_doppia_strisciata_del_mattino_non_sposta_niente()
    {
        // L'uscita dalla notte strisciata due volte a due minuti: è una sola uscita, e va
        // tutta al giorno che ha aperto il turno.
        TimesheetDay c = Calcola(
            "2026-08-20",
            new[] { T("2026-08-20 06:03", "OUT"), T("2026-08-20 06:05", "OUT") },
            new NightContext(Yesterday: new[] { T("2026-08-19 19:46", "IN") }));

        Assert.Equal("0h 0m", c.RegularHours);
        Assert.Contains("contano sul giorno prima", c.Note);
    }

    // ── La pausa timbrata dentro la notte ─────────────────────────────────────

    [Fact]
    public void La_pausa_timbrata_dopo_mezzanotte_resta_dentro_il_turno()
    {
        // 🪤 Chi fa la notte timbra anche la pausa, e a quell'ora cade dopo mezzanotte.
        // Turno 21:00 → 07:00 con mezz'ora di stacco all'una: sono nove ore e mezza, tutte
        // sul mercoledì. Prendendo dal giorno dopo la sola uscita dell'una, il turno si
        // sarebbe spezzato in 4h + 5h30 e lo straordinario sarebbe sparito.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 21:00", "IN") },
            new NightContext(Tomorrow: new[]
            {
                T("2026-08-20 01:00", "OUT"),
                T("2026-08-20 01:30", "IN"),
                T("2026-08-20 07:00", "OUT"),
            }));

        Assert.Equal("21:00", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("07:00", c.Uscita2);
        Assert.Equal("0h 30m", c.BreakTime);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("1h 30m", c.Overtime);
        Assert.False(c.HasAnomaly);
    }

    [Fact]
    public void La_pausa_timbrata_prima_di_mezzanotte_non_fa_sparire_le_ore()
    {
        // Turno 22:00 → 06:00 con la pausa alle 23:00: otto ore meno mezza, sette e mezza.
        // Con le sessioni tagliate a mezzanotte ne venivano tre per due caselle, e sei ore
        // sparivano dal sistema.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[]
            {
                T("2026-08-19 22:00", "IN"),
                T("2026-08-19 23:00", "OUT"),
                T("2026-08-19 23:30", "IN"),
            },
            new NightContext(Tomorrow: new[] { T("2026-08-20 06:00", "OUT") }));

        Assert.Equal("22:00", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("06:00", c.Uscita2);
        Assert.Equal("0h 30m", c.BreakTime);
        Assert.Equal("7h 30m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
        Assert.False(c.HasAnomaly);
    }

    [Fact]
    public void Il_giorno_dopo_cede_anche_le_timbrature_della_pausa()
    {
        // Le tre timbrature del mattino (uscita, rientro dalla pausa, uscita vera) sono
        // tutte del turno cominciato ieri: al giovedì non ne resta nessuna.
        TimesheetDay c = Calcola(
            "2026-08-20",
            new[]
            {
                T("2026-08-20 01:00", "OUT"),
                T("2026-08-20 01:30", "IN"),
                T("2026-08-20 07:00", "OUT"),
            },
            new NightContext(Yesterday: new[] { T("2026-08-19 21:00", "IN") }));

        Assert.Equal("0h 0m", c.RegularHours);
        Assert.False(c.HasAnomaly);
        Assert.Contains("contano sul giorno prima", c.Note);
    }

    [Fact]
    public void Uno_stacco_troppo_lungo_non_e_una_pausa_ed_e_un_secondo_turno()
    {
        // Giornata piena PIÙ la notte: sedici ore in un giorno solo. Le ore si contano
        // tutte — nessuna sparisce — ma cinque ore di stacco non sono una pausa pranzo, e
        // una giornata così va guardata da un umano.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[]
            {
                T("2026-08-19 08:00", "IN"),
                T("2026-08-19 12:00", "OUT"),
                T("2026-08-19 13:00", "IN"),
                T("2026-08-19 17:00", "OUT"),
                T("2026-08-19 22:00", "IN"),
            },
            new NightContext(Tomorrow: new[] { T("2026-08-20 06:00", "OUT") }));

        Assert.True(c.HasAnomaly);
        Assert.StartsWith("⚠ INCOMPLETO: due turni nella stessa giornata", c.Note);

        // 8h di giorno + 8h di notte: sedici ore, e ci sono tutte.
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("8h 0m", c.Overtime);

        // «INCOMPLETO» è la parola con cui il sollecito riconosce una giornata da far
        // verificare alla persona.
        Assert.True(HrDayReminder.Serve(c.Note, new DateTime(2026, 8, 19), Oggi));
    }

    [Fact]
    public void Chi_esce_dalla_notte_e_rientra_per_la_giornata_normale_non_fa_un_turno_di_ventidue_ore()
    {
        // 🪤 Uscire alle 06:00 dalla notte e rientrare alle 08:00 non è una pausa: è la
        // giornata dopo che comincia. Senza il controllo sull'ora del rientro la coda si
        // allungava di pausa in pausa e il mercoledì diventava un turno 22:00 → 20:00,
        // con undici ore e mezza di straordinario dal nulla.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 22:00", "IN") },
            new NightContext(Tomorrow: new[]
            {
                T("2026-08-20 06:00", "OUT"),
                T("2026-08-20 08:00", "IN"),
                T("2026-08-20 11:00", "OUT"),
                T("2026-08-20 11:30", "IN"),
                T("2026-08-20 20:00", "OUT"),
            }));

        Assert.Equal("22:00", c.Entrata1);
        Assert.Equal("24:00", c.Uscita1);
        Assert.Equal("00:00", c.Entrata2);
        Assert.Equal("06:00", c.Uscita2);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
    }

    [Fact]
    public void La_pausa_timbrata_si_toglie_anche_quando_resta_una_sessione_sola()
    {
        // 🪤 Straordinario serale con la pausa cena timbrata, che sfora la mezzanotte di
        // poco: l'uscita delle 00:10 arrotonda a mezzanotte, la seconda sessione si annulla
        // e resta una sessione sola. Le due ore di pausa devono uscire dalle ore lavorate
        // lo stesso — prima restavano dentro e venivano pagate.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[]
            {
                T("2026-08-19 18:00", "IN"),
                T("2026-08-19 20:00", "OUT"),
                T("2026-08-19 22:00", "IN"),
            },
            new NightContext(Tomorrow: new[] { T("2026-08-20 00:10", "OUT") }));

        Assert.Equal("4h 0m", c.RegularHours);   // 18→20 e 22→24, non sei ore
        Assert.Equal("2h 0m", c.BreakTime);
    }

    [Fact]
    public void La_pausa_timbrata_non_si_somma_a_quella_dedotta_d_ufficio()
    {
        // Turno lungo con pausa timbrata alle 06:00 che finisce nel pomeriggio: la pausa
        // canonica delle 12:30 NON va imposta sopra a quella vera, o si perde un'ora.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 23:50", "IN") },
            new NightContext(Tomorrow: new[]
            {
                T("2026-08-20 03:00", "OUT"),
                T("2026-08-20 03:30", "IN"),
                T("2026-08-20 08:00", "OUT"),
            }));

        // 00:00 → 08:00 meno la mezz'ora timbrata: sette ore e mezza.
        Assert.Equal("7h 30m", c.RegularHours);
        Assert.Equal("0h 30m", c.BreakTime);
        Assert.DoesNotContain("AUTO_P", c.Note);
    }

    // ── Quando la notte è ancora aperta ───────────────────────────────────────

    [Fact]
    public void La_notte_non_ancora_chiusa_non_regala_ore()
    {
        // È quello che l'import trova ogni sera: l'entrata delle 22:20 c'è, l'uscita del
        // giorno dopo non è ancora arrivata. Zero ore e un avviso, non quindici ore.
        TimesheetDay c = Calcola(
            "2026-08-20",
            new[] { T("2026-08-20 06:03", "OUT"), T("2026-08-20 22:20", "IN") },
            new NightContext(Yesterday: new[] { T("2026-08-19 19:46", "IN") }));

        Assert.True(c.HasAnomaly);
        Assert.Equal("0h 0m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
    }

    [Fact]
    public void Sulla_giornata_in_corso_la_nota_resta_quella_che_il_servizio_cerca()
    {
        // 🪤 «Giornata in corso» è la chiave con cui l'import ripesca le giornate rimaste
        // indietro (`WHERE note = 'Giornata in corso'`): appenderci il marcatore della
        // notte le renderebbe invisibili, e resterebbero a zero ore per sempre.
        TimesheetDay c = Calcola(
            "2026-09-01",
            new[] { T("2026-09-01 19:46", "IN") },
            new NightContext(Yesterday: new[] { T("2026-08-31 17:04", "OUT") }));

        Assert.Equal("Giornata in corso", c.Note);
    }

    [Fact]
    public void Due_entrate_di_fila_non_diventano_un_turno_di_ventotto_ore()
    {
        // Entra la mattina, dimentica l'uscita, rientra la sera per la notte.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 08:00", "IN"), T("2026-08-19 19:00", "IN") },
            new NightContext(Tomorrow: new[] { T("2026-08-20 06:00", "OUT") }));

        Assert.True(c.HasAnomaly);
        Assert.StartsWith("⚠ INCOMPLETO: manca una timbratura della notte", c.Note);

        // Quel che è certo è la coppia 19:00 → 06:00: undici ore, non ventotto.
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("3h 0m", c.Overtime);
    }

    // ── Quando NON è una notte ────────────────────────────────────────────────

    [Fact]
    public void Senza_contesto_il_motore_si_comporta_come_prima()
    {
        // È la garanzia del banco di prova: nessun contesto, nessuna ricomposizione.
        TimesheetDay c = Calcola("2026-08-19", new[] { T("2026-08-19 19:46", "IN") });

        Assert.Equal("⚠ INCOMPLETO: Solo entrata", c.Note);
        Assert.True(c.HasAnomaly);
        Assert.Equal("0h 0m", c.RegularHours);
    }

    [Fact]
    public void Una_uscita_dimenticata_al_mattino_non_diventa_una_notte()
    {
        // Entra alle 08:00 e non timbra più; il giorno dopo comincia con un'uscita perché
        // ha dimenticato anche l'entrata. Due errori, non un turno.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 08:00", "IN") },
            new NightContext(Tomorrow: new[] { T("2026-08-20 17:00", "OUT") }));

        Assert.Equal("⚠ INCOMPLETO: Solo entrata", c.Note);
        Assert.True(c.HasAnomaly);
    }

    [Fact]
    public void Senza_la_controparte_il_giorno_dopo_non_si_ricompone_niente()
    {
        // L'entrata serale c'è, ma il giorno dopo comincia con un'ALTRA entrata: la notte
        // non è mai stata chiusa.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[] { T("2026-08-19 19:46", "IN") },
            new NightContext(Tomorrow: new[] { T("2026-08-20 08:00", "IN") }));

        Assert.Equal("⚠ INCOMPLETO: Solo entrata", c.Note);
    }

    [Fact]
    public void Una_giornata_normale_che_comincia_prestissimo_non_diventa_notturna()
    {
        // Monge, martedì 11/08: entra alle 05:00 ed esce alle 17:32. Un'ora cade prima
        // delle 6 e quindi è notte — ma lo straordinario l'ha fatto la sera, non l'alba:
        // la maggiorazione resta la diurna (fascia a).
        TimesheetDay c = Calcola(
            "2026-08-11",
            new[] { T("2026-08-11 05:00", "IN"), T("2026-08-11 17:32", "OUT") });

        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("3h 30m", c.Overtime);
        Assert.Equal("3h 30m", c.Fasce["A"]);
        Assert.Equal("0h 0m", c.Fasce["G"]);
    }

    [Fact]
    public void La_nota_piu_lunga_sta_nella_colonna_del_database()
    {
        // hr_days.note è VARCHAR(255): l'avviso più la nota del motore più i due marcatori
        // della notte devono starci, altrimenti MySQL tronca e la coda si perde.
        TimesheetDay c = Calcola(
            "2026-08-19",
            new[]
            {
                T("2026-08-19 06:30", "OUT"),
                T("2026-08-19 08:00", "IN"),
                T("2026-08-19 12:00", "OUT"),
                T("2026-08-19 13:00", "IN"),
                T("2026-08-19 17:00", "OUT"),
                T("2026-08-19 22:00", "IN"),
            },
            new NightContext(
                Yesterday: new[] { T("2026-08-18 22:00", "IN") },
                Tomorrow: new[] { T("2026-08-20 06:00", "OUT") }));

        Assert.True(c.Note.Length <= 255, $"nota di {c.Note.Length} caratteri: «{c.Note}»");
    }

    [Theory]
    [InlineData("2026-08-19 19:46", "2026-08-20 06:03", true)]   // la notte vera di Monge
    [InlineData("2026-08-20 22:20", "2026-08-21 06:30", true)]   // l'altra notte di Monge
    [InlineData("2026-08-16 20:59", "2026-08-17 07:28", true)]   // la domenica di Sinapi
    [InlineData("2026-08-19 08:00", "2026-08-20 06:03", false)]  // entrata mattutina: non è una notte
    [InlineData("2026-08-19 19:46", "2026-08-20 13:00", false)]  // uscita del pomeriggio: non è una notte
    [InlineData("2026-08-19 19:46", "2026-08-21 06:03", false)]  // due mezzanotti in mezzo
    [InlineData("2026-08-19 12:00", "2026-08-20 11:59", false)]  // 24 ore: è un errore, non un turno
    public void Il_riconoscimento_della_notte_sta_dentro_i_paletti(string entrata, string uscita, bool atteso) =>
        Assert.Equal(atteso, NightShift.IsNightShift(Ora(entrata), Ora(uscita)));

    // ── Le due meccaniche, viste da vicino ────────────────────────────────────

    [Fact]
    public void La_ricomposizione_sposta_le_ore_dalla_parte_giusta()
    {
        var ieri = new[] { T("2026-08-19 19:46", "IN") };
        var domani = new[] { T("2026-08-21 06:30", "OUT"), T("2026-08-21 13:36", "IN") };

        List<RawPunch> turno = NightShift.Compose(
            new DateTime(2026, 8, 20),
            new[] { T("2026-08-20 06:03", "OUT"), T("2026-08-20 22:20", "IN") },
            new NightContext(ieri, domani),
            out NightSplit split);

        Assert.Equal(NightSplit.HandedToPreviousDay | NightSplit.RunsPastMidnight, split);
        Assert.Equal(2, turno.Count);
        Assert.Equal(Ora("2026-08-20 22:20"), turno[0].PunchedAt);   // l'uscita delle 06:03 è di ieri
        Assert.Equal(Ora("2026-08-21 06:30"), turno[1].PunchedAt);   // l'uscita di domani è mia
    }

    [Fact]
    public void Il_taglio_di_mezzanotte_e_marcato_come_messo_dal_sistema()
    {
        List<RawPunch> tagliato = NightShift.SplitAtMidnight(
            new DateTime(2026, 8, 20),
            new[] { T("2026-08-20 22:20", "IN"), T("2026-08-21 06:30", "OUT") });

        Assert.Equal(4, tagliato.Count);
        Assert.Equal(new DateTime(2026, 8, 21), tagliato[1].PunchedAt);
        Assert.Equal(new DateTime(2026, 8, 21), tagliato[2].PunchedAt);
        Assert.True(tagliato[1].Synthetic);
        Assert.True(tagliato[2].Synthetic);
        Assert.True(NightShift.IsExit(tagliato[1].Direction));    // 24:00, la fine
        Assert.True(NightShift.IsEntry(tagliato[2].Direction));   // 00:00, la ripresa
        Assert.All(tagliato.Where(t => !t.Synthetic), t => Assert.False(t.Synthetic));
    }
}
