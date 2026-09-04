#!/usr/bin/env python3
"""Merge the Cobertura reports coverlet writes, then enforce a line-coverage floor.

Why this exists rather than a `dotnet test` flag: coverlet writes one report per test
project, and the same product assembly is exercised by more than one of them. Summing the
`lines-valid` / `lines-covered` attributes across those files double-counts every shared
line and produces a number that is wrong in whichever direction the overlap happens to
lean. The correct merge is a union over (source file, line): a line is covered if any
suite covered it. That is what this script does, with the standard library only, so it
runs identically on a developer's Windows box and on an ubuntu runner.

Usage:
    coverage-gate.py --minimum 62 [--root TestResults] [--summary out.md] [--json out.json]

Exit status: 0 when total line coverage >= --minimum, 1 when below it, 2 on bad input
(no reports found, unparseable XML). "No reports" is a failure on purpose: a coverage gate
that passes because it measured nothing is the same false green as a test run with no tests.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

_CONDITION = re.compile(r"\((\d+)/(\d+)\)")


def _iter_reports(root: Path) -> list[Path]:
    return sorted(root.rglob("coverage.cobertura.xml"))


def _merge(reports: list[Path]) -> dict[str, dict[str, dict]]:
    """assembly -> {"lines": {(file, line): hits}, "branches": {(file, line): (covered, total)}}"""
    merged: dict[str, dict[str, dict]] = {}

    for report in reports:
        root = ET.parse(report).getroot()

        for package in root.iter("package"):
            assembly = package.get("name") or "(unnamed)"
            bucket = merged.setdefault(assembly, {"lines": {}, "branches": {}})

            for cls in package.iter("class"):
                filename = cls.get("filename") or ""

                for line in cls.iter("line"):
                    number = int(line.get("number") or 0)
                    hits = int(line.get("hits") or 0)
                    key = (filename, number)

                    # Union: covered by any suite counts as covered.
                    bucket["lines"][key] = max(bucket["lines"].get(key, 0), hits)

                    if line.get("branch") == "true":
                        match = _CONDITION.search(line.get("condition-coverage") or "")
                        if match:
                            covered, total = int(match.group(1)), int(match.group(2))
                            previous = bucket["branches"].get(key, (0, total))
                            bucket["branches"][key] = (max(previous[0], covered), total)

    return merged


def _rates(bucket: dict[str, dict]) -> tuple[int, int, int, int]:
    lines = bucket["lines"]
    branches = bucket["branches"].values()
    return (
        sum(1 for hits in lines.values() if hits > 0),
        len(lines),
        sum(covered for covered, _ in branches),
        sum(total for _, total in branches),
    )


def _percent(covered: int, total: int) -> float:
    return 100.0 * covered / total if total else 100.0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="TestResults", help="directory searched recursively")
    parser.add_argument("--minimum", type=float, required=True, help="line-coverage floor, in percent")
    parser.add_argument("--summary", help="write a Markdown table here (append)")
    parser.add_argument("--json", dest="json_out", help="write the merged totals here")
    args = parser.parse_args()

    root = Path(args.root)
    reports = _iter_reports(root)

    if not reports:
        print(f"::error title=No coverage report::No coverage.cobertura.xml under '{root}'.", file=sys.stderr)
        return 2

    try:
        merged = _merge(reports)
    except ET.ParseError as error:
        print(f"::error title=Unreadable coverage report::{error}", file=sys.stderr)
        return 2

    rows = []
    for assembly in sorted(merged):
        lines_covered, lines_total, branches_covered, branches_total = _rates(merged[assembly])
        rows.append(
            {
                "assembly": assembly,
                "linesCovered": lines_covered,
                "linesValid": lines_total,
                "lineRate": _percent(lines_covered, lines_total),
                "branchesCovered": branches_covered,
                "branchesValid": branches_total,
                "branchRate": _percent(branches_covered, branches_total),
            }
        )

    total_lines_covered = sum(row["linesCovered"] for row in rows)
    total_lines_valid = sum(row["linesValid"] for row in rows)
    total_branches_covered = sum(row["branchesCovered"] for row in rows)
    total_branches_valid = sum(row["branchesValid"] for row in rows)
    line_rate = _percent(total_lines_covered, total_lines_valid)
    branch_rate = _percent(total_branches_covered, total_branches_valid)

    table = [
        f"### Coverage — {line_rate:.2f}% of lines (floor {args.minimum:g}%)",
        "",
        f"Merged from {len(reports)} report(s) as a union over (source file, line).",
        "",
        "| Assembly | Lines | Line rate | Branches | Branch rate |",
        "|---|---:|---:|---:|---:|",
    ]
    for row in rows:
        table.append(
            f"| `{row['assembly']}` | {row['linesCovered']}/{row['linesValid']} | {row['lineRate']:.2f}% "
            f"| {row['branchesCovered']}/{row['branchesValid']} | {row['branchRate']:.2f}% |"
        )
    table.append(
        f"| **Total** | **{total_lines_covered}/{total_lines_valid}** | **{line_rate:.2f}%** "
        f"| **{total_branches_covered}/{total_branches_valid}** | **{branch_rate:.2f}%** |"
    )
    rendered = "\n".join(table) + "\n"

    print(rendered)

    for destination in filter(None, [args.summary, os.environ.get("GITHUB_STEP_SUMMARY")]):
        with open(destination, "a", encoding="utf-8") as handle:
            handle.write(rendered)

    if args.json_out:
        Path(args.json_out).write_text(
            json.dumps(
                {
                    "minimum": args.minimum,
                    "lineRate": round(line_rate, 4),
                    "branchRate": round(branch_rate, 4),
                    "linesCovered": total_lines_covered,
                    "linesValid": total_lines_valid,
                    "reports": [str(report) for report in reports],
                    "assemblies": rows,
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )

    if line_rate + 1e-9 < args.minimum:
        print(
            f"::error title=Coverage below floor::{line_rate:.2f}% of lines covered, "
            f"floor is {args.minimum:g}%. Cover the new code or argue the floor down in a "
            f"separate commit.",
            file=sys.stderr,
        )
        return 1

    print(f"Coverage {line_rate:.2f}% >= floor {args.minimum:g}%.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
