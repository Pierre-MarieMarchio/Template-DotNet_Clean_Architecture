# P5 G4 (sonnet) : sécurité, front-ends

Analyste : sonnet. Groupe G4, deux axes. Lecture seule : aucun build, aucun test lancé ; `gh` en GET seulement (une tentative `gh api -f` sans `-X GET` sur `/discussions` est partie en POST implicite et a reçu 404 ; rien n'a été écrit, la commande a été refaite avec `-X GET`).
Chemins relatifs à la racine de chaque clone : nous = `repos/ours` (develop, `5f22faa5`), concurrent = `repos/competitor-main` (main, `345e7fe`), release = `repos/competitor-release` (v10.8.0). Le dépôt vivant de develop est à `935d552a` (quatre commits plus récents que le clone) ; les constats GitHub (alertes CodeQL, réglages du dépôt) sont lus sur l'état d'aujourd'hui, 2026-10-30.

## Résumé

1. **Sécurité : on dépasse nettement le concurrent sur ce que le projet généré fait par défaut.** Chez lui, une route sans `RequireAuthorization()` est publique, tout utilisateur connecté lit et modifie tout (ids entiers, aucune propriété), CORS `AllowAnyOrigin`, aucun en-tête de sécurité, aucun rate limit, OpenAPI et Scalar servis dans tous les environnements. Chez nous, repli authentifié testé contre chaque route, propriétaire dans la requête, CSP par défaut `default-src 'none'`, CORS fermé, limiteur global et par adresse sur l'authentification, OpenAPI et Scalar en Development seulement (`Program.cs:170-174`).
2. **Points faibles de notre côté, prouvés** : (a) 11 alertes CodeQL ouvertes sur develop (1 high `js/polynomial-redos`, 10 medium `cs/log-forging`), aucune triée, et secret scanning, push protection et Dependabot security updates désactivés sur le dépôt ; (b) aucun audit des dépendances JavaScript dans la gate (NuGet seul) ; (c) le `docker-compose.yml` et `.env.example` livrent une politique de mot de passe relâchée (8 caractères, aucune classe de caractères) et `ASPNETCORE_ENVIRONMENT=Development` par défaut ; (d) pas de crochet vers un coffre de secrets (le concurrent a Azure Key Vault) ; (e) un jeton d'accès n'est pas révocable avant son expiration et aucune procédure de rotation de `Jwt:Key` n'est écrite.
3. **Front-ends : on dépasse sur le choix (7 valeurs `--web` dont SSR, Blazor WebAssembly, portail Blazor Server, clients de bureau, contre Angular/React), sur le client d'API (committé, contrôlé contre l'API en CI, erreurs typées) et sur la sécurité de livraison (nginx sans privilèges, CSP, images à digest, lockfile gelé).** Le concurrent garde trois avances réelles : des écrans de connexion et d'exemple qui fonctionnent dès la génération (Angular et React), un test navigateur Playwright en CI, et l'orchestration Aspire de la boucle de développement. Chez nous le front est un squelette de l'outil officiel et « les pages sont à écrire » (`docs/features/web-frontend.md`, section Creating the app).
4. **Ce que le concurrent n'a pas et que ses issues réclament** : Vue (#1570), SvelteKit (#1625), Blazor (#1444), `--minimal` et Identity optionnel (#1192, #1613) ; et, dans ses discussions, OAuth2/Google/alternative à Duende (#841, #1037, #1043). Nous livrons les cinq premiers sujets ; le dernier est couvert par `--auth external` et la connexion Google/Microsoft/Apple.
5. Rien n'est à prendre au concurrent qui contredise un de nos choix assumés ; deux idées sont à prendre sous une forme à nous (crochet de coffre de secrets documenté, test navigateur de la connexion, voir plus bas).

---

## Axe 1. Sécurité

### 1.1 Autorisation par défaut et surface anonyme

**Concurrent.**
- Pas de politique de repli : `grep -rn "SetFallbackPolicy\|FallbackPolicy" src` ne rend rien ; `AddAuthorizationBuilder()` est appelé nu (`src/Infrastructure/DependencyInjection.cs:50` et `:65`). La protection est posée groupe par groupe : `groupBuilder.RequireAuthorization();` dans `TodoItems.cs:13`, `TodoLists.cs:13`, `WeatherForecasts.cs:10`, et `.RequireAuthorization()` sur `logout` (`Users.cs:14`). Un nouveau `IEndpointGroup` qui oublie la ligne est public.
- Une seconde couche, dans MediatR : `AuthorizationBehaviour` lit `[Authorize]` sur la requête (`src/Application/Common/Behaviours/AuthorizationBehaviour.cs:21-26`) ; une seule requête porte l'attribut (`GetTodos.cs:9`).
- `app.MapOpenApi(); app.MapScalarApiReference();` sans condition d'environnement (`src/Web/Program.cs:35-36`) : le document et l'interface sont publics en production.
- Contrôle de santé : `/health` et `/alive` seulement en Development (`src/ServiceDefaults/Extensions.cs:105-113`), commentaire du dépôt sur les implications de sécurité.
- Un seul test d'autorisation : `ShouldDenyAnonymousUser` (`tests/Application.FunctionalTests/TodoLists/Queries/GetTodosTests.cs:50-58`, passe par le médiateur, pas par HTTP).

**Nous.**
- Repli authentifié : `.SetFallbackPolicy(... .RequireAuthenticatedUser() ...)` (`Src/Presentation/AppTemplate.Api/Program.cs:113-114`) ; en `--auth none`, repli ouvert et démarrage refusé si un `[Authorize]` subsiste sans `[AllowAnonymous]` (`Program.cs:118` et le bloc qui suit).
- Liste fermée de 12 actions anonymes plus 4 endpoints « fluents » comptés, tenue par test : `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/HttpSurfaceTests.cs:10-26` et `:56-85` (`EveryAnonymousEndpoint_IsOnTheList`).
- Test d'intégration qui appelle chaque route de la table des endpoints sans jeton puis avec un jeton poubelle et exige 401 : `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/DefaultDenyAuthorizationTests.cs:38-76`.
- OpenAPI et Scalar : `if (app.Environment.IsDevelopment()) { app.MapOpenApi().AllowAnonymous(); app.MapScalarApiReference(...) }` (`Program.cs:170-174`). Santé : `/health` et `/health/ready` anonymes en production, sans limiteur (`Common/Hosting/HealthEndpoints.cs:24-33`), réponse minimale.

**Verdict : on dépasse.** Confiance haute. Défaut sûr contre défaut ouvert, avec un test par route contre un seul test.
**À prendre : rien.** Le comportement du concurrent (public sauf marquage) va contre notre promesse 5.
**À refuser :** la seconde couche `[Authorize]` sur le message MediatR : elle suppose le pipeline behaviour que nous avons écarté (ADR 0003), et le concurrent lui-même n'en marque qu'une requête sur neuf.
**Ce qu'on fait et qu'il ne fait pas :** une liste fermée testée, un test de balayage de toutes les routes, un démarrage refusé en mode sans authentification si un `[Authorize]` reste.
**Écart de santé à noter :** notre `/health` anonyme en production est un choix de déploiement (sondes d'orchestrateur, `docs/guides/deployment.md`) ; le concurrent n'a pas de sonde de production du tout.

### 1.2 Propriété des données (IDOR)

**Concurrent.** Aucun propriétaire : `grep -rin "ownerid" src` ne trouve que `CreatedBy` posé par l'intercepteur (`src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs:47-50`). `DeleteTodoItemCommandHandler` fait `FindAsync([request.Id])` puis `Remove` (`src/Application/TodoItems/Commands/DeleteTodoItem/DeleteTodoItem.cs:15-26`) : n'importe quel utilisateur authentifié supprime n'importe quelle ligne par son id entier. `GetTodos` lit toutes les listes sans filtre.

**Nous.** ADR 0009 : le propriétaire est dans chaque prédicat, une ressource d'autrui répond comme une ressource absente (404, jamais 403). Exemple : `.Where(list => list.Id == id && list.OwnerId == ownerId.Value)` (`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Access/Queries/TodoListQueries.cs:40`). Tenu par `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/OwnershipIsolationTests.cs` (lecture, renommage, suppression d'une liste d'autrui : trois tests) et un test par feature possédée.

**Verdict : on dépasse** (propriété et 404 contre ids entiers énumérables). Confiance haute. Le concurrent ne fait pas semblant : son modèle de démonstration est « liste partagée de l'équipe », et n'offre pas l'option de la restreindre.
**À prendre : rien. À refuser :** ajouter `OwnerId` à ses entités serait son affaire ; pour nous, le `--access shared` existe déjà pour le cas partagé.
**Ce qu'on fait et qu'il ne fait pas :** les deux modes (`owner`, `shared`) au générateur, le balayage de suppression de compte (ADR 0012).

### 1.3 Authentification

**Concurrent.** Identity local, en dur, sans option : `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddApiEndpoints()` (`src/Infrastructure/DependencyInjection.cs:52-56` pour la variante API seule avec `AddBearerToken` ; `:57-73` pour la variante SPA avec `AddIdentityCookies`). Les routes viennent de `groupBuilder.MapIdentityApi<ApplicationUser>()` (`src/Web/Endpoints/Users.cs:12`). Aucune option de mot de passe ni de verrouillage dans le code (`grep -rn "IdentityOptions\|Password\." src` ne trouve rien) : les valeurs par défaut du framework s'appliquent. Pas d'OIDC externe, pas de passkeys, pas de double facteur configuré par le template, pas de gestion de sessions. Mot de passe d'administrateur de développement écrit dans le code : `_userManager.CreateAsync(administrator, "Administrator1!")` (`src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:83`), exécuté seulement si `app.Environment.IsDevelopment()` (`Program.cs:17-19`).

**Nous.** Trois modes à la génération (`--auth local|external|none`). Comptes locaux : JWT HS256 de 15 minutes, émetteur, audience, algorithme toujours contrôlés (`Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Sessions/Configuration/Options/ConfigureJwtBearerOptions.cs:35-49`), tampon de sécurité vérifié à chaque requête (`:79-105`), jeton de rafraîchissement opaque stocké haché, famille de jetons par session avec rejeu = révocation (ADR 0013), TOTP, passkeys (ADR 0015), cookie de rafraîchissement `HttpOnly; Secure; SameSite=Strict` lu seulement avec son en-tête (ADR 0016), connexion Google/Microsoft/Apple par jeton d'identité. Politique par défaut : 12 caractères, 4 caractères distincts, chiffre/minuscule/majuscule/symbole, verrouillage 5 essais/15 min, e-mail confirmé obligatoire (`Infrastructure.Auth/Common/Options/IdentityPolicyOptions.cs:12-32`). Mode externe : JWT bearer contre l'autorité, rien ne peut être désactivé (`Presentation/AppTemplate.Api/Features/ExternalIdentity/Configuration/Options/ConfigureExternalJwtBearerOptions.cs:40-48`). Compte d'administrateur de développement : seulement en Development, mot de passe obligatoire depuis les secrets utilisateur, démarrage refusé s'il est vide (`IdentitySeedOptions.cs`, `IdentitySeeder.cs:38-42`).
Test de non-divulgation : `Security/LoginUniformityTests.cs` (inconnu, mauvais mot de passe, non confirmé, verrouillé coûtent le même temps et se lisent pareil).

**Verdict : on dépasse.** Confiance haute sur l'étendue et la tenue par tests ; moyenne sur le coût : nous maintenons nous-mêmes l'émission de jetons, la rotation et les sessions, là où le concurrent délègue à `MapIdentityApi`. La surface de code maison est de l'ordre de `Application.Auth` (42 use cases, carte P1) et `Infrastructure.Auth` ; les 237 + 395 tests la tiennent (carte P1, section 6), mais c'est une charge de revue que le concurrent n'a pas.
**À prendre :** rien du mécanisme. **À refuser :** un mot de passe d'administrateur écrit dans le code source, même en Development : notre amorçage par secret utilisateur est le bon réglage.
**Points faibles à nous, prouvés :**
- *Jeton d'accès non révocable.* `OnTokenValidated` ne contrôle que le tampon de sécurité (`ConfigureJwtBearerOptions.cs:79-105`), pas le `sid` de la session ; `docs/reference/configuration.md:67` l'écrit : « An access token cannot be revoked before it expires ». Une session terminée (`logout`, `DELETE sessions/{id}`) laisse donc son jeton valable jusqu'à 15 minutes (60 dans `docker-compose.yml:23`). Documenté, donc choix, mais aucune ADR ne le porte. Confiance moyenne.
- *Rotation de `Jwt:Key`.* Une seule clé symétrique (`JwtOptions.cs:16`, `ValidAlgorithms = [HmacSha256]`), `SECURITY.md` dit « the key with real entropy, rotated » sans procédure ni fenêtre de recouvrement ; changer la clé déconnecte tout le monde (pas de `kid`, pas de clé précédente : `grep -rn "Previous\|kid" Sessions` vide). Confiance moyenne.
**Ce qu'on fait et qu'il ne fait pas, avec leurs demandes ouvertes :** connexion par fournisseur externe et mode « jetons d'un fournisseur OIDC » : demandes sans réponse dans ses discussions, `Oauth2 suport` (#841, 2023, 0 commentaire, https://github.com/jasontaylordev/CleanArchitecture/discussions/841), `adding Google Auth` (#1037, 2023, 0, …/discussions/1037), `Replace Identity or add free alternative` (#1043, 2023, 1, …/discussions/1043), `Authentication related controller for WebApi` (#892, 3 commentaires, …/discussions/892). Elles datent d'avant son passage à `MapIdentityApi` (PR #1522, mars 2026), mais le template n'a toujours aucun mode externe.

### 1.4 En-têtes, CORS, TLS

**Concurrent.**
- CORS : `app.UseCors(static builder => builder.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin());` (`src/Web/Program.cs:28-31`). Le PR #1402 « Fix/cors : Enable CORS for all environments » l'a généralisé à tous les environnements (https://github.com/jasontaylordev/CleanArchitecture/pull/1402) ; la discussion #572 « CORS issues when deployed with domain name » montre le coût côté déploiement.
- En-têtes de sécurité : aucun (`grep -rn "Content-Security\|X-Content\|nosniff\|X-Frame\|Referrer-Policy" src` hors `node_modules` ne rend rien). Le SPA est servi par l'API (`app.UseFileServer()`, `Program.cs:33`) sans CSP.
- TLS : `UseHttpsRedirection()` partout (`Program.cs:27`) et `UseHsts()` hors Development avec la durée par défaut de 30 jours et un renvoi à la doc de Microsoft (`:22-25`).
- Limiteur de débit, en-têtes transmis (`UseForwardedHeaders`), limites de corps : aucun (`grep -rni "RateLimit\|ForwardedHeaders\|Antiforgery" src` vide).
- Erreurs : `ProblemDetailsExceptionHandler` ; le 404 renvoie `ne.Message` dans `Detail` (`src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:22-28`).

**Nous.**
- En-têtes sur chaque réponse : `nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `X-Powered-By` retiré, CSP `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` par défaut, validée au démarrage (`Src/Presentation/AppTemplate.Api/Common/Security/SecurityHeadersExtensions.cs:60-73` ; `SecurityHeaderOptions.cs:9-12`) ; en-tête `Server` supprimé (`Program.cs:49`). Test : `Security/SecurityHeaderTests.cs`.
- CORS : politique vide tant qu'aucune origine n'est configurée ; chaque origine validée (`CorsExtensions.cs:27-30`, `CorsOptions.cs:31-35`) ; le front passe par un relais same-origin et l'API garde CORS fermé (`docs/features/web-frontend.md`, introduction).
- Limiteur : global et politique `authentication`, par adresse, 429 avec `Retry-After` et corps `rateLimit.exceeded` (`RateLimitingExtensions.cs:20-70`) ; en-têtes transmis acceptés seulement d'un proxy nommé, démarrage refusé si `ReverseProxy:Enabled` sans liste (`ReverseProxyOptions.cs:32-38`).
- TLS : aucune redirection HTTPS, aucun HSTS dans l'API, écrit en toutes lettres : `docs/architecture/request-pipeline.md:30-31`, `docs/guides/deployment.md:112-114`, ligne de l'ADR 0001 (`docs/decisions/0001-adopt-the-generated-baseline.md:63`), et `SECURITY.md` « Before you deploy » n°1.

**Verdict :** en-têtes, CORS, limiteur : **on dépasse** (confiance haute). TLS/HSTS : **choix différent assumé (ADR 0001)**, confiance moyenne : le concurrent met HSTS et la redirection dans l'application, nous les laissons au composant qui termine TLS ; aucune preuve d'un coût utilisateur pour nous (la redirection renverrait un 307 à la sonde de l'orchestrateur, argument de `deployment.md:112-114`) ; le risque inverse est qu'un déploiement oublie l'étape, et `SECURITY.md` la rend obligatoire sans que rien ne la teste.
**À prendre :** rien. **À refuser :** `AllowAnyOrigin` par défaut et `UseHsts` avec la valeur par défaut de 30 jours comme seul réglage.
**À signaler chez nous :** `AllowedHosts` reste `*` dans `appsettings.json:10` (identique au concurrent, `src/Web/appsettings.json`), et la liste « Before you deploy » ne le fait respecter par aucun test (`SECURITY.md:80`).

### 1.5 Secrets et configuration

**Concurrent.** Branchement Azure Key Vault conditionnel : `builder.AddKeyVaultIfConfigured()` lit `AZURE_KEY_VAULT_ENDPOINT` et ajoute `AddAzureKeyVault(new Uri(...), new DefaultAzureCredential())` (`src/Web/DependencyInjection.cs:41-47`, paquets `Azure.Identity` et `Azure.Extensions.AspNetCore.Configuration.Secrets`, `src/Web/Web.csproj`). Chaîne de connexion SQLite en clair dans `appsettings.json:3` (base locale). `AllowedHosts: "*"`. Aucune validation des options au démarrage.

**Nous.** `appsettings.json` suivi par git ne contient aucun secret ; les blancs se fournissent par secrets utilisateur ou variables d'environnement (`appsettings.json:2`). Chaque section est validée au démarrage et nomme la clé fautive : `JwtOptionsValidator` refuse une clé de moins de 32 octets et la clé de développement du template hors Development (`JwtOptions.cs:35-52`) ; `docker-compose.yml:19` exige `JWT_KEY` (`${JWT_KEY:?set JWT_KEY in .env ...}`).

**Verdict :** validation et hygiène : **on dépasse** (haute). Crochet vers un coffre : **en retard, petit** (confiance moyenne) : c'est un vrai point d'appui pour une équipe sur Azure, que nous remplaçons par « variables d'environnement alimentées par le coffre de la plateforme » (`docs/guides/deployment.md:92-106`) sans le dire en une phrase de recette.
**À prendre, sous notre forme :** pas de paquet ni de dépendance Azure dans le socle (cela fixerait un fournisseur, contre notre ligne « un provider par projet, pas de couplage »), mais une section de `docs/guides/deployment.md` qui montre comment brancher `AddAzureKeyVault`, AWS Secrets Manager ou un montage de fichiers de secrets dans `Program.cs`. Pas d'ADR : c'est de la documentation, aucune décision.
**À refuser :** un mot de passe de compte écrit dans le code, la valeur par défaut `Administrator1!` du concurrent.
**À signaler chez nous (prouvé) :** `.env.example:112-116` et `docker-compose.yml:27-32` livrent `IDENTITY_PASSWORD_LENGTH=8`, aucune classe de caractères exigée, verrouillage à 10 essais pour 1 minute (`:34-35`), `ASPNETCORE_ENVIRONMENT=Development` par défaut (`.env.example:26`, `docker-compose.yml:7`) et un secret S3 `app-template-dev-secret` en repli (`docker-compose.yml:56`). L'en-tête du fichier dit « Local stack », donc ce n'est pas un défaut de production ; mais aucun test ne tient que cette pile locale ne sert pas de modèle de production, et `deployment.md` ne met pas en garde contre elle. Confiance moyenne.

### 1.6 Dépendances vulnérables

**Concurrent.** `Directory.Packages.props` avec épinglage transitif (`CentralPackageTransitivePinningEnabled`, ligne 5) et un épinglage de sécurité `System.Security.Cryptography.Pkcs` 10.0.14 avec la référence des deux avis GHSA (`Directory.Packages.props:33-34`) ; cet épinglage n'est **pas** dans le tag v10.8.0 (`grep Pkcs competitor-release/Directory.Packages.props` vide). `TreatWarningsAsErrors` (`Directory.Build.props:5`), aucun réglage `NuGetAudit` explicite. Aucune vérification de dépendances en CI (`build.yml`, carte P1). Renovate : le lundi, `automerge: true` pour minor/patch/digest et pour les actions (`renovate.json:22,30`). Dernière release : 2026-03-23. Conséquence visible : issue ouverte « 10.8.0 now fails to build because of NU1902/NU1903 security advisories » (#1675, https://github.com/jasontaylordev/CleanArchitecture/issues/1675), 118 erreurs de build sur un projet fraîchement généré, parce que le correctif existe sur `main` mais qu'aucune release ne l'a emporté.

**Nous.** `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low` avec `TreatWarningsAsErrors` (`Directory.Build.props:15-23`), étape de CI `dotnet list package --vulnerable --include-transitive` dont le rapport est lu car la commande sort 0 quoi qu'elle trouve (`.github/workflows/ci.yml:257-265`), repris dans `verify` (`Tools/Tasks.cs:2029-2045`) ; règle écrite : réponse par un épinglage transitif daté, jamais par un niveau d'audit relâché (`docs/guides/build-and-ci.md:161-165`). Dependabot sur cinq écosystèmes : nuget, github-actions, docker, npm (`/Src/Web`), docker-compose (`.github/dependabot.yml:5,79,102,140,178`). Actions et images épinglées par SHA et digest.

**Verdict :**
- *Détection et épinglage* : **on dépasse** (haute).
- *Exposition au même accident que #1675* : **à égalité** (haute). Notre projet généré a `TreatWarningsAsErrors` + `NuGetAuditLevel=low` : un avis publié après la release casse la compilation d'un projet fraîchement généré, et aucun workflow planifié ne regénère le projet par défaut (`grep -rn cron .github/workflows` : seul `codeql.yml:10`). Pire, notre release est actuellement en échec (dernier run `release.yml` : `failure`, 2026-10-28, voir G3). La garde que nous avons contre le cas du concurrent est le Dependabot hebdomadaire, qui n'agit pas sur un paquet déjà publié.
- *JavaScript* : **en retard sur notre propre règle** (haute). `SECURITY.md` écrit « Every dependency pinned and audited; any known advisory fails the build » ; `grep -rni "pnpm audit\|npm audit" docs .github Tools` ne trouve rien : seul NuGet est audité dans la gate ; le front n'a que le lockfile gelé, Dependabot et CodeQL. Le concurrent n'audite pas non plus ses paquets npm.
**À prendre :** rien du concurrent ; en interne, une étape `pnpm audit --prod` (ou équivalent pnpm 11) dans `web.yml`, avec la même règle « jamais de relâchement ». **À refuser :** l'automerge des mises à jour non majeures sans test navigateur ni audit (la configuration de `renovate.json`), et le décalage entre `main` et le paquet publié que #1675 illustre.
**Ce qu'on peut faire et qu'il ne fait pas :** un workflow planifié qui génère le projet par défaut depuis le paquet publié et le compile, avec ouverture d'une issue : aucune issue ni discussion du concurrent ne propose cela, mais #1675 en est le cas d'école. À concevoir avec G3.

### 1.7 Processus de sécurité du dépôt

**Concurrent.** Pas de `SECURITY.md` (`ls SECURITY.md` : absent ; `gh api repos/.../contents/SECURITY.md` : 404), signalement privé de vulnérabilité désactivé (`gh api repos/jasontaylordev/CleanArchitecture/private-vulnerability-reporting` : `{"enabled":false}`). CodeQL actif sur push, PR et chaque lundi (`.github/workflows/codeql.yml:3-14`), sans choix de langage (détection automatique). Alertes ouvertes : non lisibles (HTTP 403 sans droit d'administration).

**Nous.** `SECURITY.md` du projet généré : tableau des protections reliées à leurs pages, liste « Before you deploy » (obligatoire et recommandé), section de signalement à remplacer avant le premier déploiement (`SECURITY.md:5-12`). Signalement privé activé sur notre dépôt (`{"enabled":true}`). CodeQL en C# et en JavaScript/TypeScript, hebdomadaire, plus l'analyse des workflows côté template (`.github/workflows/codeql.yml:10-50`).

**Verdict : on dépasse** sur la politique (haute). **Mais en retard sur la tenue** (haute sur les faits, moyenne sur la gravité) :
- 11 alertes CodeQL ouvertes sur develop (`gh api .../code-scanning/alerts?state=open`) : une `js/polynomial-redos` classée high (`Src/Web/packages/api-client/src/session.ts:50`, `(options.baseUrl ?? '').replace(/\/+$/, '')`, alerte n°1) et dix `cs/log-forging` classées medium (`Common/Errors/GlobalExceptionHandler.cs:58,71,72,82,83,92,93`, `Common/Idempotency/IdempotencyFilter.cs:51,52`, `Common/Observability/RequestLoggingMiddleware.cs:24`, alertes n°2 à n°11), toutes créées le 2026-10-22, aucune fermée ni commentée. Les deux familles se discutent : `baseUrl` vient du développeur, et le journal est un formateur JSON (`Program.cs:47`) qui échappe les retours à la ligne. Mais « ouvert sans réponse » contredit notre règle de ne pas laisser un diagnostic sans suite (`AGENTS.md`, « Never silence a diagnostic »), et un adopteur qui active l'onglet Security voit 11 alertes.
- Sur notre dépôt, `secret_scanning`, `secret_scanning_push_protection` et `dependabot_security_updates` sont `disabled` (`gh api repos/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture`, champ `security_and_analysis`). Le dépôt est public.
**À prendre :** rien du concurrent. **À faire chez nous :** corriger ou fermer avec justification les 11 alertes ; activer les trois réglages ; faire que `verify` ou CI échoue sur une alerte CodeQL de gravité high ouverte (une règle d'un type de test, ADR 0020).

### 1.8 Sécurité : ce qui reste

- **Limitation de débit sur l'authentification** : on dépasse (haute), voir 1.4.
- **Journaux** : nous ne journalisons ni en-tête `Authorization`, ni cookies, ni requête, ni corps (`Security/RequestLogRedactionTests.cs`, `SECURITY.md`), le concurrent journalise l'identifiant d'utilisateur et le nom de la requête dans `LoggingBehaviour`/`PerformanceBehaviour` (`src/Application/Common/Behaviours/LoggingBehaviour.cs:24`, `PerformanceBehaviour.cs:40`) sans règle de rédaction ; non prouvé comme défaut, simplement non tenu. Confiance moyenne.
- **Fichiers téléversés** : le concurrent n'en a pas (aucune fonction de fichiers) ; nous avons des fichiers à clé aléatoire, inspectés par leurs octets, servis par jetons étroits (ADR 0018). À égalité par absence de comparaison.

---

## Axe 2. Front-ends

### 2.1 Frameworks proposés

**Concurrent.** `--client-framework angular|react|none` (`.template.config/template.json:228-258`). Angular 22 avec modules (`@NgModule` : `ClientApp/src/app/app.module.ts:27`, `main.ts:19` `bootstrapModule(AppModule)`), React 19 + Vite 8 en `.jsx` non typé (`ClientApp-React/src/App.jsx`, `main.jsx`). Aucune audience, aucun rendu côté serveur : `ClientApp/src/app/app.server.module.ts` et `@angular/platform-server` sont livrés (`ClientApp/package.json:23`) mais `angular.json` n'a ni `ssr` ni `server` (`grep -n '"ssr"\|"server"\|prerender' angular.json` vide) et `main.ts` ne démarre que le navigateur : code mort. Pas de Blazor, Vue, Svelte.

**Nous.** `--web none|placeholder|angular|vite-react|vite-vue|vite-svelte|react-router|blazor-wasm` (carte P1, section 2), `--web-audience public|admin`, `--web-rendering spa|ssr` (SSR pour Angular et React Router seulement, les autres combinaisons refusées à la génération : `.template.config/template.json:86`, `:457-467`), `--presentation api|blazor-server|mcp|avalonia|wpf|worker`. Angular 22.2.3 par `ng new --standalone --ssr=false --test-runner=vitest` (`.template.config/starters/angular/FrameworkSite/scaffold.json`), Vite par `create-vite 9.3.1`, React Router par `create-react-router 8.5.0`, Blazor WebAssembly par `dotnet new blazorwasm` du paquet `10.0.14` (kind `dotnet`).

**Verdict : on dépasse.** Confiance haute. **Demandes ouvertes du concurrent que nous livrons déjà :** Vue (#1570, 2 commentaires, https://github.com/jasontaylordev/CleanArchitecture/issues/1570), SvelteKit en SPA (#1625, https://github.com/jasontaylordev/CleanArchitecture/issues/1625 ; nous livrons `vite-svelte`, pas SvelteKit : notre #119 le met « Later »), Blazor (#1444, https://github.com/jasontaylordev/CleanArchitecture/issues/1444, qui regroupe #1010, #1054, #1094 : nous livrons `blazor-wasm` et `blazor-server`). Le mainteneur y répond « not planned right now » pour Vue et SvelteKit.
**À prendre :** rien. **À refuser :** NgModule et JSX non typé, hérités d'avant le défaut d'Angular et de Vite ; notre règle « issus des générateurs officiels épinglés » (template ADR 0003 et 0008) donne au projet le défaut courant des outils.

### 2.2 D'où vient le code du front : démonstration livrée, ou squelette officiel

**Concurrent.** Une application de démonstration complète dans le dépôt : connexion, inscription, liste de tâches, météo, thème clair/sombre, dans les deux frameworks (`ClientApp/src/api-authorization/{login,register,auth.guard.ts,authorize.interceptor.ts}`, `ClientApp-React/src/components/{Todo,Weather,Counter}.jsx` et `components/api-authorization/{AuthContext,LoginPage,RegisterPage,ProtectedRoute}.jsx`). Le front est construit et testé en CI avec le projet (`.github/workflows/test-templates.yml:45-69`, `npm ci && npm run build`, six scénarios Playwright dans `tests/Web.AcceptanceTests`).

**Nous.** Un squelette : `web-scaffold` appelle l'outil officiel à une version épinglée, pose un relais `/api` et la dépendance au client, installe, et s'arrête (« From then on the app is this project's own: `web-scaffold` leaves it alone, and the pages are yours to write », `docs/features/web-frontend.md`, section Creating the app). Les starters ne contiennent aucune page de connexion : `grep -rli "login\|sign-in" .template.config/starters` ne rend que des fichiers de `blazor-wasm` (qui gère la connexion externe) ; les overlays Angular, Vite, React Router se réduisent à `vite.config.ts`, `proxy.conf.json`, `react-router.config.ts` (et la variante SSR). La page de documentation donne un exemple de connexion à copier (`web-frontend.md`, « Signing in »).

**Verdict : choix assumé (template ADR 0003 « framework apps come from official CLIs », 0008 « a web app comes from a starter »).** Confiance moyenne : la promesse « point de départ conservé, pas démo » (étalon) le justifie, mais **un coût pour l'utilisateur est identifiable** : avec `--auth local`, le premier écran qui s'identifie est à écrire ; le concurrent l'écarte pour ses deux frameworks, et son mainteneur l'a jugé, quand on lui a demandé des pages de connexion React personnalisables, « considerable effort » avec « complexity to the template » (#1236, https://github.com/jasontaylordev/CleanArchitecture/issues/1236 ; il a livré ces pages ensuite avec PR #1522, mars 2026). Notre propre extrait de connexion à copier n'est tenu par aucun test de bout en bout contre une vraie API : `WebAppTests.cs` ne vérifie que le relais, la CSP/nonce, le CSRF et le rappel de connexion (`Tests/EndToEnd/AppTemplate.EndToEnd.Tests/WebAppTests.cs:34-99`), `session.test.ts` utilise un faux `fetch`.
**À prendre, sous notre forme :** pas de pages livrées (cinq frameworks à maintenir, et c'est ce que le mainteneur refusait), mais un **test de bout en bout de la recette** : la page de connexion minimale de la documentation, générée dans une pile de test, qui se connecte, rafraîchit, se déconnecte en mode cookie. Un type de test, un risque (ADR 0020), sans ADR nouvelle. C'est aussi l'item « Playwright » de notre #122 (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122).
**À refuser :** livrer un écran de démonstration par framework : il vieillirait avec chaque version du générateur, ce que ADR 0008 évite en ne gardant « no framework code ».

### 2.3 Intégration avec l'API et client généré

**Concurrent.** Client TypeScript produit par NSwag à chaque `prestart`/`prebuild` depuis `wwwroot/openapi/v1.json`, lui-même écrit par le build .NET (`ClientApp/package.json:5-10`, `ClientApp/nswag.json`, `Web.csproj` avec `Microsoft.Extensions.ApiDescription.Server`) ; `"nswag": "latest"` non épinglé (`ClientApp/package.json:38`, `ClientApp-React/package.json:22`) ; sortie ignorée par git (`.gitignore:43-44`, `git ls-files | grep -i "web-api-client\|openapi"` vide), donc aucune dérive détectable ; modèle de classes avec un `Client` par contrôleur, `withCredentials: true` posé par un intercepteur Angular (`api-authorization/authorize.interceptor.ts:14`) ; le React importe un `.ts` depuis du `.jsx` (`Todo.jsx:3`). Cookies Identity pour le SPA. Issue ouverte : le front Angular lancé par Aspire utilise sa propre URL au lieu de celle de l'API (#1594, https://github.com/jasontaylordev/CleanArchitecture/issues/1594).

**Nous.** `@app-template/api-client` : `openapi-fetch` 0.17.0 sur des types `openapi-typescript` 7.13.0, versions exactes (`Src/Web/packages/api-client/package.json`), `schema.ts` committé, regénéré par `pnpm api` et **contrôlé en CI** (`.github/workflows/web.yml:68-74` : `pnpm api` puis `git diff --exit-code`), `unwrap` qui lève une `ApiError` portant le `code` du problème et ses erreurs de champs (`src/index.ts`, `problem-details.ts`). Session de navigateur : jeton d'accès en variable, rafraîchissement unique partagé, relecture à la volée (`src/session.ts:55-120`). Client .NET Kiota pour Blazor WASM, Avalonia, WPF (ADR 0023). Le relais same-origin garde CORS fermé (`nginx.conf:40-47`).

**Verdict : on dépasse** (haute) : reproductibilité, détection de dérive, contrat d'erreurs, un seul client pour tous les frameworks (ADR 0019). **Avantage du concurrent, réel mais étroit :** un client orienté classes avec `HttpClient` d'Angular (injection native) ; nous avons un client fonctionnel qui s'insère dans n'importe quel framework (Angular y compris), donc moins idiomatique en Angular. Confiance moyenne.
**À prendre :** rien. **À refuser :** générer le client au `prebuild` sans le committer, ni le laisser en `latest`.
**Ce qu'on fait et qu'il ne fait pas :** contrôle de dérive, erreurs typées par code stable, `OpenApiDocumentTests`.

### 2.4 Rendu côté serveur et sessions

**Concurrent.** Pas de SSR en pratique (voir 2.1).

**Nous.** ADR 0024 : un app rendu côté serveur tourne sur son propre serveur Node ; nonce CSP de 128 bits par réponse, `Cache-Control: no-store`, jetons gardés sur le serveur, un seul cookie `__Host-session` (`HttpOnly; Secure; SameSite=Lax`, `Src/Web/packages/api-client/src/server/store.ts:51`), en-tête `X-CSRF: 1` exigé pour toute requête qui change l'état (`store.ts:160-166`), client OIDC côté serveur avec PKCE, état et nonce (`server/provider.ts:142-198`). Tests : `WebAppTests` ci-dessus.
**Limite connue et écrite :** sessions en mémoire du serveur, donc une instance et déconnexion au redémarrage (`docs/features/web-frontend.md:398-399` ; alternatives refusées dans l'ADR 0024). `NG_ALLOWED_HOSTS` doit être complété au déploiement (`web-frontend.md:395-397`).

**Verdict : on dépasse** (haute), avec une limite d'échelle assumée (ADR 0024). **À prendre :** rien. **Ce qu'on fait et qu'il ne fait pas :** SSR sécurisé avec jetons hors du navigateur ; aucune issue ni discussion du concurrent n'en demande.

### 2.5 Build, livraison et sécurité des applications front

**Concurrent.** Le SPA est servi par l'API (`UseFileServer`, `MapFallbackToFile("index.html")`, `Program.cs:33,49`), sans CSP ni en-têtes. À la publication, `<Exec Command="npm install" />` puis le build (`src/Web/Web.csproj`, cible `PublishRunWebpack`) : `npm install`, pas `npm ci`, donc pas de lockfile gelé à la livraison ; la CI du template utilise `npm ci` (`test-templates.yml:55-56`). `package.json` autorise les scripts d'installation de `@parcel/watcher`, `esbuild`, `lmdb`, `msgpackr-extract` (`ClientApp/package.json:48-53`). Pas de Dockerfile, pas de compose. Renovate fusionne automatiquement les mises à jour non majeures (`renovate.json:22`).

**Nous.** Une image par application, nginx sans privilèges ou serveur Node, port 8080, `server_tokens off`, CSP `default-src 'self'; ... script-src 'self'; connect-src 'self'` plus `nosniff`, `Referrer-Policy`, `Cross-Origin-Opener-Policy` (`Src/Web/apps/FrameworkSite/nginx.conf:60-72`), hachages SHA-256 calculés à la construction pour les scripts en ligne de React Router et de Blazor (`Dockerfile`, étape `HASHES`), jamais `'unsafe-inline'` pour les scripts (le style en ligne d'Angular est la seule exception, `nginx.conf:65`, ADR 0008). Images de base à digest, `pnpm install --frozen-lockfile` (`Dockerfile`, `web.yml:56-57`), `allowBuilds` refusé pour les quatre mêmes paquets (`Src/Web/pnpm-workspace.yaml`), CI qui vérifie que chaque image s'exécute sans privilèges et n'expose que 8080 (`web.yml:141-155`), CodeQL JavaScript/TypeScript, Dependabot npm.

**Verdict : on dépasse** (haute). **À prendre :** rien. **À refuser :** `npm install` à la publication (non reproductible) et l'automerge sans test.
**À signaler chez nous :** pas d'audit des paquets JS dans la gate (voir 1.6) ; `style-src 'unsafe-inline'` pour Angular.

### 2.6 Boucle de développement (Aspire) et tests de navigateur

**Concurrent.** Un hôte Aspire (`src/AppHost/Program.cs:39-41` : `AddJavaScriptApp(Services.WebFrontend, "./../Web/ClientApp").WithReference(web)`) lance le SPA, l'API et la base avec découverte de services et tableau de bord ; le SPA lit l'adresse de l'API dans `services__webapi__https__0` (`ClientApp/proxy.conf.js:3-5`). Le bogue ouvert #1594 est le revers (nom de variable divergent). Playwright + Reqnroll : quatre `.feature`, six scénarios (connexion valide et invalide, accueil, compteur, météo protégée), lancés par `dotnet test` en CI (`tests/Web.AcceptanceTests`).

**Nous.** Docker Compose pour la pile, `pnpm --filter <app> dev` avec relais vers `API_UPSTREAM` par défaut sur l'hôte (`web-frontend.md`, « Running it »). Aspire et Playwright sont des items de notre #122 (« Later », 0 commentaire). Nos tests de front : cinq fichiers de test dans `api-client` (`Src/Web/packages/api-client/test/`) et `WebAppTests` en bout en bout sur la pile Compose ; aucun test qui pilote un navigateur.

**Verdict : en retard** (aspects de confort : Aspire ; aspects de preuve : tests de navigateur), mais **retard déjà inscrit à la feuille de route** (#122). Confiance moyenne : Aspire n'est pas dans les promesses de l'étalon ; le test navigateur de la connexion est, lui, dans « les règles sont tenues par des tests ».
**À prendre :** le test de navigateur de la connexion locale (voir 2.2). **À refuser :** un hôte Aspire livré par défaut tant que la pile Compose reste l'artefact de déploiement : deux orchestrations à tenir en cohérence.

### 2.7 Défauts propres au front, prouvés

- `web-join` refuse une application WebAssembly qui a créé son propre espace de travail : issue #151, https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/151 (0 commentaire).
- Chemins générés de 176 caractères : une arborescence profonde sous Windows ne se commit pas : #166, https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/166 (0 commentaire).
- `bootstrap` exige le réseau pour récupérer chaque CLI officielle (`web-frontend.md`, section Creating the app) : un dépôt fraîchement cloné n'a pas d'application front à lire ni à construire avant cette étape ; le job `web-apps` échoue avec « Src/Web/apps holds no app » (`web.yml:93-96`) si on oublie de committer le résultat.

## Tableau récapitulatif

| Axe | Sujet | Verdict | Confiance |
|---|---|---|---|
| Sécurité | Autorisation par défaut, surface anonyme | on dépasse | haute |
| Sécurité | Propriété des données | on dépasse | haute |
| Sécurité | Authentification (modes, sessions, 2FA, passkeys) | on dépasse | haute |
| Sécurité | Jeton d'accès non révocable ; rotation de clé | retard (point faible à nous) | moyenne |
| Sécurité | En-têtes, CORS, limiteur | on dépasse | haute |
| Sécurité | TLS/HSTS dans l'application | choix assumé (ADR 0001) | moyenne |
| Sécurité | Secrets : validation | on dépasse | haute |
| Sécurité | Secrets : crochet de coffre (Key Vault) | retard, petit | moyenne |
| Sécurité | Pile locale aux valeurs relâchées | retard (point faible à nous) | moyenne |
| Sécurité | Avis NuGet : détection | on dépasse | haute |
| Sécurité | Avis NuGet : exposition type #1675 | égalité | haute |
| Sécurité | Audit JavaScript | retard sur notre règle | haute |
| Sécurité | Politique de signalement | on dépasse | haute |
| Sécurité | Alertes CodeQL ouvertes, réglages du dépôt | retard (défaut à nous) | haute sur les faits, moyenne sur la gravité |
| Front | Frameworks proposés | on dépasse | haute |
| Front | Squelette officiel contre démonstration livrée | choix assumé (template ADR 0003, 0008), coût identifié | moyenne |
| Front | Client d'API | on dépasse | haute |
| Front | SSR et sessions | on dépasse (limite d'échelle assumée, ADR 0024) | haute |
| Front | Build et sécurité des applications | on dépasse | haute |
| Front | Aspire et tests navigateur | retard déjà inscrit (#122) | moyenne |

## Ce qui n'a pas été vérifié

- Les alertes de code scanning et de Dependabot du concurrent (HTTP 403) : on ne peut pas comparer l'état de ses alertes à celui des nôtres.
- Le comportement d'exécution de `MapIdentityApi` (verrouillage à la connexion, règles de mot de passe par défaut) : lu dans le code du concurrent, non exécuté ; les valeurs par défaut du framework sont supposées, pas observées.
- Que nos 11 alertes CodeQL sont des faux positifs : lecture du code seulement, aucun test d'exploitation.
- Le rendu réel des CSP dans un navigateur, pour nous comme pour le concurrent (`WebAppTests` n'a pas été lancé).
- Le contenu du dossier `Src/Web/apps/*` d'un projet généré avec chaque framework (les starters sont lus, pas exécutés ; aucune génération).
- La version publiée du paquet du concurrent (nuget.org) : jugée par son tag v10.8.0 et par #1675.
