using System.Text.Json;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ATEC.PM.Server.Controllers;

// Changelog delle versioni pubblicate: le voci le scrive `aggiorna-server.ps1` a ogni
// deploy (prime righe dei commit git dall'ultima voce) in `changelog.json`, spedito
// accanto alle DLL; le segnalazioni chiuse escono da `bug_reports.fixed_in_build`.
// Chiave dedicata: nasce a livello 3 (solo Admin), concedibile dalla pagina Permessi.
[ApiController]
[Route("api/changelog")]
[Authorize]
[RequireFeature("nav.changelog")]
public class ChangelogController : ControllerBase
{
    private readonly DbService _db;
    public ChangelogController(DbService db) { _db = db; }

    // Forma del file su disco (il campo hash serve solo al deploy per sapere da dove
    // riprendere il git log: al client non arriva).
    private sealed class FileChangelog { public List<FileVoce> Voci { get; set; } = new(); }
    private sealed class FileVoce
    {
        public string Build { get; set; } = "";
        public string Data { get; set; } = "";
        public string Hash { get; set; } = "";
        public List<string> Modifiche { get; set; } = new();
    }

    [HttpGet]
    public IActionResult Get()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "changelog.json");
            FileChangelog file = System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<FileChangelog>(
                      System.IO.File.ReadAllText(path),
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? new FileChangelog()
                : new FileChangelog();

            using var c = _db.Open();
            var bugs = c.Query<(int Id, string Title, string Build)>(
                "SELECT id, COALESCE(title,''), COALESCE(fixed_in_build,'') FROM bug_reports WHERE COALESCE(fixed_in_build,'') <> ''").ToList();
            Dictionary<string, List<ChangelogSegnalazione>> perBuild = bugs
                .GroupBy(b => b.Build)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(b => b.Id)
                          .Select(b => new ChangelogSegnalazione { Id = b.Id, Title = b.Title })
                          .ToList());

            List<ChangelogVoce> voci = file.Voci
                .Select(v => new ChangelogVoce
                {
                    Build = v.Build,
                    Data = v.Data,
                    Modifiche = v.Modifiche,
                    Segnalazioni = perBuild.TryGetValue(v.Build, out var s) ? s : new(),
                })
                .ToList();

            // Build citate da bug_reports ma assenti dal file (chiusure precedenti alla
            // nascita del changelog): voce sintetica, così la storia non ha buchi.
            foreach (string build in perBuild.Keys.Except(file.Voci.Select(v => v.Build)))
                voci.Add(new ChangelogVoce { Build = build, Segnalazioni = perBuild[build] });

            // Il build id è un timestamp (yyyyMMdd-HHmm): l'ordinamento testuale È quello
            // cronologico, dalla versione più recente.
            voci = voci.OrderByDescending(v => v.Build, StringComparer.Ordinal).ToList();
            return Ok(ApiResponse<List<ChangelogVoce>>.Ok(voci));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<ChangelogVoce>>.Fail($"Errore: {ex.Message}"));
        }
    }
}
