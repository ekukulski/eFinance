namespace eFinance.Services;

public record RegisterLoadResult<T>(bool Success, string? Error, List<T>? Entries);
