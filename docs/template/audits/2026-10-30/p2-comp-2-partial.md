# Persona p2-comp-2 : developpeur .NET, premier usage de Clean.Architecture.Solution.Template 10.8.0

STATUT : PARCOURS INCOMPLET. Le premier `dotnet build` du projet genere a ete refuse par le classifieur de permissions de l'environnement
("Code from External"). Aucun build, test ni lancement n'a donc ete execute. Rien n'a ete contourne. Une decision de l'utilisateur est necessaire.

## Journal horodate
- 12:24:14 Prerequis locaux (hors template) : SDK 10.0.301, Docker 29.6.2, Node 24.18.0, npm 11.16.0.
- 12:24:1x Page nuget.org (WebFetch) : prerequis ".NET 10 SDK" et "Node.js LTS (Angular/React seulement)". Pas de mention de Docker sur nuget.org.
  Install : `dotnet new install Clean.Architecture.Solution.Template@10.8.0`. Lancement : `cd src/AppHost; dotnet run`. Options : -cf Angular|React|None, -db postgresql|sqlite|sqlserver.
- 12:24:27 `dotnet new install Clean.Architecture.Solution.Template` : succes en ~2 s. Sortie polluee par des dizaines de lignes
  "Pack Microsoft.NETCore.App.Runtime.Mono.android-arm ... already installed. Writing workload pack installation record..." (workload MAUI de ma machine : limite d'environnement, pas du template).
  Templates : ca-sln (Clean Architecture Solution), ca-usecase.
- 12:24:33 `dotnet new list ca-` : 2 templates listes, tags clairs.
- 12:24:34 `dotnet new ca-sln --help` : seules options propres : -cf/--client-framework (Angular defaut, React, None) et -db/--database (sqlite defaut, postgresql, sqlserver).
  Pas d'option Aspire (toujours present), pas d'option authentification, pas d'option tests. `ca-usecase --help` : -fn, -ut, -rt, -pn.
- 12:24:42 `dotnet new ca-sln -n Acme.Def -o def` : 2 s. Sortie : "The template "Clean Architecture Solution" was created successfully." Aucun message de suite (ni README a lire, ni prerequis).
- 12:24:44 `ca-sln -n Acme.min -cf None -db sqlite -o min` : 1 s. 
- 12:24:45 `ca-sln -n Acme.rich -cf React -db postgresql -o rich` : 1 s. (Configuration "la plus riche" : React + PostgreSQL ; il n'existe que ces deux axes.)
- 12:24:5x Lecture du README genere (def) : "Run `dotnet build`", "dotnet run --project .\src\AppHost", "dotnet test", ca-usecase depuis `.\src\Application\`.
- 12:25:0x Tentative `dotnet build` dans def : REFUSEE par l'environnement (message : "Permission for this action was denied by the Claude Code auto mode classifier. Reason: [Code from External]").
  Non contourne. Arret des etapes d'execution.
- 12:25:09 Inspection en lecture seule des trois projets generes (voir constats).

## Tableau des etapes
| Etape | Duree | Resultat | Etapes manuelles | Prerequis decouverts |
|---|---|---|---|---|
| Lire nuget.org | ~1 min | ok | 1 | SDK .NET 10 ; Node si front |
| dotnet new install | 2 s | ok | 1 | reseau (nuget.org) |
| dotnet new list / --help | 1 s | ok | 2 | - |
| Generation def (Angular+sqlite) | 2 s | ok | 1 | - |
| Generation min (None+sqlite) | 1 s | ok | 1 | - |
| Generation rich (React+postgresql) | 1 s | ok | 1 | - |
| Build def | - | NON EXECUTE (refus de l'environnement) | - | - |
| Tests def / appli qui repond | - | NON EXECUTE | - | - |
| Build + tests min | - | NON EXECUTE | - | - |
| Build, tests, appli rich | - | NON EXECUTE | - | - |

## Constats (verifies par lecture des fichiers generes ou des pages)
1. [friction, important] README genere ecrit pour Windows : `dotnet run --project .\src\AppHost` et `.\src\Application\` (def/README.md). Sous Linux/macOS les antislashs sont un piege potentiel.
2. [manque, important] Le README genere ne dit rien des prerequis : ni Docker (necessaire des qu'on choisit postgresql/sqlserver ; le README GitHub l'ecrit mais pas le README genere ni nuget.org), ni Node pour le front, ni le CLI Aspire. Le README GitHub recommande `aspire run`, le README genere et nuget.org disent `dotnet run`.
3. [manque, important] Aucune indication de l'endroit ou ecrire son premier code metier : ni dans le README genere, ni sur nuget.org, ni dans le README GitHub (qui renvoie a docs/decisions et au site externe). Seule la structure (src/Application/TodoLists, TodoItems, WeatherForecasts ; src/Domain/Entities) sert d'exemple ; `ca-usecase` est la seule aide, et exige de connaitre --feature-name.
4. [friction, mineur] `dotnet new ca-sln` ne dit rien apres creation (pas de "prochaine etape"). Pas de git init ; .gitignore present.
5. [friction, mineur] Incoherence de casse : README GitHub parle de `-cf angular|react|none` ; --help liste `Angular|React|None`. Le nom de projet `Acme.min` donne des namespaces `Acme.min.Shared` (casse conservee) : normal, mais sans avertissement.
6. [fait] global.json impose le SDK 10.0.201 avec rollForward latestFeature : mon 10.0.301 est accepte (global.json de def).
7. [fait] Les trois projets partagent la meme arborescence : src/{AppHost,Application,Domain,Infrastructure,ServiceDefaults,Shared,Web}, tests/{Application.FunctionalTests,UnitTests,Domain.UnitTests,Infrastructure.IntegrationTests,TestAppHost}. `-cf None` supprime ClientApp et tests/Web.AcceptanceTests (min/tests n'en a pas, rich/tests en a). La variante minimale n'est donc "legere" que par l'absence de Node/Playwright ; Aspire et TestAppHost restent.
8. [fait] AppHost (def/src/AppHost/Program.cs) : SQLite via `AddSqlite`, front via `AddJavaScriptApp` seulement en mode run ; PostgreSQL (rich) via `AddAzurePostgresFlexibleServer(...).RunAsContainer(... Persistent)` : un conteneur Docker est donc demande a l'execution. Les tests fonctionnels demarrent TestAppHost via Aspire (FunctionalTestSetup.cs, timeout 60 s) : Docker probablement requis pour les tests de rich ; non verifie.
9. [fait] Le README promet un seeding/recreation de la base au demarrage selon nuget.org (base supprimee puis recreee) : non verifie par execution.
10. [limite de l'environnement, pas du template] Sortie du `dotnet new install` polluee par les workloads MAUI de la machine.

## Non verifie
- Build, tests, demarrage et requete HTTP pour def, min, rich (bloque : classifieur de permissions, "Code from External").
- Aspire dashboard / `aspire run`, Scalar `/scalar`, front Angular/React (npm install), conteneur PostgreSQL.
- Duree reelle de build et de tests ; presence d'avertissements.

## Premier code metier
Je le mettrais dans src/Application/<Feature>/{Commands,Queries} (via `dotnet new ca-usecase` depuis src/Application) et src/Domain/Entities pour l'entite, en copiant TodoLists. C'est une deduction depuis l'arborescence, la doc ne l'enseigne pas.
