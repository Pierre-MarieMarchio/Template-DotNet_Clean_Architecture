namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Hands one already-rendered mail to whatever delivers it. Composition and rendering happen before
/// this port; queuing, retries and bounces after it, and neither is this layer's concern.
/// </summary>
public interface IEmailSender
{
    /// <param name="recipient">Not named <c>to</c>: that is a reserved word in some CLS languages (CA1716).</param>
    /// <param name="htmlBody">Already-rendered HTML. Callers must encode any user-supplied value.</param>
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
