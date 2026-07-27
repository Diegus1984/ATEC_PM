using System.Text;
using System.Text.RegularExpressions;
using ATEC.PM.Server;
using Dapper;
using MySqlConnector;
using Serilog;

namespace ATEC.PM.Server.Services;

/// <summary>Rimuove immagini inline (data:image/...) da HTML salvato in description_rtf.</summary>
public static class QuoteHtmlCleanup
{
    private static readonly Regex BgDataUrlRegex = new(
        @"background(?:-image)?\s*:\s*url\s*\(\s*['""]?data:image[^)]+['""]?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record CleanupStats(int Products, int QuoteItems, int MaterialItems, int LocalPathsCleared);

    public static string StripInlineDataImages(string? html)
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

        return BgDataUrlRegex.Replace(sb.ToString(), "");
    }

    private static bool ImgTagHasDataSrc(string tag)
    {
        int srcIdx = tag.IndexOf("src", StringComparison.OrdinalIgnoreCase);
        if (srcIdx < 0) return false;
        int dataIdx = tag.IndexOf("data:image", StringComparison.OrdinalIgnoreCase);
        return dataIdx > srcIdx;
    }

    public static CleanupStats Run(MySqlConnection c)
    {
        int products = CleanTable(c, "quote_products", "id");
        int quoteItems = CleanTable(c, "quote_items", "id");
        int materialItems = CleanTable(c, "quote_material_items", "id");

        int pathsCleared = c.Execute(@"
            UPDATE quote_products SET image_path='', attachment_path=''
            WHERE image_path LIKE 'C:%' OR image_path LIKE 'D:%'
               OR attachment_path LIKE 'C:%' OR attachment_path LIKE 'D:%'");

        Log.Information(
            "[QuoteHtmlCleanup] {Products} prodotti, {QuoteItems} quote_items, {MaterialItems} material_items, {Paths} path locali",
            products, quoteItems, materialItems, pathsCleared);

        return new CleanupStats(products, quoteItems, materialItems, pathsCleared);
    }

    private static int CleanTable(MySqlConnection c, string table, string idColumn)
    {
        List<(int Id, string DescriptionRtf)> rows = c.Query<(int Id, string DescriptionRtf)>(
            $"SELECT {idColumn} AS Id, description_rtf AS DescriptionRtf FROM {table} WHERE description_rtf LIKE '%data:image%'")
            .ToList();

        int cleaned = 0;
        foreach ((int id, string descriptionRtf) in rows)
        {
            string html = StripInlineDataImages(descriptionRtf);
            if (html == descriptionRtf) continue;

            c.Execute($"UPDATE {table} SET description_rtf=@Html WHERE {idColumn}=@Id",
                new { Html = html, Id = id });
            cleaned++;
        }

        return cleaned;
    }

    public static async Task RunCliAsync(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        if (OperatingSystem.IsWindows())
        {
            if (!ProtectedConfigHelper.IsConfigured())
            {
                string connStr = builder.Configuration["ConnectionStrings:Default"] ?? "";
                string jwt = builder.Configuration["Jwt:Key"] ?? "";
                if (!string.IsNullOrWhiteSpace(connStr) && !connStr.StartsWith("RUN:") &&
                    !string.IsNullOrWhiteSpace(jwt) && !jwt.StartsWith("RUN:"))
                {
                    ProtectedConfigHelper.GenerateSecretsFile(connStr, jwt);
                    ProtectedConfigHelper.CleanAppSettings();
                }
            }

            Dictionary<string, string?> secrets = ProtectedConfigHelper.LoadSecrets();
            if (secrets.Count > 0)
                builder.Configuration.AddInMemoryCollection(secrets);
        }

        builder.Services.AddSingleton<DbService>();
        builder.Services.AddSingleton<QuoteDbService>();
        WebApplication app = builder.Build();

        QuoteDbService qdb = app.Services.GetRequiredService<QuoteDbService>();
        using MySqlConnection conn = qdb.Open();

        int before = conn.ExecuteScalar<int>(@"
            SELECT
              (SELECT COUNT(*) FROM quote_products       WHERE description_rtf LIKE '%data:image%') +
              (SELECT COUNT(*) FROM quote_items          WHERE description_rtf LIKE '%data:image%') +
              (SELECT COUNT(*) FROM quote_material_items WHERE description_rtf LIKE '%data:image%')");

        CleanupStats stats = Run(conn);

        int after = conn.ExecuteScalar<int>(@"
            SELECT
              (SELECT COUNT(*) FROM quote_products       WHERE description_rtf LIKE '%data:image%') +
              (SELECT COUNT(*) FROM quote_items          WHERE description_rtf LIKE '%data:image%') +
              (SELECT COUNT(*) FROM quote_material_items WHERE description_rtf LIKE '%data:image%')");

        Console.WriteLine($"Righe con data:image prima: {before}");
        Console.WriteLine($"Pulite — prodotti: {stats.Products}, quote_items: {stats.QuoteItems}, material_items: {stats.MaterialItems}, path locali: {stats.LocalPathsCleared}");
        Console.WriteLine($"Righe con data:image dopo: {after}");

        await app.StopAsync();
    }
}
