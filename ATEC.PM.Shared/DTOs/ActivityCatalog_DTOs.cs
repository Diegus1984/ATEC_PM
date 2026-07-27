namespace ATEC.PM.Shared.DTOs;

// Voce del catalogo "Anagrafica attività": elenco globale delle attività standard di progetto,
// da cui si precaricano le milestone alla creazione di una commessa. Id stabile (l'ordine è
// gestito da sort_order); il precarico copia solo la descrizione (nessun legame residuo).
public class ActivityCatalogItem
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// Crea/modifica una voce di catalogo (etichetta + ordine + attiva).
public class ActivityCatalogSaveRequest
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// Riordino: elenco ordinato di id (la posizione nell'array diventa il nuovo sort_order 1..N).
public class ActivityCatalogReorderRequest
{
    public List<int> Ids { get; set; } = new();
}
