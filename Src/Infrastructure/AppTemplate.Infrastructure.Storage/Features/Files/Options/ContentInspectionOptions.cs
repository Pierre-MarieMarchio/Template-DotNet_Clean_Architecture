using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Options;

/// <summary>
/// Whether a malware scanner is reached, and how. Bound from the <c>ContentInspection</c> section and
/// validated at start-up.
/// <para>
/// <b>A deployment that configures nothing here starts normally, and that is the shipped state.</b>
/// With no host set, the inspection still reads every deposited file and still refuses content that
/// contradicts what was declared — that check is a table of constants in the application layer and
/// needs nothing configured. What is missing is the antivirus verdict, and it is missing visibly:
/// the adapter says so once at start-up, and <c>SECURITY.md</c> says so in writing. The alternative,
/// refusing to boot without a scanner, would make the template unusable for the deployments that
/// have no daemon to point at, and would be a stronger demand than the one made of the object store
/// itself, whose credentials are also allowed to be absent.
/// </para>
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it. Everything else in this assembly is internal.
/// </para>
/// </summary>
public sealed class ContentInspectionOptions
{
    public const string SectionName = "ContentInspection";

    /// <summary>
    /// The default port <c>clamd</c> listens on.
    /// </summary>
    public const int DefaultScannerPort = 3310;

    /// <summary>
    /// The host running <c>clamd</c>, empty for a deployment with no scanner.
    /// <para>
    /// A host and a port rather than a URL, because <b>this is not HTTP</b>. <c>clamd</c> speaks its
    /// own line protocol over a raw TCP socket, so there is no scheme to state and nothing about a
    /// URL that would be honoured.
    /// </para>
    /// </summary>
    public string ScannerHost { get; set; } = string.Empty;

    public int ScannerPort { get; set; } = DefaultScannerPort;

    /// <summary>
    /// The largest object that will be offered to the scanner, which must be at or below the
    /// scanner's own <c>StreamMaxLength</c>.
    /// <para>
    /// The default is <c>clamd</c>'s own default of 25 MiB. Stating it here rather than discovering
    /// it is what turns "the scanner hung up in the middle of a 200 MiB upload" into a decision
    /// taken before a single byte was sent: an object past this size is reported as one nothing can
    /// examine, and <c>StoredFileContentPolicy</c> refuses it. Raising this without raising
    /// <c>StreamMaxLength</c> in the daemon's own configuration moves the failure back into the
    /// middle of the transfer, where it is a broken pipe rather than an answer.
    /// </para>
    /// <para>
    /// <b>The gap between this and <c>FileSize.MaxBytes</c> is a policy decision a deployment owes
    /// itself.</b> As shipped, a file over 25 MiB is refused when a scanner is configured, because
    /// unexaminable content that gets served is the hole this whole feature exists to close. A
    /// deployment that accepts large files raises both numbers together.
    /// </para>
    /// </summary>
    public long MaxScannableBytes { get; set; } = 25L * 1024 * 1024;
}

/// <summary>
/// What a content-inspection configuration is allowed to be. Each rule refuses a value whose only
/// other symptom would be discovered by a user: a port outside its range is a connection that never
/// succeeds, and a stream ceiling of zero is every file refused as unexaminable.
/// </summary>
internal sealed class ContentInspectionOptionsValidator : IValidateOptions<ContentInspectionOptions>
{
    /// <summary>
    /// A megabyte. Below this the ceiling is smaller than a photograph, so every real upload would
    /// be refused as unexaminable and the symptom would be a system that quarantines everything.
    /// </summary>
    private const long _minimumScannableBytes = 1024L * 1024;

    public ValidateOptionsResult Validate(string? name, ContentInspectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // The port is only checked when a host is set. An unconfigured deployment leaves both at
        // their defaults, and refusing a default nothing will use would fail the boot of every
        // deployment that has no scanner.
        if (!string.IsNullOrWhiteSpace(options.ScannerHost)
            && options.ScannerPort is < 1 or > 65535)
        {
            failures.Add(
                $"'{ContentInspectionOptions.SectionName}:ScannerPort' must be between 1 and 65535.");
        }

        if (options.MaxScannableBytes < _minimumScannableBytes)
        {
            failures.Add(
                $"'{ContentInspectionOptions.SectionName}:MaxScannableBytes' must be at least " +
                $"{_minimumScannableBytes} bytes. A ceiling below that refuses ordinary files as " +
                "unexaminable, which quarantines them.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
