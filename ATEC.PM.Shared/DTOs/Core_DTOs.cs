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
