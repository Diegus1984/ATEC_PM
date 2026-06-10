using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = "";

    public static ApiResponse<T> Ok(T data, string msg = "") => new() { Success = true, Data = data, Message = msg };
    public static ApiResponse<T> Fail(string msg) => new() { Success = false, Message = msg };
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string UserRole { get; set; } = "";

    /// <summary>True se l'utente è entrato con la password iniziale (n.cognome): cambio obbligatorio.</summary>
    public bool MustChangePassword { get; set; }
}

/// <summary>Stato sessione: l'utente loggato è ancora attivo? (es. disattivato da admin).</summary>
public class SessionStatusDto
{
    public int EmployeeId { get; set; }
    public bool IsActive { get; set; }
}

public class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class FieldUpdateRequest
{
    public string Field { get; set; } = "";
    public string? Value { get; set; } = "";
}

public class ToggleActiveRequest
{
    public bool IsActive { get; set; }
}

/// <summary>Risultato paginato per liste API (page, pageSize, search).</summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}
