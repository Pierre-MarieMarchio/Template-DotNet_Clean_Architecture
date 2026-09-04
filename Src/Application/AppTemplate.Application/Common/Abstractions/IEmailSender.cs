namespace AppTemplate.Application.Common.Abstractions;

public interface IEmailSender
{
    /// <param name="recipient">Not named <c>to</c>: that is a reserved word in some CLS languages (CA1716).</param>
    /// <param name="htmlBody">Already-rendered HTML. Callers must encode any user-supplied value.</param>
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
