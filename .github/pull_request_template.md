## What changes, and why

<what this pull request changes and the reason>

## The one concern

<the single concern this pull request addresses; a refactor and a behaviour change are two pull requests>

## What ran

- [ ] `dotnet run Tools/Tasks.cs verify`: <result>
- [ ] `dotnet run Tools/Tasks.cs coverage`: <result>
- Did not run, and why: <gate and reason, or none>

## What holds each guarantee

<for each guarantee, the test that holds it, seen failing once: break the code, watch it go red, restore it>

## Assistance

Optional: fill it when a tool wrote part of this change; nothing to declare otherwise.

- Which tool wrote code: <tool and model>
- Which parts it wrote, and which by hand: <files or functions>
- How they were checked: <tests run, lines read>

## Checklist

- [ ] The docs moved with the code: pages, reference rows, ADR.
- [ ] No commit or description names an AI tool as co-author: no `Co-Authored-By`, no "Generated with" line.
- [ ] Where a tool wrote code, I added an `Assisted-by: <tool>:<model>` trailer to the commit and filled in Assistance above (recommended).
- [ ] I answer for every line of this pull request, generated or not, and can explain and maintain it
  (`docs/decisions/0028-the-author-answers-for-every-line.md`).
