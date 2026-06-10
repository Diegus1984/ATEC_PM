using System;

namespace ATEC.PM.Shared;

/// <summary>Password iniziale standard: iniziale.cognome (es. Edoardo Carretta → e.carretta).</summary>
public static class InitialPasswordHelper
{
    public const int MinPasswordLength = 4;

    public static string Build(string firstName, string lastName)
    {
        string trimmedFirst = firstName.Trim();
        string trimmedLast = lastName.Trim().ToLowerInvariant();
        if (trimmedFirst.Length == 0 || trimmedLast.Length == 0)
            return string.Empty;

        char initial = char.ToLowerInvariant(trimmedFirst[0]);
        return $"{initial}.{trimmedLast}";
    }

    public static bool IsInitialPassword(string password, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        string expected = Build(firstName, lastName);
        if (string.IsNullOrEmpty(expected))
            return false;

        return string.Equals(password.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
