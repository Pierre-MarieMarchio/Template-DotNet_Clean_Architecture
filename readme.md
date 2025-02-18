## Migrtaions

renomer [CleanArchitectureTemplate] par le nom du projet

- Créé la premiere migration:

```
dotnet ef migrations add InitialCreate --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --output-dir Persistence/Migrations --context IdentityDatabaseContext
```

- Faire une migration:

```
dotnet ef migrations --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --output-dir Persistence/Migrations --context IdentityDatabaseContext
```

- Apliquer migration:

```
dotnet ef database update --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --context IdentityDatabaseContext
```
