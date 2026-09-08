using Microsoft.AspNetCore.Http;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// The content-security policy one response answers under, when it is not the configured one.
/// </summary>
/// <param name="context">The request whose response is about to start.</param>
/// <returns>
/// The policy to send, or <see langword="null"/> to send the configured
/// <see cref="SecurityHeaderOptions.ContentSecurityPolicy"/>.
/// </returns>
/// <remarks>
/// A host supplies one when a path it serves needs something the API policy refuses — an
/// API-reference page and its assets being the case this template ships. It is invoked as the
/// response starts, which is what lets it read a value an endpoint wrote while running.
/// </remarks>
public delegate string? ResponseContentSecurityPolicy(HttpContext context);
