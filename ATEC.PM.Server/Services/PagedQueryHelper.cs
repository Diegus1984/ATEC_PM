using System;
using System.Collections.Generic;

namespace ATEC.PM.Server.Services;

/// <summary>Helper LIKE e clamp paginazione per liste API.</summary>
public static class PagedQueryHelper
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Costruisce un ORDER BY sicuro: la colonna SQL viene presa SOLO dalla
    /// whitelist (mai dall'input utente), la direzione è limitata a ASC/DESC.
    /// </summary>
    public static string OrderBy(string? sortBy, string? sortDir,
        IReadOnlyDictionary<string, string> allowed, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(sortBy) && allowed.TryGetValue(sortBy, out string? col))
        {
            string dir = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase)
                ? "DESC" : "ASC";
            return $"ORDER BY {col} {dir}";
        }
        return fallback;
    }

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
