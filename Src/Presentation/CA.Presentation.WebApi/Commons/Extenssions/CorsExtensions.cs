using System;

namespace CA.Presentation.WebApi.Commons.Extenssions;

public static class CorsExtensions
{
    public static IServiceCollection AddCorsExtenssions(this IServiceCollection services)
    {
        return services.AddCors(opt =>
        {
            opt.AddPolicy("development", builder =>
            {
                builder.WithOrigins("http://localhost:4200")
                .AllowAnyHeader()
                .AllowAnyMethod();
            });
        });
    }

    public static IApplicationBuilder UseDevelopementCors(this IApplicationBuilder app)
    {
        return app.UseCors("development");
    }

}
