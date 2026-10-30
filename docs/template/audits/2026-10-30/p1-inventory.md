# P1 - Inventaire chiffré : Skyted template (ours) vs jasontaylordev/CleanArchitecture (concurrent)

Date de l'inventaire : 2026-10-30. Lecture seule : seules des requêtes GET ont été faites.
Clones : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/ours` (develop), `.../repos/competitor-main` (main), `.../repos/competitor-release` (tag v10.8.0).

## 1. Code et tests (clones)

| Indicateur | Notre dépôt (ours, develop) | Concurrent main | Concurrent v10.8.0 |
|---|---|---|---|
| Projets `.csproj` | 48 | 13 | 13 |
| LOC C# hors `obj/`, `bin/`, `Migrations/`, fichiers nommés `*Generated*` (wc) | 152 631 | 3 734 | 3 730 |
| Fichiers de test (contenant `[Fact]`/`[Test]`/`[Theory]`) | 535 | 12 | 12 |
| Attributs de test `[Fact]`/`[Test]`/`[Theory]` | 3 778 | 34 | 34 |
| Pages de doc (`docs/**/*.md`) | 88 | 5 | 0 |
| Workflows CI (`.github/workflows`) | 7 | 4 | 4 |
| Templates `template.json` | 4 (racine + webpackage, webapp, presentation) | 2 (racine + ca-use-case) | 2 |
| Item templates | 0 (sous-templates de solution : webpackage, webapp, presentation) | 1 (`ca-use-case`) | 1 |

Notes de mesure :
- LOC : les fichiers générés ne sont exclus que par leur nom ; le chiffre est approximatif.
- Tests : les `[InlineData]` ne sont pas comptés ; les cas paramétrés `[Theory]` ne comptent donc que pour une méthode.
- Concurrent main : 5 pages de doc ; le tag v10.8.0 n'en a aucune (différence main/tag).

## 2. Options des templates (lues dans `template.json`)

Le fichier de notre dépôt commence par un BOM : lecture avec `utf-8-sig`.

**Notre racine** (`.template.config/template.json`, `cleanarch-webapi`, « .NET Clean Architecture API ») :
- `auth` : local, external, none (refus combinés : none + examples none, none + files false, etc.)
- `database` : postgresql, sqlserver, mysql, sqlite
- `examples` : all, notes, none
- `files`, `ci`, `sonar`, `agentFiles` : booléens
- `web` : none, placeholder, angular, vite-react, vite-vue, vite-svelte, react-router, blazor-wasm
- `webAudience` : public, admin ; `webRendering` : spa, ssr
- `presentation` : api, blazor-server, mcp, avalonia, wpf, worker
- Ports Kestrel et AppHost (`kestrelHttpPort`, `appHost*Port`, etc.) : entiers

**Nos sous-templates** :
- `webapp` (`cleanarch-webapp`) : framework (placeholder, angular, vite-react, vite-vue, vite-svelte, react-router, blazor-wasm), audience, rendering, auth, examples, workspace, files, ci, agentFiles, hasClient
- `presentation` (`cleanarch-presentation`) : framework (blazor-server, avalonia, mcp, wpf), auth, examples, database, files, agentFiles, hasClient, hasApi
- `webpackage` (`cleanarch-webpackage`) : pas d'option

**Concurrent racine** (`ca-sln`, « Clean Architecture Solution ») :
- `ClientFramework` : Angular, React, None
- `Database` : postgresql, sqlite, sqlserver (pas de mysql)
- Ports Kestrel et AppHost : entiers

**Concurrent item template** (`ca-usecase`, « Clean Architecture Solution Use Case ») : `useCaseType` : command, query.

Comparaison : notre template expose 7 choix de web, 6 de présentation, 4 bases, 3 modes d'auth ; le concurrent expose 3 choix de front, 3 bases, aucune auth.

## 3. Vie du projet (API GitHub, GET uniquement)

| Indicateur | Notre dépôt (Pierre-MarieMarchio/Template-DotNet_Clean_Architecture) | Concurrent (jasontaylordev/CleanArchitecture) |
|---|---|---|
| Stars | 1 | 20 691 |
| Forks | 0 | 4 295 |
| Dernier push (`pushed_at`) | 2026-10-30T10:21:00Z | 2026-10-26T16:43:54Z |
| Branche par défaut | develop | main |
| Contributeurs (`anon=true`, première page) | 3 | 100 (première page seulement, valeur plancher) |
| Releases, total listées | 1 (v1.0.0, 2026-10-22) | 22 |
| Releases sur 24 mois (depuis 2024-10-30) | 1 | 17 |
| Répartition des releases du concurrent | - | 2022-06 : 2 ; 2022-10 : 1 ; 2023-06 : 1 ; 2023-11 : 1 ; 2025-07 : 1 ; 2025-11 : 1 ; 2026-02 : 1 ; 2026-03 : 14 (dont v10.8.0 le 2026-03-23) |
| Issues ouvertes (search `is:issue`) | 10 | 8 |
| Issues fermées (search `is:issue`) | 6 | 539 |
| Champ `open_issues_count` du dépôt (inclut les PR) | 14 | 10 |
| Discussions | désactivées (HTTP 410) | activées : 367 discussions |

Cadence : notre dépôt compte une seule release ; le concurrent publie en rafales (14 releases en mars 2026) et peu le reste du temps.

### Issues ouvertes de notre dépôt (les 10 : toutes à 0 commentaire)
Ordre : nombre de commentaires décroissant, puis ordre de l'API.
- 0 : « Later: the other create-vite templates (Solid, Preact, Lit, Qwik, vanilla) » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/118
- 0 : « Later: web frameworks with their own generator (Ember, SvelteKit, Nuxt, Analog, Astro, Next) » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/119
- 0 : « Later: around the apps (Aspire, Playwright, Storybook, Expo or Capacitor) » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122
- 0 : « Later: desktop and mobile clients of the API (WPF, WinForms, MAUI, Uno, WinUI) » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/120
- 0 : « Later: server hosts on the application layer (MVC, Razor Pages, gRPC, MCP server, worker, console) » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/121
- 0 : « web-join refuses a WebAssembly app that created its own workspace » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/151
- 0 : « Generated paths reach 176 characters: a deep folder on Windows cannot commit » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/166
- 0 : « Option descriptions name a choice never offered, or a flag the launcher answers » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/165
- 0 : « Later: dotnet cleanarch, the project's own CLI, with the launcher only creating » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/163
- 0 : « Avalonia: headless UI tests once Avalonia.Headless.XUnit supports xunit.v3 4.x » https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/141

### Issues ouvertes du concurrent (les 8 ouvertes ; search par commentaires)
- 3 : « Add Support for MySQL » https://github.com/jasontaylordev/CleanArchitecture/issues/1571
- 2 : « Wrong API Port is used by the Angular Frontend in default Template. » https://github.com/jasontaylordev/CleanArchitecture/issues/1594
- 2 : « Add Support for Vue » https://github.com/jasontaylordev/CleanArchitecture/issues/1570
- 2 : « Add --minimal option to exclude sample code from template » https://github.com/jasontaylordev/CleanArchitecture/issues/1192
- 2 : « Support SvelteKit with Static Adapter for SPA Client-Only Applications » https://github.com/jasontaylordev/CleanArchitecture/issues/1625
- 0 : « Add Support for Blazor » https://github.com/jasontaylordev/CleanArchitecture/issues/1444
- 0 : « 10.8.0 now fails to build because of NU1902/NU1903 security advisories » https://github.com/jasontaylordev/CleanArchitecture/issues/1675
- 0 : « Dependency Dashboard » https://github.com/jasontaylordev/CleanArchitecture/issues/1461

### Discussions du concurrent (367 au total ; top 10 par commentaires)
- 27 : « This is not exactly The Clean Architecture style » https://github.com/jasontaylordev/CleanArchitecture/discussions/482
- 16 : « Repository in Asp Core Clean Architecture » https://github.com/jasontaylordev/CleanArchitecture/discussions/405
- 10 : « How to decouple Identity from Domain Layer? » https://github.com/jasontaylordev/CleanArchitecture/discussions/415
- 9 : « Email services are part of Infrastructure, Email templates? » https://github.com/jasontaylordev/CleanArchitecture/discussions/685
- 8 : « Question: Performance overhead of throwing exception for validation Errors? » https://github.com/jasontaylordev/CleanArchitecture/discussions/401
- 8 : « Service Layer » https://github.com/jasontaylordev/CleanArchitecture/discussions/473
- 8 : « Show your clean architecture implementation » https://github.com/jasontaylordev/CleanArchitecture/discussions/631
- 8 : « Why MediatR? » https://github.com/jasontaylordev/CleanArchitecture/discussions/1009
- 8 : « gRPC Protos » https://github.com/jasontaylordev/CleanArchitecture/discussions/632
- 7 : « Can you create a schedule jobs solution? » https://github.com/jasontaylordev/CleanArchitecture/discussions/395

## 4. Distribution NuGet

| Élément | Notre dépôt | Concurrent |
|---|---|---|
| PackageId trouvés | `CleanArch.Create` et `CleanArch.WebApi.Templates` (dans nos `.csproj`) | `Clean.Architecture.Solution.Template` (indiqué dans le README : `dotnet new install`) |
| Registration nuget.org (HTTP 200) | 1 page ; version 1.0.0 pour chacun des deux | 2 pages ; dernière version 10.8.0 |
| Téléchargements | non obtenus | non obtenus |

Les compteurs de téléchargements ne sont pas dans la page statique nuget.org (rendus côté client) ; l'API de recherche n'a pas renvoyé de JSON. Le chiffre reste à relever à la main sur nuget.org.

## 5. Ce qui n'a pas été vérifié
- Téléchargements NuGet (les deux dépôts).
- Nombre exact de contributeurs du concurrent (la première page est plafonnée à 100).
- LOC exactes : fichiers générés non exclus par contenu.
- Build et tests : non lancés (inventaire statique, lecture seule).
