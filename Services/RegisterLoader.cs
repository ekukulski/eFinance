using System.IO;

namespace eFinance.Services;

public static class RegisterLoader
{
    public static RegisterLoadResult<T> TryLoad<T>(Func<List<T>> loadFunc)
    {
        try
        {
            var entries = loadFunc();
            return new(true, null, entries);
        }
        catch (FileNotFoundException ex)
        {
            return new(false, ex.Message, null);
        }
        catch (InvalidDataException ex)
        {
            return new(false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new(false, $"Unexpected error: {ex.Message}", null);
        }
    }
}
