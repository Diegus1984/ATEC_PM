namespace ATEC.PM.Shared.DTOs;

public class CustomerListItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cell { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string SdiCode { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public bool IsActive { get; set; }

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CompanyName)) return "";
            var words = CompanyName.Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "";
            if (words.Length == 1)
            {
                string word = words[0];
                return word.Length >= 2 ? word.Substring(0, 2).ToUpper() : word.Substring(0, 1).ToUpper();
            }
            else
            {
                // Take first char of first word, and first char of second word
                string first = words[0].Substring(0, 1);
                string second = words[1].Substring(0, 1);
                return (first + second).ToUpper();
            }
        }
    }
}

public class CustomerSaveRequest
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cell { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string SdiCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
