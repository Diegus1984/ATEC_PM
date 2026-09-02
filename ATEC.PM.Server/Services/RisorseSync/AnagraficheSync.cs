using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.RisorseSync;

// ═══════════════════════════════════════════════════════════════
// Le righe di ATEC PM come le legge il motore (Dapper, alias nelle
// SELECT). Classi e non record: Dapper mappa per proprietà.
// ═══════════════════════════════════════════════════════════════

/// <summary>Un dipendente di <c>employees</c> (senza admin, ADMIN e SYNC: li filtra la SELECT).</summary>
public sealed class DipendentePm
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string EmpType { get; set; } = "INTERNAL";
    public string Status { get; set; } = "ACTIVE";
    public string UserRole { get; set; } = "TECH";
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
}

/// <summary>Un reparto di <c>departments</c>.</summary>
public sealed class RepartoPm
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Un legame di <c>employee_departments</c>.</summary>
public sealed class LegamePm
{
    public int EmployeeId { get; set; }
    public int DepartmentId { get; set; }
    public bool IsResponsible { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Una commessa di <c>projects</c>.</summary>
public sealed class CommessaPm
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
}

/// <summary>Una coppia trovata dal seme: chi in PM, chi sul VPS e con quale regola.</summary>
public sealed record Abbinamento(int LocalId, int RemoteId, string Criterio);

/// <summary>L'esito del seme della mappa dipendenti (<see cref="AnagraficheSync.Abbina"/>).</summary>
public sealed class EsitoAbbinamento
{
    public List<Abbinamento> Abbinamenti { get; } = new();
    /// <summary>Dipendenti PM senza mappa e senza un gemello sul VPS: verranno CREATI se interni.</summary>
    public List<DipendentePm> NonAbbinatiPm { get; } = new();
    /// <summary>Dipendenti del VPS che nessuno reclama (es. «pasquale zamputo»): si nominano, non si toccano.</summary>
    public List<SyncEmployeeDto> SoloVps { get; } = new();
}

/// <summary>Una riga pronta per il VPS con l'id PM e l'impronta che verrà salvata a esito buono.</summary>
public sealed record RigaDaInviare<T>(int LocalId, T Dto, string Impronta, bool Mappata);

/// <summary>
/// La logica PURA della Fase 1 (anagrafiche PM → VPS + seme della mappa dipendenti):
/// normalizzazione dei nomi, abbinamento, impronte, costruzione dei payload. Niente
/// database e niente rete, così ogni regola ha il suo test (<c>AnagraficheSyncTests</c>).
/// Il giro vero, che legge MySQL e chiama il VPS, sta in <c>RisorseSyncService</c>.
/// </summary>
public static class AnagraficheSync
{
    public const string CriterioUsername = "username";
    public const string CriterioNome = "nome";
    public const string CriterioToken = "token";

    /// <summary>Ruoli che non viaggiano MAI verso il VPS: un nuovo dipendente con uno di questi arriva come PM.</summary>
    private static readonly HashSet<string> RuoliVietati = new(StringComparer.OrdinalIgnoreCase) { "ADMIN", "SYNC" };

    // ── Normalizzazione ──────────────────────────────────────────

    /// <summary>
    /// Minuscolo, trim, spazi interni collassati a uno, senza accenti («Larganà» → «largana»).
    /// Gli accenti si tolgono scomponendo in NFD e buttando i segni diacritici
    /// (<see cref="UnicodeCategory.NonSpacingMark"/>): è così che «à» diventa «a» senza una
    /// tabella di sostituzioni scritta a mano.
    /// </summary>
    public static string Normalizza(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return "";
        string nfd = testo.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        bool spazio = false;
        foreach (char ch in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                spazio = true;
                continue;
            }
            if (spazio && sb.Length > 0) sb.Append(' ');
            spazio = false;
            sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NomeCompleto(string? nome, string? cognome) =>
        Normalizza(nome) + "|" + Normalizza(cognome);

    private static HashSet<string> Token(string? nome) =>
        Normalizza(nome).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static string? UsernameNormalizzato(string? username)
    {
        string u = (username ?? "").Trim().ToLowerInvariant();
        return u.Length == 0 ? null : u;
    }

    /// <summary>
    /// Gli account di sistema del VPS (<c>admin</c>, «[SYNC] ATEC PM»): la SELECT di PM li
    /// toglie dal suo lato, qui si tolgono dal lato VPS, così non entrano né negli abbinamenti
    /// (un PM omonimo dell'admin verrebbe rifiutato a ogni giro) né nei «solo VPS».
    /// </summary>
    private static bool AccountDiSistema(SyncEmployeeDto v) =>
        RuoliVietati.Contains(v.UserRole ?? "")
        || string.Equals((v.Username ?? "").Trim(), "admin", StringComparison.OrdinalIgnoreCase);

    // ── Seme della mappa dipendenti ──────────────────────────────

    /// <summary>
    /// Abbina i dipendenti PM non ancora mappati a quelli del VPS non ancora reclamati, con
    /// tre regole in ordine di fiducia:
    /// <list type="number">
    /// <item><b>username</b> uguale (case-insensitive, trim, entrambi non vuoti);</item>
    /// <item><b>nome + cognome</b> normalizzati uguali;</item>
    /// <item><b>cognome</b> uguale e i token del nome di uno contenuti in quelli dell'altro
    /// («Vasile Ovidiu»/«Ovidiu»), SOLO se il candidato è unico su entrambi i lati.</item>
    /// </list>
    /// Un dipendente VPS si abbina al massimo una volta; chi è già nella mappa (da una parte
    /// o dall'altra) resta fuori dal gioco, come gli account di sistema del VPS. Un candidato
    /// ambiguo (due omonimi, da QUALUNQUE lato) non si abbina: meglio un dipendente creato in
    /// più che due persone scambiate.
    /// </summary>
    public static EsitoAbbinamento Abbina(
        IReadOnlyList<DipendentePm> dipendentiPm,
        IReadOnlyList<SyncEmployeeDto> dipendentiVps,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappaEsistente)
    {
        var esito = new EsitoAbbinamento();
        var remotiMappati = mappaEsistente.Values.Select(v => v.RemoteId).ToHashSet();

        List<DipendentePm> liberiPm = dipendentiPm.Where(d => !mappaEsistente.ContainsKey(d.Id)).ToList();
        List<SyncEmployeeDto> liberiVps = dipendentiVps
            .Where(v => v.Id.HasValue && !remotiMappati.Contains(v.Id.Value) && !AccountDiSistema(v))
            .ToList();

        // 1) username — unico su entrambi i lati
        foreach (DipendentePm pm in liberiPm.ToList())
        {
            string? u = UsernameNormalizzato(pm.Username);
            if (u == null) continue;
            List<SyncEmployeeDto> candidati = liberiVps.Where(v => UsernameNormalizzato(v.Username) == u).ToList();
            if (candidati.Count != 1) continue;
            if (liberiPm.Count(altro => UsernameNormalizzato(altro.Username) == u) != 1) continue;
            Prendi(esito, liberiPm, liberiVps, pm, candidati[0], CriterioUsername);
        }

        // 2) nome + cognome — unico su entrambi i lati: con due omonimi in PM e uno sul VPS
        // sceglierebbe il primo per ordine di lista, cioè a caso.
        foreach (DipendentePm pm in liberiPm.ToList())
        {
            string chiave = NomeCompleto(pm.FirstName, pm.LastName);
            if (chiave == "|") continue;
            List<SyncEmployeeDto> candidati = liberiVps.Where(v => NomeCompleto(v.FirstName, v.LastName) == chiave).ToList();
            if (candidati.Count != 1) continue;
            if (liberiPm.Count(altro => NomeCompleto(altro.FirstName, altro.LastName) == chiave) != 1) continue;
            Prendi(esito, liberiPm, liberiVps, pm, candidati[0], CriterioNome);
        }

        // 3) cognome + token del nome, solo se unico su entrambi i lati
        foreach (DipendentePm pm in liberiPm.ToList())
        {
            string cognome = Normalizza(pm.LastName);
            if (cognome.Length == 0) continue;
            HashSet<string> tokenPm = Token(pm.FirstName);
            if (tokenPm.Count == 0) continue;

            List<SyncEmployeeDto> candidati = liberiVps
                .Where(v => Normalizza(v.LastName) == cognome && TokenCompatibili(tokenPm, Token(v.FirstName)))
                .ToList();
            if (candidati.Count != 1) continue;
            SyncEmployeeDto vps = candidati[0];

            // Unico anche dall'altra parte: nessun altro PM libero deve poter reclamare lo stesso VPS.
            HashSet<string> tokenVps = Token(vps.FirstName);
            int rivali = liberiPm.Count(altro =>
                altro.Id != pm.Id
                && Normalizza(altro.LastName) == cognome
                && TokenCompatibili(Token(altro.FirstName), tokenVps));
            if (rivali > 0) continue;

            Prendi(esito, liberiPm, liberiVps, pm, vps, CriterioToken);
        }

        esito.NonAbbinatiPm.AddRange(liberiPm);
        esito.SoloVps.AddRange(liberiVps);
        return esito;
    }

    /// <summary>I token del nome di uno sono un sottoinsieme (non vuoto) di quelli dell'altro.</summary>
    private static bool TokenCompatibili(HashSet<string> a, HashSet<string> b) =>
        a.Count > 0 && b.Count > 0 && (a.IsSubsetOf(b) || b.IsSubsetOf(a));

    private static void Prendi(EsitoAbbinamento esito, List<DipendentePm> liberiPm, List<SyncEmployeeDto> liberiVps,
        DipendentePm pm, SyncEmployeeDto vps, string criterio)
    {
        esito.Abbinamenti.Add(new Abbinamento(pm.Id, vps.Id!.Value, criterio));
        liberiPm.Remove(pm);
        liberiVps.Remove(vps);
    }

    // ── Impronte ─────────────────────────────────────────────────

    /// <summary>SHA256 (esadecimale minuscolo) di FirstName|LastName|Email|EmpType|Status. Le credenziali NON entrano: cambiare password non è una modifica da inviare.</summary>
    public static string ImprontaDipendente(SyncEmployeeDto d) =>
        Sha256($"{d.FirstName}|{d.LastName}|{d.Email ?? ""}|{d.EmpType}|{d.Status}");

    /// <summary>SHA256 di Code|Title|Status.</summary>
    public static string ImprontaCommessa(SyncProjectDto p) =>
        Sha256($"{p.Code}|{p.Title}|{p.Status}");

    /// <summary>
    /// SHA256 del payload dei reparti serializzato in modo deterministico: reparti ordinati
    /// per codice, legami per EmployeeId + DepartmentCode. Lo stesso contenuto in un ordine
    /// diverso dà la stessa impronta.
    /// </summary>
    public static string ImprontaReparti(SyncDepartmentsRequest payload)
    {
        var ordinato = new SyncDepartmentsRequest
        {
            Departments = payload.Departments.OrderBy(d => d.Code, StringComparer.Ordinal).ToList(),
            Links = payload.Links
                .OrderBy(l => l.EmployeeId)
                .ThenBy(l => l.DepartmentCode, StringComparer.Ordinal)
                .ToList(),
        };
        return Sha256(JsonSerializer.Serialize(ordinato, RisorseSyncClient.JsonOptions));
    }

    private static string Sha256(string testo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo))).ToLowerInvariant();

    // ── Payload ──────────────────────────────────────────────────

    /// <summary>Il DTO di un dipendente per il VPS. Le credenziali (ruolo, username, hash) SOLO se non è mappato, cioè se sul VPS va creato.</summary>
    public static SyncEmployeeDto DtoDipendente(DipendentePm d, int? remoteId)
    {
        var dto = new SyncEmployeeDto
        {
            Id = remoteId,
            FirstName = d.FirstName,
            LastName = d.LastName,
            Email = d.Email,
            EmpType = string.IsNullOrWhiteSpace(d.EmpType) ? "INTERNAL" : d.EmpType,
            Status = string.IsNullOrWhiteSpace(d.Status) ? "ACTIVE" : d.Status,
        };
        if (remoteId == null)
        {
            // Nuovo sul VPS: stesso login di PM, ruolo mai ADMIN/SYNC (§4.4: «mai superiore a PM»).
            dto.UserRole = string.IsNullOrWhiteSpace(d.UserRole) || RuoliVietati.Contains(d.UserRole) ? "PM" : d.UserRole;
            dto.Username = string.IsNullOrWhiteSpace(d.Username) ? null : d.Username;
            dto.PasswordHash = string.IsNullOrWhiteSpace(d.PasswordHash) ? null : d.PasswordHash;
        }
        return dto;
    }

    /// <summary>
    /// I dipendenti da mandare al VPS: quelli mappati OPPURE interni (un esterno non mappato
    /// non è una risorsa del planner). Con <paramref name="invioCompleto"/> partono tutti,
    /// altrimenti solo i non mappati e quelli con l'impronta diversa dall'ultimo invio.
    /// </summary>
    public static List<RigaDaInviare<SyncEmployeeDto>> DipendentiDaInviare(
        IReadOnlyList<DipendentePm> dipendenti,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappa,
        bool invioCompleto)
    {
        var righe = new List<RigaDaInviare<SyncEmployeeDto>>();
        foreach (DipendentePm d in dipendenti.OrderBy(x => x.Id))
        {
            bool mappato = mappa.TryGetValue(d.Id, out RisorseSyncMap.Voce voce);
            if (!mappato && !d.EmpType.Equals("INTERNAL", StringComparison.OrdinalIgnoreCase)) continue;

            SyncEmployeeDto dto = DtoDipendente(d, mappato ? voce.RemoteId : null);
            string impronta = ImprontaDipendente(dto);
            if (!invioCompleto && mappato && voce.SyncedHash == impronta) continue;
            righe.Add(new RigaDaInviare<SyncEmployeeDto>(d.Id, dto, impronta, mappato));
        }
        return righe;
    }

    /// <summary>
    /// Il payload dei reparti: TUTTI i reparti PM (codice in maiuscolo) e i legami dei soli
    /// dipendenti mappati (EmployeeId = id VPS). Un legame verso un reparto che non esiste
    /// più o di un dipendente non mappato non parte.
    /// </summary>
    public static SyncDepartmentsRequest CostruisciReparti(
        IReadOnlyList<RepartoPm> reparti,
        IReadOnlyList<LegamePm> legami,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappaDipendenti)
    {
        var codici = new Dictionary<int, string>();
        var payload = new SyncDepartmentsRequest();
        foreach (RepartoPm r in reparti.OrderBy(x => (x.Code ?? "").Trim().ToUpperInvariant(), StringComparer.Ordinal))
        {
            string codice = (r.Code ?? "").Trim().ToUpperInvariant();
            if (codice.Length == 0) continue;
            codici[r.Id] = codice;
            payload.Departments.Add(new SyncDepartmentDto
            {
                Code = codice,
                Name = r.Name ?? "",
                SortOrder = r.SortOrder,
                IsActive = r.IsActive,
            });
        }

        foreach (LegamePm l in legami)
        {
            if (!codici.TryGetValue(l.DepartmentId, out string? codice)) continue;
            if (!mappaDipendenti.TryGetValue(l.EmployeeId, out RisorseSyncMap.Voce voce)) continue;
            payload.Links.Add(new SyncEmployeeDepartmentDto
            {
                EmployeeId = voce.RemoteId,
                DepartmentCode = codice,
                IsResponsible = l.IsResponsible,
                IsPrimary = l.IsPrimary,
            });
        }
        payload.Links = payload.Links
            .OrderBy(l => l.EmployeeId)
            .ThenBy(l => l.DepartmentCode, StringComparer.Ordinal)
            .ToList();
        return payload;
    }

    /// <summary>
    /// Le commesse da mandare al VPS: quelle mappate OPPURE in stato ACTIVE (§7 punto 4: la
    /// tendina del planner). Stessa regola d'invio dei dipendenti.
    /// </summary>
    public static List<RigaDaInviare<SyncProjectDto>> CommesseDaInviare(
        IReadOnlyList<CommessaPm> commesse,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappa,
        bool invioCompleto)
    {
        var righe = new List<RigaDaInviare<SyncProjectDto>>();
        foreach (CommessaPm p in commesse.OrderBy(x => x.Id))
        {
            bool mappata = mappa.TryGetValue(p.Id, out RisorseSyncMap.Voce voce);
            if (!mappata && !p.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;

            var dto = new SyncProjectDto
            {
                Id = mappata ? voce.RemoteId : null,
                Code = p.Code ?? "",
                Title = p.Title ?? "",
                Status = string.IsNullOrWhiteSpace(p.Status) ? "ACTIVE" : p.Status,
            };
            string impronta = ImprontaCommessa(dto);
            if (!invioCompleto && mappata && voce.SyncedHash == impronta) continue;
            righe.Add(new RigaDaInviare<SyncProjectDto>(p.Id, dto, impronta, mappata));
        }
        return righe;
    }

    /// <summary>true se l'ultimo invio completo manca o ha più di 24 ore: si rimanda tutto, così un VPS ripristinato da backup si riallinea da solo entro un giorno.</summary>
    public static bool ServeInvioCompleto(DateTime? ultimoInvioCompletoUtc, DateTime adessoUtc) =>
        ultimoInvioCompletoUtc == null || adessoUtc - ultimoInvioCompletoUtc.Value >= TimeSpan.FromHours(24);
}
