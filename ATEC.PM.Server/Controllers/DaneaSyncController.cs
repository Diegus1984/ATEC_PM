using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Services;
using FirebirdSql.Data.FirebirdClient;
using Dapper;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/danea-sync")]
[Authorize]
public class DaneaSyncController : ControllerBase
{
    private readonly DaneaSyncService _sync;

    /// <summary>Tabelle Firebird esplorabili (stesso set usato da import/sync).</summary>
    private static readonly HashSet<string> AllowedExploreTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "TAnagrafica",
        "TArticoli",
        "TDocTestate",
        "TDocRighe",
        "TDocIva",
        "TTipiDoc"
    };

    public DaneaSyncController(DaneaSyncService sync) => _sync = sync;

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            isSyncing = DaneaSyncService.IsSyncing,
            lastSync = DaneaSyncService.LastSync?.ToString("dd/MM/yyyy HH:mm:ss"),
            lastError = DaneaSyncService.LastError,
            progress = DaneaSyncService.ProgressMessage,
            suppliers = DaneaSyncService.SuppliersCount,
            customers = DaneaSyncService.CustomersCount,
            articles = DaneaSyncService.ArticlesCount
        });
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunSync()
    {
        if (DaneaSyncService.IsSyncing)
            return Conflict(new { message = "Sincronizzazione già in corso" });

        _ = Task.Run(() => _sync.RunSync());
        return Ok(new { message = "Sincronizzazione avviata" });
    }

    // ═══════════════════════════════════════════════
    // ESPLORA DATABASE DANEA (Firebird) — solo ADMIN/PM
    // ═══════════════════════════════════════════════

    [HttpGet("explore/tables")]
    [Authorize(Roles = "ADMIN,PM")]
    public IActionResult GetTables()
    {
        List<string> tables = AllowedExploreTables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        return Ok(new { count = tables.Count, tables });
    }

    [HttpGet("explore/tables/{tableName}/columns")]
    [Authorize(Roles = "ADMIN,PM")]
    public async Task<IActionResult> GetColumns(string tableName)
    {
        if (!TryValidateTableName(tableName, out string safeTable))
            return BadRequest(new { error = "Tabella non consentita" });

        try
        {
            string? connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            using FbConnection fb = new FbConnection(connStr);
            await fb.OpenAsync();

            IEnumerable<dynamic> columns = await fb.QueryAsync<dynamic>(@"
                SELECT rf.RDB$FIELD_NAME AS ColumnName,
                       f.RDB$FIELD_TYPE AS FieldType,
                       f.RDB$FIELD_LENGTH AS FieldLength,
                       rf.RDB$NULL_FLAG AS NotNull
                FROM RDB$RELATION_FIELDS rf
                JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
                WHERE rf.RDB$RELATION_NAME = @Table
                ORDER BY rf.RDB$FIELD_POSITION",
                new { Table = safeTable.PadRight(31) });

            string FbTypeName(int typeCode) => typeCode switch
            {
                7 => "SMALLINT",
                8 => "INTEGER",
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => "CHAR",
                16 => "BIGINT",
                27 => "DOUBLE",
                35 => "TIMESTAMP",
                37 => "VARCHAR",
                261 => "BLOB/TEXT",
                _ => $"TYPE_{typeCode}"
            };

            List<object> result = columns.Select(c =>
            {
                IDictionary<string, object> dict = (IDictionary<string, object>)c;
                List<object?> vals = dict.Values.Cast<object?>().ToList();
                string colName = vals.ElementAtOrDefault(0)?.ToString()?.Trim() ?? "";
                int fieldType = 0;
                if (vals.ElementAtOrDefault(1) is { } v1) int.TryParse(v1.ToString(), out fieldType);
                int fieldLen = 0;
                if (vals.ElementAtOrDefault(2) is { } v2) int.TryParse(v2.ToString(), out fieldLen);
                bool isNullable = vals.ElementAtOrDefault(3) == null;

                return new { name = colName, type = FbTypeName(fieldType), length = fieldLen, nullable = isNullable };
            }).ToList<object>();

            return Ok(new { table = safeTable, columnCount = result.Count, columns = result });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("explore/tables/{tableName}/search")]
    [Authorize(Roles = "ADMIN,PM")]
    public async Task<IActionResult> SearchData(string tableName, [FromQuery] string column = "", [FromQuery] string q = "", [FromQuery] int limit = 50)
    {
        if (!TryValidateTableName(tableName, out string safeTable))
            return BadRequest(new { error = "Tabella non consentita" });

        if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(q))
            return Ok(new { error = "Parametri 'column' e 'q' obbligatori" });

        if (limit > 200) limit = 200;
        if (limit < 1) limit = 1;

        try
        {
            string? connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            using FbConnection fb = new FbConnection(connStr);
            await fb.OpenAsync();

            HashSet<string> allowedColumns = await GetTableColumnNamesAsync(fb, safeTable);
            string safeColumn = column.Trim();
            if (!allowedColumns.Contains(safeColumn))
                return BadRequest(new { error = "Colonna non consentita" });

            string sql = $"SELECT FIRST {limit} * FROM \"{safeTable}\" WHERE \"{safeColumn}\" LIKE @Search";
            List<Dictionary<string, object?>> rows = (await fb.QueryAsync(sql, new { Search = $"%{q}%" }))
                .Select(row =>
                {
                    IDictionary<string, object> dict = (IDictionary<string, object>)row;
                    return dict.ToDictionary<KeyValuePair<string, object>, string, object?>(
                        kv => kv.Key.Trim(),
                        kv => kv.Value is string s ? s.Trim() : kv.Value
                    );
                }).ToList();

            return Ok(new { table = safeTable, column = safeColumn, query = q, rowCount = rows.Count, rows });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("explore/tables/{tableName}/data")]
    [Authorize(Roles = "ADMIN,PM")]
    public async Task<IActionResult> GetData(string tableName, [FromQuery] int limit = 20)
    {
        if (!TryValidateTableName(tableName, out string safeTable))
            return BadRequest(new { error = "Tabella non consentita" });

        if (limit > 100) limit = 100;
        if (limit < 1) limit = 1;

        try
        {
            string? connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            using FbConnection fb = new FbConnection(connStr);
            await fb.OpenAsync();

            string sql = $"SELECT FIRST {limit} * FROM \"{safeTable}\"";
            List<Dictionary<string, object?>> rows = (await fb.QueryAsync(sql))
                .Select(row =>
                {
                    IDictionary<string, object> dict = (IDictionary<string, object>)row;
                    return dict.ToDictionary<KeyValuePair<string, object>, string, object?>(
                        kv => kv.Key.Trim(),
                        kv => kv.Value is string s ? s.Trim() : kv.Value
                    );
                }).ToList();

            return Ok(new { table = safeTable, rowCount = rows.Count, limit, rows });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    private static bool TryValidateTableName(string tableName, out string safeTable)
    {
        safeTable = tableName.Trim();
        return !string.IsNullOrEmpty(safeTable) && AllowedExploreTables.Contains(safeTable);
    }

    private static async Task<HashSet<string>> GetTableColumnNamesAsync(FbConnection fb, string safeTable)
    {
        IEnumerable<string> names = await fb.QueryAsync<string>(@"
            SELECT TRIM(rf.RDB$FIELD_NAME)
            FROM RDB$RELATION_FIELDS rf
            WHERE rf.RDB$RELATION_NAME = @Table",
            new { Table = safeTable.PadRight(31) });

        return names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
