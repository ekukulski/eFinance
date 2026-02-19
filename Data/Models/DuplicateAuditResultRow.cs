namespace eFinance.Data.Models;

public sealed class DuplicateAuditResultRow
{
    public string AccountName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";

    public long A_Id { get; set; }
    public string A_Date { get; set; } = "";
    public string A_Description { get; set; } = "";
    public string A_Amount { get; set; } = "";

    public long B_Id { get; set; }
    public string B_Date { get; set; } = "";
    public string B_Description { get; set; } = "";
    public string B_Amount { get; set; } = "";
}
