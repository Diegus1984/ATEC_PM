namespace ATEC.PM.Server.Services;

/// <summary>Helper LIKE e clamp paginazione per liste API.</summary>
public static class PagedQueryHelper
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static (int page, int pageSize, int offset) Normalize(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize, (page - 1) * pageSize);
    }

    /// <summary>Converte filtro UI (*wildcard*) in pattern SQL LIKE.</summary>
    public static string? ToLikePattern(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        string f = filter.Trim();
        bool startsWild = f.StartsWith('*');
        bool endsWild = f.EndsWith('*');
        f = f.Trim('*');
        if (string.IsNullOrEmpty(f)) return null;
        if (startsWild && endsWild) return $"%{f}%";
        if (endsWild) return $"{f}%";
        if (startsWild) return $"%{f}";
        return $"%{f}%";
    }
}
