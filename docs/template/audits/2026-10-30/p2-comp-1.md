# p2-comp-1 : développeur .NET compétent, premier usage du template jasontaylordev/CleanArchitecture (10.8.0)

Angle : je pars de la page GitHub et de son README. Environnement : WSL2, Ubuntu 26.04, SDK 10.0.301, Node 24, Docker 29, home .NET isolé.
Aucune ligne du dépôt Skyted n'a été lue ; sources : README GitHub, site cleanarchitecture.jasontaylor.dev, `--help`, fichiers générés, issues GitHub (GET).

## 1. Journal horodaté

```
12:06:32 Prérequis locaux : dotnet 10.0.301, node v24, docker OK.
12:06:33 README GitHub (gh api .../readme) : prérequis .NET 10, Node "seulement Angular/React", Docker "seulement SQL Server/PostgreSQL".
         Installation : `dotnet new install Clean.Architecture.Solution.Template`. Création : `dotnet new ca-sln -cf ... -db ... -o ...`.
12:06:41 `dotnet new install Clean.Architecture.Solution.Template` -> "Success: Clean.Architecture.Solution.Template@10.8.0 installed ... ca-sln, ca-usecase" (2,7 s).
         (Beaucoup de lignes "Pack ... is already installed / Writing workload pack installation record" : bruit du home isolé, limite d'environnement.)
12:06:44 `dotnet new ca-sln --help` : options -cf (Angular|React|None, défaut Angular), -db (postgresql|sqlite|sqlserver, défaut sqlite).
         Surprise mineure : le README écrit angular|react|none en minuscules, l'aide les affiche en majuscules (insensible à la casse, OK).
12:06:50 `dotnet new ca-sln -o Def` -> "created successfully" (2,2 s). Angular + SQLite. README généré : build, run, scaffolding, tests.
12:06:59 `dotnet build` (documenté : "the template should build cleanly out of the box") :
         "0 Warning(s) 120 Error(s)" en 16 s, ex. :
         .../tests/Application.FunctionalTests/Application.FunctionalTests.csproj : error NU1903: Warning As Error: Package 'System.Security.Cryptography.Pkcs' 10.0.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-846f-f764-76mx
         .../src/AppHost/AppHost.csproj : error NU1902: Warning As Error: Package 'MessagePack' 2.5.192 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-2f33-pr97-265q
         Paquets en cause : MessagePack 2.5.192, System.Security.Cryptography.Xml/Pkcs 10.0.1, OpenTelemetry.Api et .Exporter.OpenTelemetryProtocol 1.15.0, Microsoft.OpenApi 2.0.0, SQLitePCLRaw.lib.e_sqlite3 2.1.11.
         Le message ne dit pas quoi faire ensuite.
12:07:47 Site de doc : "Your first project" dit "Fix any errors before proceeding — the template should build cleanly out of the box". Aucune page ne parle de NU1902/NU1903.
12:08:08 Recherche d'issues (gh api GET) : #1675 "10.8.0 now fails to build because of NU1902/NU1903 security advisories", ouverte le 2026-10-28, sans commentaire ni contournement.
         Cause confirmée par l'auteur du ticket : dernière release 10.8.0 du 2026-03-23 ; les versions ont été relevées sur la branche (commits du 2026-10-25/26) mais jamais republiées sur nuget.org.
12:08:20 Contournement deviné (rien dans la doc) : `dotnet build -p:NuGetAudit=false` -> "Build succeeded. 0 Warning(s) 0 Error(s)" en 15 s.
12:08:55 `dotnet test -p:NuGetAudit=false` (le README dit `dotnet test`) : 1 min 16 s.
         Domain.UnitTests 6/6, Application.UnitTests 8/8, Application.FunctionalTests 19/19 ; Infrastructure.IntegrationTests : 0 test, aucune ligne de résultat ;
         Web.AcceptanceTests : "Failed! - Failed: 6, Passed: 0" :
         "Microsoft.Playwright.PlaywrightException : Executable doesn't exist at /home/pmmarchio/.cache/ms-playwright/chromium_headless_shell-1208/... Looks like Playwright was just installed or updated. Please run the following command to download new browsers: pwsh bin/Debug/netX/playwright.ps1 install"
         La doc dit pourtant "All tests should pass on a freshly scaffolded solution" et "no configuration or manual setup required".
12:10:37 `pwsh artifacts/bin/Web.AcceptanceTests/debug/playwright.ps1 install chromium` : "Error: ERROR: Playwright does not support chromium on ubuntu26.04-x64"
         (limite d'environnement : Ubuntu 26.04 trop récent pour Playwright). Effet de bord : "Removing unused browser at /home/pmmarchio/.cache/ms-playwright/chromium_headless_shell-1155" dans le vrai home.
         Le chemin artifacts/bin/.../playwright.ps1 n'est pas celui du message (bin/Debug/netX) : il faut le trouver avec `find`. Il faut aussi pwsh, absent d'une installation .NET standard.
12:10:50 Contournement : PLAYWRIGHT_HOST_PLATFORM_OVERRIDE=ubuntu24.04-x64 + PLAYWRIGHT_BROWSERS_PATH local -> navigateur téléchargé en 23 s.
12:11:16 Tests d'acceptation : toujours 6 échecs, "TargetClosedException : Target page, context or browser has been closed" (le chrome ne démarre pas : bibliothèques système probablement absentes).
         Classé "non vérifié (limite de l'agent/de l'environnement)", pas une friction du template.
12:12:05 README : `dotnet run --project .\src\AppHost` (antislash Windows). Sous Linux/bash, la commande est tolérée (dotnet normalise), mais le build échoue pour la même cause NU190x.
12:12:13 Lancement sous verrou Docker : `dotnet run --project src/AppHost -p:NuGetAudit=false`. En 12 s :
         "warn: The ASP.NET Core developer certificate is not trusted. For information about trusting ... https://aka.ms/aspnet/https-trust-dev-cert"
         "warn: No trusted Aspire development certificate was found. See https://aka.ms/aspire/devcerts"
         "Login to the dashboard at https://def.dev.localhost:17286/login?t=..." puis "Distributed application started."
12:18:46 Sondes HTTP réelles (nouvelle exécution, 120 s après le démarrage pour laisser `npm install` finir) :
         frontend (proxy Aspire) `http://localhost:33051/` -> 200 text/html ; API Web `http://localhost:38459/api/TodoLists` -> 401 (RequireAuthorization) ;
         `https://localhost:41889/scalar` -> 302 ; `http://localhost:38459/` -> 307 ; dashboard `https://localhost:17286/health` -> 200 text/plain.
         Les ports du Web et du frontend sont dynamiques : seuls le dashboard (navigateur) ou `ss -ltnp` les donnent. Ni le README ni la doc ne les listent.
12:19:26 `dotnet new ca-sln -cf none -db sqlite -o Min` -> créé (sans ClientApp, sans Web.AcceptanceTests).
12:19:30 `dotnet build` : "108 Error(s)", mêmes NU1902/NU1903 (MessagePack, OpenTelemetry, Microsoft.OpenApi, SQLitePCLRaw, Pkcs, Xml) en 1,8 s.
12:19:35 `dotnet test -p:NuGetAudit=false` sur Min : REFUSÉ par le classifieur d'autorisation de l'outil ("Code from External"). Je n'ai pas contourné. Non vérifié (limite de l'agent).
12:19:55 `dotnet new ca-sln -cf react -db postgresql -o Rich` -> créé. AppHost : `AddAzurePostgresFlexibleServer(...).RunAsContainer(... Persistent)` : Docker requis. Build/test/run non tentés (refus ci-dessus).
12:20:05 Lecture de la doc : scaffolding, couche application, présentation, Aspire, bases de données, tests.
12:20:17 Recherche dans la doc de "administrator@localhost", "dev-certs", "playwright install", "NU1903" : 0 occurrence dans les pages lues.
```

## 2. Mesures

| Étape | Durée | Résultat | Étapes manuelles | Prérequis découverts |
|---|---|---|---|---|
| Lire le README GitHub | ~1 min | OK | 0 | .NET 10, Node (SPA), Docker (SQL Server/PostgreSQL) bien annoncés |
| `dotnet new install` | 2,7 s | OK | 1 | réseau |
| `dotnet new ca-sln --help` | 1 s | OK | 1 | |
| Générer Def (Angular + SQLite) | 2,2 s | OK | 1 | |
| `dotnet build` Def, tel que documenté | 16 s | ECHEC, 120 erreurs NU1902/NU1903 | 0 | aucun contournement documenté |
| Chercher la cause (issue #1675) | ~1 min | cause trouvée, ticket ouvert | 2 (gh, lecture) | |
| `dotnet build -p:NuGetAudit=false` Def | 15 s | OK | 1 (contournement deviné) | |
| `dotnet test` Def | 1 min 16 s | ECHEC partiel : 33 tests verts (6+8+19), 6 acceptation en échec | 0 | navigateurs Playwright, pwsh, une plateforme supportée |
| Installer Playwright | 2 s puis 23 s | ECHEC (Ubuntu 26.04) puis OK avec override | 3 | pwsh, `find` du script |
| Tests d'acceptation après install | 25 s | ECHEC (chrome ne démarre pas) | 0 | non vérifié (environnement) |
| `dotnet run --project src/AppHost` Def | 12 s jusqu'à "started" ; npm install fini avant 120 s | OK avec `-p:NuGetAudit=false` | 1 | certificat de dev non approuvé, npm install au premier lancement |
| Requête HTTP réelle Def | immédiat | frontend 200, API 401, health 200 | 3 (trouver les ports) | |
| Générer Min (`-cf none`) | 2 s | OK | 1 | |
| `dotnet build` Min, tel que documenté | 1,8 s | ECHEC, 108 erreurs | 0 | |
| `dotnet test` Min | n/a | refusé par le classifieur : non vérifié | | |
| Générer Rich (React + PostgreSQL) | 2 s | OK | 1 | Docker (PostgreSQL en conteneur) |
| Build / test / run Rich | n/a | non tentés : non vérifié | | |

Taux de réussite du parcours « seulement avec la doc » : 0 sur 3 builds (Def, Min, Rich) réussis sans deviner `-p:NuGetAudit=false`.

## 3. Où mettre mon premier code métier

La doc répond bien : une fonctionnalité = un dossier dans `src/Application/<Feature>/` avec `Commands/<UseCase>/` et `Queries/<UseCase>/` (page « Application layer »), l'entité dans `src/Domain/Entities`, un groupe d'endpoints dans `src/Web/Endpoints/` qui implémente `IEndpointGroup` (découverte automatique), et `dotnet new ca-usecase -n ... -fn ... -ut command|query -rt ...` à lancer depuis `src/Application` génère commande, handler et validateur. L'exemple TodoLists/TodoItems montre le modèle complet. Prouvé par la lecture de `src/Web/Endpoints/TodoLists.cs` et des pages « Code scaffolding » et « Presentation layer ». Le scaffolding lui-même n'a pas été exécuté (refus du classifieur, voir friction 7). Manques : la doc ne dit pas comment ajouter une entité et la migration (SQLite recréée au démarrage, migrations pour la production renvoyées à une autre page), ni comment s'authentifier (voir friction 5).

## 4. Frictions, du pire au moindre

1. **Bloquant : le template publié ne compile pas.** `dotnet build` échoue sur toutes les configurations testées (Def 120 erreurs, Min 108) : `TreatWarningsAsErrors` dans `Directory.Build.props` + audit NuGet (NU1902/NU1903) sur des paquets figés dans `Directory.Packages.props` (MessagePack 2.5.192, System.Security.Cryptography.Xml/Pkcs 10.0.1, OpenTelemetry 1.15.0, Microsoft.OpenApi 2.0.0, SQLitePCLRaw 2.1.11). Preuve : journal 12:06:59 et 12:19:30. Issue #1675 ouverte depuis le 2026-10-28, sans réponse ; la branche principale a relevé les versions (commits du 2026-10-26) mais 10.8.0 (2026-03-23) est toujours la dernière version nuget. La doc affirme l'inverse (« should build cleanly out of the box »). Le message d'erreur ne dit pas quoi faire. `dotnet run` du README échoue pour la même raison.
2. **Important : `dotnet test` ne passe pas, contrairement à la doc.** « All tests should pass on a freshly scaffolded solution » et « no configuration or manual setup required » : les 6 tests d'acceptation (Angular/React) exigent un téléchargement de navigateurs Playwright, via `pwsh` (non fourni avec le SDK) et un chemin `artifacts/bin/Web.AcceptanceTests/debug/playwright.ps1` différent de celui du message (`bin/Debug/netX`). Preuve : journal 12:08:55 et 12:10:37. Rien dans le README généré, ni dans la doc des tests. La partie plateforme non supportée (Ubuntu 26.04) et chrome qui ne démarre pas sont des limites de mon environnement, pas du template.
3. **Moyen : les deux warnings de certificat ne sont pas expliqués dans la doc.** « developer certificate is not trusted » et « No trusted Aspire development certificate » au premier lancement ; aucune page ne cite `dotnet dev-certs https --trust` (0 occurrence). Le dashboard demande un jeton `?t=` imprimé seulement dans la console.
4. **Moyen : trouver l'application qui répond.** Le README dit « The Aspire dashboard will open automatically », mais l'URL de l'API et du frontend sont dynamiques (ex. 38459 et 33051 ici) ; sans navigateur, il faut `ss -ltnp`. L'ouverture automatique ne fonctionne pas sans bureau (WSL/SSH/conteneur) : limite en partie d'environnement, mais aucun port fixe ni commande `curl` n'est donné.
5. **Moyen : toute l'API répond 401.** `/api/TodoLists` exige une authentification (cookie Identity) ; l'identifiant d'amorçage `administrator@localhost` n'est que dans `src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:79` (le mot de passe est dans le même fichier). Ni README, ni doc, ni Web.http ne l'indiquent explicitement ; le premier appel d'un nouveau venu est donc un 401 sans piste.
6. **Mineur : README généré et README GitHub avec antislash Windows** (`dotnet run --project .\src\AppHost`) ; fonctionne sous bash mais surprenant, et la doc du site utilise la barre oblique. README généré qui mentionne `dotnet test` sans prérequis. Majuscules/minuscules des valeurs de `-cf` différentes entre README et `--help`.
7. **Mineur (limite de l'agent, pas du template) :** `dotnet test` sur Min et tout le parcours Rich (build, test, run avec PostgreSQL en conteneur) refusés ou non tentés après un refus du classifieur ; l'installation du Playwright a supprimé un navigateur inutilisé dans le vrai `~/.cache/ms-playwright` (comportement de l'outil Playwright, effet de bord hors du dossier isolé). Un chemin de cache NuGet d'un autre agent apparaît dans une commande `dotnet-getdocument` (`.../p2-ours-1/home/.nuget/packages/...`) : anomalie d'isolation de mon environnement, non imputable au template.

## 5. Points forts (prouvés)

- Installation et génération sont rapides et sans question : 2,7 s et 2,2 s, un seul message de succès clair (12:06:41, 12:06:50).
- Quand l'audit est désactivé, le build est propre : 0 avertissement, 0 erreur en 15 s (12:08:20) ; 33 tests unitaires et fonctionnels verts en 1 min 16 s (12:08:55).
- Le lancement par Aspire fonctionne du premier coup avec SQLite : base, API et frontend démarrés en 12 s, health 200, frontend 200 (12:18:46) ; Node n'est pas requis pour `-cf none` et Docker non requis pour SQLite : le README l'annonçait correctement.
- La structure en tranches verticales, `ca-usecase`, et `IEndpointGroup` à découverte automatique répondent à « où mettre mon premier code » (section 3).

## 6. Non vérifié

- `dotnet test` de Min, et build, tests et exécution de Rich (React + PostgreSQL) : refus du classifieur d'autorisation après le build/test/run de Def ; non contourné.
- Tests d'acceptation Playwright : chrome ne démarre pas sur cet hôte (Ubuntu 26.04), limite d'environnement.
- Scaffolding `ca-usecase` non exécuté ; connexion avec l'administrateur seed non essayée ; Aspire CLI (`aspire run`) non installé, non testé ; Scalar non ouvert dans un navigateur.
