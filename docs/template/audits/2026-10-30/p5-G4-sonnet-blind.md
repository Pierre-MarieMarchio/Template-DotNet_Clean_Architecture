# Dossier anonymisé G4 : sécurité, front-ends

Deux dépôts de template .NET 10 (Clean Architecture), nommés Dépôt A et Dépôt B. Chacun produit un projet .NET avec un hôte HTTP, de l'authentification et au moins un front-end. Les faits viennent de la lecture des deux arbres et de l'API GitHub (lecture seule) à la date du 2026-10-30 ; aucun build ni test n'a été lancé. Pour chaque point, A est décrit d'abord, puis B, dans le même ordre et avec les mêmes rubriques. Les noms de projets, de packages et d'outils maison sont remplacés par ProjA, ProjB, « l'outil de A », « l'outil de B ».

## Axe 1. Sécurité

### 1.1 Autorisation par défaut et surface anonyme

Dépôt A
- Aucune politique de repli : `AddAuthorizationBuilder()` est appelé sans `SetFallbackPolicy` (recherche de `FallbackPolicy` dans les sources : aucune occurrence).
- La protection est posée par groupe de routes :
  ```csharp
  public static void Map(RouteGroupBuilder groupBuilder)
  {
      groupBuilder.RequireAuthorization();
  ```
  Trois groupes sur quatre portent l'appel (listes de tâches, éléments, météo) ; la route de déconnexion porte `.RequireAuthorization()` elle-même ; le groupe des routes d'identité est exposé par `MapIdentityApi`.
- Une seconde couche dans le pipeline MediatR : un comportement lit l'attribut `[Authorize]` sur la requête (rôles, politiques) ; une requête sur neuf porte l'attribut.
- OpenAPI et Scalar sont mappés sans condition d'environnement :
  ```csharp
  app.MapOpenApi();
  app.MapScalarApiReference();
  ```
- Les routes de santé sont mappées seulement quand `IsDevelopment()` est vrai, avec un commentaire du dépôt sur les implications de sécurité en production.
- Un test d'autorisation : un utilisateur anonyme est refusé sur la requête qui porte `[Authorize]` (envoyée par le médiateur, sans HTTP).

Dépôt B
- Politique de repli qui exige un utilisateur authentifié :
  ```csharp
  builder.Services.AddAuthorizationBuilder()
      .SetFallbackPolicy(new AuthorizationPolicyBuilder()
          .RequireAuthenticatedUser()
          .Build());
  ```
  Quand le projet est généré sans authentification, le repli laisse tout passer et le démarrage est refusé si un `[Authorize]` subsiste sans `[AllowAnonymous]`.
- Une liste fermée de 12 actions `[AllowAnonymous]` plus 4 endpoints anonymes déclarés par l'appel fluent, comptés à part, est lue par un test d'architecture qui échoue si une action s'ajoute.
- Un test d'intégration appelle chaque route de la table des endpoints, sans jeton puis avec un jeton invalide, et attend 401 pour chaque route non marquée anonyme.
- OpenAPI et Scalar sont mappés dans un bloc `if (app.Environment.IsDevelopment())`. Les routes `/health` et `/health/ready` sont anonymes dans tous les environnements, sans limiteur de débit, avec une réponse minimale.
- Pas de seconde couche d'autorisation sur les messages : les contrôleurs portent l'attribut, avec une politique nommée `Administrator` sur cinq d'entre eux.

### 1.2 Propriété des données

Dépôt A
- Les entités n'ont pas de propriétaire ; l'intercepteur d'audit renseigne `CreatedBy` et `LastModifiedBy`.
- Les identifiants sont des entiers. La suppression d'un élément :
  ```csharp
  var entity = await _context.TodoItems.FindAsync([request.Id], cancellationToken);
  Guard.Against.NotFound(request.Id, entity);
  _context.TodoItems.Remove(entity);
  ```
- La requête de lecture des listes ne filtre pas par utilisateur.

Dépôt B
- Chaque requête et chaque écriture d'une feature possédée filtre par propriétaire dans le même prédicat :
  ```csharp
  .Where(list => list.Id == id && list.OwnerId == ownerId.Value)
  ```
  Une ressource d'autrui répond comme une ressource absente (404).
- Les identifiants sont des GUID. Un test d'intégration vérifie qu'une liste d'autrui n'est ni lisible, ni renommable, ni supprimable ; chaque feature possédée a son test.
- Le générateur du projet offre deux modes : `owner` (filtre par propriétaire) et `shared` (tous les utilisateurs authentifiés voient tout).

### 1.3 Authentification

Dépôt A
- Identity local, sans choix d'option à la génération. Deux variantes selon le front choisi :
  ```csharp
  builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme);   // API seule
  builder.Services.AddAuthentication(...).AddIdentityCookies();                          // avec SPA
  builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
      .AddEntityFrameworkStores<ApplicationDbContext>().AddApiEndpoints();
  ```
- Les routes viennent de `MapIdentityApi<ApplicationUser>()`.
- Aucune configuration de `IdentityOptions` (mot de passe, verrouillage) dans le code ; les valeurs par défaut du framework s'appliquent.
- Pas de fournisseur OpenID Connect externe, pas de passkeys ni de double facteur configuré par le template.
- Un compte administrateur est créé en Development avec un mot de passe écrit dans le code source :
  ```csharp
  await _userManager.CreateAsync(administrator, "Administrator1!");
  ```

Dépôt B
- Trois modes choisis à la génération : comptes locaux, jetons d'un fournisseur OpenID Connect externe, aucune authentification.
- Comptes locaux : JWT signé HS256, 15 minutes, émetteur, audience et algorithme toujours contrôlés, avec 30 secondes de tolérance d'horloge ; le tampon de sécurité du compte est vérifié à chaque requête :
  ```csharp
  ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
  ...
  if (await signInManager.ValidateSecurityStampAsync(context.Principal) is null)
      context.Fail("This token's security stamp is no longer valid.");
  ```
- Jeton de rafraîchissement opaque, stocké haché ; une famille de jetons par session ; la présentation d'un jeton déjà consommé révoque toutes les sessions du compte. Double facteur TOTP, passkeys WebAuthn, connexion par jeton d'identité de trois fournisseurs.
- Politique par défaut : 12 caractères, 4 caractères distincts, chiffre, minuscule, majuscule et symbole requis, verrouillage après 5 essais pour 15 minutes, e-mail confirmé requis.
- Un cookie de rafraîchissement (`HttpOnly`, `Secure`, `SameSite=Strict`) est lu seulement avec un en-tête dédié.
- Le compte administrateur de développement n'est créé qu'en Development, avec un mot de passe lu des secrets utilisateur ; un mot de passe vide fait échouer le démarrage.
- Un jeton d'accès reste valable jusqu'à son expiration après la fin de sa session ; la page de référence des réglages le dit. La clé de signature est unique ; aucune procédure de rotation n'est écrite.
- Un test compare la réponse à une connexion pour un compte inconnu, un mauvais mot de passe, une adresse non confirmée et un compte verrouillé.

### 1.4 En-têtes, CORS, TLS

Dépôt A
- CORS :
  ```csharp
  app.UseCors(static builder => builder.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin());
  ```
  Une PR du dépôt l'a généralisé à tous les environnements.
- Aucun en-tête de sécurité n'est posé par le code (recherche de `Content-Security`, `nosniff`, `X-Frame`, `Referrer-Policy` : aucune occurrence). Le SPA est servi par l'API via `UseFileServer()`.
- `UseHttpsRedirection()` partout ; `UseHsts()` hors Development avec la durée par défaut (30 jours).
- Pas de limiteur de débit, pas de configuration des en-têtes transmis, pas d'antiforgery (recherche : aucune occurrence).
- Le gestionnaire d'erreurs place le message de l'exception dans `Detail` pour un 404.

Dépôt B
- Sur chaque réponse : `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `X-Powered-By` retiré, et une CSP par défaut :
  ```csharp
  "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"
  ```
  L'en-tête `Server` de Kestrel est supprimé.
- CORS : la politique n'autorise rien tant qu'aucune origine n'est configurée ; chaque origine configurée est validée au démarrage ; le front passe par un relais de même origine.
- Limiteur de débit : un plafond global et une politique dédiée à l'authentification, par adresse cliente ; réponse 429 avec `Retry-After` et un code d'erreur stable. Les en-têtes transmis ne sont acceptés que d'un proxy nommé ; le démarrage est refusé si le proxy est activé avec des listes vides.
- Pas de redirection HTTPS ni de HSTS dans l'application ; la documentation écrit que la terminaison TLS et ces deux réglages appartiennent au composant placé devant l'API, et la liste « avant de déployer » le rend obligatoire.
- `AllowedHosts` vaut `*` dans les réglages livrés.

### 1.5 Secrets et configuration

Dépôt A
- Un branchement Azure Key Vault conditionnel :
  ```csharp
  var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
  if (!string.IsNullOrWhiteSpace(keyVaultUri))
      builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
  ```
- Une chaîne de connexion SQLite figure dans le fichier de réglages suivi par git. `AllowedHosts` vaut `*`. Les options ne sont pas validées au démarrage.

Dépôt B
- Le fichier de réglages suivi par git ne contient pas de secret ; les valeurs vides se fournissent par secrets utilisateur ou variables d'environnement. Chaque section de réglages est validée au démarrage et le message nomme la clé fautive :
  ```csharp
  else if (!environment.IsDevelopment() && options.Key.StartsWith("dev-only", StringComparison.Ordinal))
      failures.Add("'Jwt:Key' is the development key the template ships, which is public. ...");
  ```
- Le fichier Docker Compose exige `JWT_KEY` (`${JWT_KEY:?set JWT_KEY in .env ...}`). Il porte aussi, pour la pile locale, une politique de mot de passe de 8 caractères sans classe de caractères requise, un verrouillage de 10 essais pour 1 minute, `ASPNETCORE_ENVIRONMENT` à Development par défaut et un secret S3 de repli de développement. L'en-tête du fichier dit « Local stack ».
- Pas de branchement vers un coffre de secrets dans le code ; la documentation indique les variables d'environnement.

### 1.6 Dépendances vulnérables

Dépôt A
- Gestion centrale des versions avec épinglage transitif. Un épinglage de sécurité de `System.Security.Cryptography.Pkcs` (10.0.14) cite deux avis GHSA ; il figure sur la branche principale et pas dans le tag de la dernière release.
- `TreatWarningsAsErrors` à `true` ; aucun réglage `NuGetAudit` explicite. Pas d'étape de CI qui liste les paquets vulnérables.
- Bot de mises à jour : exécution le lundi, fusion automatique des mises à jour mineures, correctifs et digests, ainsi que des actions GitHub.
- Dernière release le 2026-03-23. Une issue ouverte (créée le 2026-10-28) rapporte que le projet généré depuis le paquet publié échoue à la compilation avec 118 erreurs `NU1902/NU1903`.

Dépôt B
- Gestion centrale des versions ; `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, `TreatWarningsAsErrors=true`.
- Étape de CI qui exécute `dotnet list package --vulnerable --include-transitive` et lit son rapport (la commande sort 0 quoi qu'elle trouve) ; la même vérification est dans la tâche de gate locale.
- Règle écrite : répondre par un épinglage transitif daté, jamais en relâchant le niveau d'audit.
- Dependabot sur cinq écosystèmes (nuget, github-actions, docker, npm, docker-compose) ; actions et images épinglées par SHA et digest.
- Aucune étape d'audit des paquets JavaScript (`pnpm audit` ou équivalent) n'existe dans la CI ni la gate. La documentation de sécurité écrit « Every dependency pinned and audited ».
- Pas de workflow planifié qui génère le projet par défaut et le compile ; une seule planification existe (CodeQL, hebdomadaire).

### 1.7 Processus de sécurité du dépôt

Dépôt A
- Pas de fichier `SECURITY.md` ; signalement privé de vulnérabilité désactivé (`{"enabled":false}`).
- CodeQL sur push, pull request et chaque lundi, avec détection automatique du langage.
- Les alertes de code scanning ne sont pas lisibles sans droit d'administration (HTTP 403).

Dépôt B
- Un `SECURITY.md` dans le projet généré : tableau des protections et page décrivant chacune, liste « avant de déployer » (obligatoire, recommandé), section de signalement à remplacer. Signalement privé activé sur le dépôt.
- CodeQL en C# et en JavaScript/TypeScript, hebdomadaire.
- 11 alertes de code scanning ouvertes sur la branche de développement, toutes créées le 2026-10-22 : une `js/polynomial-redos` classée high (`const baseUrl = (options.baseUrl ?? '').replace(/\/+$/, '')`) et dix `cs/log-forging` classées medium (journaux contenant la méthode et le chemin de la requête, dans le gestionnaire d'exceptions, le filtre d'idempotence et le middleware de journal de requêtes). Le journal utilise le formateur JSON de la console.
- Sur le dépôt : secret scanning, push protection du secret scanning et mises à jour de sécurité Dependabot sont `disabled`.

## Axe 2. Front-ends

### 2.1 Frameworks proposés

Dépôt A
- Une option `ClientFramework` : Angular (défaut), React, ou aucun.
- Angular 22 écrit avec des modules :
  ```ts
  @NgModule({ ... })
  platformBrowser(providers).bootstrapModule(AppModule)
  ```
  React 19 avec Vite 8, composants en `.jsx` (non typés).
- Un module serveur Angular (`app.server.module.ts`) et la dépendance `@angular/platform-server` sont présents ; le fichier de configuration `angular.json` n'a ni entrée `ssr` ni entrée `server`, et `main.ts` n'amorce que le navigateur.
- Pas d'option d'audience, pas d'option de rendu côté serveur. Pas de projet Blazor, Vue ou Svelte.
- Le suivi des demandes du dépôt contient des issues ouvertes pour Vue, SvelteKit en SPA et Blazor.

Dépôt B
- Une option `web` : `none`, `placeholder`, `angular`, `vite-react`, `vite-vue`, `vite-svelte`, `react-router`, `blazor-wasm` ; une option `webAudience` (`public`, `admin`) ; une option `webRendering` (`spa`, `ssr`) acceptée pour Angular et React Router, refusée à la génération pour les autres.
- Une option `presentation` multivaluée : `api`, `blazor-server`, `mcp`, `avalonia`, `wpf`, `worker`.
- Chaque framework est écrit par son générateur officiel à une version exacte, avec ses arguments dans un fichier de description :
  ```json
  { "package": "@angular/cli", "version": "22.2.3",
    "arguments": ["new", "{name}", "--standalone", "--routing", "--ssr=false", "--test-runner=vitest", ...] }
  ```
  `create-vite 9.3.1` pour React, Vue et Svelte ; `create-react-router 8.5.0` ; `dotnet new blazorwasm` d'un paquet épinglé en 10.0.14.

### 2.2 Code du front livré avec le template

Dépôt A
- Une application d'exemple dans chaque framework : pages de connexion et d'inscription, liste de tâches, météo, compteur, bascule de thème.
  ```
  ClientApp/src/api-authorization/{login,register,auth.guard.ts,authorize.interceptor.ts}
  ClientApp-React/src/components/api-authorization/{AuthContext,LoginPage,RegisterPage,ProtectedRoute}.jsx
  ```
- La CI du template génère chaque combinaison, construit le client (`npm ci && npm run build`) et exécute des tests navigateur Playwright.

Dépôt B
- Un outil de bootstrap exécute le générateur officiel à une version épinglée, pose un relais `/api`, la dépendance vers le client et les scripts, installe les dépendances, puis ne touche plus à l'application. La documentation écrit : « the pages are yours to write ».
- Les dossiers d'overlay des starters Angular, Vite et React Router contiennent des fichiers de configuration (`vite.config.ts`, `proxy.conf.json`, `react-router.config.ts`) et, pour le rendu serveur, un serveur et ses entrées ; aucune page de connexion n'y figure (recherche de `login` et `sign-in` dans ces starters : aucune occurrence). Le starter Blazor WebAssembly contient la connexion à un fournisseur externe.
- La documentation donne un extrait de connexion à copier (`createBrowserSession`, `api.POST('/api/v1/auth/login', ...)`). Les tests de bout en bout du front (quatre théories sur la pile Compose) vérifient le relais `/api`, la CSP et le nonce, l'en-tête CSRF et le rappel de connexion ; les tests du paquet de client utilisent un faux `fetch`.
- L'outil de bootstrap a besoin du réseau pour récupérer chaque générateur.

### 2.3 Intégration avec l'API et client généré

Dépôt A
- Le client TypeScript est produit par NSwag à chaque `prestart` et `prebuild` depuis un document OpenAPI que le build .NET écrit :
  ```json
  "prebuild": "npm run generate-api",
  "generate-api": "nswag run /runtime:Net100"
  ...
  "nswag": "latest"
  ```
- Les fichiers générés sont exclus de git ; le document OpenAPI n'est pas suivi non plus.
- Un client par contrôleur, en classes ; pour Angular, `HttpClient` et une injection par jeton ; un intercepteur pose `withCredentials: true`. Les pages React importent le client `.ts` depuis du `.jsx`.
- Les cookies d'Identity portent la session du SPA. Une issue ouverte rapporte que le front Angular lancé par Aspire utilise sa propre URL à la place de celle de l'API.

Dépôt B
- Un paquet de client dans l'espace de travail pnpm : `openapi-fetch` 0.17.0 sur des types `openapi-typescript` 7.13.0, versions exactes ; le fichier `schema.ts` généré est suivi par git.
- La CI regénère le client et échoue si la sortie diffère de ce qui est committé :
  ```yaml
  - name: The API client matches the API
    run: |
      pnpm api
      git diff --exit-code -- packages/api-client/src/generated
  ```
- Une fonction `unwrap` lève une `ApiError` portant le `code` du problème et les erreurs de champs ; une réponse qui n'est pas un document de problème devient `response.unreadable`.
- Session de navigateur : le jeton d'accès vit dans une variable, le cookie porte le jeton de rafraîchissement, un seul rafraîchissement à la fois.
- Pour Blazor WebAssembly, Avalonia et WPF, un client .NET est généré par Kiota depuis le même document. Le front appelle l'API par un relais de même origine, l'API garde CORS fermé.

### 2.4 Rendu côté serveur et sessions

Dépôt A
- Pas de rendu côté serveur en service (voir 2.1).

Dépôt B
- Un front rendu côté serveur tourne sur son propre serveur Node : un nonce de 128 bits dans la CSP de chaque réponse, `Cache-Control: no-store`, les jetons gardés par le serveur, un unique cookie `__Host-session` (`HttpOnly; Secure; SameSite=Lax`) :
  ```ts
  return `${sessionCookie}=${id}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(seconds)}`;
  ```
- Une requête qui change l'état sans l'en-tête `X-CSRF: 1` reçoit 403. Avec un fournisseur externe, le serveur est le client OpenID Connect (code d'autorisation, PKCE, état, nonce).
- Les sessions sont dans la mémoire du serveur (`new Map<string, Session>()`) : une instance, et un redémarrage déconnecte tout le monde ; la documentation le dit. La liste `NG_ALLOWED_HOSTS` de l'image est `localhost,127.0.0.1` et se complète au déploiement.

### 2.5 Build, livraison et sécurité des applications front

Dépôt A
- Le SPA est servi par l'API (`UseFileServer`, `MapFallbackToFile("index.html")`) sans CSP ni autre en-tête de sécurité.
- À la publication du projet .NET :
  ```xml
  <Exec WorkingDirectory="$(SpaRoot)" Command="npm install" />
  ```
- `package.json` autorise les scripts d'installation de quatre paquets :
  ```json
  "allowScripts": { "@parcel/watcher": true, "esbuild": true, "lmdb": true, "msgpackr-extract": true }
  ```
- Pas de Dockerfile ni de fichier Compose. Les mises à jour mineures sont fusionnées automatiquement.

Dépôt B
- Une image par application : nginx sans privilèges ou serveur Node, port 8080, `server_tokens off`, avec :
  ```nginx
  add_header Content-Security-Policy "default-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'" always;
  add_header X-Content-Type-Options "nosniff" always;
  add_header Referrer-Policy "no-referrer" always;
  add_header Cross-Origin-Opener-Policy "same-origin" always;
  ```
- Pour les pages dont le build écrit des scripts en ligne, une étape de la construction de l'image calcule leur SHA-256 et l'ajoute à `script-src` ; `style-src` admet `'unsafe-inline'` pour Angular.
- Images de base épinglées par digest ; `pnpm install --frozen-lockfile` ; les scripts d'installation des mêmes quatre paquets sont refusés (`allowBuilds: false`) ; la CI vérifie que chaque image tourne avec un utilisateur non root et n'expose que 8080.

### 2.6 Boucle de développement et tests de navigateur

Dépôt A
- Un hôte Aspire lance le SPA, l'API et la base, avec découverte de services :
  ```csharp
  builder.AddJavaScriptApp(Services.WebFrontend, "./../Web/ClientApp").WithReference(web)
  ```
  Le proxy de développement du SPA lit l'adresse de l'API dans `services__webapi__https__0`.
- Quatre fichiers `.feature` Reqnroll et six scénarios Playwright (connexion valide et invalide, accueil, compteur, météo protégée), exécutés par `dotnet test` en CI. Trois tests Angular (vitest), aucun test React.

Dépôt B
- Docker Compose pour la pile ; en développement, `pnpm --filter <app> dev` avec un relais vers `API_UPSTREAM` (par défaut l'API sur l'hôte). Pas d'hôte Aspire.
- Cinq fichiers de test dans le paquet de client, plus les quatre théories de bout en bout (2.2). Aucun test ne pilote un navigateur.
- Le suivi des demandes du dépôt contient une issue ouverte qui regroupe Aspire, Playwright, Storybook et des applications mobiles.

### 2.7 Défauts et limites relevés dans le dépôt

Dépôt A
- Code mort côté Angular (module serveur sans configuration de rendu), `nswag` à `latest`, `npm install` à la publication, pages React en `.jsx` non typées.
- Le tag de la dernière release produit un projet qui échoue à la compilation (issue ouverte, 1.6).

Dépôt B
- Une issue ouverte : la commande d'ajout d'une application web refuse une application WebAssembly qui a créé son propre espace de travail.
- Une issue ouverte : des chemins générés de 176 caractères empêchent de committer sous Windows dans un dossier profond.
- Un dépôt cloné n'a pas d'application front avant l'exécution du bootstrap ; le job CI de découverte échoue avec « holds no app » si le résultat n'est pas committé.
