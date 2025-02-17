using System;

using CleanArchitectureTemplate.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureTemplate.Infrastructure.Persistence.Context;

public class IdentityDatabaseContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public IdentityDatabaseContext(DbContextOptions<IdentityDatabaseContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);


    }


}
