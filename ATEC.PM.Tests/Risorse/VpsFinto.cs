using System.Net;
using System.Text;
using System.Text.Json;
using ATEC.PM.Server.Services.RisorseSync;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Risorse;

/// <summary>
/// Il VPS finto per i giri end-to-end (anagrafiche e allocazioni): un <see cref="HttpMessageHandler"/>
/// che risponde come <c>/api/sync</c> vero, tenendo tutto in memoria.
/// <list type="bullet">
/// <item>login e stato;</item>
/// <item><c>GET employees</c> dall'elenco <see cref="Dipendenti"/>; <c>PUT employees/projects</c>
/// con esiti riga per riga (Id null → <c>created</c> con un id nuovo; Id presente →
/// <c>updated</c>; i cognomi in <see cref="DaSaltare"/> → <c>skipped</c>);</item>
/// <item>i reparti li ricorda: come il VPS vero risponde <c>Created</c>/<c>Updated</c> solo per
/// quelli nuovi o cambiati, 0/0 se cambiano solo i legami;</item>
/// <item>le <see cref="Allocazioni"/>: <c>GET assignments</c> le restituisce; <c>POST assignments</c>
/// crea (Id null → id nuovo, <c>created</c>), aggiorna (<c>updated</c>) o risponde
/// <c>unchanged</c> se la riga è identica, <c>skipped</c> se l'Id non esiste o il dipendente è in
/// <see cref="DipendentiDaSaltare"/>; <c>POST assignments/delete</c> toglie (<c>deleted</c> /
/// <c>missing</c>).</item>
/// </list>
/// Tiene ogni chiamata col suo corpo (<see cref="Chiamate"/>).
/// </summary>
internal sealed class VpsFinto : HttpMessageHandler
{
    private const string LoginOk = "{\"success\":true,\"data\":{\"token\":\"jwt-finto\",\"employeeId\":1,\"fullName\":\"[SYNC] ATEC PM\",\"userRole\":\"SYNC\"},\"message\":\"\"}";
    private const string StatoOk = "{\"success\":true,\"data\":{\"serverUtc\":\"2026-09-02T10:00:00Z\",\"employees\":2,\"projects\":0,\"departments\":0,\"assignments\":0,\"version\":\"1.4.0\"},\"message\":\"\"}";

    public List<SyncEmployeeDto> Dipendenti { get; } = new();
    public HashSet<string> DaSaltare { get; } = new();
    public List<SyncAssignmentDto> Allocazioni { get; } = new();
    /// <summary>Id VPS dei dipendenti per cui la POST delle allocazioni risponde <c>skipped</c>.</summary>
    public HashSet<int> DipendentiDaSaltare { get; } = new();
    public List<(string Metodo, string Percorso, string Corpo)> Chiamate { get; } = new();
    /// <summary>
    /// Eseguito PRIMA di rispondere alla <c>GET assignments</c>, cioè dopo che il motore ha già
    /// letto le righe di PM: per simulare un utente che salva dal planner a giro in corso.
    /// </summary>
    public Action? PrimaDelleAllocazioni { get; set; }
    private int _prossimoId = 100;
    private int _prossimaAllocazione = 500;
    /// <summary>I reparti già sul VPS: codice → firma dei campi.</summary>
    private readonly Dictionary<string, string> _reparti = new();

    public int Put => Chiamate.Count(x => x.Metodo == "PUT");

    /// <summary>Le POST sulle allocazioni (creazioni/aggiornamenti), senza le cancellazioni.</summary>
    public int PostAllocazioni => Chiamate.Count(x => x.Metodo == "POST" && x.Percorso.EndsWith("/api/sync/assignments"));

    /// <summary>Le POST di cancellazione delle allocazioni.</summary>
    public int PostCancellazioni => Chiamate.Count(x => x.Metodo == "POST" && x.Percorso.EndsWith("/api/sync/assignments/delete"));

    /// <summary>Il corpo dell'ULTIMA scrittura (PUT o POST) su quel percorso, deserializzato come lo legge il VPS.</summary>
    public T Corpo<T>(string percorso) =>
        JsonSerializer.Deserialize<T>(Chiamate.Last(x => x.Metodo != "GET" && x.Percorso == percorso).Corpo, RisorseSyncClient.JsonOptions)!;

    /// <summary>Mette un'allocazione «già sul VPS» con un id nuovo e la ritorna.</summary>
    public SyncAssignmentDto Allocazione(int employeeId, string tipo, DateOnly inizio, DateOnly fine,
        string? descrizione = null, int? projectId = null, int? updatedBy = null, DateTime? updatedAtUtc = null)
    {
        var a = new SyncAssignmentDto
        {
            Id = _prossimaAllocazione++, EmployeeId = employeeId, Tipo = tipo, DataInizio = inizio, DataFine = fine,
            Descrizione = descrizione, ProjectId = projectId, UpdatedBy = updatedBy, UpdatedAt = updatedAtUtc,
            CreatedAt = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
        };
        Allocazioni.Add(a);
        return a;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string corpo = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        string percorso = request.RequestUri!.AbsolutePath;
        Chiamate.Add((request.Method.Method, percorso, corpo));

        if (percorso.EndsWith("/api/auth/login")) return Json(LoginOk);
        if (percorso.EndsWith("/api/sync/status")) return Json(StatoOk);
        if (request.Method == HttpMethod.Get && percorso.EndsWith("/api/sync/employees"))
            return Ok(Dipendenti);
        if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/employees"))
            return Ok(Esiti(JsonSerializer.Deserialize<List<SyncEmployeeDto>>(corpo, RisorseSyncClient.JsonOptions)!,
                d => d.Id, d => DaSaltare.Contains(d.LastName)));
        if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/departments"))
        {
            SyncDepartmentsRequest r = JsonSerializer.Deserialize<SyncDepartmentsRequest>(corpo, RisorseSyncClient.JsonOptions)!;
            int creati = 0, aggiornati = 0, invariati = 0;
            foreach (SyncDepartmentDto d in r.Departments)
            {
                string firma = $"{d.Name}|{d.SortOrder}|{d.IsActive}";
                if (!_reparti.TryGetValue(d.Code, out string? prima)) creati++;
                else if (prima != firma) aggiornati++;
                else invariati++;
                _reparti[d.Code] = firma;
            }
            return Ok(new SyncCountsDto { Created = creati, Updated = aggiornati, Unchanged = invariati, Links = r.Links.Count });
        }
        if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/projects"))
            return Ok(Esiti(JsonSerializer.Deserialize<List<SyncProjectDto>>(corpo, RisorseSyncClient.JsonOptions)!,
                p => p.Id, _ => false));
        if (request.Method == HttpMethod.Get && percorso.EndsWith("/api/sync/assignments"))
        {
            PrimaDelleAllocazioni?.Invoke();
            return Ok(Allocazioni);
        }
        if (request.Method == HttpMethod.Post && percorso.EndsWith("/api/sync/assignments/delete"))
            return Ok(Cancella(JsonSerializer.Deserialize<SyncDeleteRequest>(corpo, RisorseSyncClient.JsonOptions)!));
        if (request.Method == HttpMethod.Post && percorso.EndsWith("/api/sync/assignments"))
            return Ok(Upsert(JsonSerializer.Deserialize<List<SyncAssignmentUpsertDto>>(corpo, RisorseSyncClient.JsonOptions)!));
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private List<SyncUpsertResultDto> Esiti<T>(List<T> righe, Func<T, int?> id, Func<T, bool> salta)
    {
        var esiti = new List<SyncUpsertResultDto>();
        for (int i = 0; i < righe.Count; i++)
        {
            if (salta(righe[i]))
                esiti.Add(new SyncUpsertResultDto { Index = i, Action = "skipped", Error = "EmployeeId inesistente" });
            else if (id(righe[i]) is int esistente)
                esiti.Add(new SyncUpsertResultDto { Index = i, Id = esistente, Action = "updated" });
            else
                esiti.Add(new SyncUpsertResultDto { Index = i, Id = _prossimoId++, Action = "created" });
        }
        return esiti;
    }

    /// <summary>POST assignments come sul VPS: created / updated / unchanged / skipped, riga per riga.</summary>
    private List<SyncUpsertResultDto> Upsert(List<SyncAssignmentUpsertDto> righe)
    {
        var esiti = new List<SyncUpsertResultDto>();
        for (int i = 0; i < righe.Count; i++)
        {
            SyncAssignmentUpsertDto r = righe[i];
            if (DipendentiDaSaltare.Contains(r.EmployeeId))
            {
                esiti.Add(new SyncUpsertResultDto { Index = i, Action = "skipped", Error = $"EmployeeId {r.EmployeeId} inesistente" });
                continue;
            }
            if (r.DataFine < r.DataInizio)
            {
                esiti.Add(new SyncUpsertResultDto { Index = i, Action = "skipped", Error = "date invertite" });
                continue;
            }
            if (r.Id == null)
            {
                var nuova = new SyncAssignmentDto
                {
                    Id = _prossimaAllocazione++, CreatedAt = DateTime.UtcNow,
                    UpdatedAt = r.UpdatedAt ?? DateTime.UtcNow,
                };
                Copia(r, nuova);
                Allocazioni.Add(nuova);
                esiti.Add(new SyncUpsertResultDto { Index = i, Id = nuova.Id, Action = "created" });
                continue;
            }
            SyncAssignmentDto? esistente = Allocazioni.FirstOrDefault(a => a.Id == r.Id);
            if (esistente == null)
            {
                esiti.Add(new SyncUpsertResultDto { Index = i, Action = "skipped", Error = $"Id {r.Id} inesistente" });
                continue;
            }
            if (Uguale(esistente, r))
            {
                esiti.Add(new SyncUpsertResultDto { Index = i, Id = esistente.Id, Action = "unchanged" });
                continue;
            }
            Copia(r, esistente);
            esistente.UpdatedAt = r.UpdatedAt ?? DateTime.UtcNow;
            esiti.Add(new SyncUpsertResultDto { Index = i, Id = esistente.Id, Action = "updated" });
        }
        return esiti;
    }

    private static void Copia(SyncAssignmentUpsertDto da, SyncAssignmentDto a)
    {
        a.EmployeeId = da.EmployeeId;
        a.Tipo = da.Tipo;
        a.DataInizio = da.DataInizio;
        a.DataFine = da.DataFine;
        a.ProjectId = da.ProjectId;
        a.ServiceId = da.ServiceId;
        a.OtherActivityId = da.OtherActivityId;
        a.Descrizione = da.Descrizione;
        a.UpdatedBy = da.UpdatedBy;
    }

    private static bool Uguale(SyncAssignmentDto a, SyncAssignmentUpsertDto b) =>
        a.EmployeeId == b.EmployeeId && a.Tipo == b.Tipo && a.DataInizio == b.DataInizio && a.DataFine == b.DataFine
        && a.ProjectId == b.ProjectId && a.ServiceId == b.ServiceId && a.OtherActivityId == b.OtherActivityId
        && (a.Descrizione ?? "") == (b.Descrizione ?? "");

    /// <summary>POST assignments/delete: deleted / missing per ogni id.</summary>
    private List<SyncUpsertResultDto> Cancella(SyncDeleteRequest req)
    {
        var esiti = new List<SyncUpsertResultDto>();
        for (int i = 0; i < req.Ids.Count; i++)
        {
            int tolte = Allocazioni.RemoveAll(a => a.Id == req.Ids[i]);
            esiti.Add(new SyncUpsertResultDto { Index = i, Id = req.Ids[i], Action = tolte > 0 ? "deleted" : "missing" });
        }
        return esiti;
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        Json(JsonSerializer.Serialize(ApiResponse<T>.Ok(data), RisorseSyncClient.JsonOptions));

    private static HttpResponseMessage Json(string corpo) =>
        new(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
}
