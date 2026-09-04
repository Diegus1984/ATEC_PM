namespace ATEC.PM.Server.Services;

/// <summary>
/// La parte «decisionale» dello specchio anagrafiche Danea → <c>suppliers</c>/<c>customers</c>,
/// senza database né Firebird: come si legge una riga di <c>TAnagrafica</c> e quando una riga
/// locale va riscritta.
///
/// <para><b>Perché esiste.</b> Fino al 04/09/2026 <see cref="DaneaSyncService"/> faceva un
/// <c>INSERT … ON DUPLICATE KEY UPDATE</c> per OGNI anagrafica a OGNI giro: misurato in
/// produzione, 40.032 scritture su <c>suppliers</c> e 7.308 su <c>customers</c> in due giorni,
/// per 2.214 e 408 righe che cambiano di rado. Ora si confronta prima con quello che c'è già e
/// si scrive solo ciò che è diverso. Qui non serve un'impronta salvata: le colonne confrontate
/// sono poche e si leggono direttamente dalla tabella locale.</para>
///
/// <para>Il confronto è <b>stretto</b> (NULL ≠ stringa vuota): una colonna locale a NULL viene
/// riscritta a <c>''</c> una volta, come faceva il sync di prima a ogni giro, e da lì è uguale.</para>
/// </summary>
public static class SpecchioDanea
{
    /// <summary>Fornitore come lo scrive il sync (già ripulito: <c>Trim</c> ovunque, indirizzo composto).</summary>
    public sealed record Fornitore(string Vat, string Nome, string Referente, string Email, string Tel, string Address, string Cf, string Note);

    /// <summary>Le colonne di <c>suppliers</c> che l'UPDATE riscrive per una riga già presente: sono le sole confrontate.</summary>
    public sealed record FornitoreLocale(string? Nome, string? Referente, string? Email, string? Tel, string? Address, string? Note);

    /// <summary>Cliente come lo scrive il sync.</summary>
    public sealed record Cliente(string Vat, string Nome, string Referente, string Email, string Pec, string Tel, string Cell, string Address,
        string Cf, string Pagamento, string Sdi, string CodAnagr, int? IDAnagr, string Note);

    /// <summary>Le colonne di <c>customers</c> che l'UPDATE riscrive per una riga già presente: sono le sole confrontate.</summary>
    public sealed record ClienteLocale(string? Nome, string? Email, string? Pec, string? Tel, string? Address, string? Sdi, int? IDAnagr, string? Note);

    /// <summary>Colonne di <c>suppliers</c> riscritte dall'UPDATE: devono coincidere con quelle nella query (un test lo pretende).</summary>
    public static readonly string[] ColonneConfrontateFornitori =
        { "company_name", "contact_name", "email", "phone", "address", "notes" };

    /// <summary>Colonne di <c>customers</c> riscritte dall'UPDATE.</summary>
    public static readonly string[] ColonneConfrontateClienti =
        { "company_name", "email", "pec", "phone", "address", "sdi_code", "notes", "easyfatt_id" };

    /// <summary>
    /// L'indirizzo come lo compone il sync da sempre: <c>via, cap città (prov)</c>, senza
    /// spazi e virgole ai bordi. Con tutti i pezzi vuoti resta <c>()</c>: è così da sempre e
    /// cambiarlo riscriverebbe ogni riga vuota per un dettaglio estetico.
    /// </summary>
    public static string Indirizzo(string? indirizzo, string? cap, string? citta, string? prov) =>
        $"{Testo(indirizzo)}, {Testo(cap)} {Testo(citta)} ({Testo(prov)})".Trim(' ', ',');

    /// <summary>Riga di <c>TAnagrafica</c> → fornitore; <c>null</c> se senza partita IVA (il sync la salta).</summary>
    public static Fornitore? DaFornitore(IDictionary<string, object?> r)
    {
        string vat = Testo(Campo(r, "PartitaIva"));
        if (vat.Length == 0) return null;
        return new Fornitore(
            Vat: vat,
            Nome: Testo(Campo(r, "Nome")),
            Referente: Testo(Campo(r, "Referente")),
            Email: Testo(Campo(r, "Email")),
            Tel: Testo(Campo(r, "Tel")),
            Address: Indirizzo(Campo(r, "Indirizzo"), Campo(r, "Cap"), Campo(r, "Citta"), Campo(r, "Prov")),
            Cf: Testo(Campo(r, "CodiceFiscale")),
            Note: Testo(Campo(r, "Note")));
    }

    /// <summary>Riga di <c>TAnagrafica</c> → cliente; <c>null</c> se senza partita IVA.</summary>
    public static Cliente? DaCliente(IDictionary<string, object?> r)
    {
        string vat = Testo(Campo(r, "PartitaIva"));
        if (vat.Length == 0) return null;
        object? id = r.TryGetValue("IDAnagr", out object? v) ? v : null;
        return new Cliente(
            Vat: vat,
            Nome: Testo(Campo(r, "Nome")),
            Referente: Testo(Campo(r, "Referente")),
            Email: Testo(Campo(r, "Email")),
            Pec: Testo(Campo(r, "Pec")),
            Tel: Testo(Campo(r, "Tel")),
            Cell: Testo(Campo(r, "Cell")),
            Address: Indirizzo(Campo(r, "Indirizzo"), Campo(r, "Cap"), Campo(r, "Citta"), Campo(r, "Prov")),
            Cf: Testo(Campo(r, "CodiceFiscale")),
            Pagamento: Testo(Campo(r, "PagamentoDefault")),
            Sdi: Testo(Campo(r, "FE_CodUfficio")),
            CodAnagr: Testo(Campo(r, "CodAnagr")),
            IDAnagr: id is null or DBNull ? null : Convert.ToInt32(id),
            Note: Testo(Campo(r, "Note")));
    }

    /// <summary>Riga assente in locale, o una delle colonne riscritte dall'UPDATE diversa.</summary>
    public static bool DaRiscrivere(FornitoreLocale? locale, Fornitore remoto) =>
        locale is null
        || locale.Nome != remoto.Nome || locale.Referente != remoto.Referente || locale.Email != remoto.Email
        || locale.Tel != remoto.Tel || locale.Address != remoto.Address || locale.Note != remoto.Note;

    /// <summary>Riga assente in locale, o una delle colonne riscritte dall'UPDATE diversa.</summary>
    public static bool DaRiscrivere(ClienteLocale? locale, Cliente remoto) =>
        locale is null
        || locale.Nome != remoto.Nome || locale.Email != remoto.Email || locale.Pec != remoto.Pec
        || locale.Tel != remoto.Tel || locale.Address != remoto.Address || locale.Sdi != remoto.Sdi
        || locale.IDAnagr != remoto.IDAnagr || locale.Note != remoto.Note;

    /// <summary>Com'è la riga locale DOPO aver scritto il fornitore: serve a confrontare la riga successiva con la stessa partita IVA (Danea ne ha, e vince l'ultima).</summary>
    public static FornitoreLocale DopoScrittura(Fornitore f) =>
        new(f.Nome, f.Referente, f.Email, f.Tel, f.Address, f.Note);

    /// <summary>Idem per il cliente.</summary>
    public static ClienteLocale DopoScrittura(Cliente c) =>
        new(c.Nome, c.Email, c.Pec, c.Tel, c.Address, c.Sdi, c.IDAnagr, c.Note);

    /// <summary>
    /// Una riga per partita IVA: l'ULTIMA in ordine di arrivo, nell'ordine in cui compare per la
    /// prima volta. È la regola «vince l'ultima» di sempre, applicata prima del confronto invece
    /// che scrivendo tutte le copie una sopra l'altra.
    /// </summary>
    public static IReadOnlyList<Fornitore> UltimaPerPartitaIva(IEnumerable<Fornitore> righe) => Ultime(righe, f => f.Vat);

    /// <summary>Idem per i clienti.</summary>
    public static IReadOnlyList<Cliente> UltimaPerPartitaIva(IEnumerable<Cliente> righe) => Ultime(righe, c => c.Vat);

    private static IReadOnlyList<T> Ultime<T>(IEnumerable<T> righe, Func<T, string> chiave)
    {
        var ordine = new List<string>();
        var ultima = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T r in righe)
        {
            string k = chiave(r);
            if (!ultima.ContainsKey(k)) ordine.Add(k);
            ultima[k] = r;
        }
        return ordine.Select(k => ultima[k]).ToList();
    }

    private static string? Campo(IDictionary<string, object?> r, string nome) =>
        r.TryGetValue(nome, out object? v) && v is not null && v is not DBNull ? Convert.ToString(v) : null;

    private static string Testo(string? s) => s?.Trim() ?? "";
}
