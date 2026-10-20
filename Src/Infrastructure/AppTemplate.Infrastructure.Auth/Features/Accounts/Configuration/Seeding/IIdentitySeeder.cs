namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;

public interface IIdentitySeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
