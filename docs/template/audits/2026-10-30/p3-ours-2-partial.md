# p3-ours-2 (récupéré de la sortie structurée, le fichier n'avait pas pu être écrit : ENOSPC)

## Résumé

AUDIT INCOMPLET, bloqué par l'environnement. Le fichier rapport n'a PAS pu être écrit (ENOSPC : /tmp, un tmpfs de 16 Go, est plein à 100 %, 4 Ko libres de 13:05 à 13:27). Tout le contenu est donc ici, dans les constats.

Ce qui a été fait : README GitHub lu, `dnx CleanArch.Create@1.1.0-dev -- --help` (1,3 s, aide claire), génération du projet Acme.Tracker avec les défauts (10 s après une première tentative en échec sur `--yes`), `git init` à la main, lecture statique de la doc du projet généré (AGENTS.md, docs/README.md, first-run, adding-a-feature, adding-a-setting, removing-features, overview, domain-and-results) et de l'aide de Tools/Tasks.cs.

Bloqué : `dotnet run Tools/Tasks.cs bootstrap` puis `help` échouent au restore avec « No space left on device », y compris pour des packs runtime linux-x64 et l'ILCompiler NativeAOT. Je n'ai pas touché aux dossiers des autres agents (règle absolue), et l'auto-mode a refusé un nettoyage de /tmp/NuGetScratch. Les tâches 1 à 8, verify, coverage, `dotnet cleanarch` et les messages de la règle d'architecture sont « non vérifié (limite de l'environnement) ».

Frictions du template, par ordre d'importance :
- F1, à confirmer : le restore de Tools/Tasks.cs tire des packs runtime lourds, NativeAOT compris, même pour `help`. Il n'y a pas de `#:property PublishAot=false` (Tools/Tasks.cs:1 ne porte que UseAppHost=false). Les homes isolés des personas pèsent 3,2 à 3,8 Go chacun, ce qui est compatible avec l'hypothèse. Prérequis non dit : plusieurs centaines de Mo et de l'espace disque.
- F2 : avec `dnx`, `--yes` doit venir après `--` ; le message d'erreur dit « add --yes » sans dire où.
- F3 : le projet généré n'est pas un dépôt git, alors que la doc dit « commit what it writes » ; `--setup` du launcher le fait, mais je n'ai pas vérifié qu'il soit cité dans les docs.
- F4 : le launcher offre `web-app` et `presentation`, mais la doc du projet ne cite que `dotnet new cleanarch-webapp` et `cleanarch-presentation` plus `presentation-join` (overview.md:48-55). Aucune mention de `dnx` ou `CleanArch.Create` dans les .md du projet.
- F5 : pas de `dotnet cleanarch` : .config/dotnet-tools.json ne contient que dotnet-ef et dotnet-sonarscanner.
- F6 : 11 685 lignes d'outillage embarquées, dont les auto-tests du générateur.
- F7/F8 : `new-feature` n'édite rien, il imprime environ 11 à 16 édits ; 12 listes fermées, chacune gardée par un test (point fort).
- F9 : aucun guide pour un domain event ; seule la page d'architecture le décrit.
- F10 (point fort) : guide de réglage en 7 étapes avec snippet, test de limites et undo.
- F11 : le retrait des exemples est manuel, et laisse du périmé sans signal (.env, compose, appsettings, .http). `--apply` refuse après édition, donc la feature de la tâche 1 ne serait peut-être pas retirable par `--apply` une fois câblée à la main (à vérifier).
À relancer avec un /tmp libre ou CLAUDE_CODE_TMPDIR sur un autre volume.

## Constats

- **F0** [manque/bloquant] Audit non exécutable : /tmp (tmpfs 16G) plein à 100 %, bootstrap et le rapport .md ont échoué en ENOSPC. Limite d'environnement, pas friction du template.
  - Preuve : df -h /tmp : tmpfs 16G 16G 4.1M 100 % ; `dotnet run Tools/Tasks.cs bootstrap` : 'No space left on device : /tmp/NuGetScratchpmmarchio/...' ; Write du rapport : 'ENOSPC: no space left on device, write' ; df -k à 13:27 : 4 Ko libres ; du -sh personas : p2-comp-1 4.7G, p2-ours-1 3.4G, p3-comp-1 3.7G, p3-ours-1 2.7G
- **F1** [friction/important] Le restore de Tools/Tasks.cs (file-based app) télécharge des packs runtime linux-x64 et l'ILCompiler NativeAOT même pour `help` ; aucun PublishAot=false ; prérequis non dit (espace et téléchargement). Cause AOT par défaut non vérifiée.
  - Preuve : `dotnet run Tools/Tasks.cs help` : "Failed to download package 'Microsoft.AspNetCore.App.Runtime.linux-x64.10.0.9' ... runtime.linux-x64.microsoft.dotnet.ilcompiler/10.0.9 ... No space left on device" ; Tools/Tasks.cs:1 '#:property UseAppHost=false' seul ; grep PublishAot Tools/ Directory.Build.props : 0 résultat ; docs/getting-started/first-run.md Prerequisites : SDK et Docker seulement
- **F2** [friction/mineur] Sans TTY, `--yes` placé avant `--` est absorbé par dnx ; le message du launcher dit d'ajouter --yes sans préciser où (marche : `dnx CleanArch.Create@1.1.0-dev -- new -n X --yes`). Limite d'agent partielle.
  - Preuve : `dnx CleanArch.Create@1.1.0-dev --yes -- new -n Acme.Tracker` : "There's no terminal to ask questions in, and these weren't given: --presentation, --web, --auth, ... Pass them as options, or add --yes to use the defaults." ; avec --yes après -- : généré en 10 s
- **F3** [friction/mineur] Le projet généré n'est pas un dépôt git alors que la doc demande de committer ce que bootstrap écrit ; `--setup` du launcher le ferait. Citation de --setup dans les docs du projet non vérifiée.
  - Preuve : git status dans Acme.Tracker avant mon git init : 'fatal: not a git repository' ; dnx --help : '--setup  after new, run every setup step without asking: commit, bootstrap, commit, copy .env' ; AGENTS.md : 'run bootstrap and commit what it writes'
- **F4** [manque/mineur] Le launcher propose `web-app` et `presentation` (découvrables via --help) mais la doc du projet généré ne documente que `dotnet new cleanarch-webapp|cleanarch-presentation` puis presentation-join ; dnx/CleanArch.Create absents de tous les .md.
  - Preuve : dnx --help : 'web-app  add a web app to the project you're in', 'presentation  add a presentation project ...' ; docs/getting-started/overview.md:48-55 ; grep -rn 'dnx\|CleanArch.Create' --include=*.md : 0 résultat
- **F5** [fait/info] `dotnet cleanarch` n'existe pas dans le projet généré (non essayé, restore impossible).
  - Preuve : .config/dotnet-tools.json : dotnet-ef 10.0.14 et dotnet-sonarscanner 12.0.0 seulement ; aucune mention dans docs/ ni Tools/
- **F6** [choix/info] Le projet généré embarque 11 685 lignes d'outillage, auto-tests du générateur compris.
  - Preuve : wc -l Tools/*.cs : NewFeature.cs 5865, Tasks.cs 1891, RemoveFeature.cs 1670, NewUseCase.cs 1441, CoverageGate.cs 612, TestSummary.cs 206 ; NewFeature.cs:2022 expect(first.Output.Contains("Eleven edits"...))
- **F7** [friction/important] Statique, non exécuté : `new-feature` écrit les fichiers mais n'édite rien et imprime environ 11 édits à la main (AppDbContext, ApplicationModule, PersistenceModule, Program.cs, 2 compositions de test, +5 pour le balayage du premier owner), puis migration-add, row docs/README.md et api.md.
  - Preuve : docs/guides/adding-a-feature.md : 'The command writes files and edits none.' ; 'An owner feature that is the project's first prints five more' ; Tools/NewFeature.cs:2022 'Eleven edits'
- **F8** [fait/info] Statique : 12 listes fermées à compléter à la main, chacune avec un test qui échoue tant qu'elle ne la nomme pas (point fort : pas d'oubli silencieux, mais travail manuel).
  - Preuve : docs/guides/adding-a-feature.md#the-closed-lists-a-feature-joins : HostComposition, ApplicationModuleTests, OpenAPI, api.md, AppDbContext/PersistenceModelTests, LayoutConventionTests, DomainEventTests._deliberatelyUnconsumed, ApplicationPorts.NotPorts, _anonymousActions, ConfigurationSurfaceTests, BackgroundWorkTests, sln/Dockerfile/ModuleDependencyTests, coverage.minimum
- **F9** [manque/mineur] Pas de guide pas à pas pour un domain event (seulement la page d'architecture) ; pas d'outil non plus. Un host supplémentaire n'a que overview.md et `Tasks.cs help`.
  - Preuve : ls docs/guides : adding-a-feature, adding-a-setting, build-and-ci, deployment, migrations, removing-features, testing ; docs/architecture/domain-and-results.md:31-46 ; docs/getting-started/overview.md:48-55 ; Tasks.cs help : presentation-scaffold/join, web-scaffold/join
- **F10** [avance/info] Statique : guide de réglage très guidé (7 étapes numérotées, snippet de registration, test de limites, vérification par commande, undo) et gardé par ConfigurationSurfaceTests. Non exécuté.
  - Preuve : docs/guides/adding-a-setting.md (Steps 1-7, Check, Undo)
- **F11** [friction/important] Statique : retirer les exemples est manuel (dossiers + ~11 fichiers à éditer) et laisse du périmé que rien ne signale (appsettings, .env.example, docker-compose, .http, docs). `--apply` refuse une feature dont une registration existe : contradiction possible avec la tâche 1, à vérifier.
  - Preuve : docs/guides/removing-features.md : 'What to edit' (11 lignes), 'Configuration and samples: None of these breaks a build, and each goes stale' ; '--apply ... refuses, deleting nothing, once a file was edited or added or a registration exists'

## Non vérifié

- Tâches 1 à 8 : aucune exécutée (disque /tmp plein)
- bootstrap, verify, coverage, end-to-end : non exécutés
- Dotnet run Tools/Tasks.cs help : échoue au restore (ENOSPC)
- dotnet cleanarch : non essayé
- Message de la règle d'architecture cassée (tâche 8) : non obtenu
- Le rapport /out/p3-ours-2.md n'a pas pu être écrit (ENOSPC) : son contenu est dans ce StructuredOutput
- Hypothèse F1 (PublishAot par défaut) non vérifiée
- Tableau final : toutes les cellules des tâches 1 à 8 sont 'n.v.'
