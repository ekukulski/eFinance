namespace eFinance.Importing
{
    public sealed record ImportResult(int Inserted, int Ignored, int Failed);
}
