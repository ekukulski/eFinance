namespace KukiFinance.Services;

public record RegisterLoadResult<T>(bool Success, string? Error, List<T>? Entries);
