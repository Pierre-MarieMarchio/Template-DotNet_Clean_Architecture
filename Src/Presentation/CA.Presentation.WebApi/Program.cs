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

builder.Services.AddApplicationLayer();
builder.Services.AddIdentityInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddPersistenceInfrastructure(builder.Configuration, useInMemoryDatabase);
builder.Services.AddPresentationWebApiLayer();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerExtensions();
builder.Services.AddCorsExtenssions();
builder.Logging.AddConsole();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    if (!useInMemoryDatabase)
    {
        var identityDb = services.GetRequiredService<AppIdentityDbContext>();
        var appDb = services.GetRequiredService<AppDbContext>();

        await DatabaseHelper.EnsureDatabaseReadyAsync(identityDb);
        await DatabaseHelper.EnsureDatabaseReadyAsync(appDb);

        if ((await identityDb.Database.GetPendingMigrationsAsync()).Any())

            await identityDb.Database.MigrateAsync();

        if ((await appDb.Database.GetPendingMigrationsAsync()).Any())
            await appDb.Database.MigrateAsync();
    }

    var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();

    await DefaultRole.SeedAsync(roleManager);
    await DefaultUser.SeedAsync(userManager);
}

app.UseDevelopementCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwaggerExtensions();
app.MapControllers();


await app.RunAsync();
