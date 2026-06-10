namespace ATEC.PM.Shared.DTOs;

// Aggregazione di stato DDP (colonna della matrice Stati × Aggregazioni). Editabile da "Aggregazioni DDP".
public class DdpAggregation
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "SET";   // SET | ALL | DATED | SUBGROUPS
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Stati che compongono l'aggregazione (le celle ✓ della matrice).
    public List<string> StatusKeys { get; set; } = new();
}

// Salvataggio di un'aggregazione: nome/descrizione + appartenenze (overwrite completo). Code/Kind non modificabili.
public class DdpAggregationSaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> StatusKeys { get; set; } = new();
}
