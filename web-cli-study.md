# Étude : générer le front avec les CLI officielles

Date : 26 octobre 2026. Base lue : `develop` au commit 6ed4e950. Rien n'est implémenté ici, c'est une
proposition à valider.

Une remarque avant de commencer, parce qu'elle change la lecture du reste. Le template s'appuie déjà
sur les CLI officielles : React sort de `create-vite` (template `react-ts`) et Angular de `ng new`,
c'est l'objet de `docs/template/decisions/0003-framework-apps-come-from-official-clis.md`. La vraie
question est donc d'élargir le catalogue et de choisir comment l'option le nomme, pas de changer de
principe.

## 1. Le mécanisme actuel

### Ce qui est dans le dépôt

Chaque framework a son dossier sous `Src/Web/apps/` : `ReactSite`, `AngularSite`, plus
`PlaceholderSite` qui n'a pas de CLI. Un dossier de framework contient six choses :

- un Dockerfile (build Node 24 puis nginx non privilégié) ;
- un nginx.conf (en-têtes de sécurité, CSP stricte, cache, repli sur index.html, relais de /api/ et
  /health vers l'API) ;
- un compose.yml (le service web-&lt;nom&gt;, le port WEB_&lt;NOM&gt;_HOST_PORT) ;
- un README.md d'environ 150 lignes ;
- `scaffold.json`, qui nomme le paquet de la CLI, sa version exacte, ses arguments et les fusions JSON
  à faire ;
- un dossier overlay/ avec les fichiers à poser par-dessus la sortie de la CLI.

Ces quatre premiers fichiers sont presque identiques d'un framework à l'autre. Entre React et Angular,
le Dockerfile ne diffère que par le dossier de sortie (dist/ contre dist/angular-site/browser/) et une
variable NG_CLI_ANALYTICS. Le nginx.conf ne diffère que par `style-src 'unsafe-inline'`, dont Angular a
besoin. Le compose.yml ne diffère que par les noms. Le README est le même texte à 80 %.

### Le code qui crée l'app

Tout se trouve dans `Tools/Tasks.cs`.

- `WebScaffold` cherche sous Src/Web/apps les dossiers qui ont un `scaffold.json` mais pas encore de
  `package.json`. Il appelle `ScaffoldApp` pour chacun, puis lance `pnpm install --no-frozen-lockfile`
  et `PinInstalledVersions`, réinstalle si quelque chose a été épinglé, et finit par `pnpm api`.
  `dotnet run Tools/Tasks.cs bootstrap` l'appelle quand Src/Web contient un package.json.
- `ScaffoldApp` crée un dossier temporaire .web-scaffold-&lt;guid&gt; dans Src/Web et y lance
  `pnpm dlx <paquet>@<version> <arguments>` en remplaçant {name} et {directory}. Il vérifie qu'un
  package.json est sorti, copie l'overlay par-dessus (en écrasant), supprime ce que liste `remove`,
  applique les fusions `merge` (fusion profonde, une valeur null supprime la clé), puis recopie le tout
  dans le dossier de l'app sans écraser les fichiers de câblage. Pour finir, il supprime
  `scaffold.json` et overlay/. Comme le dossier n'a plus de descripteur, l'app n'est plus jamais
  recréée.
- `PinInstalledVersions` remplace chaque plage de version du package.json par la version réellement
  installée, lue dans node_modules/&lt;dep&gt;/package.json.
- `WebLockfiles` n'existe que dans le dépôt du template. Pour chaque app qui a déjà un package.json,
  c'est-à-dire aujourd'hui le seul placeholder, il copie le workspace sans les autres apps, lance
  `pnpm install --lockfile-only` et compare le résultat au lockfile de variante. Il vérifie aussi que
  template.json génère bien cette variante et signale les variantes orphelines.
  `dotnet run Tools/Tasks.cs web-lockfiles --check` fait partie de `verify`.

### Ce que l'overlay apporte vraiment

Il en apporte moins qu'on pourrait le croire.

Pour React, l'overlay contient un seul fichier, `Src/Web/apps/ReactSite/overlay/vite.config.ts` :
le plugin React, le port 5173 et un proxy de /api/ et /health vers API_UPSTREAM (par défaut
http://localhost:5187). La fusion du package.json ajoute le nom react-site, la dépendance
`@app-template/api-client` en workspace:*, un script test (`vitest run --passWithNoTests`) et vitest
5.0.3.

Pour Angular, l'overlay contient `Src/Web/apps/AngularSite/overlay/proxy.conf.json`. Les fusions
ajoutent dans angular.json le port 4200, le proxy et `inlineCritical: false` (son handler onload est
refusé par la CSP). Elles ajoutent aussi dans `tsconfig.json` `allowImportingTsExtensions` et `noEmit`,
et dans le package.json le nom, les scripts dev et test, l'api-client, et retirent `packageManager`.

Le reste ne vient pas de l'overlay :

- `Src/Web/packages/api-client` est un paquet du workspace, commun à toutes les apps. `pnpm api`
  compile l'API avec OpenApiGenerateDocumentsOnBuild puis passe le document à openapi-typescript. La
  session navigateur de l'auth locale (createBrowserSession, sessionMiddleware) vit dans ce paquet.
- Pour OIDC, il n'y a pas de code. Le README de l'app explique comment brancher une bibliothèque et
  ajouter l'origine du fournisseur à connect-src.
- Les paquets eslint-config et tsconfig du workspace servent aux paquets, pas aux apps. L'app React
  lint avec oxlint, que create-vite installe. L'app Angular n'a pas de linter du tout : le README
  renvoie à `ng add angular-eslint`.
- `Src/Web/pnpm-workspace.yaml` est partagé. Il liste en allowBuilds les scripts d'installation
  refusés pour la chaîne Angular (esbuild, lmdb, @parcel/watcher, msgpackr-extract), y compris dans un
  projet React.

### Les versions

- La CLI est épinglée à la version exacte dans `scaffold.json` : create-vite 9.3.1, @angular/cli
  22.2.3 (npm est passé à 22.2.4 depuis).
- Les dépendances de l'app ne sont pas figées par le template. Elles sont résolues au bootstrap, à
  partir des plages que la CLI écrit, puis épinglées. Deux projets générés à une semaine d'écart
  peuvent donc avoir des versions différentes. Après le premier commit, le lockfile du projet fait foi,
  et Dependabot surveille Src/Web.
- pnpm est figé par `packageManager` (11.28.2 avec son sha512), Node par `Src/Web/.node-version`
  (24.22.0) et par le digest de l'image dans le Dockerfile.
- Petit écart relevé au passage : la fusion React épingle vitest 5.0.3, alors que l'api-client est en
  5.0.5.

### Lockfiles et acceptation

Seul le placeholder a un lockfile de variante. Les apps de framework n'en ont pas, puisque leur
lockfile est écrit au moment où la CLI tourne. Ajouter un framework ne coûte donc pas de lockfile de
variante, contrairement à ce que laissait penser la demande. Le prix de ce choix est que la
reproductibilité s'arrête à la version de la CLI.

Dans `.github/workflows/template-acceptance.yml`, six jambes touchent au web : placeholder minimal,
React avec auth locale, Angular avec auth externe, back office React, React puis un back office
Angular ajouté par cleanarch-webapp, et un projet sans web qui reçoit React avec --workspace. Chaque
jambe web enchaîne :

1. web-lockfiles --check ;
2. l'ajout d'une deuxième app ;
3. web-scaffold ;
4. pnpm install --frozen-lockfile, lint, test et build ;
5. l'ajout d'un paquet ;
6. un deuxième web-scaffold, qui ne doit rien changer ;
7. `Tools/CheckDocPaths.cs` sur le projet généré ;
8. docker compose build et up ;
9. un curl sur chaque port, qui vérifie la CSP avec frame-ancestors, nosniff, Referrer-Policy,
   Cache-Control no-cache, /health relayé et /api/ qui répond avec le code de problème de l'API.

Ce sont les jambes les plus longues de la matrice, puisqu'elles montent toute la pile.

### Le back office

`--web-audience admin` ne change aujourd'hui que deux phrases du README de l'app : le titre, et une
note sur le 403 des routes d'administration. Aucun écran n'est généré. La description de l'option dans
template.json dit que « the framework apps shape their screens by it », ce qui ne correspond pas au
code. C'est un défaut à corriger, que l'on donne suite à cette étude ou non. Pour l'étude, cela veut
dire que l'audience ne coûte rien par framework.

### Ce que coûte un framework aujourd'hui

En comptant ce qu'il a fallu pour Angular :

- un dossier d'app complet (les quatre fichiers presque copiés, plus scaffold.json et overlay/) ;
- dans `.template.config/template.json` : un choix, un symbole calculé, trois symboles de renommage
  (PascalCase, kebab, MAJUSCULES), un cas dans le switch des ports, une règle d'exclusion, un bloc
  include dans `docker-compose.yml` et une ligne dans .env.example ;
- tout cela une deuxième fois dans `.template.config/webapp/.template.config/template.json`, avec ses
  symboles appNameXxx et son port ;
- au moins une jambe d'acceptation avec la pile Docker ;
- dans le launcher, une couleur d'accent (GuidedConversation) et des tests qui citent les valeurs ;
- dans la doc : les tableaux de `docs/template/using-the-template.md` et
  `docs/template/maintaining-the-template.md`, `docs/features/web-frontend.md`, overview, first-run,
  les conditions de `Src/Web/AGENTS.md`, la liste de `bug_report.yml` ;
- éventuellement des entrées allowBuilds dans le pnpm-workspace.yaml partagé.

L'essentiel de ce coût vient de la duplication des dossiers et des symboles de renommage, pas de la
CLI elle-même.

### Vérification de la base

J'ai généré un projet `--web react` depuis un instantané de develop, avec un hive dédié, puis lancé
web-scaffold. Tout passe en 51 secondes, pnpm api compris.

## 2. Les CLI candidates

Les versions ont été relevées sur npm le 26 octobre 2026. Les téléchargements sont ceux de la dernière
semaine, pour la CLI et pour le framework. Toutes les commandes ont été lancées avec pnpm 11.28.2
(corepack) et Node 24.21.

| CLI | Version | Téléchargements CLI / framework | Rendu | Résultat du test |
|---|---|---|---|---|
| create-vite | 9.3.1 | 1,0 M / vite 127 M | SPA statique | 9 templates sur 9 OK |
| @angular/cli | 22.2.4 | 3,3 M / 3,6 M | SPA (ssr=false) | OK avec la fusion tsconfig |
| create-vue | 3.24.0 | 11 k / vue 11 M | SPA | OK à la main, bloque dans ScaffoldApp |
| sv (SvelteKit) | 1.1.1 | 33 k / kit 2,2 M | SSR, SPA possible | OK en SPA, scripts inline |
| create-next-app | 16.4.3 | 257 k / next 45 M | SSR, export possible | OK après quatre corrections, scripts inline |
| create-nuxt (nuxi) | 4.0.1 | 16 k (nuxi 99 k) / nuxt 1,5 M | SSR, SPA possible | OK en SPA, scripts inline |
| create-astro | 5.3.1 | 44 k / astro 5,1 M | statique multi-pages | OK, mais piège sur dossier non vide |
| create-react-router | 8.5.0 | 3 k / — | SSR, SPA possible | OK en SPA, scripts inline |

Je ne les ai pas testées, faute d'intérêt suffisant : create-qwik (le template qwik-ts de Vite couvre
le cas), create-solid (même chose avec solid-ts), @tanstack/create-start et create-tsrouter-app
(encore en 0.x, environ 500 téléchargements par semaine).

### Le banc de test

Pour chaque CLI, dans un dossier temporaire hors du dépôt :

1. copier le workspace (package.json, `Src/Web/pnpm-workspace.yaml`, les paquets avec l'api-client) ;
2. lancer la CLI dans apps/ ;
3. ajouter la dépendance à l'api-client et un fichier qui appelle `createApiClient()`, importé depuis
   le point d'entrée ;
4. lancer pnpm install, puis build, lint et test quand le script existe ;
5. compter les balises script et style inline dans l'index.html produit.

Ce dernier point compte, parce que le nginx.conf des apps impose `script-src 'self'` sans hash ni
nonce. Un script inline y est bloqué. Je l'ai déduit de la règle CSP sans l'observer dans un
navigateur.

### create-vite

```
pnpm dlx create-vite@9.3.1 <dossier> --template <t> --no-interactive --no-immediate
```

Templates disponibles : vanilla-ts, vue-ts, react-ts, react-compiler-ts, preact-ts, lit-ts, svelte-ts,
solid-ts, qwik-ts.

Avec un dossier non vide, la CLI s'arrête (« Operation cancelled »). `--overwrite` vide le dossier,
ce qui ne nous convient pas. C'est la raison pour laquelle ScaffoldApp passe par un dossier
temporaire, et ce choix reste le bon pour toutes les CLI testées.

Résultats : les neuf templates génèrent, s'installent et compilent avec l'api-client importé. Aucun ne
produit de script ni de style inline, et la sortie est toujours dist/. Quelques différences entre
eux :

- le build de react-ts, vue-ts, solid-ts, preact-ts et qwik-ts lance tsc ou vue-tsc ;
- celui de svelte-ts ne lance que `vite build`, la vérification de types étant dans le script check
  (svelte-check) ;
- seuls les templates React ont un lint (oxlint) ;
- aucun n'a de script test.

Les points d'attention sont donc le script test à ajouter partout, comme aujourd'hui pour React, et la
question du lint (voir les questions ouvertes).

C'est le cas le plus simple : même forme que l'app React actuelle. Seul l'import du plugin change dans
le vite.config.ts de l'overlay.

### Angular CLI

C'est la commande actuelle de `scaffold.json`, à passer en 22.2.4. Sans la fusion `tsconfig.json`, le
build échoue sur l'api-client (TS5097 : un import qui finit en .ts exige allowImportingTsExtensions).
La fusion actuelle est donc indispensable, et le test le confirme. Test vitest OK, pas de lint.

J'ai aussi essayé `outputPath: { base: "dist", browser: "" }` dans angular.json : le build sort alors
directement dans dist/. Cela permet d'avoir un même dossier de sortie pour toutes les apps, et donc un
seul Dockerfile (voir la recommandation).

### create-vue

```
pnpm dlx create-vue@3.24.0 <nom> --ts --router --vitest --eslint
```

create-vue est le générateur officiel de Vue, plus complet que le template vue-ts de Vite : routeur,
vitest et eslint plus oxlint. Il a un défaut gênant pour nous : le dossier doit être un nom de paquet
valide. Lancé comme ScaffoldApp le fait, avec un dossier .web-scaffold-&lt;guid&gt;/cv-site, il pose sa
question « Package name » et attend. Dans mon essai à travers le vrai web-scaffold, il a attendu
jusqu'au timeout (code 143). Lancé depuis le dossier parent avec un simple nom, il fonctionne.

Il faudrait aussi des fusions : test:unit lance vitest en mode watch, lint fait `--fix`, et le build
passe par run-p.

Ce test montre un défaut plus général de ScaffoldApp : le processus hérite de l'entrée standard, donc
une CLI qui se met à poser une question bloque bootstrap au lieu d'échouer.

### sv create (SvelteKit)

```
pnpm dlx sv@1.1.1 create <dossier> --template minimal --types ts --add eslint vitest=usages:unit --no-install
```

D'après l'aide de sv, il faut fournir --template, --types, --add et --install (ou --no-install) pour
qu'il ne pose aucune question. Avec un dossier non vide, il demande confirmation.

Par défaut, il utilise adapter-auto, c'est-à-dire un rendu serveur. Pour une sortie servie par nginx,
il faut @sveltejs/adapter-static (4.0.0), `fallback: 'index.html'` et `ssr = false` dans
src/routes/+layout.ts. La config est maintenant dans vite.config.ts (le starter n'a plus de
svelte.config.js).

Je l'ai fait passer par le vrai web-scaffold, avec un `scaffold.json` et un overlay, sans changer une
ligne de C#. lint, test, check et build passent dans le workspace, à côté de l'app React. L'image
Docker se construit (en changeant dist/ en build/), et nginx sert le repli avec la CSP.

Le problème : la page de repli contient un script inline et un style inline, que la CSP actuelle
bloque. SvelteKit a une option csp (mode hash) que je n'ai pas testée en mode SPA.

### create-next-app

```
pnpm dlx create-next-app@16.4.3 <dossier> --ts --eslint --app --src-dir --no-tailwind --no-react-compiler --import-alias "@/*" --use-pnpm --skip-install --disable-git --no-agents-md --no-agent-feedback --yes
```

Ce que l'essai a montré :

- Par défaut, la CLI écrit un AGENTS.md, alors que le template promet que son AGENTS.md est le seul
  fichier pour agents. `--no-agents-md` est donc obligatoire.
- `--yes` reprend les préférences enregistrées sur la machine, ce qui pose un problème de
  reproductibilité. `--reset` existe mais je ne l'ai pas testé.
- La CLI pose son propre pnpm-workspace.yaml dans l'app (allowBuilds sharp et unrs-resolver). pnpm
  l'ignore puisque l'app fait partie du workspace, et l'install échoue avec ERR_PNPM_IGNORED_BUILDS
  sur unrs-resolver. Il faut ajouter l'entrée dans le yaml racine, et ScaffoldApp ne sait fusionner
  que dans les fichiers de l'app.
- Il faut aussi allowImportingTsExtensions dans tsconfig.json pour l'api-client.
- Pour une sortie statique, `output: 'export'` échoue (« PPR cannot be enabled in export mode ») tant
  que cacheComponents et partialPrefetching restent activés, ce qu'ils sont par défaut. Il faut donc
  remplacer next.config.ts en entier.

Après ces corrections, l'export sort dans out/ et le lint passe. Mais la page contient trois scripts
inline et deux styles inline, et sans serveur, pas de nonce possible. Next sans SSR va contre l'usage
normal du framework. De toutes les candidates, c'est celle qui s'adapte le moins bien.

### create-nuxt

```
pnpm dlx create-nuxt@4.0.1 <dossier> --template minimal --packageManager pnpm --no-install --no-gitInit
```

Le template est téléchargé depuis GitHub, donc il faut un accès réseau à github.com en plus du
registre npm. Avec un dossier existant, la CLI refuse sans --force.

Par défaut, le build produit un serveur Nitro dans .output/server. Avec `ssr: false` et
`nuxt generate`, on obtient .output/public avec index.html et 200.html, mais deux scripts inline. Le
starter n'a ni lint ni test, et il ajoute un postinstall `nuxt prepare`.

### create-astro

```
pnpm dlx create-astro@5.3.1 <dossier> --template minimal --no-install --no-git --no-ai --skip-houston --yes
```

La sortie est statique par défaut (dist/index.html), sans aucun script inline. Astro produit un site
multi-pages et non une SPA, donc le repli `try_files $uri /index.html` de nginx n'est pas le bon. Pas
de lint ni de test. Le package.json demande Node 22.23.4 ou plus.

Le piège : dans un dossier non vide, la CLI annonce un succès mais écrit le projet dans un autre
dossier, au nom tiré au hasard (« subsequent-sphere » dans mon essai). ScaffoldApp le détecterait,
puisqu'il ne trouverait pas de package.json, mais c'est à savoir.

### create-react-router

```
pnpm dlx create-react-router@8.5.0 <dossier> --no-install --no-git-init --no-agent-skills --yes
```

Le template par défaut fait du rendu serveur, avec Tailwind et son propre Dockerfile. Avec
`ssr: false`, on obtient build/client/index.html, mais avec quatre scripts inline. La vérification de
types échoue sans allowImportingTsExtensions. Le starter impose aussi typescript ^5.9 et @types/node
^22, alors que le workspace est en TypeScript 6.

### Ce qui ressort

Les SPA produites par Vite et Angular respectent déjà ce que l'overlay doit poser : une sortie
statique dans un dossier connu, aucun script inline, un proxy configurable dans le serveur de dev, des
scripts lint, test et build que l'on complète par fusion. Tous ces templates passent sans modifier le
C#.

Les méta-frameworks (SvelteKit, Next, Nuxt, React Router) ont été conçus pour le rendu serveur. Pour
les mettre derrière nginx, il faut les reconfigurer. Ils produisent alors des scripts inline, ce qui
oblige à affaiblir la CSP. Le vrai mode SSR change le déploiement : il faut une image Node à la place
de nginx, revoir le relais vers l'API et le cookie de refresh (ADR 0016), et la CSP par nonce. Ce
sujet mérite une décision à part, qui n'a pas sa place dans cette évolution.

## 3. Modèles d'option

Une contrainte à garder en tête : dotnet new ne connaît que des options statiques. Chaque valeur
possible doit être déclarée dans les deux template.json, et une combinaison interdite se traduit par
un paramètre caché avec isRequired (ADR 0001 du template), que le launcher évalue (ADR 0004).

### (a) Deux options : --web-cli et --web-template

```
--web-cli angular|vite|sveltekit|...   --web-template react-ts|vue-ts|svelte-ts|...
```

- template.json : --web-template doit lister tous les templates de toutes les CLI. Chaque couple
  invalide (angular avec vue-ts, sveltekit avec n'importe quel template) demande un refus. Le nombre
  de refus grandit avec le catalogue.
- Launcher : il faut un askedWhen écrit à la main pour ne poser la question du template qu'avec Vite.
  C'est précisément ce que l'ADR 0004 tolère sans le souhaiter.
- Back office et matrice : rien ne change sur le fond, mais les refus ajoutent des cas au job des
  combinaisons refusées.
- Compatibilité : --web react disparaît. À moins de garder un pont entre trois options, c'est un
  changement cassant, donc une 2.0.
- Doc : deux options, plus un tableau des couples valides.

Mon avis : ce modèle expose comme un axe de choix un détail d'implémentation (quelle CLI génère
l'app), alors que l'utilisateur choisit un framework.

### (b) Une seule option --web dont la valeur nomme la CLI et le template

```
--web none|placeholder|angular|vite-react|vite-vue|vite-svelte|...   (+ react gardé comme alias de vite-react)
```

- template.json : une liste de choix, chaque valeur correspondant à un starter. Aucun refus entre
  options.
- Launcher : aucun code, puisque les choix sont lus dans le manifeste. Il reste la table des couleurs
  et les tests qui citent les valeurs.
- Back office : inchangé. Matrice : une couverture par valeur (voir plus bas comment la regrouper).
- Compatibilité : angular ne change pas, et react reste accepté comme alias. Le changement n'est donc
  pas cassant, une version mineure (1.1) suffit, et l'alias peut disparaître en 2.0. Même chose pour
  `cleanarch-webapp --framework`.
- Doc : une ligne par valeur, qui dit quelle CLI et quel template elle lance.

### (c) La valeur nomme le framework, la CLI reste un choix du template

```
--web none|placeholder|react|vue|svelte|solid|angular
```

C'est le plus court, et compatible par construction : react veut déjà dire create-vite react-ts. La
limite apparaît quand un framework a deux générateurs : vue peut venir de create-vite vue-ts ou de
create-vue, svelte de Vite ou de SvelteKit. Il faut alors des suffixes, et on revient au modèle (b)
pour ces cas.

### (d) Une commande de CLI libre

Je l'écarte : impossible de garantir le résultat, de le tester en acceptation, ou de vérifier qu'il
respecte la CSP.

## 4. Recommandation

### Le modèle

Je recommande le modèle (b) : une seule option --web, dont les valeurs nomment la CLI et le template
(vite-react, vite-vue, vite-svelte, angular), et react gardé comme alias jusqu'à la 2.0. Le même
ensemble de valeurs sert pour `cleanarch-webapp --framework`. Si vous préférez des noms plus courts, le
modèle (c) se défend, mais il faudra trancher le cas de Vue (question 3).

### D'abord, réduire le coût d'un starter

Avant d'ajouter quoi que ce soit, il faut supprimer la duplication. Sans cette étape, chaque framework
coûterait autant qu'Angular.

- Un seul dossier d'app générique pour les apps de framework (Dockerfile, nginx.conf, compose.yml,
  README), avec un seul jeu de jetons de renommage au lieu de trois par framework.
- Un dossier par starter, du type .template.config/starters/&lt;id&gt;/, qui ne contient que
  `scaffold.json` et overlay/, et que template.json copie selon la valeur de --web.
- Un contrat écrit, que tout starter doit respecter :
  - le build sort dans dist/ avec un index.html ;
  - aucun script inline ;
  - un serveur de dev qui relaie /api/ et /health ;
  - des scripts build, lint et test ;
  - le nom du paquet en kebab-case.

  Angular le respecte grâce à la fusion outputPath testée plus haut. Le seul écart restant est
  `style-src 'unsafe-inline'`, que nginx.conf peut garder sous une condition.

Avec cette base, un starter Vite de plus coûte un `scaffold.json` d'une quinzaine de lignes, un
vite.config.ts, une valeur dans chacun des deux template.json et sa place dans l'acceptation.

Pour l'acceptation, plutôt qu'une jambe avec la pile Docker par starter, je propose une jambe « tous
les starters ». Elle génère un projet, ajoute chaque autre starter avec cleanarch-webapp, lance
web-scaffold, lint, test et build, construit les images et fait le curl sur chaque port. J'y
ajouterais une vérification qui manque aujourd'hui : aucun index.html produit ne contient de script
inline. C'est elle qui aurait signalé les méta-frameworks. Les jambes qui varient l'auth restent sur
react et angular.

### La première version

- angular (inchangé, CLI en 22.2.4) ;
- vite-react, avec react comme alias ;
- vite-vue ;
- vite-svelte.

Pour vite-vue et vite-svelte, les tests montrent qu'ils passent par le mécanisme actuel sans modifier
le C#. Vue et Svelte sont les deux frameworks les plus demandés après React et Angular.

### Ce que je remets à plus tard

- vite-solid, vite-preact, vite-lit, vite-qwik, vite-vanilla : ils génèrent et compilent tous, et
  coûteront peu une fois la duplication supprimée. Je les ajouterais à la demande.
- create-vue, si l'on veut le générateur officiel de Vue plutôt que vite-vue. Il faut d'abord que
  ScaffoldApp lance la CLI depuis le dossier temporaire.
- SvelteKit, Nuxt et React Router en mode SPA : faisables, mais il faut d'abord décider ce qu'on fait
  de la CSP (hash, unsafe-inline ou rien).
- Next : celui qui s'adapte le moins bien. Il demande un yaml racine modifié, une config remplacée,
  des fonctions désactivées et des scripts inline.
- Astro : un bon candidat pour un site public, mais multi-pages, avec un autre repli nginx et la CLI
  qui écrit ailleurs sans prévenir.
- Le vrai SSR : il demande une image Node, un relais différent et une autre CSP. Ce serait un ADR à
  part, plutôt en 2.0.

### L'ADR à écrire

Un nouvel ADR du template, le 0008, qui remplace le 0003. Son titre serait du genre « Une app web
vient d'un starter : une CLI, un template, un contrat ». Il dirait :

- que le template garde un catalogue de starters, et non des dossiers par framework ;
- qu'un starter est une CLI épinglée, ses arguments, ses fusions et son overlay ;
- le contrat de sortie, avec l'interdiction des scripts inline ;
- la règle de nommage des valeurs et la politique des alias ;
- que le rendu serveur est hors du périmètre, et pourquoi.

À revoir le jour où un starter ne peut plus respecter le contrat, ou si un projet a besoin du SSR.

### Le plan, par étapes

Chaque étape peut être livrée seule et laisse la matrice verte.

1. L'ADR et le contrat, sans changement de code. Fichiers : le nouvel ADR dans docs/template/decisions,
   la liste des décisions de `docs/template/maintaining-the-template.md` et sa section « Web sources
   and lockfiles », et le 0003 passé à « Superseded ».
2. Durcir ScaffoldApp, toujours sans changement d'option, dans `Tools/Tasks.cs` :
   - lancer la CLI avec le dossier temporaire comme dossier courant et {directory} égal au seul nom
     (ce qui règle create-vue) ;
   - fermer l'entrée standard, pour qu'une CLI qui pose une question échoue au lieu de bloquer ;
   - vérifier après le build que dist/index.html existe.
3. Factoriser sans changer les valeurs : ReactSite et AngularSite deviennent un dossier générique plus
   deux starters. Fichiers :
   - les deux template.json (symboles, sources, exclusions, port) ;
   - `docker-compose.yml` et .env.example ;
   - les README d'app et `Src/Web/AGENTS.md` ;
   - les fusions angular.json pour la sortie dans dist/ ;
   - `Tools/Tasks.cs` si WebLockfiles doit ignorer le nouveau dossier.

   Les jambes actuelles, toujours avec react et angular, prouvent que rien n'a bougé pour un projet.
4. Ajouter les nouveaux noms : vite-react, avec react en alias, dans les deux template.json. Il faut
   aussi mettre à jour :
   - le launcher (table des couleurs, tests des manifestes et du questionnaire), à faire après
     l'autre session en cours sur Launcher/ ;
   - la doc : `docs/template/using-the-template.md`, `docs/features/web-frontend.md`, overview,
     first-run, `README.md`, `bug_report.yml`.
5. Ajouter vite-vue et vite-svelte : leurs dossiers de starter, leurs valeurs, la jambe « tous les
   starters » avec la vérification des scripts inline dans `.github/workflows/template-acceptance.yml`,
   le tableau de la matrice dans maintaining-the-template.
6. Sortir la 1.1 : un commit feat, sans BREAKING CHANGE puisque l'alias garde la compatibilité.

## 5. Questions ouvertes

1. Modèle (b) avec les noms vite-xxx, ou modèle (c) avec les seuls noms de framework ?
2. L'alias react : le garder jusqu'à la 2.0, le garder pour toujours, ou casser tout de suite en
   publiant une 2.0 ?
3. Vue : create-vite vue-ts, minimal et de même forme que React, ou create-vue, officiel et plus
   complet, mais qui demande l'étape 2 et plus de fusions ?
4. Le lint : imposer un lint à chaque starter (Angular et Svelte n'en ont pas aujourd'hui), ou garder
   ce que la CLI fournit ?
5. Les méta-frameworks en SPA : acceptez-vous d'affaiblir la CSP (unsafe-inline ou hash) pour eux, ou
   restent-ils hors du catalogue tant que le SSR n'est pas traité ?
6. Le SSR : est-il un objectif à terme ? Si oui, il faut un ADR à part et une image Node.
7. Le budget CI : combien de jambes avec la pile Docker peut-on se permettre ? Ma proposition en
   ajoute une seule, quel que soit le nombre de starters.
8. Le back office : corriger la description de --web-audience pour qu'elle colle au code, ou donner
   un jour à l'audience admin un vrai contenu ?
9. Les dépendances des apps sont résolues au bootstrap, pas figées par le template. Est-ce que cela
   vous va toujours avec plus de starters, chacun ayant sa propre surface de dépendances ?

## Annexe : ce qui a été testé

Tout s'est fait dans le scratchpad de la session, hors du dépôt, avec Node 24.21.0, pnpm 11.28.2 via
corepack et Docker 29.6.2.

- Les 9 templates de create-vite 9.3.1 : génération, install, build et lint quand il existe, avec
  l'api-client importé. Tout passe.
- Angular CLI 22.2.4 : build en échec sans la fusion tsconfig puis OK avec, test OK, sortie dans dist/
  vérifiée via outputPath.
- create-vue 3.24.0 : OK depuis le dossier parent, bloqué à travers le vrai web-scaffold.
- sv 1.1.1 : OK en SSR, OK en SPA avec adapter-static, check, lint et test OK.
- create-next-app 16.4.3 : échec à l'install, puis OK en export statique après quatre corrections.
- create-nuxt 4.0.1 : build SSR et generate en SPA OK.
- create-astro 5.3.1 : build OK.
- create-react-router 8.5.0 : build SSR et SPA OK, vérification de types en échec à cause de l'import
  .ts.
- Dossier non vide : testé pour create-vite, create-vue, sv, create-astro et create-nuxt.
- Scripts inline : comptés dans 13 index.html produits.
- Le template, installé avec --debug:custom-hive depuis un `git archive` de develop :
  - un projet --web react, web-scaffold OK (51 s) ;
  - deux starters ajoutés à la main dans ce projet (vite vue-ts et SvelteKit) à travers le vrai
    web-scaffold ;
  - pnpm lint, test et build OK sur les trois apps ;
  - deux images Docker construites et lancées : le lien profond répond en 200 avec la CSP.

Ce qui n'a pas été testé :

- l'exécution réelle dans un navigateur, donc le blocage des scripts inline est déduit et non observé ;
- l'option csp de SvelteKit ;
- `--reset` de create-next-app ;
- le comportement d'une CLI avec l'entrée standard fermée ;
- l'acceptation complète (`verify`) : rien n'a été modifié dans le dépôt.
