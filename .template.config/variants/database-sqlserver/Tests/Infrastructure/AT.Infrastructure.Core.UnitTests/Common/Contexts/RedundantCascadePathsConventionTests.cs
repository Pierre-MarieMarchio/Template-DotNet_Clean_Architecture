using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Contexts;

public sealed class RedundantCascadePathsConventionTests
{
    [Fact]
    public void ACascadeTheRowsAlreadyFollowThroughALongerPath_IsDropped()
    {
        using var context = new ProbeContext();

        DeleteBehaviorOf<Token, User>(context).ShouldBe(DeleteBehavior.NoAction);
    }

    [Fact]
    public void TheCascadesOfTheLongerPath_AreKept()
    {
        using var context = new ProbeContext();

        DeleteBehaviorOf<Token, Family>(context).ShouldBe(DeleteBehavior.Cascade);
        DeleteBehaviorOf<Family, User>(context).ShouldBe(DeleteBehavior.Cascade);
    }

    [Fact]
    public void ACascadeWithoutAnotherPath_IsKept()
    {
        using var context = new ProbeContext();

        DeleteBehaviorOf<Order, Customer>(context).ShouldBe(DeleteBehavior.Cascade);
    }

    private static DeleteBehavior DeleteBehaviorOf<TDependent, TPrincipal>(DbContext context) =>
        context.Model.FindEntityType(typeof(TDependent))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal))
            .DeleteBehavior;

    private sealed class User
    {
        public Guid Id { get; set; }
    }

    private sealed class Family
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
    }

    private sealed class Token
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid FamilyId { get; set; }
    }

    private sealed class Customer
    {
        public Guid Id { get; set; }
    }

    private sealed class Order
    {
        public Guid Id { get; set; }

        public Guid CustomerId { get; set; }
    }

    private sealed class ProbeContext() : DbContext(
        new DbContextOptionsBuilder<ProbeContext>().UseDatabase(DatabaseProvider.DesignTimeConnectionString).Options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyDatabaseConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Family>().HasOne<User>().WithMany().HasForeignKey(family => family.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Token>().HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Token>().HasOne<Family>().WithMany().HasForeignKey(token => token.FamilyId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Order>().HasOne<Customer>().WithMany().HasForeignKey(order => order.CustomerId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
