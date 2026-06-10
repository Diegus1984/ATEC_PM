namespace ATEC.PM.Shared.DTOs;

// DTO del sottosistema Gamma Robot (distinta schede/componenti per robot+quadro).
// Anagrafica componenti = quote_products (listino "Gamma Ricambi").
// gamma_robot (modello) → gamma_quadro (configurazione/controllore) → gamma_distinta.

/// <summary>Modello robot (il nome): IRB 1600 Type A, IRB 2400, ...</summary>
public class GammaRobotDto
{
    public int Id { get; set; }
    public string Modello { get; set; } = "";
    public string? Serie { get; set; }
    public string Brand { get; set; } = "ABB";
    public string? Note { get; set; }
    public int QuadriCount { get; set; }
}

/// <summary>Configurazione/quadro di un robot (controllore + payload + OS).</summary>
public class GammaQuadroDto
{
    public int Id { get; set; }
    public int RobotId { get; set; }
    public string? Controllore { get; set; }
    public string? Generazione { get; set; }
    public string? Payload { get; set; }
    public string? AreaLavoro { get; set; }
    public string? OsVersion { get; set; }
    public string? SystemKey { get; set; }
    public string? Note { get; set; }
    public int ComponentiCount { get; set; }
}

/// <summary>Riga di distinta: componente che compone un quadro, col prezzo VB.</summary>
public class GammaDistintaItemDto
{
    public int Id { get; set; }
    public int QuadroId { get; set; }
    public int? ProductId { get; set; }
    public string? Sezione { get; set; }
    public string? Slot { get; set; }
    public string? CodeRaw { get; set; }
    public int Qty { get; set; }
    public bool IsAlternate { get; set; }
    public bool IsOptional { get; set; }
    public string? Note { get; set; }
    public string? RawText { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public decimal? PrezzoVb { get; set; }
}

/// <summary>Componente del listino Gamma per la vista magazzino (per-componente).</summary>
public class GammaComponentDto
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Categoria { get; set; }
    public decimal? PrezzoVb { get; set; }
    public int RobotCount { get; set; }
}

/// <summary>Dove entra un componente: utilizzo per vista magazzino (per-componente).
/// Aggregato per modello+quadro: Occorrenze = numero di configurazioni (varianti payload/area).</summary>
public class GammaUsageDto
{
    public string Modello { get; set; } = "";
    public string? Controllore { get; set; }
    public string? Generazione { get; set; }
    public string? Slot { get; set; }
    public bool IsAlternate { get; set; }
    public int Occorrenze { get; set; }
}

// ── EDITOR COMPOSIZIONE (write, solo ADMIN) ──────────────────────

/// <summary>Aggiunta di una riga di distinta. Slot vuoto su principale → assegnato server-side = codice prodotto.</summary>
public class GammaDistintaAddRequest
{
    public int QuadroId { get; set; }
    public int ProductId { get; set; }
    public string? Sezione { get; set; }
    public string? Slot { get; set; }
    public int Qty { get; set; } = 1;
    public bool IsAlternate { get; set; }
    public bool IsOptional { get; set; }
    public string? Note { get; set; }
}

/// <summary>Modifica di una riga di distinta (solo i campi valorizzati vengono aggiornati).</summary>
public class GammaDistintaUpdateRequest
{
    public int? Qty { get; set; }
    public bool? IsAlternate { get; set; }
    public bool? IsOptional { get; set; }
    public string? Note { get; set; }
}

/// <summary>Create/edit anagrafica robot (modello).</summary>
public class GammaRobotSaveRequest
{
    public string Modello { get; set; } = "";
    public string? Serie { get; set; }
    public string Brand { get; set; } = "ABB";
    public string? Note { get; set; }
}

/// <summary>Create/edit anagrafica quadro (configurazione di un robot).</summary>
public class GammaQuadroSaveRequest
{
    public string? Controllore { get; set; }
    public string? Generazione { get; set; }
    public string? Payload { get; set; }
    public string? AreaLavoro { get; set; }
    public string? OsVersion { get; set; }
    public string? SystemKey { get; set; }
    public string? Note { get; set; }
}
