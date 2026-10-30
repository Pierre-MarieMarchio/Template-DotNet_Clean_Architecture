# Dossier anonymisé G5 : documentation, onboarding, communauté, entretien

Deux dépôts de templates `dotnet new` pour une solution .NET 10 en Clean Architecture : Dépôt A et Dépôt B. Pour chaque axe, A est décrit d'abord, B ensuite. Les faits sont relevés le 2026-10-30. Les noms propres des dépôts, des paquets, des outils maison, des auteurs, des URL et des numéros de décision sont remplacés par des repères (`<ProjA>`, `<ProjB>`, `<outil de A>`, `<outil de B>`, `<n>`).

---

## Axe 1. Où vit la documentation et sous quelle forme

**Dépôt A**
- Le dépôt contient 5 fichiers Markdown sous `docs/` : un index de décisions, un gabarit, 3 décisions. La racine porte un README de 118 lignes.
- Le README renvoie à un site externe : `For full documentation, visit **[<site de A>](<url>)**.`
- Le site est construit avec Starlight (v0.38.1) sur Astro (v6.0.4), avec une recherche Pagefind. Son sitemap compte 17 URLs : accueil, 4 pages de prise en main, 5 pages d'architecture, 7 pages de fonctionnalités. Son source n'est pas dans le dépôt.
- Le README du projet généré fait 68 lignes et se termine par : `To learn more about the template go to the [project website](caDocsUrl).` Le projet généré ne reçoit aucune page de `docs/`.
- Le README racine porte 5 badges (build, analyse de code, version du paquet, téléchargements du paquet, réseau social), une liste « Technologies » et un tableau « Versions » qui associe chaque version de .NET (de .NET Core 3.1 à .NET 9) à une branche.
- Métadonnées du dépôt GitHub : une description d'une ligne, une page d'accueil renvoyant au site, 9 topics.

**Dépôt B**
- Le dépôt contient 88 fichiers Markdown sous `docs/` (environ 88 500 mots) : prise en main 3, architecture 10, fonctionnalités 8, guides 7, référence 3, décisions 29 (index compris), gabarits de page 3, puis un sous-dossier `docs/template/` de 4 pages et 20 décisions propres au template.
- Tout `docs/` sauf `docs/template/` est copié dans le projet généré, conditionné par les options choisies avec des marqueurs `<!--#if (...)-->`.
- `docs/README.md` s'ouvre sur un tableau : `| Folder | A page belongs here when the reader is |`, suivi d'une section « Adding a page » (où va une page, de quel gabarit partir).
- Aucun site rendu : la documentation se lit sur GitHub ou dans le clone.
- Le README racine fait 110 lignes, sans badge ; il contient une liste « What you get » de 9 puces, un démarrage rapide, un tableau d'options, une roadmap, un support, la licence. Le README du projet généré est un squelette de 90 lignes dont la première consigne est : `Replace this paragraph with what your product does, for whom, and what it is not.`
- Métadonnées du dépôt GitHub : description vide, page d'accueil vide, aucun topic.

## Axe 2. Cohérence entre la documentation, le code et les tests

**Dépôt A**
- Aucun test ne lit la documentation.
- Une décision écrit que les événements de domaine sont publiés « after `SaveChangesAsync` ». Le code d'un intercepteur EF Core :
  ```csharp
  public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(...)
  {
      await DispatchDomainEvents(eventData.Context);
      return await base.SavingChangesAsync(eventData, result, cancellationToken);
  }
  ```
  Une page du site écrit : « after the save completes, publishes any domain events ».
- Le README racine écrit « Angular 21 » ; le `package.json` du client déclare `"@angular/core": "^22.2.2"`.
- Le README du projet généré écrit « The solution contains unit, integration, and functional tests » ; le projet de tests d'intégration contient 0 test.
- Une page du site écrit « the template should build cleanly out of the box » et « All tests should pass on a freshly scaffolded solution ». Sur la version publiée, un `dotnet build` d'une génération par défaut s'est terminé avec 120 erreurs (NU1902 et NU1903 : avertissements de sécurité NuGet traités en erreurs), et 6 tests d'acceptation ont échoué faute de navigateurs Playwright. Une issue ouverte décrit le même échec de build.
- Le site contient une page de déploiement par Azure Developer CLI, avec les commandes à lancer.

**Dépôt B**
- Des tests lisent la documentation : un test exige une ligne de `docs/reference/api.md` pour chaque code d'erreur du code (et inversement) ; un autre exige une section de `docs/reference/configuration.md` pour chaque réglage ; un troisième vérifie que les tâches, options, chemins et liens cités existent.
- Dans le projet généré, ce troisième test ne lit que `AGENTS.md`, `README.md`, `CONTRIBUTING.md` et `SECURITY.md` :
  ```csharp
  private static readonly string[] _rootPages = [_agentFileName, "README.md", "CONTRIBUTING.md", "SECURITY.md"];
  ```
  Un script qui lit les chemins cités dans tout `docs/` existe dans le dépôt du template ; il est exclu du projet généré (`"Tools/<script>.cs"` dans la liste d'exclusion de `template.json`).
- Écarts relevés entre textes et fichiers :
  - `SECURITY.md` : « For the template itself, open a GitHub issue ... a public report is the fastest » ; le README : « report a vulnerability privately, in a security advisory » ; la configuration des issues : « never in a public issue » ; le dépôt a le rapport privé de vulnérabilité activé.
  - La roadmap range « WPF and Windows Forms » sous « Coming » ; `wpf` est une valeur acceptée de `--presentation`.
  - Le tableau « Service | Address » du README du projet généré ne liste pas le portail ni le serveur MCP que la page de premier lancement liste (ports 8090 et 8091).
  - La page d'utilisation du template contient deux fois la séquence `dotnet new install` / `dotnet new update` sous deux intitulés identiques.
  - L'aide de `dotnet new` liste la valeur `mysql` de `--database` (« MySQL 8.4: a table prefix per feature ... ») ; un autre paramètre du même fichier refuse cette valeur à la génération.
  - Le guide « adding a setting » demande d'écrire le validateur « beside » la classe d'options ; l'exemple cité place le validateur `internal` dans le même fichier, et un test interdit deux types publics par fichier.
- Une page de déploiement existe pour l'image, les migrations, les proxys et l'arrêt ; elle ne cible pas un fournisseur cloud précis.

## Axe 3. Les décisions d'architecture

**Dépôt A**
- 3 décisions, un gabarit et un index (`| ADR | Title | Date | Status |`).
- Sections de chaque décision : Status, Date, Context, Decision, Rationale, Consequences (« Easier » et « Harder »). Une décision contient en plus « When this decision does not apply » (renvoi vers DDD et repositories).
- Sujets : EF Core dans la couche Application ; Aspire pour l'orchestration et les tests ; `MediatR.Contracts` dans le Domain.
- Aucun test ne référence ces décisions.

**Dépôt B**
- 28 décisions de projet, plus 20 décisions propres au template.
- L'index des décisions fixe trois critères d'écriture : une équipe compétente aurait pu décider autrement ; un mainteneur doit en connaître les conséquences avant de changer ; ce n'est pas évident d'après le code, les conventions ou la pratique .NET courante.
- Les décisions propres à un projet généré sont numérotées à partir de 1000, au statut `Proposed` tant que l'équipe ne les accepte pas ; une décision remplacée passe à `Superseded by`.
- Le gabarit contient « Alternatives considered » et « Revisit when ». L'index écrit : « Where a test can hold a decision, it does, and the ADR names the test: a decision and the test that holds it change together, in the same commit. »
- Une décision liste les choix hérités du template avec une cellule « Changed by ».

## Axe 4. Le chemin de prise en main

**Dépôt A**
- README :
  ```bash
  dotnet new install <PaquetA>
  dotnet new <nom-court-A> --client-framework [angular|react|none] --database [postgresql|sqlite|sqlserver] --output YourProjectName
  aspire run        # ou : dotnet run --project .\src\AppHost
  ```
- Prérequis : .NET 10 ; Node pour Angular ou React ; un runtime de conteneurs seulement pour SQL Server et PostgreSQL (SQLite est la valeur par défaut). Trois exemples de commandes sont donnés.
- Mesure sur la version publiée (premier contact, sans lire le code) : installation 2,7 s, génération 2,2 s, `dotnet build` en échec (120 erreurs NU1902/NU1903, aucun contournement dans la documentation) ; avec `-p:NuGetAudit=false`, build sans erreur, lancement par Aspire en 12 s ; les ports de l'API et du front sont dynamiques ; l'API répond 401 sans piste d'identifiant ; deux avertissements de certificat de développement non expliqués dans la documentation. Sur 3 générations, 0 build réussi en suivant seulement la documentation.
- Le README du projet généré écrit ses chemins avec des antislashs.

**Dépôt B**
- README :
  ```bash
  dnx <OutilB>                     # pose les questions, puis génère
  cd <Company.Product>
  dotnet run Tools/<lanceur>.cs <initialisation>   # une fois : à valider par un commit
  cp .env.example .env
  docker compose up -d --build --wait
  ```
  L'API répond sur :8080, l'UI OpenAPI sur :8080/scalar/v1, un serveur de mail de test sur :8025.
- Prérequis : .NET 10.0.100 ou plus récent, Docker avec Compose v2.
- Mesure sur la branche de développement (version 1.1.0-dev, premier contact) : le mode guidé génère, valide, lance l'étape d'initialisation (2 min 27 pour le projet par défaut, 3 min 17 pour un projet riche) et prépare `.env` ; 3 257 tests passent sans conteneur en 1 min 10 ; la pile par défaut est saine en 38,7 s ; une inscription renvoie 202 et le mail arrive dans le serveur de test. Frictions relevées : l'option `--yes` est interceptée par `dnx`, donc l'exemple non interactif de la documentation ne fonctionne pas tel quel (il faut `--`) ; `GET /` répond 401 ; la suite de tests sans conteneur d'un projet riche prend 6 min 25.
- La version publiée sur nuget.org n'a pas été mesurée.

## Axe 5. Instructions pour les agents de code

**Dépôt A**
- Aucun fichier `AGENTS.md`, `CLAUDE.md`, `copilot-instructions.md` ni `.cursorrules` dans le dépôt.
- Une issue demandait d'ajouter un `AGENTS.md` pour que Copilot génère du code conforme ; elle a été ouverte le 2026-07-11 et fermée le 2026-07-16 sans commentaire.

**Dépôt B**
- Un `AGENTS.md` à la racine et un second sous `Src/Web/` : commandes, où va le code, règles de changement, définition de « terminé », ordre de priorité des sources de vérité.
- Une option de génération `--agent-files` (vrai par défaut) retire ces fichiers ; les pages qui les citent renvoient alors à la page qui porte la règle.
- Le test de citations lit ces fichiers (cf. axe 2).

## Axe 6. Reprendre une version plus récente du template dans un projet généré

**Dépôt A**
- Une discussion « Best strategy to keep a CA solution up to date » a une réponse retenue du mainteneur : « There's no automated upgrade mechanism - that's a limitation of the `dotnet new` template system itself », suivie de la procédure : désinstaller le template, installer la version qui correspond au projet.

**Dépôt B**
- Une page décrit l'entrée d'un projet généré dans un dépôt existant. Aucune page ne décrit la reprise d'une version plus récente du template.
- Le CHANGELOG définit un changement « notable » (qui modifie le projet généré) et ce que « breaking » signifie : un travail manuel dans un projet existant. La branche de développement contient deux commits `feat!` depuis la dernière release.

## Axe 7. Audience

**Dépôt A**
- 20 692 étoiles, 4 295 forks, 371 observateurs ; créé le 2019-10-07.
- Paquet NuGet : 275 469 téléchargements au total, dont 6 011 pour la dernière version.
- 103 identités de commit.

**Dépôt B**
- 1 étoile, 0 fork, 0 observateur ; créé le 2025-02-17.
- Deux paquets NuGet, version 1.0.0 : 89 et 96 téléchargements.
- 2 identités de commit humaines et 2 bots.

## Axe 8. Releases : cadence et état

**Dépôt A**
- 22 releases : 2022-06 : 2 ; 2022-10 : 1 ; 2023-06 : 1 ; 2023-11 : 1 ; 2025-07 : 1 ; 2025-11-11 : 1 ; puis 15 entre le 2026-02-28 et le 2026-03-23. La dernière date du 2026-03-23, soit 221 jours avant le relevé.
- 58 commits sur `main` depuis la dernière release : 51 d'un bot de mise à jour de dépendances, 7 de l'auteur. Le bot fusionne automatiquement les mises à jour mineures et correctifs.
- La publication du paquet est déclenchée par la publication manuelle d'une release GitHub :
  ```yaml
  on:
    release:
      types: [published]
  ```
- Une issue ouverte le 2026-10-28, sans commentaire, signale que la version publiée ne compile plus (NU1902/NU1903).

**Dépôt B**
- 1 release : la 1.0.0, le 2026-10-22, avec deux paquets joints.
- La chaîne de release utilise release-please sur la branche de développement ; le commentaire de tête du workflow : `release-please keeps a release pull request on develop, and merging it tags v<version>; the jobs below then pack, check, attest, attach and publish both packages ...`.
- Le workflow se termine en échec à chaque push depuis le 2026-10-27 à 19h : 35 échecs consécutifs sur 36 échecs au total ; dernier succès le 2026-10-27 à 18:58 UTC. Le journal : `release-please failed: Unexpected token '﻿', "﻿{ "$sch"... is not valid JSON`, juste après la lecture du fichier `.template.config/template.json`, dont les premiers octets sont `efbb bf7b`.
- La pull request de release ouverte propose 1.1.0, a été mise à jour pour la dernière fois le 2026-10-28, avant deux commits `feat!` des 2026-10-29 et 2026-10-30. Les jobs de publication sont ignorés (`skipped`).
- Un bot de mise à jour de dépendances ouvre des pull requests hebdomadaires groupées ; 28 commits de ce bot sont fusionnés.

## Axe 9. Notes de version et changelog

**Dépôt A**
- Pas de fichier CHANGELOG. Le corps de chaque release est la liste générée des pull requests (`## What's Changed`), recopiée dans le fichier de description du paquet.
- Exemple, la dernière release : 6 puces, dont 5 de bots de dépendances et une pull request de l'auteur nommée « Slim down Todo demo and add colour theming ».
- Une release majeure précédente : une seule puce (« Upgrade to .NET 10 »).
- Chaque release a une discussion d'annonce (18 discussions dans la catégorie Announcements).

**Dépôt B**
- `CHANGELOG.md` de 1 158 lignes, avec une politique écrite : `What counts as notable in a template ... anything that changes the *generated* project` et `What "breaking" means here`. Une section « 1.0.0: what the first release contains » décrit les ajouts par option.
- Le corps de la release GitHub 1.0.0 est une ligne :
  ```
  ## 1.0.0 (2026-10-22)
  ### Documentation
  * **security:** ask a SQLite project to move to a server database before it deploys
  ```
  La section « what the first release contains » du fichier n'est pas dans ce corps.
- Aucune discussion d'annonce (les discussions sont désactivées).

## Axe 10. Questions, issues, discussions

**Dépôt A**
- Configuration des issues : issues vides autorisées ; un lien « Ask a question » vers les discussions ; deux modèles Markdown (bug, fonctionnalité) sans champ obligatoire.
- 367 discussions : Q&A 184, General 122, Ideas 36, Announcements 18, Show and tell 6, Polls 1. Par année : 2021 : 60, 2022 : 90, 2023 : 102, 2024 : 64, 2025 : 27, 2026 : 22. Q&A avec réponse retenue : 38 sur 184 ; sur les 12 derniers mois : 6 Q&A, 3 avec réponse. Dernière discussion d'un utilisateur : 2026-04-17.
- Issues : 539 fermées, 8 ouvertes. Les fermetures se regroupent par lots (2025-07-16 à 07-28, 2026-02-18 à 02-25, 2026-10-11) ; deux issues de mars 2026 ont été fermées le jour même ; d'autres ont attendu de 2 à 5 mois. Une issue sur un port d'API erroné dans le front Angular, ouverte le 2026-03-26, n'a aucun commentaire du mainteneur.

**Dépôt B**
- Configuration des issues : `blank_issues_enabled: false` ; un seul lien de contact (rapport de sécurité, vers l'outil d'advisory privé) ; deux formulaires YAML (bug : version, source d'installation, commande de génération complète, étapes ; fonctionnalité).
- Discussions désactivées.
- 16 issues (10 ouvertes, 6 fermées), toutes ouvertes par l'auteur du dépôt, sans commentaire d'un tiers. Elles décrivent le plan de travail : étiquette `roadmap`, titres « Later: ... ». Les fermées l'ont été en 1 à 3 jours.
- Le README indique : « Report a bug or ask for a feature through the repository's issue forms ».

## Axe 11. Contributeurs

**Dépôt A**
- L'auteur apparaît sous 4 identités : 551, 76, 10 et 1 commits, soit environ 637 des 800 commits non automatiques (80 %). Sur le dernier an : 72 des 82 commits non automatiques (88 %) ; trois autres contributeurs y ont 7, 2 et 1 commits.
- Deux bots de dépendances : 106 et 70 commits. 103 identités de commit au total.

**Dépôt B**
- L'auteur apparaît sous 2 identités : 495 et 19 commits, soit 100 % des commits non automatiques ; un bot de dépendances (28 commits) et un bot de GitHub Actions (1). 543 commits au total.
- Sur les 100 pull requests les plus récentes : 62 de l'auteur, 36 du bot de dépendances, 2 du bot de release.

## Axe 12. Fichiers de santé de la communauté et sécurité

**Dépôt A**
- Code de conduite : présent. Guide de contribution : absent. Modèle de pull request : absent. Fichier de politique de sécurité : absent. Rapport privé de vulnérabilité : désactivé. Fichier de financement : présent. Score de santé GitHub : 71.

**Dépôt B**
- Code de conduite : absent. Guide de contribution : présent (71 lignes). Modèle de pull request : présent, tenu par un test. Fichier de politique de sécurité : présent (144 lignes). Rapport privé de vulnérabilité : activé. Fichier de financement : absent. Score de santé GitHub : 71.

## Axe 13. Demandes ouvertes ou fermées des utilisateurs de chaque dépôt et réponses déjà écrites ailleurs

**Dépôt A**
- Une issue demande une option pour générer sans les entités d'exemple ; le mainteneur répond que c'est « on the roadmap » (2026-02-19) ; une autre, fermée le 2026-10-11, demande de rendre optionnels l'exemple et l'identité.
- Issues ouvertes qui demandent le support de Vue, de Blazor, de MySQL, de SvelteKit en SPA statique.
- Des discussions demandent pourquoi MediatR, un dépôt de type repository, un service de mail, une solution de jobs planifiés ; deux issues portent sur le passage de MediatR à un modèle commercial et sur un avertissement de licence.

**Dépôt B**
- Le template expose `--examples none` (génération sans exemples), `--auth external` et `--auth none` (sans comptes locaux), `--web vite-vue`, `--web blazor-wasm` et `--presentation blazor-server`. Il n'expose pas MySQL (la roadmap le range sous « Not planned ») ni SvelteKit (un autre support Svelte, `vite-svelte`, existe).
- La documentation d'architecture liste ce qui est volontairement absent (MediatR, repository générique, soft delete, outbox, AutoMapper, Minimal APIs) avec un renvoi vers une décision ou une page ; un module de mail multilingue et des tâches planifiées sous bail sont documentés.
- Le guide de build explique que `NuGetAudit` fait échouer le build sur toute alerte connue, y compris de niveau bas, et que la règle est une épingle transitive dans `Directory.Packages.props` ; l'étape de vérification du projet lit la sortie de `dotnet list package --vulnerable --include-transitive`.
