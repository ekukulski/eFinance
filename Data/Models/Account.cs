using System;

namespace eFinance.Data.Models
{
    public sealed class Account
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string AccountType { get; set; } = ""; // "CreditCard", "Checking", etc.
        public bool IsActive { get; set; } = true;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
