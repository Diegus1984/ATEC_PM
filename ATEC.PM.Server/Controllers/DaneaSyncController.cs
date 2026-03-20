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
    public DaneaSyncController(DaneaSyncService sync) => _sync = sync;

    [HttpGet("status")]
    [AllowAnonymous]
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
    // ESPLORA DATABASE DANEA (Firebird)
    // ═══════════════════════════════════════════════

    [HttpGet("explore/tables")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTables()
    {
        try
        {
            string connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            using var fb = new FbConnection(connStr);
            await fb.OpenAsync();

            var tables = await fb.QueryAsync<dynamic>(@"
                SELECT RDB$RELATION_NAME AS TableName
                FROM RDB$RELATIONS
                WHERE RDB$SYSTEM_FLAG = 0
                  AND RDB$VIEW_BLR IS NULL
                ORDER BY RDB$RELATION_NAME");

            var result = tables.Select(t =>
            {
                var dict = (IDictionary<string, object>)t;
                var val = dict.Values.FirstOrDefault();
                return val?.ToString()?.Trim() ?? "";
            }).Where(n => !string.IsNullOrEmpty(n)).ToList();
            return Ok(new { count = result.Count, tables = result });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("explore/tables/{tableName}/columns")]
    [AllowAnonymous]
    public async Task<IActionResult> GetColumns(string tableName)
    {
        try
        {
            string connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            using var fb = new FbConnection(connStr);
            await fb.OpenAsync();

            var columns = await fb.QueryAsync<dynamic>(@"
                SELECT rf.RDB$FIELD_NAME AS ColumnName,
                       f.RDB$FIELD_TYPE AS FieldType,
                       f.RDB$FIELD_LENGTH AS FieldLength,
                       rf.RDB$NULL_FLAG AS NotNull
                FROM RDB$RELATION_FIELDS rf
                JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
                WHERE rf.RDB$RELATION_NAME = @Table
                ORDER BY rf.RDB$FIELD_POSITION",
                new { Table = tableName.PadRight(31) });

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

            var result = columns.Select(c =>
            {
                var dict = (IDictionary<string, object>)c;
                var vals = dict.Values.ToList();
                string colName = vals.ElementAtOrDefault(0)?.ToString()?.Trim() ?? "";
                int fieldType = 0;
                if (vals.ElementAtOrDefault(1) != null) int.TryParse(vals[1].ToString(), out fieldType);
                int fieldLen = 0;
                if (vals.ElementAtOrDefault(2) != null) int.TryParse(vals[2].ToString(), out fieldLen);
                bool isNullable = vals.ElementAtOrDefault(3) == null;

                return new { name = colName, type = FbTypeName(fieldType), length = fieldLen, nullable = isNullable };
            }).ToList();

            return Ok(new { table = tableName, columnCount = result.Count, columns = result });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("explore/tables/{tableName}/search")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchData(string tableName, [FromQuery] string column = "", [FromQuery] string q = "", [FromQuery] int limit = 50)
    {
        try
        {
            string connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(q))
                return Ok(new { error = "Parametri 'column' e 'q' obbligatori" });

            if (limit > 200) limit = 200;

            using var fb = new FbConnection(connStr);
            await fb.OpenAsync();

            var rows = (await fb.QueryAsync(
                $"SELECT FIRST {limit} * FROM \"{tableName}\" WHERE \"{column}\" LIKE @Search",
                new { Search = $"%{q}%" }))
                .Select(row =>
                {
                    var dict = (IDictionary<string, object>)row;
                    return dict.ToDictionary(
                        kv => kv.Key.Trim(),
                        kv => kv.Value is string s ? s.Trim() : kv.Value
                    );
                }).ToList();

            return Ok(new { table = tableName, column, query = q, rowCount = rows.Count, rows });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("explore/tables/{tableName}/data")]
    [AllowAnonymous]
    public async Task<IActionResult> GetData(string tableName, [FromQuery] int limit = 20)
    {
        try
        {
            string connStr = _sync.BuildConnectionString();
            if (string.IsNullOrEmpty(connStr))
                return Ok(new { error = "Connessione Danea non configurata" });

            if (limit > 100) limit = 100;

            using var fb = new FbConnection(connStr);
            await fb.OpenAsync();

            var rows = (await fb.QueryAsync($"SELECT FIRST {limit} * FROM \"{tableName}\""))
                .Select(row =>
                {
                    var dict = (IDictionary<string, object>)row;
                    return dict.ToDictionary(
                        kv => kv.Key.Trim(),
                        kv => kv.Value is string s ? s.Trim() : kv.Value
                    );
                }).ToList();

            return Ok(new { table = tableName, rowCount = rows.Count, limit, rows });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }
}
