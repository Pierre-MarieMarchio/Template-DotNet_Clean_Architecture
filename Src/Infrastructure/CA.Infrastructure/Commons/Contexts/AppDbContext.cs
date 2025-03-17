using CA.Application.Commons.Interfaces.Services;
using CA.Domain.Modules.TodoList.Entity;
using CA.Infrastructure.Commons.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CA.Infrastructure.Commons.Contexts;

public class AppDbContext(DbContextOptions<AppDbContext> options, IAuthenticatedUserService authenticatedUser) : DbContext(options)
{
    public DbSet<TodoItem> TodoItems { get; set; }
    public DbSet<TodoTag> TodoTags { get; set; }
    public DbSet<ListTodos> TodoLists { get; set; }


    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        ChangeTracker.ApplyAuditing(authenticatedUser);
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TodoItem>()
            .HasOne(t => t.TodoList)
            .WithMany(l => l.TodoItems)
            .HasForeignKey(t => t.TodoListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TodoItem>()
            .HasMany(t => t.TodoTags)
            .WithMany(t => t.TodoItems);
    }
}
