using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

Regex bgDataUrlRegex = new(
    @"background(?:-image)?\s*:\s*url\s*\(\s*['""]?data:image[^)]+['""]?\s*\)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);

string root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "ATEC.PM.Server"));
if (!Directory.Exists(root))
    root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ATEC.PM.Server"));

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(root)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

string? cs = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("Connection string mancante in appsettings.json");
    return 1;
}

await using MySqlConnection conn = new(cs);
await conn.OpenAsync();

if (args.Contains("--inspect"))
{
    foreach (string table in new[] { "quote_products", "quote_items", "quote_material_items" })
    {
        string? sample = await conn.ExecuteScalarAsync<string?>(
            $"SELECT description_rtf FROM {table} WHERE description_rtf LIKE '%data:image%' LIMIT 1");
        if (sample == null) continue;
        Console.WriteLine($"=== {table} (len={sample.Length}) ===");
        Console.WriteLine(sample.Length > 800 ? sample[..800] : sample);
        Console.WriteLine();
    }
    return 0;
}

int before = await conn.ExecuteScalarAsync<int>(@"
    SELECT
      (SELECT COUNT(*) FROM quote_products       WHERE description_rtf LIKE '%data:image%') +
      (SELECT COUNT(*) FROM quote_items          WHERE description_rtf LIKE '%data:image%') +
      (SELECT COUNT(*) FROM quote_material_items WHERE description_rtf LIKE '%data:image%')");

Console.WriteLine($"Righe con data:image prima: {before}");

int products = await CleanTable(conn, "quote_products", "id");
int quoteItems = await CleanTable(conn, "quote_items", "id");
int materialItems = await CleanTable(conn, "quote_material_items", "id");

int paths = await conn.ExecuteAsync(@"
    UPDATE quote_products SET image_path='', attachment_path=''
    WHERE image_path LIKE 'C:%' OR image_path LIKE 'D:%'
       OR attachment_path LIKE 'C:%' OR attachment_path LIKE 'D:%'");

int after = await conn.ExecuteScalarAsync<int>(@"
    SELECT
      (SELECT COUNT(*) FROM quote_products       WHERE description_rtf LIKE '%data:image%') +
      (SELECT COUNT(*) FROM quote_items          WHERE description_rtf LIKE '%data:image%') +
      (SELECT COUNT(*) FROM quote_material_items WHERE description_rtf LIKE '%data:image%')");

Console.WriteLine($"Pulite — prodotti: {products}, quote_items: {quoteItems}, material_items: {materialItems}, path locali: {paths}");
Console.WriteLine($"Righe con data:image dopo: {after}");
return after > 0 ? 2 : 0;

async Task<int> CleanTable(MySqlConnection c, string table, string idCol)
{
    List<(int Id, string Html)> rows = (await c.QueryAsync<(int Id, string Html)>(
        $"SELECT {idCol} AS Id, description_rtf AS Html FROM {table} WHERE description_rtf LIKE '%data:image%'"))
        .ToList();

    int n = 0;
    foreach ((int id, string html) in rows)
    {
        string cleaned = StripInlineDataImages(html);
        if (cleaned == html) continue;
        await c.ExecuteAsync($"UPDATE {table} SET description_rtf=@Html WHERE {idCol}=@Id",
            new { Html = cleaned, Id = id });
        n++;
    }

    return n;
}

string StripInlineDataImages(string? html)
{
    if (string.IsNullOrEmpty(html)) return html ?? "";

    StringBuilder sb = new StringBuilder(html.Length);
    int pos = 0;
    while (pos < html.Length)
    {
        int imgStart = html.IndexOf("<img", pos, StringComparison.OrdinalIgnoreCase);
        if (imgStart < 0)
        {
            sb.Append(html, pos, html.Length - pos);
            break;
        }

        sb.Append(html, pos, imgStart - pos);

        int tagEnd = html.IndexOf('>', imgStart);
        if (tagEnd < 0)
        {
            string tail = html[imgStart..];
            if (!ImgTagHasDataSrc(tail))
                sb.Append(tail);
            break;
        }

        string tag = html.Substring(imgStart, tagEnd - imgStart + 1);
        if (ImgTagHasDataSrc(tag))
            pos = tagEnd + 1;
        else
        {
            sb.Append(tag);
            pos = tagEnd + 1;
        }
    }

    return bgDataUrlRegex.Replace(sb.ToString(), "");
}

bool ImgTagHasDataSrc(string tag)
{
    int srcIdx = tag.IndexOf("src", StringComparison.OrdinalIgnoreCase);
    if (srcIdx < 0) return false;
    int dataIdx = tag.IndexOf("data:image", StringComparison.OrdinalIgnoreCase);
    return dataIdx > srcIdx;
}
