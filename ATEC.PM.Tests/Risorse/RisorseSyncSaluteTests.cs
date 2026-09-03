using ATEC.PM.Server.Migrations;
using ATEC.PM.Server.Services.RisorseSync;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Risorse;

/// <summary>
/// #147 — l'avviso nel planner «ATEC Risorse (VPS) non risponde»: la regola pura di
/// <see cref="RisorseSyncService.ValutaSalute"/>. Le cose da difendere: si conta dall'ultimo
/// giro RIUSCITO (non da <c>LastRun</c>, che si aggiorna anche a giro fallito), un servizio
/// appena partito ha diritto ai suoi 10 minuti, un «ok» vecchio di giorni letto da res_settings
/// dopo un riavvio non fa scattare l'avviso al primo secondo, e a sincronizzazione spenta o a
/// loop mai partito l'avviso tace.
/// </summary>
public class RisorseSyncSaluteTests
{
    private static readonly DateTime Adesso = new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
    private static DateTime MinutiFa(int minuti) => Adesso.AddMinutes(-minuti);

    [Fact]
    public void Spenta_o_non_configurata_non_avvisa_mai()
    {
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(false, MinutiFa(600), MinutiFa(500), "VPS non raggiungibile", Adesso);

        Assert.False(s.Attiva);
        Assert.False(s.VpsNonRisponde);
        Assert.Equal(0, s.MinutiSenzaRisposta);
    }

    [Fact]
    public void Loop_mai_partito_non_avvisa()
    {
        // Services:RisorseSync spento: attiva sulla carta, ma nessun giro può riuscire — non è il VPS a tacere.
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, null, null, null, Adesso);

        Assert.True(s.Attiva);
        Assert.False(s.VpsNonRisponde);
    }

    [Fact]
    public void Loop_appena_partito_senza_giri_riusciti_non_avvisa()
    {
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, MinutiFa(1), null, null, Adesso);

        Assert.False(s.VpsNonRisponde);
        Assert.Null(s.UltimoGiroOkUtc);
    }

    [Fact]
    public void Un_giro_riuscito_da_poco_va_bene()
    {
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, MinutiFa(600), MinutiFa(3), null, Adesso);

        Assert.False(s.VpsNonRisponde);
        Assert.Equal(MinutiFa(3), s.UltimoGiroOkUtc);
        Assert.Equal(0, s.MinutiSenzaRisposta);
    }

    [Fact]
    public void Senza_giri_riusciti_da_oltre_dieci_minuti_avvisa_con_i_minuti_e_l_errore()
    {
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, MinutiFa(600), MinutiFa(11), "VPS non raggiungibile: timeout", Adesso);

        Assert.True(s.VpsNonRisponde);
        Assert.Equal(11, s.MinutiSenzaRisposta);
        Assert.Equal("VPS non raggiungibile: timeout", s.Errore);
        Assert.Equal(MinutiFa(11), s.UltimoGiroOkUtc);
    }

    [Fact]
    public void Dieci_minuti_esatti_sono_gia_silenzio()
    {
        Assert.True(RisorseSyncService.ValutaSalute(true, MinutiFa(600), MinutiFa(10), null, Adesso).VpsNonRisponde);
        Assert.False(RisorseSyncService.ValutaSalute(true, MinutiFa(600), Adesso.AddSeconds(-599), null, Adesso).VpsNonRisponde);
    }

    [Fact]
    public void Un_ok_vecchio_di_giorni_prima_dell_avvio_non_conta_e_i_minuti_partono_dall_avvio()
    {
        // Riavvio del servizio con l'esito «ok» di tre giorni fa in res_settings: prima di 10 minuti dall'avvio niente…
        Assert.False(RisorseSyncService.ValutaSalute(true, MinutiFa(5), Adesso.AddDays(-3), null, Adesso).VpsNonRisponde);

        // …poi si conta dall'avvio, non dall'«ok» vecchio.
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, MinutiFa(120), Adesso.AddDays(-3), "VPS non raggiungibile", Adesso);
        Assert.True(s.VpsNonRisponde);
        Assert.Equal(120, s.MinutiSenzaRisposta);
    }

    [Fact]
    public void I_minuti_sono_interi_per_difetto()
    {
        RisorseSyncSaluteDto s = RisorseSyncService.ValutaSalute(true, MinutiFa(600), Adesso.AddSeconds(-(14 * 60 + 59)), null, Adesso);

        Assert.Equal(14, s.MinutiSenzaRisposta);
    }
}

/// <summary>
/// La M120 (#147, punto 3): esiste con quel numero, toglie i doppioni della foto del piano
/// tenendo il più recente, mette la chiave unica e si può rifare.
/// </summary>
public class ChiaveUnicaFotoPianoMigrazioneTests
{
    [Fact]
    public void La_M120_e_scoperta_dal_runner_con_versione_120()
    {
        IMigrazione? m = new MigrationRunner(NullLogger.Instance).Migrazioni.SingleOrDefault(x => x.Versione == 120);
        Assert.NotNull(m);
        Assert.IsType<M120_ChiaveUnicaFotoPiano>(m);
        Assert.Contains("res_plan_snapshots", m!.Descrizione);
    }

    [FactRichiedeMySql]
    public void La_M120_toglie_i_doppioni_tenendo_il_piu_recente_mette_la_chiave_e_si_puo_rifare()
    {
        using var db = new DatabaseDiProva("foto120");
        db.CreaSchemaCompleto(); // qui la M120 è già passata (e InitTables crea la tabella già con la chiave)
        Assert.Contains(120, db.VersioniApplicate());
        using MySqlConnection c = db.Apri();

        // ── si torna alla forma pre-M120: niente chiave, e un doppione come li lasciava il vecchio codice ──
        c.Execute($"ALTER TABLE res_plan_snapshots DROP INDEX {M120_ChiaveUnicaFotoPiano.NomeChiave}");
        Assert.False(ChiavePresente(c));
        c.Execute("INSERT INTO res_plan_snapshot_batches (created_utc) VALUES (UTC_TIMESTAMP())");
        int batch = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        Riga(c, batch, 7, "Vecchia");
        Riga(c, batch, 7, "Nuova");
        Riga(c, batch, 8, "Sola");
        Assert.Equal(3, Righe(c));

        // ── la migrazione ──
        new M120_ChiaveUnicaFotoPiano().Applica(c, NullLogger.Instance);

        Assert.True(ChiavePresente(c));
        Assert.Equal(2, Righe(c));
        Assert.Equal("Nuova", c.ExecuteScalar<string>(
            "SELECT descrizione FROM res_plan_snapshots WHERE batch_id = @B AND assignment_id = 7", new { B = batch }));

        // ── e si può rifare: nessuna eccezione, niente cambia ──
        new M120_ChiaveUnicaFotoPiano().Applica(c, NullLogger.Instance);
        Assert.Equal(2, Righe(c));

        // La chiave rifiuta davvero un secondo rigo per la stessa allocazione nello stesso batch…
        Assert.ThrowsAny<MySqlException>(() => Riga(c, batch, 8, "Doppione"));
        // …ma la stessa allocazione in un altro batch (la foto del giorno dopo) è normale.
        c.Execute("INSERT INTO res_plan_snapshot_batches (created_utc) VALUES (UTC_TIMESTAMP())");
        Riga(c, c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()"), 8, "Il giorno dopo");
        Assert.Equal(3, Righe(c));
    }

    private static void Riga(MySqlConnection c, int batch, int assignmentId, string descrizione) =>
        c.Execute(@"
            INSERT INTO res_plan_snapshots (batch_id, assignment_id, employee_id, tipo, data_inizio, data_fine, descrizione)
            VALUES (@B, @A, 1, 'OP', '2026-09-07', '2026-09-11', @D)",
            new { B = batch, A = assignmentId, D = descrizione });

    private static int Righe(MySqlConnection c) => c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_plan_snapshots");

    private static bool ChiavePresente(MySqlConnection c) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'res_plan_snapshots' AND index_name = @Nome AND non_unique = 0",
            new { Nome = M120_ChiaveUnicaFotoPiano.NomeChiave }) == 1;
}
