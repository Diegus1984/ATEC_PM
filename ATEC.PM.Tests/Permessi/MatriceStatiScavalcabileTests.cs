using ATEC.PM.Server.Services;
using ATEC.PM.Shared;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Segnalazione #140 — la matrice degli avanzamenti DDP ha una via d'uscita.
///
/// <para>La matrice è una guida per chi compila, non una prigione: chi ha
/// <c>action.ddp_status_override</c> (gli amministratori col jolly e il PM a cui la si dà dalla
/// scheda persona) sceglie qualunque stato, perché è la persona che deve rimettere in riga un
/// collega che ha sbagliato assegnazione. Senza quella via d'uscita un errore di stato è
/// definitivo per tutti.</para>
///
/// <para>Queste prove congelano il patto: <b>stessa transizione, verdetto opposto</b> a seconda
/// del privilegio, e la chiave è una sola — quella che il server controlla è la stessa che il
/// client legge da <c>/features/my</c> per aprire la finestra completa.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class MatriceStatiScavalcabileTests
{
    private readonly SchemaCondiviso _schema;

    public MatriceStatiScavalcabileTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>
    /// Stato terminale (riga con <c>to_key</c> vuoto): da lì non si va da nessuna parte — a meno
    /// che chi scrive abbia il privilegio, ed è esattamente il caso in cui serve (una riga
    /// mandata per sbaglio in un vicolo cieco).
    /// </summary>
    [FactRichiedeMySql]
    public void StatoTerminale_vietatoATutti_ammessoAChiScavalca()
    {
        using MySqlConnection c = _schema.Apri();

        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type='COMMERCIAL' AND from_key='ZZ'");
        c.Execute(@"INSERT INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    VALUES ('COMMERCIAL', 'ZZ', '')");

        string? vincolato = DdpTransitionService.Validate(
            c, DdpTransitionService.TypeCommercial, "ZZ", "DISP");
        string? conPrivilegio = DdpTransitionService.Validate(
            c, DdpTransitionService.TypeCommercial, "ZZ", "DISP", cache: null, ignoraMatrice: true);

        Assert.NotNull(vincolato);
        Assert.Null(conPrivilegio);
    }

    /// <summary>
    /// Anche la finestra di partenza («INIZIO», riga appena creata) si scavalca: la riga nasce
    /// nello stato che serve, senza passare da uno stato di comodo per poi correggerlo.
    /// </summary>
    [FactRichiedeMySql]
    public void FinestraDiPartenza_vietataATutti_ammessaAChiScavalca()
    {
        using MySqlConnection c = _schema.Apri();

        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type='COMMERCIAL' AND from_key='INIZIO'");
        c.Execute(@"INSERT INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    VALUES ('COMMERCIAL', 'INIZIO', 'VER')");

        string? vincolato = DdpTransitionService.Validate(
            c, DdpTransitionService.TypeCommercial, null, "DISP");
        string? conPrivilegio = DdpTransitionService.Validate(
            c, DdpTransitionService.TypeCommercial, null, "DISP", cache: null, ignoraMatrice: true);

        Assert.NotNull(vincolato);
        Assert.Null(conPrivilegio);
    }

    /// <summary>
    /// Il privilegio scavalca la matrice anche quando la lettura passa dalla cache: le due strade
    /// devono dare lo stesso sì, altrimenti il permesso funzionerebbe a giorni alterni.
    /// </summary>
    [FactRichiedeMySql]
    public void ConCache_ilPrivilegioValeUgualmente()
    {
        using MySqlConnection c = _schema.Apri();

        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type='OFFICINA' AND from_key='ZZ'");
        c.Execute(@"INSERT INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    VALUES ('OFFICINA', 'ZZ', '')");

        var cache = new AnagraficheCache(Microsoft.Extensions.Logging.Abstractions.NullLogger<AnagraficheCache>.Instance);

        Assert.NotNull(DdpTransitionService.Validate(
            c, DdpTransitionService.TypeOfficina, "ZZ", "DISP", cache));
        Assert.Null(DdpTransitionService.Validate(
            c, DdpTransitionService.TypeOfficina, "ZZ", "DISP", cache, ignoraMatrice: true));
    }

    /// <summary>
    /// La chiave del privilegio è UNA: quella che il server controlla dev'essere a catalogo e
    /// viva, altrimenti il client la leggerebbe da <c>/features/my</c> e non la troverebbe mai —
    /// il permesso resterebbe spento senza che nessun errore lo dica.
    /// </summary>
    [Fact]
    public void LaChiaveDelPrivilegio_esisteACatalogoEdEViva()
    {
        VoceCatalogo? voce = PermessiCatalogo.VociPrimarie()
            .FirstOrDefault(v => v.Chiave == DdpTransitionService.FeatureScavalcaMatrice);

        Assert.NotNull(voce);
        Assert.False(voce!.Ritirata, "chiave ritirata: nessuno potrebbe più avere il privilegio");
        Assert.False(voce.SoloClient, "il server la controlla davvero: non è una chiave di solo client");
    }
}
