using System.Globalization;
using eFinance.Data.Models;
using Microsoft.Data.Sqlite;

namespace eFinance.Data.Repositories
{
    /// <summary>
    /// Master list of categories stored in SQLite.
    /// Categories are referenced by Transactions.CategoryId.
    /// </summary>
    public sealed class CategoryRepository
    {
        private readonly SqliteDatabase _db;

        public CategoryRepository(SqliteDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync(bool activeOnly = true)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = activeOnly
                ? @"SELECT Id, Name, IsActive, CreatedUtc, UpdatedUtc
                    FROM Categories
                    WHERE IsActive = 1
                    ORDER BY Name;"
                : @"SELECT Id, Name, IsActive, CreatedUtc, UpdatedUtc
                    FROM Categories
                    ORDER BY Name;";

            var list = new List<Category>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(Read(r));

            return list;
        }

        public async Task<Category?> GetByIdAsync(long id)
        {
            if (id <= 0) return null;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Name, IsActive, CreatedUtc, UpdatedUtc
                                FROM Categories
                                WHERE Id = $id
                                LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            return Read(r);
        }

        public async Task<Category?> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Name, IsActive, CreatedUtc, UpdatedUtc
                                FROM Categories
                                WHERE LOWER(TRIM(Name)) = LOWER(TRIM($name))
                                LIMIT 1;";
            cmd.Parameters.AddWithValue("$name", name);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            return Read(r);
        }

        public async Task<long> InsertAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Category name is required.", nameof(name));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Categories (Name, IsActive, CreatedUtc)
VALUES ($name, 1, $utc);
SELECT last_insert_rowid();
";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }

        public async Task UpdateAsync(Category c)
        {
            if (c is null) throw new ArgumentNullException(nameof(c));
            if (c.Id <= 0) throw new ArgumentOutOfRangeException(nameof(c.Id));
            if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Name is required.", nameof(c));

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Categories
SET Name = $name,
    IsActive = $active,
    UpdatedUtc = $utc
WHERE Id = $id;
";
            cmd.Parameters.AddWithValue("$id", c.Id);
            cmd.Parameters.AddWithValue("$name", c.Name.Trim());
            cmd.Parameters.AddWithValue("$active", c.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Returns true if any transactions reference this category id.
        /// </summary>
        public async Task<bool> IsInUseAsync(long categoryId)
        {
            if (categoryId <= 0) return false;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT EXISTS(
    SELECT 1
    FROM Transactions
    WHERE CategoryId = $id
    LIMIT 1
);";
            cmd.Parameters.AddWithValue("$id", categoryId);

            var result = await cmd.ExecuteScalarAsync();

            // SQLite EXISTS returns 0/1 (as an integer).
            if (result is null || result is DBNull) return false;
            return Convert.ToInt64(result) == 1;
        }

        /// <summary>
        /// Soft-delete (archive) a category so it no longer appears in active lists,
        /// while preserving historical references from transactions.
        /// </summary>
        public async Task SoftDeleteAsync(long categoryId)
        {
            if (categoryId <= 0) return;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Categories
SET IsActive = 0,
    UpdatedUtc = $utc
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", categoryId);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(long id)
        {
            if (id <= 0) return;

            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Categories WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        private static Category Read(SqliteDataReader r)
        {
            // CreatedUtc/UpdatedUtc are stored as TEXT ISO strings.
            var created = r.IsDBNull(3)
                ? DateTime.UtcNow
                : DateTime.Parse(r.GetString(3), null, DateTimeStyles.RoundtripKind);

            DateTime? updated = null;
            if (!r.IsDBNull(4))
                updated = DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind);

            return new Category
            {
                Id = r.GetInt64(0),
                Name = r.IsDBNull(1) ? "" : r.GetString(1),
                IsActive = !r.IsDBNull(2) && r.GetInt32(2) == 1,
                CreatedUtc = created,
                UpdatedUtc = updated
            };
        }
    }
}
