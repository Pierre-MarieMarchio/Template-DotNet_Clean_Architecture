using CleanArchitectureTemplate.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DotNetEnv;
using System.Reflection;


var builder = WebApplication.CreateBuilder(args);
Env.Load();

var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
var loadedNames = loadedAssemblies.Select(a => a.GetName().Name).ToArray();
var referencedPaths = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll");

foreach (var path in referencedPaths)
{
    try
    {
        var assemblyName = AssemblyName.GetAssemblyName(path);
        if (!loadedNames.Contains(assemblyName.Name))
        {
            loadedAssemblies.Add(Assembly.Load(assemblyName));
        }
    }
    catch
    {
        //...
    }
}

var assembliesToScan = loadedAssemblies
    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName) && a.FullName.StartsWith("CleanArchitectureTemplate"))
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
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Manager")))
    .AsImplementedInterfaces()
    .WithTransientLifetime()
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Repository")))
    .AsImplementedInterfaces()
    .WithTransientLifetime());



builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddAuthorization();
builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<IdentityDatabaseContext>()
.AddDefaultTokenProviders();

var connectionString = $"Server={Environment.GetEnvironmentVariable("DATABASE_SERVER")};Database={Environment.GetEnvironmentVariable("DATABASE_NAME")};User Id={Environment.GetEnvironmentVariable("DATABASE_USER")};Password={Environment.GetEnvironmentVariable("DATABASE_PASSWORD")};TrustServerCertificate=True;";
builder.Services.AddDbContext<IdentityDatabaseContext>(opt => opt.UseSqlServer(connectionString));

var app = builder.Build();


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
