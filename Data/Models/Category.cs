using System;

namespace eFinance.Data.Models
{
    public sealed class Category
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";

        public bool IsActive { get; set; } = true;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedUtc { get; set; }

        public override string ToString() => Name;
    }
}
