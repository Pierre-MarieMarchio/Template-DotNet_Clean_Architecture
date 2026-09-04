"""Check every repository path cited in the Markdown docs against the filesystem.

A wrong path in a template's documentation costs the reader an hour, so it is worth a check
rather than a proofread. Only paths inside backticks are considered — prose mentioning a folder
by name is not a claim about the tree.
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

# Referenced by name in ADR tables and prose without a directory, resolved relative to docs/adr.
ADR_LOCAL = re.compile(r"^\d{4}-[\w\-]+\.md$")

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

        # `docs/adr/0009` is accepted shorthand for the numbered record; resolve by prefix.
        adr_shorthand = re.match(r"^docs/adr/(\d{4})$", cleaned)
        if adr_shorthand:
            checked += 1
            if not any((REPO / "docs" / "adr").glob(f"{adr_shorthand.group(1)}-*.md")):
                missing.append(f"{rel_md}: `{raw}` (no ADR with that number)")
            continue

        if ADR_LOCAL.match(cleaned):
            checked += 1
            if not (REPO / "docs" / "adr" / cleaned).exists():
                missing.append(f"{rel_md}: `{raw}`")
            continue

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

# --- The ADR index -----------------------------------------------------------------------
# docs/adr/README.md carries a table with one row per record. A new record whose row nobody
# added is invisible to every reader who starts from the index, and the omission is silent:
# the file is still there, so no link is broken and the check above passes.
adr_dir = REPO / "docs" / "adr"
adr_problems: list[str] = []
adr_count = 0

if adr_dir.is_dir():
    index = adr_dir / "README.md"
    if not index.exists():
        adr_problems.append("docs/adr/README.md is missing, so the records have no index")
    else:
        index_text = index.read_text(encoding="utf-8")
        records = sorted(p.name for p in adr_dir.glob("[0-9][0-9][0-9][0-9]-*.md"))
        adr_count = len(records)

        for record in records:
            if record not in index_text:
                adr_problems.append(f"docs/adr/README.md has no row for `{record}`")

        # And the reverse: a row naming a record that no longer exists.
        for linked in sorted(set(re.findall(r"\(((?:\d{4})-[\w\-]+\.md)\)", index_text))):
            if not (adr_dir / linked).exists():
                adr_problems.append(f"docs/adr/README.md links `{linked}`, which does not exist")

        numbers = [int(name[:4]) for name in records]
        duplicates = sorted({n for n in numbers if numbers.count(n) > 1})
        for number in duplicates:
            adr_problems.append(f"two records share the number {number:04d}")

    print(f"Checked {adr_count} architecture decision record(s) against the index.")

print()

problems = sorted(set(missing)) + adr_problems

if problems:
    print(f"PROBLEMS ({len(problems)}):")
    for entry in problems:
        print(f"  ! {entry}")
    sys.exit(1)

print("Every cited path exists, and the ADR index matches the records on disk.")
