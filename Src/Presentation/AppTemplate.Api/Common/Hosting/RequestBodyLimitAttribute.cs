using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// Raises one action's request body limit above <c>RequestLimits:MaxRequestBodyBytes</c>, to a value
/// a subclass reads from configuration. <see cref="RequestLimitsExtensions.UseApiRequestLimits"/>
/// applies it to the declared length, the server's own limit and a multipart body; this filter
/// reads the form before model binding, so a body that overruns while being read answers 413 too.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public abstract class RequestBodyLimitAttribute : Attribute, IAsyncResourceFilter
{
    /// <summary>The largest body the action accepts, in bytes.</summary>
    /// <param name="services">The request's services, to read the configured value from.</param>
    public abstract long MaxRequestBodyBytes(IServiceProvider services);

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (httpContext.Request.HasFormContentType)
        {
            try
            {
                await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                await RequestLimitsExtensions.WriteTooLargeAsync(httpContext);
                context.Result = new EmptyResult();

                return;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                // A malformed form: model binding meets the same cached failure and answers 400.
            }
        }

        await next();
    }
}
