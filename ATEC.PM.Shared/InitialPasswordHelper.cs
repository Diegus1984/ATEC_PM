using System;

namespace ATEC.PM.Shared;

/// <summary>Password iniziale e username standard: nome.cognome (es. Edoardo Carretta → edoardo.carretta).</summary>
public static class InitialPasswordHelper
{
    public const int MinPasswordLength = 4;

    public static string Build(string firstName, string lastName)
    {
        string trimmedFirst = firstName.Trim().ToLowerInvariant();
        string trimmedLast = lastName.Trim().ToLowerInvariant();
        if (trimmedFirst.Length == 0 || trimmedLast.Length == 0)
            return string.Empty;

        return $"{trimmedFirst}.{trimmedLast}";
    }

    public static bool IsInitialPassword(string password, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        string expected = Build(firstName, lastName);
        if (string.IsNullOrEmpty(expected))
            return false;

        if (string.Equals(password.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // Retrocompatibilità per il vecchio formato n.cognome
        string trimmedFirst = firstName.Trim();
        string trimmedLast = lastName.Trim().ToLowerInvariant();
        if (trimmedFirst.Length > 0 && trimmedLast.Length > 0)
        {
            string legacy = $"{char.ToLowerInvariant(trimmedFirst[0])}.{trimmedLast}";
            if (string.Equals(password.Trim(), legacy, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
