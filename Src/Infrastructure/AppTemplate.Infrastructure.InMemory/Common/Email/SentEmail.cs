namespace AppTemplate.Infrastructure.InMemory.Common.Email;

public sealed record SentEmail(string Recipient, string Subject, string HtmlBody, DateTimeOffset SentAt);
