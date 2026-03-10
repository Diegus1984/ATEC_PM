using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class CashFlowData
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public decimal ProjectRevenue { get; set; }
    public DateTime? StartDate { get; set; }
    public decimal PaymentAmount { get; set; }
    public int MonthCount { get; set; } = 13;
    public bool IsInitialized { get; set; }
    public List<CashFlowCategoryDto> Categories { get; set; } = new();
    public List<CashFlowDataItemDto> DataItems { get; set; } = new();
}

public class CashFlowCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = "";
    public int SortOrder { get; set; }
}

public class CashFlowDataItemDto
{
    public string DataType { get; set; } = "";
    public int RefId { get; set; }
    public int MonthNumber { get; set; }
    public decimal NumValue { get; set; }
    public DateTime? DateValue { get; set; }
}

public class CashFlowInitRequest
{
    public decimal PaymentAmount { get; set; }
    public int MonthCount { get; set; } = 13;
}

public class CashFlowCategorySaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = "";
}

public class CashFlowDataSaveRequest
{
    public string DataType { get; set; } = "";
    public int RefId { get; set; }
    public int MonthNumber { get; set; }
    public decimal NumValue { get; set; }
    public DateTime? DateValue { get; set; }
}
