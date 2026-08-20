using System.ComponentModel.DataAnnotations;

namespace ATEC.PM.Shared.DTOs;

public class DepartmentDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public decimal DefaultMarkup { get; set; } = 1.450m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// I limiti qui sotto ricalcano le colonne vere di <c>departments</c>: senza, un valore fuori
/// misura arriva a MySQL e torna come «Data too long for column» oppure «Out of range» — cioè,
/// per chi sta salvando, un «Errore interno del server» che non dice cosa correggere.
/// <para><b>Nessun <c>[Required]</c></b>: codice e nome li controlla già
/// <c>DepartmentsController</c> con messaggi propri («Codice obbligatorio»), migliori di quelli
/// che genererebbe l'annotazione.</para>
/// </summary>
public class DepartmentSaveRequest
{
    public int Id { get; set; }

    [MaxLength(10, ErrorMessage = "Il codice non può superare 10 caratteri")]
    public string Code { get; set; } = "";

    [MaxLength(100, ErrorMessage = "Il nome non può superare 100 caratteri")]
    public string Name { get; set; } = "";

    // DECIMAL(8,2) → oltre 999.999,99 il database rifiuta; sotto zero non ha senso.
    [Range(0, 999999.99, ErrorMessage = "Il costo orario deve essere fra 0 e 999.999,99")]
    public decimal HourlyCost { get; set; }

    // DECIMAL(5,3) → il ricarico sta fra 0 e 99,999.
    [Range(0, 99.999, ErrorMessage = "Il ricarico deve essere fra 0 e 99,999")]
    public decimal DefaultMarkup { get; set; } = 1.450m;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
