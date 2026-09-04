"""Check every repository path cited in the Markdown docs against the filesystem.

A wrong path in a template's documentation costs the reader an hour, so it is worth a check
rather than a proofread. Only paths inside backticks are considered — prose mentioning a folder
by name is not a claim about the tree.

A second pass covers the files that explain themselves in comments rather than in Markdown —
the deployment manifests, the `.http` scratchpad, the build scripts. They cite a document by
bare path with no backticks around it, so the first pass cannot see them, and deleting a
documentation directory left seven such references pointing at nothing while this check stayed
green. Only the documentation directory's own prefix is considered there: unlike a bare
filename, it is unambiguous enough to check without inventing false positives out of code
identifiers.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()

# Inside backticks, anything that looks like a repo-relative path with a real extension or a
# known top-level directory.
CODE_SPAN = re.compile(r"`([^`\n]+)`")
CANDIDATE = re.compile(
    r"^(?:\./)?"
    r"(?:Src|Tests|docs|\.github|\.config)/[\w\-./]+"
    r"|^(?:[\w\-.]+\.(?:sln|slnx|props|json|yml|md|ps1|http|runsettings|minimum|csproj|py))$"
)

missing: list[str] = []
checked = 0

md_files = sorted(REPO.rglob("*.md"))
md_files = [
    p for p in md_files
    if "bin" not in p.parts and "obj" not in p.parts and ".git" not in p.parts
]

for md in md_files:
    text = md.read_text(encoding="utf-8", errors="replace")
    rel_md = md.relative_to(REPO).as_posix()

    for span in CODE_SPAN.finditer(text):
        raw = span.group(1).strip()

        # Skip command lines, globs, code, and anything with shell/expression syntax.
        if any(ch in raw for ch in "*$<>|(){}\"'") or " " in raw:
            continue
        if raw.startswith(("http://", "https://", "-", "/")):
            continue

        cleaned = raw.removeprefix("./")

        if "/" not in cleaned:
            # A bare filename in prose is not a claim that it sits at the repository root.
            # It only has to exist somewhere in the tree.
            #
            # The extension must be a real file type from a closed list. Without that, code
            # identifiers (`TodoItem.MaxTags`), error codes (`auth.required`), versions
            # (`net10.0`) and addresses (`127.0.0.1`) all look like filenames and the check
            # drowns in noise it invented.
            if not re.match(
                r"^[\w\-.]+\."
                r"(?:sln|slnx|props|json|ya?ml|md|ps1|sh|http|runsettings|minimum|csproj|py|cs)$",
                cleaned,
            ):
                continue
            checked += 1
            found = any(
                p for p in REPO.rglob(cleaned)
                if "bin" not in p.parts and "obj" not in p.parts and ".git" not in p.parts
            )
            if not found:
                missing.append(f"{rel_md}: `{raw}` (not found anywhere in the tree)")
            continue

        if not CANDIDATE.match(cleaned):
            continue

        checked += 1
        # A path may legitimately name a directory or a file.
        if not (REPO / cleaned).exists():
            missing.append(f"{rel_md}: `{raw}`")

print(f"Scanned {len(md_files)} Markdown file(s); checked {checked} path reference(s).")

# --- Second pass: bare docs/ references in the files that comment rather than document. --------

# Trailing punctuation belongs to the sentence, not to the path. A possessive is the one that
# does not look like punctuation: a reference written as "<path>'s own recommendation" names a
# document, not a directory whose last segment ends in an apostrophe-s.
TRAILING = re.compile(r"(?:'s|[.,;:)\]]+)$")
BARE_DOC_PATH = re.compile(r"(?<![\w`/])docs/[\w\-./]+")

COMMENTING_SUFFIXES = (".yaml", ".yml", ".http", ".ps1", ".py", ".props", ".cs", ".editorconfig")

commenting_files = sorted(
    p for p in REPO.rglob("*")
    if p.is_file()
    and p.suffix in COMMENTING_SUFFIXES
    and "bin" not in p.parts
    and "obj" not in p.parts
    and ".git" not in p.parts
    and ".vs" not in p.parts
)

bare_checked = 0

for source in commenting_files:
    text = source.read_text(encoding="utf-8", errors="replace")
    rel_source = source.relative_to(REPO).as_posix()

    for match in BARE_DOC_PATH.finditer(text):
        cited = TRAILING.sub("", match.group(0))

        bare_checked += 1

        if not (REPO / cited).exists():
            missing.append(f"{rel_source}: {cited}")

print(
    f"Scanned {len(commenting_files)} commented file(s); "
    f"checked {bare_checked} bare docs/ reference(s)."
)

print()

problems = sorted(set(missing))

if problems:
    print(f"PROBLEMS ({len(problems)}):")
    for entry in problems:
        print(f"  ! {entry}")
    sys.exit(1)

print("Every cited path exists.")
