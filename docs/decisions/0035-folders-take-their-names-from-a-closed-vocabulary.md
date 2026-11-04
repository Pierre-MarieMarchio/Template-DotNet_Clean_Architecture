# 0035. Folders take their names from a closed vocabulary

Status: Accepted

## Context

A feature's files spread over several projects, one per layer, and each project could name its
folders as its authors see fit. Free names drift: one feature keeps its handlers in `Handlers/`,
another in `UseCases/`, a third in `Services/`, and a `Helpers/` or a `Managers/` collects what found
no home. A reader then cannot tell from a path what a file is, a generator cannot know where to write
the next one, and removing a feature means finding its files by reading them. The usual .NET answer,
a convention written in a page, holds until the first change that does not read it.

## Decision

- **Every project names its folders from a closed list of words.** A project root holds its project
  file, one composition file and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
  Under `Common/` and under `Features/<Feature>/`, each level answers one question, in order: does it
  know a feature, what role does it play (`UseCases/`, `Ports/`, `Model/`, `Access/`, `Rows/`, …),
  what is it (`Commands/`, `Dtos/`, `Repositories/`, …), and which one is it. The list of words is per
  project and per layer, and [the project layout](../reference/project-layout.md) holds every word
  and the suffix its files take.
- **A test holds the list in both directions.** `LayoutConventionTests` refuses a folder whose word
  is not its project's, and a project with no vocabulary of its own;
  `FeatureFolderVocabularyTests` refuses a file that does not carry its folder's suffix, an exemption
  that names no file any more, and a type under `Helpers/` that is not a static class.
- **A new word is a change of the list**: an entry in `LayoutConventionTests`, a row in the project
  layout, and a review that asks whether an existing word already says it.

## Alternatives considered

- **Folder names left to each feature, with a convention page.** No list to keep, and a tree that
  reads differently in every feature once the page is not read.
- **Folders by technical kind at the project's root**, `Controllers/`, `Services/`, `Validators/`.
  Familiar, but a feature's files scatter across the tree, and nothing shows which ones leave with
  it.
- **Suffixes checked, folders free.** The type names stay readable, and a path still says nothing a
  generator or a reader can rely on.
- **An open list with a deny list of vague words.** It stops `Utils/` and `Manager/`, and lets every
  other synonym in.

## Consequences

- A path says what a file is and which feature it belongs to, so `Features/<Feature>/` is everything
  that leaves with a feature, and the generators write a feature's files where its words say.
- A file that fits no word is a design question asked in review, not a folder created on the way.
- A project added under `Src` fails the build until it has its vocabulary, as
  [adding a project](../reference/project-layout.md#adding-a-project) says.
- The list grows by deliberate edits: a word is cheap to add, and each one is a word every reader
  must learn.

[Inside a feature](../architecture/modules-and-boundaries.md#inside-a-feature) describes the four
levels.

## Revisit when

Most new folders need a new word, which means the list no longer describes how features are built,
or a feature needs a shape the four levels cannot express.
