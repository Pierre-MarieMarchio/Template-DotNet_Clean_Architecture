# Choix du contexte

Dans l'application, deux contextes Entity Framework Core sont définis :

**DatabaseContext :** Utilisé pour les opérations générales sur la base de données sans Identity .

**IdentityDatabaseContext :** Utilisé pour les opérations générales sur la base de données et pour la gestion des utilisateurs et des rôles avec Identity.

## Utilisation des contextes

Le choix du contexte à inclure dans Program.cs dépend de l'usage souhaité :

ajoutez l'un des contextes dans Program.cs :

```
builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
```

Ou pour Identity :

```
dDbContext<IdentityDatabaseContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
```
