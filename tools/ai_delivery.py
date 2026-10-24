#!/usr/bin/env python3
"""Fail-closed three-role Codex / GitHub delivery runner for UCR.

Runs on an authenticated developer machine. Keeps work in an isolated git worktree.
No direct changes to master and no automatic merge.
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path


def call(argv: list[str], cwd: Path, *, stdin: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    p = subprocess.run(argv, cwd=cwd, input=stdin, text=True, capture_output=True, errors="replace")
    if check and p.returncode:
        raise RuntimeError(f"Command failed ({p.returncode}): {' '.join(argv)}\n{p.stdout[-5000:]}\n{p.stderr[-5000:]}")
    return p


def codex(cwd: Path, audit: Path, role: str, prompt: str, schema: dict | None = None) -> str:
    suffix = audit / role
    argv = ["codex", "exec"]
    if role.startswith("developer"):
        argv += ["--sandbox", "workspace-write"]
    else:
        argv += ["--sandbox", "read-only", "--ask-for-approval", "never"]
    if schema is not None:
        schema_path = suffix.with_suffix(".schema.json")
        schema_path.write_text(json.dumps(schema), encoding="utf-8")
        argv += ["--output-schema", str(schema_path)]
    answer = suffix.with_suffix(".answer.txt")
    argv += ["--output-last-message", str(answer), "-"]
    result = call(argv, cwd, stdin=prompt, check=False)
    suffix.with_suffix(".stdout.txt").write_text(result.stdout, encoding="utf-8")
    suffix.with_suffix(".stderr.txt").write_text(result.stderr, encoding="utf-8")
    if result.returncode or not answer.exists():
        raise RuntimeError(f"{role} failed; inspect {audit}")
    return answer.read_text(encoding="utf-8")


def obj_schema(fields: dict, required: list[str]) -> dict:
    return {"type": "object", "properties": fields, "required": required, "additionalProperties": False}


DIRECTION_SCHEMA = obj_schema({
    "intent": {"type": "string"},
    "acceptance_criteria": {"type": "array", "items": {"type": "string"}},
    "regression_tests": {"type": "array", "items": {"type": "string"}},
    "visual_evidence_required": {"type": "boolean"},
    "out_of_scope": {"type": "array", "items": {"type": "string"}}
}, ["intent", "acceptance_criteria", "regression_tests", "visual_evidence_required", "out_of_scope"])

VERDICT_SCHEMA = obj_schema({
    "pass": {"type": "boolean"},
    "findings": {"type": "array", "items": {"type": "string"}},
    "verified_evidence": {"type": "array", "items": {"type": "string"}},
    "unverified_requirements": {"type": "array", "items": {"type": "string"}}
}, ["pass", "findings", "verified_evidence", "unverified_requirements"])


def is_clean(root: Path) -> bool:
    return not call(["git", "status", "--porcelain"], root).stdout.strip()


def include_new_files(worktree: Path) -> None:
    out = subprocess.run(["git", "ls-files", "--others", "--exclude-standard", "-z"], cwd=worktree, capture_output=True).stdout
    names = [n.decode("utf-8", "surrogateescape") for n in out.split(b"\0") if n]
    if names:
        call(["git", "add", "-N", "--", *names], worktree)


def failed_runs(worktree: Path, branch: str) -> str:
    result = call(["gh", "run", "list", "--branch", branch, "--limit", "8", "--json", "databaseId,conclusion,name,status"], worktree, check=False)
    if result.returncode:
        return result.stderr[-5000:]
    try:
        runs = json.loads(result.stdout)
    except json.JSONDecodeError:
        return result.stdout[-5000:]
    logs = []
    for run in runs:
        if run.get("conclusion") == "failure":
            detail = call(["gh", "run", "view", str(run["databaseId"]), "--log-failed"], worktree, check=False)
            logs.append(f"{run.get('name')}: {detail.stdout[-10000:]} {detail.stderr[-2000:]}")
    return "\n".join(logs)[:20000] or "CI checks failed or no completed results were available."


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("task", help="Outcome requested by the product owner")
    parser.add_argument("--attempts", type=int, default=3)
    args = parser.parse_args()
    if args.attempts < 1:
        parser.error("--attempts must be >= 1")
    for binary in ("git", "codex", "gh"):
        if not shutil.which(binary):
            parser.error(f"Missing prerequisite: {binary}")
    root = Path(call(["git", "rev-parse", "--show-toplevel"], Path.cwd()).stdout.strip())
    branch_now = call(["git", "branch", "--show-current"], root).stdout.strip()
    if branch_now != "master" or not is_clean(root):
        parser.error("Start from a clean master checkout. No files have been changed.")
    audit = Path(tempfile.mkdtemp(prefix="ucr-ai-delivery-"))
    worktree = audit / "worktree"
    branch = "ai/delivery-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    call(["git", "worktree", "add", "-b", branch, str(worktree), "master"], root)
    (audit / "task.txt").write_text(args.task, encoding="utf-8")
    print(f"Audit/worktree: {audit}", flush=True)

    philosophy = (worktree / "docs" / "PRODUCT_PHILOSOPHY.md").read_text(encoding="utf-8")
    director_prompt = (
        "Act ONLY as project director. Interpret the owner's task against the product philosophy. "
        "Do not edit code. Specify concrete acceptance criteria, regression tests, exclusions, "
        "and whether current runtime screenshots are mandatory. Don't invent evidence.\n\n"
        f"OWNER TASK:\n{args.task}\n\nPRODUCT PHILOSOPHY:\n{philosophy}"
    )
    contract = json.loads(codex(worktree, audit, "director-plan", director_prompt, DIRECTION_SCHEMA))
    (audit / "contract.json").write_text(json.dumps(contract, indent=2), encoding="utf-8")
    base = call(["git", "rev-parse", "HEAD"], worktree).stdout.strip()
    feedback = "None yet."
    pr_created = False

    for attempt in range(1, args.attempts + 1):
        task_spec = json.dumps(contract, indent=2)
        implementation = (
            "You are the DEVELOPER, not the project director or reviewer. Implement the bounded "
            "contract below and fix the latest rejection feedback. Modify only this worktree. "
            "Do not commit, push, merge or declare your own work accepted. "
            "Preserve functionality, write regression tests and run available local checks. "
            "Never fabricate builds or screenshots.\n\n"
            f"CONTRACT:\n{task_spec}\n\nREJECTION / CI FEEDBACK:\n{feedback}"
        )
        codex(worktree, audit, f"developer-{attempt}", implementation)
        include_new_files(worktree)
        diff = call(["git", "diff", base, "--stat"], worktree).stdout
        if not diff.strip():
            feedback = "No changes detected relative to original baseline."
            continue

        review_prompt = (
            "You are a NEW, INDEPENDENT QUALITY REVIEWER. Read AGENTS.md and the product "
            "philosophy. Review ALL changes since baseline using git diff and inspect tests. "
            "Do not edit any file. Check every criterion, regressions, WPF controls, hit "
            "targets and reported evidence. Pass=false for any objectively demonstrated code blocker. List missing mandatory "
            "visual requirements in unverified_requirements; CI must run before final judgement. Source changes are NOT proof of a working UI. Include concrete "
            "findings and specify which evidence was actually obtained.\n\n"
            f"BASELINE SHA: {base}\nCONTRACT:\n{task_spec}"
        )
        review = json.loads(codex(worktree, audit, f"reviewer-{attempt}", review_prompt, VERDICT_SCHEMA))
        (audit / f"review-{attempt}.json").write_text(json.dumps(review, indent=2), encoding="utf-8")
        if not review["pass"]:
            feedback = json.dumps(review, indent=2)
            print(f"Review rejected attempt {attempt}; developer receives findings.", flush=True)
            continue

        call(["git", "add", "-A"], worktree)
        if call(["git", "diff", "--cached", "--quiet"], worktree, check=False).returncode != 0:
            call(["git", "commit", "-m", f"Implement owner request (reviewed attempt {attempt})"], worktree)
        call(["git", "push", "-u", "origin", branch], worktree)
        if not pr_created:
            p = call(["gh", "pr", "create", "--draft", "--base", "master",
                      "--head", branch, "--title", f"AI delivery: {args.task[:70]}",
                      "--body", "Independent director/developer/reviewer cycle. Draft until Windows CI and evidence gates pass."], worktree)
            (audit / "pull_request.txt").write_text(p.stdout, encoding="utf-8")
            pr_created = True

        # The existing workflows, not the agents, establish Windows build/test status.
        time.sleep(10)  # Allow GitHub to register newly queued checks.
        checks = call(["gh", "pr", "checks", "--watch"], worktree, check=False)
        (audit / f"ci-{attempt}.txt").write_text(checks.stdout + "\n" + checks.stderr, encoding="utf-8")
        if checks.returncode:
            feedback = "Windows CI did not pass.\n" + failed_runs(worktree, branch)
            print(f"CI rejected attempt {attempt}; developer receives failure logs.", flush=True)
            continue

        # Fail closed: this runner has no Windows desktop screenshot capture yet.
        # Do not promote visual work on the strength of a build or AI assertion.
        changed = call(["git", "diff", base, "--name-only"], worktree).stdout.lower()
        if contract["visual_evidence_required"] or ".xaml" in changed:
            print("BLOCKED: Windows build passed, but current runtime visual evidence is not automated. Draft PR retained.", file=sys.stderr)
            return 1

        final_prompt = (
            "Act as PROJECT DIRECTOR, separate from developer and reviewer. Read AGENTS.md, "
            "the product philosophy, git diff versus baseline and the reviewer evidence. "
            "Windows CI has passed, but NEVER infer visual correctness from that. "
            "Return pass=false if any product intent is unsatisfied or mandatory visual "
            "evidence is missing. Do not edit files.\n"
            f"OWNER TASK:\n{args.task}\nBASELINE: {base}\n"
            f"CONTRACT:\n{task_spec}\nREVIEW:\n{json.dumps(review)}"
        )
        final = json.loads(codex(worktree, audit, f"director-final-{attempt}", final_prompt, VERDICT_SCHEMA))
        (audit / f"director-final-{attempt}.json").write_text(json.dumps(final, indent=2), encoding="utf-8")
        if final["pass"] and not final["unverified_requirements"]:
            call(["gh", "pr", "ready"], worktree)
            print("READY FOR OWNER CONSIDERATION (not merged): " + (audit / "pull_request.txt").read_text().strip())
            return 0
        feedback = json.dumps(final, indent=2)
        print(f"Director rejected attempt {attempt}; developer receives findings.", flush=True)

    print(f"BLOCKED, not complete. No merge. Audit/worktree: {audit}", file=sys.stderr)
    if pr_created:
        print("Draft PR: " + (audit / "pull_request.txt").read_text().strip(), file=sys.stderr)
    return 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RuntimeError, OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"BLOCKED, not complete: {exc}", file=sys.stderr)
        sys.exit(1)
