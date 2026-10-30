# P5 - Groupe G4 : sécurité et front-ends (analyste opus)

Date : 2026-10-30. Lecture seule ; `gh` en GET seulement ; aucun build, aucune génération.

Abréviations de chemins :
- `ours/` = `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/ours` (develop, 5f22faa5)
- `comp/` = `.../repos/competitor-main` (main, 345e7fe) ; `rel/` = `.../repos/competitor-release` (v10.8.0)

Étalon : `out/00-cadrage.md`, promesses 5 (sécurité sérieuse par défaut) et 7 (fronts au choix, issus des générateurs officiels épinglés).
Matière reprise : cartes P1, inventaire P1, personas P2/P3, socle P4. Chaque reprise a été revérifiée dans le code ; les chiffres des personas (temps, échecs de build) sont cités comme tels, pas rejoués.

---

## Axe 1 - Sécurité

### 1.1 Authentification

**Concurrent.** Un seul mode, Identity local, sans option. `AddIdentityCookies()` pour la variante avec SPA, `AddBearerToken` (jeton opaque d'Identity) pour l'API seule :

```csharp
// comp/src/Infrastructure/DependencyInjection.cs:46-73
#if (UseApiOnly)
        builder.Services.AddAuthentication()
            .AddBearerToken(IdentityConstants.BearerScheme);
#else
        builder.Services.AddAuthentication(options => { options.DefaultScheme = IdentityConstants.ApplicationScheme; ... })
            .AddIdentityCookies();
```

Les routes sont celles du framework : `groupBuilder.MapIdentityApi<ApplicationUser>();` (`comp/src/Web/Endpoints/Users.cs:12`). Aucune option d'Identity n'est réglée (`grep -rn "RequireConfirmed\|Lockout\|Password\.\|IEmailSender" comp/src` : vide) : longueur de mot de passe, verrouillage, confirmation d'adresse sont les valeurs par défaut du framework, et aucun `IEmailSender` n'est enregistré. Le compte d'amorçage `administrator@localhost` / `Administrator1!` est écrit dans le code (`comp/src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:79,83`), créé en Development seulement (`comp/src/Web/Program.cs:17-20`).

**Nous.** Trois modes à la génération (`--auth local|external|none`, inventaire P1 §2). En local : JWT d'accès + famille de refresh tokens (ADR 0013), cookie `HttpOnly` pour le navigateur (ADR 0016), passkeys (ADR 0015), 2FA, clés d'accès pour les clients sans rafraîchissement (ADR 0026), 7 features d'authentification (`ls ours/Src/Application/AppTemplate.Application.Auth/Features` : AccessKeys, Accounts, ExternalLogins, Passkeys, SecurityEvents, Sessions, TwoFactor). Politique de mot de passe réglée et validée : 12 caractères par défaut, plancher refusé au démarrage (`ours/Src/Infrastructure/AppTemplate.Infrastructure.Auth/Common/Options/IdentityPolicyOptions.cs:12,26,44-47`) ; verrouillage configuré (`AuthModule.cs:244-245`). Le compte d'amorçage est désactivé et sans mot de passe dans le fichier suivi (`ours/Src/Presentation/AppTemplate.Api/appsettings.json:154-157`), et le validateur l'exige quand on l'active (`IdentitySeedOptions.cs:34-37`). Tests : `PasswordPolicyTests`, `AccountLockoutTests`, `RefreshTokenTests`, `RefreshTokenCookieTests`, `SecurityStampRotationTests` (`ours/Tests/Integration/AppTemplate.Api.IntegrationTests/Security/`).

**Verdict : on dépasse (confiance haute).** Le concurrent ne propose ni OIDC, ni rafraîchissement maîtrisé, ni 2FA dans son code, et laisse tout réglage au framework.

### 1.2 Autorisation par défaut et propriété

**Concurrent.** Pas de politique de repli : `builder.Services.AddAuthorizationBuilder();` sans `SetFallbackPolicy` (`comp/src/Infrastructure/DependencyInjection.cs:50,65`). Chaque groupe d'endpoints doit dire `groupBuilder.RequireAuthorization();` (`comp/src/Web/Endpoints/TodoItems.cs:13`, `TodoLists.cs:13`, `WeatherForecasts.cs:10`). Un groupe neuf qui l'oublie est anonyme ; l'item template `ca-usecase` n'écrit pas d'endpoint (carte P1 concurrent §5), donc rien ne le rappelle. La couche application a son propre `[Authorize]` traité par `AuthorizationBehaviour` (`comp/src/Application/Common/Behaviours/AuthorizationBehaviour.cs:24-75`), posé sur une seule requête sur neuf (`comp/src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:9` ; `grep -rn "\[Authorize" comp/src` = 1 résultat applicatif). Aucune propriété des données : `GetTodosQuery` renvoie toutes les listes de tous les utilisateurs (`GetTodos.cs:43-47`) ; `CreatedBy` n'est qu'un champ d'audit (`comp/src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs:47`). Aucun test n'appelle HTTP (carte P1 concurrent §6 : `grep -rn 'CreateClient|HttpClient' tests` ne trouve que `AspireSetup.cs:37`), donc aucun test ne tient « tout endpoint exige un utilisateur ».

**Nous.** Repli authentifié :

```csharp
// ours/Src/Presentation/AppTemplate.Api/Program.cs:112-115
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
```

En `--auth none`, le repli laisse passer et le démarrage refuse tout `[Authorize]` restant (`Program.cs:116-121,138-161`). `DefaultDenyAuthorizationTests.EveryEndpointNotMarkedAnonymous_Answers401WithoutATokenAndForAGarbageOne` parcourt la source d'endpoints et appelle chaque route, sans jeton puis avec un jeton invalide (`ours/Tests/Integration/AppTemplate.Api.IntegrationTests/Security/DefaultDenyAuthorizationTests.cs:40-80`). Le propriétaire est dans la requête, 404 jamais 403 (ADR 0009), tenu par `OwnershipIsolationTests` (7 tests, dont `AnExistingForeignList_AnswersExactlyAsAnAbsentOneDoes`, ligne 107) et `StoredFileOwnershipTests`.

**Verdict : on dépasse (confiance haute).**

**Défaut interne trouvé (moyen, confiance haute).** Le portail Blazor Server, hôte de présentation que le template propose (`--presentation blazor-server`), ne suit pas cette règle : `AddAuthorizationBuilder().AddPolicy(Administrator, ...)` sans repli (`ours/.template.config/starters/blazor-server/Src/Presentation/AppTemplate.PresentationAppPortal/Features/Auth/Composition/CookieSessionComposition.cs:77-78`), et la doc le dit : « No page, and no authorization by default: a page asks for a signed-in user with `[Authorize]` » (`ours/docs/features/presentation.md:126`). Les use cases gardent `currentUser.RequireUserId()` (carte P1 nous §2, `CreateTodoListUseCase.cs:13-47`), donc les données restent protégées ; mais la promesse « default-deny » de l'étalon s'arrête à l'API, sans ADR qui l'assume pour le portail. Voir 1.4 pour ses en-têtes.

### 1.3 Surface anonyme

**Concurrent.** Anonymes : toutes les routes de `MapIdentityApi` (register, login, refresh, confirmEmail, resendConfirmationEmail, forgotPassword, resetPassword) ; OpenAPI et Scalar dans **tous** les environnements (`comp/src/Web/Program.cs:35-36`) ; les fichiers statiques et le repli `index.html` (`Program.cs:33,48`). Aucun limiteur de débit (`grep -rn RateLimit comp/src` : vide, confirmé par P4 sonnet B37). Les réponses de `/register` sont celles d'Identity, qui renvoie les erreurs de validation du `UserManager` (dont le doublon d'adresse) : énumération possible ; c'est un comportement du framework, non rejoué ici (confiance moyenne). Le front masque le détail (`'Registration failed. Please try again.'`, `comp/src/Web/ClientApp/src/api-authorization/register/register.component.ts:37`), pas l'API.

**Nous.** Liste fermée de 12 actions `[AllowAnonymous]` + 4 endpoints « fluents » (OpenAPI, Scalar, 2 sondes), tenue par `HttpSurfaceTests.EveryAnonymousEndpoint_IsOnTheList` (`ours/Tests/Architecture/AppTemplate.Architecture.Tests/Rules/HttpSurfaceTests.cs:10-28,56-60`). OpenAPI et Scalar en Development seulement (`Program.cs:170-175`). Routes anonymes muettes (ADR 0014) : `LoginUniformityTests`, `RegistrationFlowTests` ; limiteur par adresse sur l'authentification (`ours/Src/Presentation/AppTemplate.Api/Common/Security/RateLimitingExtensions.cs:39-44`, `RateLimitingTests`).

**Verdict : on dépasse (confiance haute).**

### 1.4 En-têtes, CORS, HTTPS

**Concurrent.** HSTS hors Development et redirection HTTPS, rien d'autre ; CORS ouvert à toute origine :

```csharp
// comp/src/Web/Program.cs:24-31
    app.UseHsts();
...
app.UseHttpsRedirection();
app.UseCors(static builder =>
    builder.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());
```

Aucune CSP, ni `X-Frame-Options`, ni `nosniff` (`grep -rn -i "csp\|Content-Security\|X-Frame\|nosniff" comp/src` : vide). La PR « Fix/cors » qui voulait « Enable CORS for all environments » a été fermée sans fusion (https://github.com/jasontaylordev/CleanArchitecture/pull/1402).

**Nous.** En-têtes posés sur toute réponse, problèmes compris : `nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, CSP default-deny validée au démarrage, `X-Powered-By` retiré (`ours/Src/Presentation/AppTemplate.Api/Common/Security/SecurityHeadersExtensions.cs:60-73` ; `SecurityHeaderOptions.cs:9-10` : `"default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"`). Scalar en Development sous une CSP à nonce (`Program.cs:174`, `ApiReferenceContentSecurityPolicy.cs`, `ApiReferencePolicyTests`). CORS fermé tant qu'aucune origine n'est listée (`CorsExtensions.cs:25-30`), sans credentials. Pas de HSTS ni de redirection : TLS terminé en amont, ligne de l'ADR 0001 (`ours/docs/decisions/0001-adopt-the-generated-baseline.md:63`). Tests : `SecurityHeaderTests` (5 tests, dont `AProblemDetailsResponse_CarriesEverySecurityHeader`), `CorsExtensionsTests`, `CorsOptionsValidatorTests`.

**Verdict : on dépasse (confiance haute) ; absence de HSTS = choix assumé (ADR 0001).**

**Défaut interne trouvé (moyen, confiance haute).** Le portail Blazor Server n'envoie aucun en-tête de sécurité : son `Program.cs` masque l'en-tête `Server` (`.../AppTemplate.PresentationAppPortal/Program.cs:31`) puis enchaîne `UseAuthentication`, `UseAuthorization`, `UseAntiforgery`, `MapRazorComponents` (lignes 76-105) sans CSP, `frame-ancestors`, `X-Frame-Options` ni `nosniff` (`grep -rn -i "XFrameOptions\|nosniff\|ContentSecurityPolicy"` dans le starter et dans `Presentation.Core` : vide). Son formulaire de connexion peut donc être encadré par un autre site (clickjacking). L'API, les apps web servies par nginx et les apps SSR envoient, elles, ces en-têtes ; le portail est le seul hôte HTTP qui ne le fait pas, sans ADR ni ligne de doc pour le justifier.

### 1.5 Secrets et journaux

**Concurrent.** Azure Key Vault branché quand `AZURE_KEY_VAULT_ENDPOINT` est fourni (`comp/src/Web/DependencyInjection.cs:39-48`), avec `DefaultAzureCredential`. Pas de user-secrets. Journal : `LoggingBehaviour` écrit toute requête MediatR déstructurée, `{@Request}` (`comp/src/Application/Common/Behaviours/LoggingBehaviour.cs:32-33`) : une future commande portant un secret le journaliserait. Les routes d'Identity ne passent pas par MediatR, donc pas de fuite effective aujourd'hui (déduction de lecture, comme P4 sonnet).

**Nous.** Fichiers suivis sans secret (`appsettings.json:2` « holding no secret » ; champs vides lignes 16, 70-71, 125, 157). Clé JWT de développement publique refusée hors Development :

```csharp
// ours/Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Sessions/Configuration/Options/JwtOptions.cs:46-51
            && options.Key.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add(
                $"'{JwtOptions.SectionName}:Key' is the development key the template ships, which is public. " + ...
```

Longueur ≥ 32 octets exigée (`JwtOptions.cs:39-42`), test `JwtOptionsValidatorTests`. Aucun fournisseur de coffre : la doc renvoie au gestionnaire de secrets de la plateforme (`ours/docs/guides/deployment.md:92-106`). Journaux : `RequestLogRedactionTests.TheAuthenticationFlow_WritesNoCredentialToAnyLog` et `NoLogEntry_NamesTheAuthorizationHeaderOrACookie` (lignes 14, 55).

**Verdict : on dépasse sur les garde-fous (confiance haute) ; à égalité au plus sur le coffre (confiance moyenne).** L'intégration Key Vault du concurrent est propre à Azure ; nous restons neutres vis-à-vis du nuage. Aucune ADR ne le dit (P4 devil B6 le classe « hors périmètre »).

### 1.6 Dépendances vulnérables

**Concurrent.** `TreatWarningsAsErrors` (`comp/Directory.Build.props:5`) avec l'audit NuGet par défaut ; Renovate le lundi, fusion automatique des mises à jour mineures (`rel/renovate.json`) ; CodeQL sans langage déclaré (`comp/.github/workflows/codeql.yml:34-42`). Une épingle de sécurité transitive dans main (`comp/Directory.Packages.props:33-34`, Pkcs 10.0.14). Mais la version publiée 10.8.0 fige des paquets devenus vulnérables (`rel/Directory.Packages.props:58-62`, OpenTelemetry 1.15.0) : le projet généré ne compile plus, issue ouverte sans réponse « 10.8.0 now fails to build because of NU1902/NU1903 security advisories » (https://github.com/jasontaylordev/CleanArchitecture/issues/1675, 2026-10-28, 0 commentaire). Les personas l'ont vécu : 120 erreurs NU1902/NU1903 au premier `dotnet build` (`out/p2-comp-1.md:19-21,63`, `out/p3-comp-1.md:17-18`), contournement en ajoutant `<WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>`. Côté npm : `"nswag": "latest"` non épinglé (`comp/src/Web/ClientApp/package.json:38`, `ClientApp-React/package.json:22`), versions en `^`, et la publication lance `npm install` et non `npm ci` (`comp/src/Web/Web.csproj:42`).

**Nous.** Audit NuGet explicite au niveau le plus bas (`ours/Directory.Build.props:15,19-21` : `NuGetAuditMode` all, `NuGetAuditLevel` low), plus `dotnet list package --vulnerable --include-transitive` lu par `verify` (`ours/Tools/Tasks.cs:2031-2045`) et par le job CI `vulnerable-packages` (`ours/.github/workflows/ci.yml:239-265`). Dependabot sur 5 écosystèmes dont npm et les images (`ours/.github/dependabot.yml:5,79,102,140,178`). CodeQL `csharp` + `javascript-typescript` + `actions` (`ours/.github/workflows/codeql.yml:42-50`). Versions exactes, images par digest (`ours/Src/Web/apps/FrameworkSite/Dockerfile:5,14,66,82`), `pnpm install --frozen-lockfile` en CI (`web.yml:57`) et en image (`Dockerfile:36`, `--offline`), scripts d'installation refusés (`ours/Src/Web/pnpm-workspace.yaml` : `allowBuilds` à `false`). La doc donne la réponse à un avis : une épingle transitive, jamais un niveau d'audit relâché (`ours/docs/guides/build-and-ci.md:161-164`).

**Verdict : on dépasse (confiance haute), avec deux réserves prouvées.**

1. **Même mécanisme que l'issue #1675 (risque, confiance moyenne).** `TreatWarningsAsErrors` + audit `low` : un projet généré depuis une version publiée cessera de compiler dès qu'un avis touche un paquet qu'elle fige. 1.0.0 est sur nuget.org depuis le 2026-10-22 (inventaire P1 §3) ; aucun workflow planifié ne génère depuis le paquet publié pour le détecter (`grep -n "schedule\|cron" ours/.github/workflows/*.yml` : seul `codeql.yml:9-10`). Notre avance est la réponse écrite (`build-and-ci.md:161`), pas l'immunité. Non vérifié : l'état réel de 1.0.0 aujourd'hui.
2. **Aucun audit des paquets npm.** `verify` et `web.yml` ne lancent ni `pnpm audit` ni équivalent (`grep -rn "pnpm audit\|npm audit" ours/Tools ours/.github ours/docs` : vide). Les avis .NET cassent le build au niveau `low`, les avis JavaScript n'arrêtent rien ; Dependabot ne fait que proposer. Le concurrent n'a pas mieux.

### 1.7 Ce qu'on gagnerait à prendre au concurrent (sécurité)

- **Rien de structurel.** Sa sécurité est celle du framework par défaut.
- **Éventuellement, un point d'extension de coffre**, non câblé sur Azure : une page « secrets en production » qui montre l'ajout d'un `IConfigurationSource` (Key Vault, AWS Secrets Manager, Vault) sans rien livrer. Si l'équipe décide de ne jamais en livrer, l'écrire en une ligne d'ADR 0001 (« la plateforme injecte les secrets ») pour que l'écart soit un choix assumé et non un oubli. Gain faible.

### 1.8 Ce qu'il faut refuser (sécurité)

- **CORS `AllowAnyOrigin`** (`comp/src/Web/Program.cs:28-31`) : contraire à la promesse 5 ; notre liste blanche validée est meilleure.
- **OpenAPI et Scalar en production** (`comp/src/Web/Program.cs:35-36`) : la surface anonyme s'élargit sans besoin.
- **Autorisation à opt-in par groupe** et `[Authorize]` applicatif à côté de celui de HTTP : deux mécanismes, dont aucun par défaut.
- **`MapIdentityApi` tel quel** : erreurs d'inscription renvoyées à l'appelant, contraire à l'ADR 0014.
- **Compte administrateur au mot de passe écrit dans le code** (`ApplicationDbContextInitialiser.cs:79,83`), même limité à Development.
- **Journaliser la requête entière** (`{@Request}`) : contraire à `RequestLogRedactionTests`.

### 1.9 Ce qu'on peut faire qu'il ne fait pas (sécurité)

- **Fermer les deux défauts internes** : un repli authentifié et des en-têtes (`frame-ancestors 'none'`, `nosniff`, `Referrer-Policy`) dans le portail Blazor Server, avec un test d'hôte du même genre que `SecurityHeaderTests` ; ou une ADR qui dit pourquoi le portail n'en a pas besoin.
- **Un audit JavaScript dans le gate** (`pnpm audit --audit-level low` dans `verify` quand le workspace existe, et dans `web.yml`), avec la même règle que NuGet : une `overrides` épinglée et datée, jamais un niveau relâché. Une ADR courte si le niveau diffère de NuGet.
- **Une sentinelle planifiée sur la version publiée** : un job hebdomadaire qui installe le paquet de nuget.org, génère les formes par défaut et lance `restore` + `build` ; il ouvre une issue au premier NU190x. C'est exactement ce qui a manqué au concurrent pendant 7 mois (#1675). Ne demande pas de build dans cet audit, seulement un workflow.
- **Répondre aux demandes de ses utilisateurs, déjà couvertes chez nous** :
  - « Replace Identity or add free alternative » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1043) et « Duende IdentityServer isn't free for production » (https://github.com/jasontaylordev/CleanArchitecture/discussions/864) : notre `--auth external` (OIDC de n'importe quel fournisseur) et notre Identity local sans serveur tiers ;
  - « CORS issues when deployed with domain name » (https://github.com/jasontaylordev/CleanArchitecture/discussions/572) : notre liste d'origines validée au démarrage et le relais même origine des apps web ;
  - « How to secure API resources without user login » (https://github.com/jasontaylordev/CleanArchitecture/discussions/733) : nos clés d'accès (ADR 0026).
  Ces discussions sont des arguments de README, pas du travail.

---

## Axe 2 - Front-ends

### 2.1 Frameworks proposés

**Concurrent.** `ClientFramework` : Angular (défaut), React, None (inventaire P1 §2). Le code des deux apps est maintenu dans le dépôt du template, sous `src/Web/ClientApp` et `src/Web/ClientApp-React`, et le modèle renomme le second (`template.json`, modificateur `"rename": {"ClientApp-React": "ClientApp"}`). Angular en NgModule (`standalone: false`, `comp/src/Web/ClientApp/src/api-authorization/register/register.component.ts:10` ; `app.module.ts:27-60`) ; React en JSX non typé (`comp/src/Web/ClientApp-React/src/*.jsx`, 15 fichiers `.jsx`). Écart release/main : 10.8.0 livre Angular 21 + Karma (`rel/src/Web/ClientApp/package.json:21,41-45`), main est en Angular 22 + vitest, non publié. Demandes ouvertes : Vue (https://github.com/jasontaylordev/CleanArchitecture/issues/1570), SvelteKit statique (https://github.com/jasontaylordev/CleanArchitecture/issues/1625), Blazor (https://github.com/jasontaylordev/CleanArchitecture/issues/1444).

**Nous.** `--web none|placeholder|angular|vite-react|vite-vue|vite-svelte|react-router|blazor-wasm`, `--web-audience public|admin`, `--web-rendering spa|ssr` (inventaire P1 §2). Aucune app n'est copiée dans le template : chaque framework est un descripteur et un overlay, et l'app est écrite par la CLI officielle à une version exacte au bootstrap (template ADR 0003 puis 0008) :

```json
// ours/.template.config/starters/angular/FrameworkSite/scaffold.json:1-9
  "framework": "angular",
  "package": "@angular/cli",
  "version": "22.2.3",
  "arguments": ["new", "{name}", ... "--standalone", "--routing", "--ssr=false", "--test-runner=vitest", ...]
```

Plus des hôtes .NET de présentation (Blazor Server, MCP, Avalonia, WPF, worker), et des apps ajoutées après coup par `cleanarch-webapp` et `cleanarch-webpackage` (`ours/docs/features/web-frontend.md:294-330`). Différés : SvelteKit, Nuxt, Next, Astro (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/119), autres templates create-vite (#118).

**Verdict : on dépasse (confiance haute).** Les trois demandes ouvertes chez lui (Vue, Svelte, Blazor) sont des valeurs d'option chez nous ; SvelteKit est différé des deux côtés.

### 2.2 Intégration avec l'API

**Concurrent.** La SPA est servie par l'API, même processus, même origine : `app.UseFileServer();` et `app.MapFallbackToFile("index.html");` (`comp/src/Web/Program.cs:33,48`) ; la publication construit la SPA et la copie dans `wwwroot` (`comp/src/Web/Web.csproj:40-60`). En développement, Aspire lance le serveur de dev qui relaie `/api` (`comp/src/Web/ClientApp/proxy.conf.js:3-18`, `ClientApp-React/vite.config.ts:4-25`). L'URL de base vient de `<base href>` (`app.module.ts:22-25`). Issue ouverte : l'Angular par défaut appelle le mauvais port sous Aspire (https://github.com/jasontaylordev/CleanArchitecture/issues/1594). Cookie Identity + `withCredentials` (`authorize.interceptor.ts:14`) ; pas de jeton anti-CSRF, la protection repose sur `SameSite=Lax` par défaut du cookie d'Identity et sur le corps JSON exigé par `logout` (`Users.cs:19`) (confiance moyenne : défaut du framework, non rejoué).

**Nous.** Chaque app est un déployable : nginx non privilégié pour une SPA, son propre serveur Node pour une app SSR, Blazor WASM publié derrière nginx ; chacun relaie `/api/` et `/health` à `API_UPSTREAM`, « so the browser talks to one origin per app and the API keeps CORS closed » (`ours/docs/features/web-frontend.md:17-19`). Navigateur : jeton d'accès en mémoire, refresh dans le cookie `HttpOnly` de l'API (ADR 0016, `web-frontend.md:144-171`). SSR : le serveur garde les deux jetons, le navigateur n'a que `__Host-session` (`HttpOnly; Secure; SameSite=Lax`, `ours/Src/Web/packages/api-client/src/server/store.ts:5,51`) et tout appel qui change l'état exige `X-CSRF: 1` (`store.ts:160-166`) (ADR 0024). Tenu de bout en bout sur la pile : `AWebApp_ServesItsPageAndRelaysApiToTheApi`, `AServerRenderedApp_RefusesACallThatChangesStateWithoutXCsrf_WhereASinglePageAppRelaysIt`, `AServerRenderedApp_RefusesASignInCallbackThisBrowserDidNotStart_...` (`ours/Tests/EndToEnd/AppTemplate.EndToEnd.Tests/WebAppTests.cs:36,79,99`).

**Verdict : choix différent (topologie), on dépasse sur la sécurité de l'intégration (confiance haute).** Servir la SPA par l'API est plus simple à déployer (un artefact) ; un front séparé coûte une image et un relais de plus. Le coût utilisateur de notre choix n'est pas prouvé ; il n'est pas non plus posé dans une ADR face à l'alternative « servie par l'API » (ADR 0024 le fait seulement pour le SSR, template ADR 0008 ne liste pas cette alternative).

### 2.3 Client généré

**Concurrent.** NSwag, version `latest` non épinglée (`package.json:38`), lancé en `prestart` et `prebuild` (`package.json:7,10`) sur `../wwwroot/openapi/v1.json` que le build .NET écrit (`nswag.json:7`, `Web.csproj:8-9`). Le client n'est pas suivi par git (`comp/.gitignore:43-44`) : le front ne compile pas sans le SDK .NET et sans un build de l'API. Gabarit Angular (`HttpClient`) ou Fetch pour React (`ClientApp-React/nswag.json:18`). Erreurs : `SwaggerException` (`nswag.json:32`), aucun contrat de code d'erreur.

**Nous.** Un seul client TypeScript pour tous les frameworks : schéma `openapi-typescript` committé, wrapper `openapi-fetch` (ADR 0019) ; `unwrap` lève une `ApiError` portant le `code` stable du problème et les erreurs de champ (`ours/docs/features/web-frontend.md:95-139`) ; sessions navigateur et serveur dans le même paquet. Versions exactes (`ours/Src/Web/packages/api-client/package.json:16-28`). 67 tests vitest (`grep -c "it(\|test("` sur les 5 fichiers de `ours/Src/Web/packages/api-client/test/` : 9 + 7 + 19 + 12 + 20). CI régénère et échoue sur différence (`ours/.github/workflows/web.yml:68-69`). Pour Blazor WASM et le bureau : client .NET Kiota (ADR 0023), vérifié par `api-client --check` dans `verify` (carte P1 nous §3).

**Verdict : on dépasse (confiance haute).**

### 2.4 Rendu serveur (SSR)

**Concurrent.** Aucun. Reste mort : `app.server.module.ts` et `@angular/platform-server` (`comp/src/Web/ClientApp/src/app/app.server.module.ts:1-10`, `package.json:23`), mais `angular.json` n'a ni entrée serveur ni `ssr` (`angular.json:17-41`) et la publication ne prend que `dist\browser` (`Web.csproj:51`). Autre reste : `"allowedCommonJsDependencies": ["oidc-client"]` (`angular.json:27-29`) sans `oidc-client` dans les dépendances.

**Nous.** `--web-rendering ssr` pour Angular et React Router (template ADR 0011, ADR 0024). Le serveur Angular calcule un nonce par réponse et le hachage des scripts inline du gabarit :

```ts
// ours/.template.config/starters/angular/ssr/FrameworkSite/overlay/src/server.ts:32-49,77-81
const templateScriptHashes = existsSync(pageTemplate) ? [...readFileSync(pageTemplate, 'utf8').matchAll(...)].map(... `'sha256-...'`) : [];
const contentSecurityPolicy = (nonce: string) => [ "default-src 'self'", ... ["script-src 'self'", ...templateScriptHashes, `'nonce-${nonce}'`].join(' '), ...].join('; ');
...
  const nonce = randomBytes(16).toString('base64');
  response.setHeader('Content-Security-Policy', contentSecurityPolicy(nonce));
  response.setHeader('Cache-Control', 'no-store');
```

Test de pile : `AWebAppsPage_RunsAnInlineScriptOnlyByItsHashOrItsResponsesNonce_AndNeverRepeatsANonce` (`WebAppTests.cs:55`). Limite assumée : sessions en mémoire, une seule instance, un redémarrage déconnecte (ADR 0024, `web-frontend.md` section « Images and headers »).

**Verdict : on dépasse (confiance haute) ; instance unique = choix assumé (ADR 0024).**

### 2.5 Build et sécurité des apps

**Concurrent.** Pas de Dockerfile ni de compose (carte P1 concurrent §5) ; publication par le build .NET qui lance `npm install` puis `npm run build` (`Web.csproj:42-46`). Aucune CSP sur les pages ; l'`index.html` Angular charge Google Fonts et exécute un script inline (`comp/src/Web/ClientApp/src/index.html:9-18`), qu'une CSP future devrait hacher.

**Nous.** Une image par app, `nginx-unprivileged` (uid 101, port 8080) ou Node en utilisateur `node` (`Dockerfile:80,82`), CSP stricte par framework dans `nginx.conf` (`ours/Src/Web/apps/FrameworkSite/nginx.conf:60-73` : `script-src 'self'`, `frame-ancestors 'none'`, `'wasm-unsafe-eval'` pour WASM seulement), `nosniff`, `Referrer-Policy`, `Cross-Origin-Opener-Policy`. CI vérifie que l'image ne tourne pas en root et n'expose que 8080 (`web.yml:141-144`). Lint, tests et build de tout le workspace en CI (`web.yml:56-66`).

**Verdict : on dépasse (confiance haute).**

### 2.6 Tests des fronts

**Concurrent.** Angular : 3 `it()` dans 2 specs ; React : 0 (carte P1 concurrent D3). Mais il a des tests navigateur : Reqnroll + Playwright, 4 features dont `Login.feature` (connexion valide et invalide) (`comp/tests/Web.AcceptanceTests/Features/`). Les personas notent qu'ils exigent `pwsh` et un téléchargement de navigateurs, non documentés (`out/p2-comp-1.md:86`), et ne couvrent que l'Angular (`out/p3-comp-1.md:58`).

**Nous.** 67 tests du client partagé, tests de pile HTTP sur CSP, nonce, CSRF et relais (`WebAppTests.cs`). Aucun test qui pilote un navigateur : la ligne Playwright est différée (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122). L'app créée par la CLI garde les tests de son gabarit, souvent aucun (persona `out/p3-ours-1.md:40` : vite-react « No test files found »). `verify` ne lance pas `pnpm lint/test/build` (`ours/Tools/Tasks.cs` : seul appel web dans `verify` = `WebLockfiles(repoRoot, check: true)`, ligne 663 ; persona `out/p3-ours-1.md:40`), seul `web.yml` le fait.

**Verdict : en retard sur le test navigateur d'un parcours (confiance haute), en avance sur tout le reste.** Ce n'est pas un choix assumé : c'est un « Later » (#122).

### 2.7 Ajouter une app après la génération

Pas de mécanisme chez le concurrent : le persona a copié 29 fichiers d'une génération jetable et écrit le relais Aspire à la main (`out/p3-comp-1.md:55`). Chez nous, `cleanarch-webapp` + `web-scaffold` écrivent l'app, mais une SPA ou une app SSR demande encore une dizaine d'éditions à la main hors `Src/Web` (compose, `.env.example`, `.dockerignore`, dependabot ×2, codeql, docs), dont 7 qu'aucun test ne contrôle (`out/p3-ours-1.md:40,75` ; `out/p3-ours-2.md:104-110`) ; `web-join` n'existe que pour Blazor WASM et a un défaut ouvert (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/151). **On dépasse (confiance haute)**, avec un reste à finir.

### 2.8 Ce qu'on gagnerait à prendre au concurrent (front-ends)

- **Un test navigateur d'un parcours de connexion**, comme `Login.feature`. Forme chez nous : un projet Playwright .NET dans `Tests/EndToEnd` qui réutilise la pile Compose déjà levée par `end-to-end`, avec un scénario par app générée (connexion, refus, déconnexion, appel protégé), sous le marqueur du framework choisi. Ce qu'il ne faut pas reprendre : Reqnroll (une couche de plus pour 6 scénarios) et l'installation de navigateurs par `pwsh` non documentée. C'est la ligne de #122 : la remonter avant 1.1 si l'équipe veut couvrir la promesse « les règles sont tenues par des tests » côté navigateur. Pas d'ADR nécessaire (ADR 0020 range déjà le risque par type de test) ; mettre à jour la table de l'ADR 0020.
- **Rien d'autre.** Servir la SPA par l'API serait un retour en arrière par rapport à la CSP par app et au relais même origine.

### 2.9 Ce qu'il faut refuser (front-ends)

- **Copier le code des apps dans le template** : c'est ce qui laisse sa release en Angular 21/Karma et main en 22/vitest, et un module SSR mort ; notre template ADR 0003/0008 l'a déjà écarté.
- **NSwag `latest`, plages `^`, `npm install` à la publication** : contraire à l'ADR 0001 (« every dependency at an exact version »).
- **Un client généré non suivi par git** : le front ne compile plus sans le SDK .NET ; ADR 0019 a choisi l'inverse.
- **JSX non typé pour React** : notre `react-ts` est la réponse à « Config react js to ts » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1374).

### 2.10 Ce qu'on peut faire qu'il ne fait pas (front-ends)

- **Mettre en avant ce qu'il refuse encore** : Vue (#1570), Svelte (#1625, en Vite et non SvelteKit chez nous), Blazor (#1444) ; le bon port d'API par construction (relais `API_UPSTREAM`, tenu par `WebAppTests.cs:36`), qui répond à #1594 ; « Can I disable single page applications » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1408) : `--web none` existe chez les deux, mais chez nous l'app s'ajoute après coup.
- **Faire entrer le workspace web dans `verify`** quand `Src/Web` existe : `pnpm lint`, `pnpm test`, `pnpm build`, comme `web.yml`, pour qu'un projet sans CI ait le même gate.
- **Finir `web-join` pour les apps Node** (les 10 éditions imprimées faites par l'outil, comme `presentation-join`), et clore #151.
- **Écrire une ADR « une app web est son propre déployable, jamais servie par l'API »**, avec l'alternative du concurrent et son coût (une image et un relais de plus), pour que la différence de 2.2 soit un choix assumé.

---

## Synthèse des constats

| Id | Axe | Constat | Verdict | Confiance |
|---|---|---|---|---|
| S1 | sécurité | Authentification : 3 modes, refresh, 2FA, passkeys, clés d'accès, politique validée, contre Identity par défaut | on dépasse | haute |
| S2 | sécurité | Repli authentifié + test qui appelle chaque route ; propriété dans la requête ; le concurrent n'a ni repli ni propriétaire | on dépasse | haute |
| S3 | sécurité | Surface anonyme fermée et testée, OpenAPI en dev, limiteur ; le concurrent publie OpenAPI/Scalar partout, sans limiteur | on dépasse | haute |
| S4 | sécurité | En-têtes et CSP testés, CORS fermé ; le concurrent : HSTS seul, CORS `AllowAnyOrigin` | on dépasse ; HSTS absent = choix assumé (ADR 0001) | haute |
| S5 | sécurité | Portail Blazor Server : ni repli d'autorisation ni en-tête de sécurité | défaut interne (pas de pendant chez le concurrent) | haute |
| S6 | sécurité | Secrets : garde-fous (clé dev refusée hors dev) contre Key Vault opt-in Azure | on dépasse (garde-fous) / égalité (coffre) | moyenne |
| S7 | sécurité | Dépendances : audit low + listing + job CI + Dependabot 5 écosystèmes ; concurrent : release 10.8.0 qui ne compile plus (#1675) | on dépasse | haute |
| S8 | sécurité | Même mécanisme que #1675 chez nous, sans sentinelle sur la version publiée ; pas d'audit npm | risque interne | moyenne |
| F1 | front-ends | 7 frameworks issus des CLI officielles épinglées contre Angular/React copiés | on dépasse | haute |
| F2 | front-ends | Front séparé derrière un relais même origine contre SPA servie par l'API | choix différent, non posé en ADR | moyenne |
| F3 | front-ends | Client TS unique committé, codes d'erreur, 67 tests ; Kiota pour .NET ; concurrent NSwag `latest` non suivi | on dépasse | haute |
| F4 | front-ends | SSR Angular/React Router avec nonce et BFF ; concurrent : aucun, module mort | on dépasse ; instance unique = choix assumé (ADR 0024) | haute |
| F5 | front-ends | Images non root, CSP par app, vérifiées en CI ; concurrent : pas d'image, pas de CSP | on dépasse | haute |
| F6 | front-ends | Pas de test navigateur ; le concurrent a Playwright sur la connexion | en retard | haute |
| F7 | front-ends | `verify` ne lance pas le workspace web ; ajout d'app = ~10 éditions à la main | défaut interne | haute |

## Non vérifié

- Le comportement exact de `MapIdentityApi` (énumération à l'inscription, `lockoutOnFailure` à la connexion) et de `SameSite` du cookie d'Identity : défauts du framework, non lus dans le clone, non rejoués.
- La compilation d'un projet généré depuis notre 1.0.0 publiée aujourd'hui (risque S8).
- Les langages analysés par le CodeQL du concurrent (aucun déclaré : détection automatique supposée).
- L'exécution des tests de pile (`WebAppTests`) et des tests d'acceptation du concurrent.
- Le contenu des apps créées par les CLI (pages, tests) : seuls les descripteurs, overlays et rapports de personas ont été lus.
