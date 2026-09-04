using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Registro Offerte, fase 1: le cinque tabelle del modulo e le due anagrafiche seminate
/// (13 tipi impianto, 10 sigle venditore). Vedi <c>docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md</c>.
///
/// <para>Il registro è la serie commerciale <b>parallela</b> ai Preventivi: numera tutte le
/// offerte emesse da ATEC (<c>S097-2026-EC</c>), comprese quelle nate fuori dal gestionale, e si
/// aggancia a un preventivo o a una commessa quando ci sono. Sostituisce l'Excel
/// «NUMERI OFFERTE» e l'applicativo autonomo ATEC Offerte.</para>
///
/// <para>Il DDL non è scritto qui: sta in <see cref="SalesOffersDbService"/>, che lo esegue anche
/// per i database nuovi dal bootstrap di <c>DbService</c>. Una definizione sola per due percorsi
/// — su un database appena creato questa migrazione passa a vuoto, e va bene così.</para>
/// </summary>
public sealed class M122_RegistroOfferte : IMigrazione
{
    public int Versione => 122;

    public string Descrizione =>
        "Registro Offerte: sales_offers + tipi, venditori, contatti e registro modifiche";

    public void Applica(MySqlConnection c, ILogger log)
    {
        SalesOffersDbService.InitTables(c, log);
        SalesOffersDbService.SeedTypes(c, log);
        SalesOffersDbService.SeedSellers(c, log);

        log.LogInformation("[Migration v122] Registro Offerte pronto.");
    }
}
