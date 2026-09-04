namespace AppTemplate.Infrastructure.InMemory.Email;

/// <summary>One delivered message, exactly as the port received it.</summary>
/// <param name="Recipient">The destination address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="HtmlBody">The rendered body, so a test can assert on a confirmation link
/// instead of on the fact that "an email was sent".</param>
/// <param name="SentAt">The instant the send happened, taken from the injected clock.</param>
public sealed record SentEmail(string Recipient, string Subject, string HtmlBody, DateTimeOffset SentAt);
