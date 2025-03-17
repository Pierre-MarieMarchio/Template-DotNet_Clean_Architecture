
using CA.Application;
using CA.Infrastructure;
using CA.Infrastructure.Commons.Contexts;
using CA.Infrastructure.Identity;
using CA.Infrastructure.Identity.Entity;
using CA.Infrastructure.Identity.Persistence.Contexts;
using CA.Infrastructure.Identity.Persistence.Seeds;
using CA.Presentation.WebApi.Commons.Extenssions;
using CA.Presentation.WebApi.Commons.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

bool useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");


// Add services to the container.

builder.Services.AddApplicationLayer();
builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddPresentationWebApiLayer();
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerExtensions();
builder.Logging.AddConsole();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    if (!useInMemoryDatabase)
    {
        await services.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    await DefaultRole.SeedAsync(services.GetRequiredService<RoleManager<AppRole>>());
    await DefaultUser.SeedAsync(services.GetRequiredService<UserManager<AppUser>>());

}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwaggerExtensions();
app.MapControllers();

app.Run();
