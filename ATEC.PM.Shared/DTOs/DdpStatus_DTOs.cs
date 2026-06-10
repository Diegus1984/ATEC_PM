namespace ATEC.PM.Shared.DTOs;

// Stato DDP (distinta di produzione): chiave fissa (salvata in bom_items.item_status) + etichetta e colori editabili.
public class DdpStatusItem
{
    public int Id { get; set; }
    public string StatusKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string ColorBg { get; set; } = "#CCCCCC";
    public string ColorFg { get; set; } = "#000000";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// Crea/modifica una causale (etichetta + colori + ordine + attiva). La chiave NON è modificabile dopo la creazione.
public class DdpStatusSaveRequest
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string ColorBg { get; set; } = "#CCCCCC";
    public string ColorFg { get; set; } = "#000000";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
