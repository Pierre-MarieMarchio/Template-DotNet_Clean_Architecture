namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// The reason Identity gave for the last ceremony it refused. The API answers every refusal the
/// same way, so this is what tells a refusal test which check refused it.
/// </summary>
internal sealed class PasskeyCeremonyFailures
{
    private string? _last;

    public string? Last => Volatile.Read(ref _last);

    public void Record(string reason) => Volatile.Write(ref _last, reason);

    public void Clear() => Volatile.Write(ref _last, null);
}
