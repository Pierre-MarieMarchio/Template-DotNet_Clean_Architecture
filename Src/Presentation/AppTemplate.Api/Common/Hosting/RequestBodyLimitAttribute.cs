using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppTemplate.Api.Common.Hosting;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public abstract class RequestBodyLimitAttribute : Attribute, IAsyncResourceFilter
{
    public abstract long MaxRequestBodyBytes(IServiceProvider services);

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
            }
        }

        await next();
    }
}
