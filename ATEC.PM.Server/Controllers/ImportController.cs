using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FirebirdSql.Data.FirebirdClient;
using System.Data;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/import")]
[AllowAnonymous]
public class ImportController : ControllerBase
{
    private readonly IConfiguration _config;

    public ImportController(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Cerca il file .eft in C:\ATEC_Commesse
    /// </summary>
    [HttpGet("easyfatt/find")]
    public IActionResult FindEasyfattDb()
    {
        var searchPath = _config["Easyfatt:SearchPath"] ?? @"C:\ATEC_Commesse";

        try
        {
            var files = Directory.GetFiles(searchPath, "*.eft", SearchOption.AllDirectories);
            if (files.Length == 0)
                return Ok(ApiResponse<object>.Fail("Nessun file .eft trovato in " + searchPath));

            var result = files.Select(f => new
            {
                Path = f,
                Name = Path.GetFileName(f),
                Size = new FileInfo(f).Length,
                Modified = new FileInfo(f).LastWriteTime
            }).ToList();

            return Ok(ApiResponse<object>.Ok(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore ricerca: {ex.Message}"));
        }
    }

    /// <summary>
    /// Cerca fbembed.dll sul PC per la connessione embedded
    /// </summary>
    [HttpGet("easyfatt/find-firebird")]
    public IActionResult FindFirebird()
    {
        var searchPaths = new[]
        {
            @"C:\Program Files (x86)\Danea Easyfatt",
            @"C:\Program Files\Danea Easyfatt",
            @"C:\Program Files (x86)\Danea",
            @"C:\Program Files\Danea",
            @"C:\Danea Easyfatt",
            @"C:\Windows\Firebird-2.5.9.27139-0_x64_embed",
            @"C:\Windows\Firebird-2.5.8.27089-0_x64_embed"
        };

        var found = new List<string>();

        foreach (var path in searchPaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                var files = Directory.GetFiles(path, "fbembed.dll", SearchOption.AllDirectories);
                found.AddRange(files);
            }
            catch { }
        }

        if (found.Count == 0)
            return Ok(ApiResponse<object>.Fail("fbembed.dll non trovata. Percorsi cercati: " + string.Join(", ", searchPaths)));

        return Ok(ApiResponse<object>.Ok(found));
    }

    /// <summary>
    /// Legge la struttura del database .eft (tabelle e colonne)
    /// </summary>
    [HttpGet("easyfatt/structure")]
    public IActionResult GetStructure([FromQuery] string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return BadRequest("filePath richiesto");

        if (!System.IO.File.Exists(filePath))
            return Ok(ApiResponse<object>.Fail("File non trovato: " + filePath));

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var conn = new FbConnection(connStr);
            conn.Open();

            // Leggi elenco tabelle
            var tablesData = conn.GetSchema("Tables");
            var tables = new List<object>();

            foreach (DataRow row in tablesData.Rows)
            {
                var tableName = row["TABLE_NAME"]?.ToString() ?? "";
                var tableType = row["TABLE_TYPE"]?.ToString() ?? "";

                // Solo tabelle utente (non di sistema)
                if (tableType != "TABLE" && tableType != "SYSTEM TABLE") continue;
                if (tableName.StartsWith("RDB$") || tableName.StartsWith("MON$")) continue;

                // Leggi colonne
                var colsData = conn.GetSchema("Columns", new[] { null, null, tableName, null });
                var columns = new List<object>();

                foreach (DataRow colRow in colsData.Rows)
                {
                    columns.Add(new
                    {
                        Name = colRow["COLUMN_NAME"]?.ToString(),
                        Type = colRow["COLUMN_DATA_TYPE"]?.ToString(),
                        Size = colRow["COLUMN_SIZE"]
                    });
                }

                // Conta righe
                int rowCount = 0;
                try
                {
                    using var cmd = new FbCommand($"SELECT COUNT(*) FROM \"{tableName}\"", conn);
                    rowCount = Convert.ToInt32(cmd.ExecuteScalar());
                }
                catch { }

                tables.Add(new
                {
                    TableName = tableName,
                    ColumnCount = columns.Count,
                    RowCount = rowCount,
                    Columns = columns
                });
            }

            return Ok(ApiResponse<object>.Ok(tables));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore lettura DB: {ex.Message}"));
        }
    }

    /// <summary>
    /// Legge un campione di righe da una tabella specifica
    /// </summary>
    [HttpGet("easyfatt/preview")]
    public IActionResult PreviewTable([FromQuery] string filePath, [FromQuery] string tableName, [FromQuery] int maxRows = 10)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(tableName))
            return BadRequest("filePath e tableName richiesti");

        if (!System.IO.File.Exists(filePath))
            return Ok(ApiResponse<object>.Fail("File non trovato"));

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var conn = new FbConnection(connStr);
            conn.Open();

            using var cmd = new FbCommand($"SELECT FIRST {maxRows} * FROM \"{tableName}\"", conn);
            using var reader = cmd.ExecuteReader();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (reader.Read())
            {
                var dict = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var val = reader[col];
                    dict[col] = val == DBNull.Value ? null : val;
                }
                rows.Add(dict);
            }

            return Ok(ApiResponse<object>.Ok(new { Columns = columns, RowCount = rows.Count, Rows = rows }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore lettura tabella: {ex.Message}"));
        }
    }

    private string BuildConnectionString(string filePath)
    {
        // Cerca fbclient.dll nella sottocartella Firebird accanto all'eseguibile
        var appDir = AppContext.BaseDirectory;
        var fbClientPath = _config["Easyfatt:FirebirdClientPath"]
            ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        return new FbConnectionStringBuilder
        {
            Database = filePath,
            ServerType = FbServerType.Embedded,
            UserID = "SYSDBA",
            Password = "masterkey",
            Charset = "NONE",
            ClientLibrary = fbClientPath
        }.ToString();
    }
}
