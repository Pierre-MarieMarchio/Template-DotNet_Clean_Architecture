namespace AppTemplate.Infrastructure.Core.Common.Templating;

/// <summary>One rendered mail: the subject and the body agree about their language by construction.</summary>
/// <param name="Subject">The template's <c>&lt;title&gt;</c>, HTML-decoded and trimmed.</param>
/// <param name="Body">The template's HTML, with every placeholder substituted.</param>
public sealed record RenderedEmail(string Subject, string Body);
