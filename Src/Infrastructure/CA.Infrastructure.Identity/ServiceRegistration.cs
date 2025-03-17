using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Infrastructure.Identity.Entity;
using CA.Infrastructure.Identity.Interfaces.Repositories;
using CA.Infrastructure.Identity.Interfaces.Services;
using CA.Infrastructure.Identity.Persistence.Contexts;
using CA.Infrastructure.Identity.Repositories;
using CA.Infrastructure.Identity.Services;
using CA.Infrastructure.Identity.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace CA.Infrastructure.Identity;

public static class ServiceRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryDatabase)
    {

        if (useInMemoryDatabase)
        {
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase(nameof(AppIdentityDbContext)));
        }
        else
        {
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthtService>();
        services.AddScoped<IAppUserService, AppUserService>();
        services.AddScoped<ITokenAccessService, TokenAccessService>();
        services.AddScoped<ITokenRefreshService, TokenRefreshService>();
        services.AddScoped<IRegisterMailerService, RegisterMailerService>();

        var identitySettings = configuration.GetSection(nameof(IdentitySettings)).Get<IdentitySettings>() ?? throw new InvalidOperationException("IdentitySettings configuration is missing.");

        var jwtSettings = configuration.GetSection(nameof(JwtSettings)).Get<JwtSettings>() ?? throw new InvalidOperationException("JwtSettings configuration is missing.");
        services.AddSingleton(jwtSettings);

        services.AddIdentity<AppUser, AppRole>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = true;
            options.User.RequireUniqueEmail = true;

            options.Password.RequireDigit = identitySettings.PasswordRequireDigit;
            options.Password.RequiredLength = identitySettings.PasswordRequiredLength;
            options.Password.RequireNonAlphanumeric = identitySettings.PasswordRequireNonAlphanumeric;
            options.Password.RequireUppercase = identitySettings.PasswordRequireUppercase;
            options.Password.RequireLowercase = identitySettings.PasswordRequireLowercase;

        }).AddEntityFrameworkStores<AppIdentityDbContext>().AddDefaultTokenProviders();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
            .AddJwtBearer(opt =>
            {
                opt.RequireHttpsMetadata = jwtSettings.RequireHttpsMetadata;
                opt.SaveToken = false;
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
                };
                opt.Events = new JwtBearerEvents()
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new { message = "You are not Authorized" });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new { message = "You are not authorized to access this resource" });
                    },
                    OnTokenValidated = async context =>
                    {
                        var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
                        if (claimsIdentity?.Claims.Any() is not true)
                            context.Fail("This token has no claims.");

                        var securityStamp = claimsIdentity?.FindFirst("AspNet.Identity.SecurityStamp");
                        if (securityStamp is null)
                            context.Fail("This token has no security stamp");

                        var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
                        var validatedUser = await signInManager.ValidateSecurityStampAsync(context.Principal);
                        if (validatedUser is null)
                            context.Fail("Token security stamp is not valid.");
                    },

                };
            });

        return services;
    }

}
