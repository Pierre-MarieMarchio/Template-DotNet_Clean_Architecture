using CA.Application.Commons.Interfaces.Repositories;
using CA.Infrastructure.Commons.Bases;
using CA.Infrastructure.Commons.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace CA.Infrastructure;

public static class ServiceRegister
{
    public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryDatabase)
    {
        if (useInMemoryDatabase)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(nameof(AppDbContext)));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        }

        services.RegisterRepositories();

        return services;
    }
    private static void RegisterRepositories(this IServiceCollection services)
    {
        var interfaceType = typeof(IBaseRepository<>);
        var interfaces = Assembly.GetAssembly(interfaceType)!.GetTypes()
            .Where(p => p.GetInterface(interfaceType.Name) != null);

        var implementations = Assembly.GetAssembly(typeof(BaseRepository<>))!.GetTypes();

        foreach (var item in interfaces)
        {
            var implementation = implementations.FirstOrDefault(p => p.GetInterface(item.Name) != null);

            if (implementation is not null)
                services.AddTransient(item, implementation);
        }
    }

}
