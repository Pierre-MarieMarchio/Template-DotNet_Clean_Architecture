namespace AppTemplate.Api.IntegrationTests.Auth;

internal sealed class PasskeyCeremonyFailures
{
    private string? _last;

    public string? Last => Volatile.Read(ref _last);

    public void Record(string reason) => Volatile.Write(ref _last, reason);

    public void Clear() => Volatile.Write(ref _last, null);
}
