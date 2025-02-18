## Migrtaions

Renomer [CleanArchitectureTemplate] par le nom du projet.
Renomer [IdentityDatabaseContext] par le context a utiliser.

- Créé le fichier de migration migration (creation de la base de donnée):

```
dotnet ef migrations add InitialCreate --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --output-dir Persistence/Migrations --context [IdentityDatabaseContext]
```

- Créé un fichier de migration une migration:

```
dotnet ef migrations --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --output-dir Persistence/Migrations --context [IdentityDatabaseContext]
```

- Apliquer les migration non apliqué:

```
dotnet ef database update --project src/[CleanArchitectureTemplate].Infrastructure --startup-project src/[CleanArchitectureTemplate].API --context [IdentityDatabaseContext]
```
