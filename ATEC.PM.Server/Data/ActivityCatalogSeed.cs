namespace ATEC.PM.Server.Data;

/// <summary>
/// Elenco standard delle voci-attività di progetto (master Excel ATEC), usato per il seed
/// iniziale dell'anagrafica attività (activity_catalog) e per il «Ripristina standard».
/// Portato dal prototipo Gestione_Commesse_V31.html (costante DEFAULT_MILESTONES).
/// </summary>
public static class ActivityCatalogSeed
{
    public static readonly string[] Labels =
    [
        "Invio Conferma Ordine",
        "Apertura Commessa",
        "Kick Off - Interno",
        "Kick Off - Cliente",
        "Redazione Specifiche Tecniche",
        "Redazione Analisi Funzionale",
        "Simulazione Robot Studio",
        "Layout impianto",
        "Approvazione Preliminare Cliente",
        "Back office SW Robot",
        "Back office SW PLC",
        "Progettazione meccanica",
        "Progettazione elettrica",
        "Inserimento Commerciali DDP",
        "Inserimento lavorazioni 101_DDP",
        "Acquisti / Lead Time commerciali",
        "Acquisti / Lead Time Materie Lav. Interne",
        "Lead Time Lavorazioni Interne",
        "Acquisti / Lead Time Lav. Esterne",
        "Preallestimento meccanico interno",
        "Preallestimento elettrico interno",
        "Debug interno",
        "FAT – Factory Acceptance Test",
        "Preparazione Fornitura",
        "Spedizione fornitura",
        "Installazione elettromeccanica – Cliente",
        "Commissioning Cliente",
        "SOP Cliente",
        "SAT – Site Acceptance Test",
        "Assistenza e Formazione Cliente",
        "Consegna documentazione as-built",
        "Chiusura commessa e lesson learned",
    ];
}
