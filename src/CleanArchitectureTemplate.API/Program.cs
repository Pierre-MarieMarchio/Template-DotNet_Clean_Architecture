using CleanArchitectureTemplate.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DotNetEnv;
using System.Reflection;


var builder = WebApplication.CreateBuilder(args);
Env.Load();

var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
var loadedPaths = loadedAssemblies.Select(a => a.Location).ToArray();
var referencedPaths = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll");

foreach (var path in referencedPaths)
{
    if (!loadedPaths.Contains(path))
    {
        try
        {
            loadedAssemblies.Add(Assembly.LoadFrom(path));
        }
        catch
        {
            // Ignorer les erreurs de chargement d'assemblée
        }
    }
}


var assembliesToScan = loadedAssemblies
    .Where(a => !a.IsDynamic && a.FullName.StartsWith("CleanArchitectureTemplate"))
    .ToArray();


// Add services to the container.
builder.Services.AddControllers();


builder.Services.Scan(scan => scan
    .FromAssemblies(assembliesToScan)
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Service")))
    .AsImplementedInterfaces()
    .WithTransientLifetime()
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Validator")))
    .AsImplementedInterfaces()
    .WithTransientLifetime()
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("UseCase")))
    .AsImplementedInterfaces()
    .WithTransientLifetime()
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Repository")))
    .AsImplementedInterfaces()
    .WithTransientLifetime());


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();
builder.Services.AddAuthentication().AddCookie(IdentityConstants.ApplicationScheme);

builder.Services.AddIdentityCore<IdentityUser<Guid>>(options =>
{

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<IdentityDatabaseContext>()
.AddDefaultTokenProviders();


var connectionString = $"Server={Environment.GetEnvironmentVariable("DATABASE_SERVER")};Database={Environment.GetEnvironmentVariable("DATABASE_NAME")};User Id={Environment.GetEnvironmentVariable("DATABASE_USER")};Password={Environment.GetEnvironmentVariable("DATABASE_PASSWORD")};TrustServerCertificate=True;";
builder.Services.AddDbContext<IdentityDatabaseContext>(opt => opt.UseSqlServer(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
