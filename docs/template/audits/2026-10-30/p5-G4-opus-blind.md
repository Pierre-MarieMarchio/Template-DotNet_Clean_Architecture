# Dossier à l'aveugle - groupe G4 : sécurité et front-ends

Deux modèles de solution .NET 10 en Clean Architecture, appelés Dépôt A et Dépôt B. Pour chaque point, A est décrit d'abord, B ensuite. Les noms de projets, d'espaces de noms et d'outils propres à chaque dépôt sont remplacés par « ProjA », « ProjB », « l'outil de A », « l'outil de B ». Les références de fichiers donnent le rôle du fichier, pas son chemin.

---

## Axe 1 - Sécurité

### 1.1 Authentification

**Dépôt A.** Un seul mode, sans option : ASP.NET Core Identity local. Cookies d'Identity quand une SPA est générée, jeton bearer opaque d'Identity en mode API seule. Les routes sont celles du framework.

```csharp
// enregistrement de l'infrastructure
#if (UseApiOnly)
        builder.Services.AddAuthentication()
            .AddBearerToken(IdentityConstants.BearerScheme);
#else
        builder.Services.AddAuthentication(options => { options.DefaultScheme = IdentityConstants.ApplicationScheme; ... })
            .AddIdentityCookies();
#endif

// groupe d'endpoints des utilisateurs
groupBuilder.MapIdentityApi<ApplicationUser>();
```

Les options d'Identity (mot de passe, verrouillage, confirmation d'adresse) ne sont pas réglées : ce sont celles du framework. Aucun service d'envoi de mail n'est enregistré. En Development, un compte administrateur est créé avec une adresse et un mot de passe écrits dans le code de l'initialiseur de base.

**Dépôt B.** Trois modes choisis à la génération : comptes locaux, jetons d'un fournisseur OpenID Connect externe, ou aucun compte. En mode local : JWT d'accès, refresh tokens organisés en familles, refresh token en cookie `HttpOnly` pour le navigateur, passkeys, double authentification, clés d'accès pour les clients qui ne rafraîchissent pas. Sept dossiers de features d'authentification. Politique de mot de passe dans un objet d'options validé au démarrage :

```csharp
// options de politique d'identité
    public int PasswordRequiredLength { get; set; } = 12;
    public int LockoutMaxFailedAccessAttempts { get; set; } = 5;
// le validateur refuse une longueur sous un plancher fixe
```

Le compte d'amorçage est désactivé par défaut, sans mot de passe dans le fichier de configuration suivi par git, et le validateur exige un mot de passe quand on l'active. Des tests d'intégration portent sur la politique de mot de passe, le verrouillage, la rotation des refresh tokens, le cookie de refresh et la rotation du security stamp.

### 1.2 Autorisation par défaut et propriété des données

**Dépôt A.** Pas de politique de repli. Chaque groupe d'endpoints déclare son exigence :

```csharp
builder.Services.AddAuthorizationBuilder();
...
public static void Map(RouteGroupBuilder groupBuilder)
{
    groupBuilder.RequireAuthorization();
    groupBuilder.MapPost(CreateTodoItem);
    ...
}
```

Un second mécanisme vit dans la couche application : un attribut `[Authorize]` propre, lu par un pipeline behavior MediatR, posé sur une requête sur neuf. Aucune notion de propriétaire : la requête de lecture renvoie toutes les listes (`_context.TodoLists.AsNoTracking().ProjectTo<...>().ToListAsync()`) ; `CreatedBy` est un champ d'audit rempli par un intercepteur. Les tests fonctionnels envoient les requêtes par le médiateur, sans appel HTTP.

**Dépôt B.** Politique de repli « utilisateur authentifié » :

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
```

En mode sans compte, le repli accepte tout et le démarrage échoue si un `[Authorize]` reste sans `[AllowAnonymous]`. Un test d'intégration énumère les endpoints et appelle chacun sans jeton puis avec un jeton invalide, en attendant 401. Le propriétaire est un critère de la requête ; l'enregistrement d'un autre appelant répond 404 ; sept tests d'intégration couvrent lecture, renommage, suppression, ajout, complétion, indistinction avec un absent, et index.

Le dépôt B propose aussi un hôte Blazor Server optionnel (portail). Son enregistrement d'autorisation ajoute une politique « Administrator » sans politique de repli ; sa documentation dit qu'aucune page n'exige d'authentification tant qu'elle ne porte pas `[Authorize]`. Les use cases appelés par ses pages exigent eux-mêmes un utilisateur.

### 1.3 Surface anonyme

**Dépôt A.** Anonymes : les routes de `MapIdentityApi` (inscription, connexion, rafraîchissement, confirmation, renvoi de confirmation, mot de passe oublié, réinitialisation), le document OpenAPI et Scalar dans tous les environnements, les fichiers statiques et le repli vers `index.html`. Pas de limiteur de débit. L'inscription renvoie les erreurs de validation d'Identity ; le front affiche un message générique.

```csharp
app.UseFileServer();
app.MapOpenApi();
app.MapScalarApiReference();
...
app.MapFallbackToFile("index.html");
```

**Dépôt B.** Douze actions marquées `[AllowAnonymous]` (inscription, connexion, connexion externe, 2FA, déconnexion, passkey en deux temps, rafraîchissement, demandes et réinitialisation de mot de passe, confirmation) et quatre endpoints anonymes déclarés en fluent (OpenAPI, Scalar, deux sondes de santé). Un test d'architecture compare les actions anonymes trouvées dans le code à cette liste. OpenAPI et Scalar sont mappés en Development seulement. Les refus de connexion et les réponses d'inscription sont uniformes quel que soit le motif, et les routes d'authentification ont une limite de débit par adresse, avec des tests dédiés.

### 1.4 En-têtes, CORS, HTTPS

**Dépôt A.**

```csharp
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseCors(static builder =>
    builder.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());
```

Aucun autre en-tête de sécurité n'est posé par le code.

**Dépôt B.** Un middleware écrit, sur toute réponse y compris les réponses d'erreur :

```csharp
headers.XContentTypeOptions = "nosniff";
headers["Referrer-Policy"] = "no-referrer";
headers.XFrameOptions = "DENY";
headers.Remove("X-Powered-By");
headers.ContentSecurityPolicy = policyOverride?.Invoke(httpContext) ?? apiPolicy;
// apiPolicy par défaut : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"
```

La CSP est une option validée au démarrage ; la page Scalar de Development reçoit une CSP à nonce. CORS : aucune politique tant que la liste d'origines configurée est vide, sans credentials. Pas de HSTS ni de redirection HTTPS ; une décision écrite du dépôt dit que TLS est terminé en amont. Tests d'intégration sur la présence des en-têtes, tests unitaires sur CORS. L'hôte Blazor Server optionnel retire l'en-tête `Server` et active l'antiforgery, sans CSP, `X-Frame-Options` ni `nosniff`.

### 1.5 Secrets et journaux

**Dépôt A.** Azure Key Vault ajouté à la configuration quand une variable d'environnement donne son adresse :

```csharp
var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
```

Un pré-processeur MediatR journalise chaque requête déstructurée :

```csharp
_logger.LogInformation("ProjA Request: {Name} {@UserId} {@UserName} {@Request}", requestName, userId, userName, request);
```

**Dépôt B.** Fichiers de configuration suivis sans secret, champs vides ; user-secrets en local, variables d'environnement ensuite ; la documentation renvoie au gestionnaire de secrets de la plateforme ; aucun fournisseur de coffre intégré. La clé JWT de développement livrée est refusée hors Development :

```csharp
else if (Encoding.UTF8.GetByteCount(options.Key) < 32) failures.Add(...);
... && options.Key.StartsWith("dev-only", StringComparison.Ordinal))
    failures.Add("... is the development key the template ships, which is public. ...");
```

Des tests d'intégration vérifient qu'aucun journal ne contient d'identifiant, d'en-tête `Authorization` ni de cookie pendant le parcours d'authentification.

### 1.6 Dépendances vulnérables

**Dépôt A.** `TreatWarningsAsErrors` avec l'audit NuGet par défaut ; Renovate hebdomadaire avec fusion automatique des mises à jour non majeures ; CodeQL sans langage déclaré. Une version transitive épinglée pour un avis connu sur la branche principale. La dernière version publiée fige des paquets devenus vulnérables depuis ; une issue ouverte signale qu'un projet généré ne compile plus (erreurs NU1902/NU1903) ; des essais de génération l'ont reproduit (120 erreurs). Côté npm : `"nswag": "latest"`, versions en `^`, `npm install` à la publication.

**Dépôt B.** `TreatWarningsAsErrors` et un audit explicite :

```xml
<NuGetAudit>true</NuGetAudit>
<NuGetAuditMode>all</NuGetAuditMode>
<NuGetAuditLevel>low</NuGetAuditLevel>
```

Plus `dotnet list package --vulnerable --include-transitive`, lu par l'outil de B et par un job CI. Dependabot sur cinq écosystèmes (NuGet, actions, images Docker, npm, compose) ; CodeQL sur C#, JavaScript/TypeScript et actions. Versions exactes, images par digest, `pnpm install --frozen-lockfile` en CI et dans les images, scripts d'installation des paquets npm refusés. La documentation prescrit une épingle transitive en réponse à un avis, jamais un niveau d'audit relâché. Aucun audit des paquets npm dans le gate ni en CI ; aucun job planifié ne génère un projet depuis la version publiée.

---

## Axe 2 - Front-ends

### 2.1 Frameworks proposés

**Dépôt A.** Option de génération : Angular (défaut), React, aucun. Le code des deux apps est conservé dans le dépôt du modèle ; le modèle exclut l'une et renomme l'autre. Angular en NgModule (`standalone: false`), React en JSX (15 fichiers `.jsx`). La version publiée livre Angular 21 avec Karma ; la branche principale est en Angular 22 avec vitest. Issues ouvertes demandant Vue, SvelteKit statique et Blazor.

**Dépôt B.** Option de génération : aucun, page statique, Angular, React (Vite), Vue (Vite), Svelte (Vite), React Router, Blazor WebAssembly ; options d'audience (public, admin) et de rendu (SPA, SSR). Le modèle ne contient pas le code des apps : un descripteur par framework nomme la CLI officielle et sa version exacte, et l'outil de B l'exécute à l'amorçage puis pose un overlay de câblage.

```json
{
  "framework": "angular",
  "package": "@angular/cli",
  "version": "22.2.3",
  "arguments": ["new", "{name}", "--standalone", "--routing", "--ssr=false", "--test-runner=vitest", "..."]
}
```

Des hôtes .NET de présentation (Blazor Server, serveur MCP, Avalonia, WPF, worker) s'y ajoutent, ainsi que des sous-modèles pour ajouter une app web ou un paquet après la génération. SvelteKit, Nuxt, Next et Astro sont listés comme ultérieurs.

### 2.2 Intégration avec l'API

**Dépôt A.** La SPA est servie par l'API : même processus, même origine ; la publication .NET construit la SPA et la copie dans `wwwroot`. En développement, Aspire lance le serveur de dev qui relaie `/api`. L'URL de base vient de la balise `<base href>`. Une issue ouverte signale un mauvais port d'API utilisé par l'Angular par défaut sous Aspire. Authentification par cookie d'Identity, avec `withCredentials: true` sur chaque requête.

```ts
const authReq = req.clone({ withCredentials: true });
```

**Dépôt B.** Chaque app est un déployable distinct : nginx pour une SPA, un serveur Node pour une app SSR, nginx pour Blazor WebAssembly. Chacun relaie `/api/` et `/health` vers l'adresse de l'API (`API_UPSTREAM`), si bien que le navigateur ne voit qu'une origine et que l'API garde CORS fermé. En SPA : jeton d'accès en mémoire, refresh token dans le cookie `HttpOnly` de l'API. En SSR : le serveur garde les jetons, le navigateur n'a qu'un cookie de session et tout appel modifiant l'état doit porter un en-tête dédié :

```ts
export const sessionCookie = '__Host-session';
...
return `${sessionCookie}=${id}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${String(seconds)}`;
...
if (!changesState(request) || request.headers['x-csrf'] === '1') { return false; }
answerProblem(response, 403, 'Forbidden', 'A request that changes state carries the X-CSRF: 1 header.', ...);
```

Des tests de bout en bout sur la pile Docker vérifient le relais, le refus sans en-tête CSRF et le refus d'un retour de connexion que le navigateur n'a pas commencé.

### 2.3 Client généré

**Dépôt A.** NSwag, version non épinglée, exécuté avant `start` et `build` du front à partir du document OpenAPI que le build .NET écrit dans `wwwroot`. Le fichier généré est ignoré par git. Gabarit Angular (`HttpClient`) ou Fetch (React). Exceptions `SwaggerException`.

**Dépôt B.** Un paquet client partagé par tous les frameworks d'un workspace pnpm : schéma produit par `openapi-typescript` et suivi par git, appels par `openapi-fetch`, une fonction qui renvoie le corps ou lève une erreur portant le code stable du problème et les erreurs de champ, plus les intercepteurs de session navigateur et serveur. Versions exactes ; 67 tests vitest ; la CI régénère le schéma et échoue sur différence. Pour Blazor WebAssembly et les hôtes de bureau, un client .NET généré par Kiota, vérifié par l'outil de B.

```ts
const api = createApiClient();
try {
  const note = await unwrap(api.POST('/api/v1/notes', { body: { title, body } }));
} catch (error) {
  if (error instanceof ApiError && error.code === 'request.validationFailed') { showErrors(error.fieldErrors); }
}
```

### 2.4 Rendu serveur

**Dépôt A.** Pas de rendu serveur actif. Un module `AppServerModule` et la dépendance `@angular/platform-server` sont présents, sans entrée serveur dans `angular.json` ; la publication ne prend que `dist/browser`. `angular.json` liste `oidc-client` comme dépendance CommonJS autorisée, sans ce paquet dans les dépendances.

**Dépôt B.** Rendu serveur au choix pour Angular et React Router. Le serveur Node calcule un nonce par réponse et les empreintes des scripts inline du gabarit :

```ts
const contentSecurityPolicy = (nonce: string) => [
  "default-src 'self'", "base-uri 'self'", "object-src 'none'", "form-action 'self'",
  "frame-ancestors 'none'", "img-src 'self' data:", "style-src 'self' 'unsafe-inline'",
  ["script-src 'self'", ...templateScriptHashes, `'nonce-${nonce}'`].join(' '), "connect-src 'self'",
].join('; ');
...
const nonce = randomBytes(16).toString('base64');
response.setHeader('Content-Security-Policy', contentSecurityPolicy(nonce));
response.setHeader('Cache-Control', 'no-store');
```

Un test de bout en bout vérifie qu'aucun script inline ne s'exécute hors empreinte ou nonce et qu'un nonce n'est jamais répété. Les sessions SSR sont en mémoire : une seule instance, un redémarrage déconnecte ; une décision écrite du dépôt le dit.

### 2.5 Build et sécurité des apps

**Dépôt A.** Pas de Dockerfile ni de compose ; déploiement prévu par Aspire. La publication .NET lance `npm install` puis `npm run build`. Pas de CSP sur les pages ; l'`index.html` Angular charge des polices Google et exécute un script inline pour le thème.

**Dépôt B.** Une image par app : `nginx-unprivileged` (uid 101, port 8080) ou Node en utilisateur `node`, images épinglées par digest. Le `nginx.conf` pose une CSP par framework, `nosniff`, `Referrer-Policy`, `Cross-Origin-Opener-Policy` et les règles de cache :

```nginx
add_header Content-Security-Policy "default-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; frame-ancestors 'none'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'" always;
add_header X-Content-Type-Options "nosniff" always;
add_header Referrer-Policy "no-referrer" always;
add_header Cross-Origin-Opener-Policy "same-origin" always;
```

La CI vérifie que chaque image ne tourne pas en root et n'expose que 8080, et lance lint, tests et build du workspace.

### 2.6 Tests des fronts

**Dépôt A.** Angular : 3 tests unitaires dans 2 fichiers ; React : aucun. Tests d'acceptation Reqnroll + Playwright, quatre features dont la connexion (identifiants valides, invalides) ; leur exécution demande l'installation des navigateurs Playwright par un script PowerShell ; ils portent sur l'app Angular.

```gherkin
Scenario: User cannot log in with invalid credentials
    Given a logged out user
    When the user logs in with invalid credentials
    Then an error is displayed
```

**Dépôt B.** 67 tests du paquet client ; tests de bout en bout HTTP sur la pile Docker (relais, CSP, nonce, CSRF, retour de connexion). Aucun test qui pilote un navigateur ; Playwright est listé comme ultérieur. L'app créée par la CLI garde les tests de son gabarit (aucun pour Vite + React d'après un essai). La commande de vérification locale de l'outil de B ne lance pas le lint, les tests et le build du workspace web ; le workflow CI web le fait.

### 2.7 Ajouter une app après la génération

**Dépôt A.** Pas de mécanisme. Un essai a consisté à générer une solution jetable avec l'autre framework, copier son dossier d'app (29 fichiers) et ajouter son lancement dans l'hôte Aspire à la main.

**Dépôt B.** Un sous-modèle écrit l'app et l'outil de B l'amorce ; pour une SPA ou une app SSR, une dizaine d'éditions restent à faire à la main hors du workspace (compose, `.env.example`, `.dockerignore`, Dependabot, CodeQL, documentation), listées par le sous-modèle. Une commande de jonction automatique existe pour Blazor WebAssembly seulement, avec un défaut ouvert.
