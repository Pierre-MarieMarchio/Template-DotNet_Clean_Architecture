"""Structural validation of the GitHub workflows, for a repository whose CI has never run.

Checks the failure modes that a YAML parse alone does not catch:
  - `needs:` naming a job that does not exist
  - a step referencing an action without a SHA pin
  - `permissions:` missing at both workflow and job level
  - a `run:` block referencing an `env:` key that is defined nowhere
  - a referenced local file (script, Dockerfile, settings) that is absent from disk
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import yaml

REPO = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
WORKFLOWS = sorted((REPO / ".github" / "workflows").glob("*.yml"))

SHA_PIN = re.compile(r"^[\w\-./]+@[0-9a-f]{40}$")
ENV_REF = re.compile(r"\$\{?\{?\s*env\.([A-Za-z_][A-Za-z0-9_]*)|\$([A-Z_][A-Z0-9_]*)|\$\{([A-Z_][A-Z0-9_]*)\}")
# Local paths the workflows hand to a tool; each must exist or the step dies at run time.
# "Dockerfile" must be preceded by a path separator: the bare word also appears inside
# `::error title=Dockerfile …` messages, which are strings, not paths.
FILE_REF = re.compile(
    r"(?:\./)?("
    r"(?:\.github/scripts/|Src/|Tests/)[\w\-./]+\.(?:py|csproj|xml)"
    r"|coverage\.(?:runsettings|minimum)"
    r"|[\w\-./]+/Dockerfile"
    r")"
)

# Go template actions inside `--format '{{...}}'` declare their own variables ($p, $_);
# they are not shell parameters and must not be checked against env blocks.
GO_TEMPLATE = re.compile(r"\{\{.*?\}\}", re.DOTALL)

problems: list[str] = []
notes: list[str] = []

# Shell variables that are always present, plus anything GitHub injects.
SHELL_BUILTINS = {
    "GITHUB_STEP_SUMMARY", "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH",
    "GITHUB_REPOSITORY", "GITHUB_REF", "GITHUB_SHA", "GITHUB_WORKSPACE",
    "GITHUB_TOKEN", "GITHUB_ACTOR", "RUNNER_OS", "HOME", "PATH", "PWD",
    "IFS", "PY", "REGISTRY",
}


def walk_steps(job: dict):
    for step in job.get("steps") or []:
        if isinstance(step, dict):
            yield step


for wf_path in WORKFLOWS:
    text = wf_path.read_text(encoding="utf-8")
    name = wf_path.name

    try:
        # `on:` parses as boolean True in YAML 1.1 — that is expected, not a bug.
        doc = yaml.safe_load(text)
    except yaml.YAMLError as exc:
        problems.append(f"{name}: does not parse — {exc}")
        continue

    if not isinstance(doc, dict):
        problems.append(f"{name}: top level is not a mapping")
        continue

    jobs = doc.get("jobs") or {}
    if not jobs:
        problems.append(f"{name}: defines no jobs")
        continue

    job_names = set(jobs)

    # Workflow-level env plus per-job env, for the shell-variable check.
    wf_env = set((doc.get("env") or {}).keys())

    top_perms = doc.get("permissions")

    for job_name, job in jobs.items():
        if not isinstance(job, dict):
            problems.append(f"{name}: job '{job_name}' is not a mapping")
            continue

        # needs: must resolve
        needs = job.get("needs") or []
        if isinstance(needs, str):
            needs = [needs]
        for dep in needs:
            if dep not in job_names:
                problems.append(
                    f"{name}: job '{job_name}' needs '{dep}', which is not a job in this workflow"
                )

        if top_perms is None and job.get("permissions") is None:
            problems.append(
                f"{name}: job '{job_name}' has no permissions at job or workflow level"
            )

        if not job.get("runs-on"):
            problems.append(f"{name}: job '{job_name}' has no runs-on")

        if job.get("timeout-minutes") is None:
            notes.append(f"{name}: job '{job_name}' sets no timeout-minutes")

        job_env = set((job.get("env") or {}).keys())
        known_env = wf_env | job_env | SHELL_BUILTINS

        for step in walk_steps(job):
            uses = step.get("uses")
            if uses and not SHA_PIN.match(uses.strip()):
                problems.append(
                    f"{name}: job '{job_name}' uses '{uses}' without a 40-char SHA pin"
                )

            step_env = set((step.get("env") or {}).keys())
            run = step.get("run")
            if not run:
                continue

            # GitHub expressions and Go template actions both use {{...}}; strip them so
            # their internal variables are not mistaken for shell parameters.
            run_shell = GO_TEMPLATE.sub("", run)

            for match in ENV_REF.finditer(run_shell):
                var = match.group(1) or match.group(2) or match.group(3)
                if not var:
                    continue
                if var in known_env or var in step_env:
                    continue
                # Loop variables and locals assigned in the same block.
                if re.search(rf"(?:^|\n)\s*(?:for\s+{var}\b|{var}=|read -r[^\n]*\b{var}\b|mapfile[^\n]*\b{var}\b)", run):
                    continue
                if re.search(rf"\b{var}\b\s*\(\)", run):
                    continue
                problems.append(
                    f"{name}: job '{job_name}' step '{step.get('name', '?')}' "
                    f"references ${var}, defined in no env block"
                )

            for match in FILE_REF.finditer(run):
                ref = match.group(1)
                if "*" in ref or "$" in ref:
                    continue
                if not (REPO / ref).exists():
                    problems.append(
                        f"{name}: job '{job_name}' step '{step.get('name', '?')}' "
                        f"references '{ref}', which does not exist on disk"
                    )

print(f"Validated {len(WORKFLOWS)} workflow(s): {', '.join(p.name for p in WORKFLOWS)}\n")

if notes:
    print("Notes:")
    for note in sorted(set(notes)):
        print(f"  - {note}")
    print()

if problems:
    print(f"PROBLEMS ({len(set(problems))}):")
    for problem in sorted(set(problems)):
        print(f"  ! {problem}")
    sys.exit(1)

print("No structural problems found.")
