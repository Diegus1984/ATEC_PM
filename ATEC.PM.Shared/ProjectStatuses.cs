namespace ATEC.PM.Shared;

/// <summary>
/// Stati di una commessa e definizione unica di «chiusa».
/// Chiusa = lavoro finito o annullato: sparisce dalle liste per default e i moduli
/// operativi (es. il foglio SAL) diventano di sola lettura. ON_HOLD NON è chiusa:
/// è sospesa, il lavoro riprenderà.
/// </summary>
public static class ProjectStatuses
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string OnHold = "ON_HOLD";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";

    public static readonly string[] Closed = { Completed, Cancelled };

    public static bool IsClosed(string? status) =>
        Closed.Contains((status ?? "").Trim().ToUpperInvariant());
}
