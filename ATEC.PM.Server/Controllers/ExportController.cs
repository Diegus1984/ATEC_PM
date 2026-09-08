using ATEC.PM.Server.Services.Export;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Export generici: il client manda una tabella (colonne tipizzate + righe), il server
/// risponde col file. Nessuna lettura dal database: i dati sono quelli che il client ha già
/// (e che quindi poteva già vedere) — per questo basta l'autenticazione, senza chiave di
/// funzione.
///
/// <para>Nato con la #150 per il Prospetto SAL; ogni altra griglia che vuole un Excel vero
/// passa di qui invece di scrivere un CSV (Excel italiano legge <c>27122.172</c> come
/// ventisette milioni) o una tabella HTML rinominata <c>.xls</c>.</para>
/// </summary>
[ApiController]
[Route("api/export")]
[Authorize]
public class ExportController : ControllerBase
{
    /// <summary>Foglio Excel <c>.xlsx</c> con filtro su ogni colonna e valori tipizzati.</summary>
    [HttpPost("xlsx")]
    public IActionResult Xlsx([FromBody] TabellaExcelRequest richiesta)
    {
        byte[] contenuto;
        try
        {
            contenuto = TabellaExcel.Genera(richiesta);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }

        return File(contenuto, TabellaExcel.Mime, TabellaExcel.NomeFile(richiesta.NomeFile));
    }
}
