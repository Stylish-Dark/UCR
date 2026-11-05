#!/usr/bin/env python3
"""Fail-closed, separate-context design and functional review of UCR CI evidence.

This script is intended for a downstream workflow after the Windows build. It does
not change any source file and has no GitHub write token. Every API request is a new
conversation, not the developer's self-assessment.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
from pathlib import Path
import sys
import urllib.error
import urllib.request
from typing import Any

API_URL = "https://api.openai.com/v1/responses"
MAX_DIFF_BYTES = 500_000
BATCH_SIZE = 4

ISSUE = {
    "type": "object",
    "properties": {
        "location": {"type": "string"},
        "severity": {"type": "string", "enum": ["critical", "major", "minor"]},
        "problem": {"type": "string"},
        "recommended_fix": {"type": "string"},
    },
    "required": ["location", "severity", "problem", "recommended_fix"],
    "additionalProperties": False,
}
SCHEMA = {
    "type": "object",
    "properties": {
        "approve": {"type": "boolean"},
        "issues": {"type": "array", "items": ISSUE},
        "coverage_gaps": {"type": "array", "items": {"type": "string"}},
        "rationale": {"type": "string"},
    },
    "required": ["approve", "issues", "coverage_gaps", "rationale"],
    "additionalProperties": False,
}


def validated_verdict(value: Any) -> dict:
    if not isinstance(value, dict) or set(value) != set(SCHEMA["properties"]):
        raise ValueError("Reviewer returned missing/extra verdict fields")
    if type(value["approve"]) is not bool:
        raise ValueError("Reviewer approval must be boolean")
    if not isinstance(value["rationale"], str) or not value["rationale"].strip():
        raise ValueError("Reviewer must provide a rationale")
    if not isinstance(value["issues"], list) or not isinstance(value["coverage_gaps"], list):
        raise ValueError("Reviewer lists missing or malformed")
    for issue in value["issues"]:
        if not isinstance(issue, dict) or set(issue) != set(ISSUE["properties"]):
            raise ValueError("Malformed issue")
        if issue["severity"] not in {"critical", "major", "minor"}:
            raise ValueError("Unknown severity")
        if not all(isinstance(issue[k], str) and issue[k].strip() for k in issue):
            raise ValueError("Empty issue field")
    if not all(isinstance(gap, str) and gap.strip() for gap in value["coverage_gaps"]):
        raise ValueError("Malformed coverage gaps")
    if value["approve"] and (value["issues"] or value["coverage_gaps"]):
        raise ValueError("Inconsistent approval despite issues/coverage gaps")
    return value


def extract_text(response: dict) -> str:
    if response.get("status") != "completed":
        raise ValueError("Model response did not complete")
    parts = [part.get("text", "")
             for item in response.get("output", [])
             for part in item.get("content", [])
             if part.get("type") == "output_text"]
    s = "".join(parts).strip()
    if not s:
        raise ValueError("Empty reviewer output")
    return s


def review(model: str, instructions: str, content: list[dict], key: str) -> dict:
    payload = {
        "model": model,
        "store": False,
        "instructions": instructions,
        "input": [{"role": "user", "content": content}],
        "text": {"format": {"type": "json_schema", "name": "ucr_qa_verdict",
                            "strict": True, "schema": SCHEMA}},
        "max_output_tokens": 6000,
    }
    req = urllib.request.Request(
        API_URL, data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"},
        method="POST")
    import time
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=210) as res:
                result = json.load(res)
            return validated_verdict(json.loads(extract_text(result)))
        except urllib.error.HTTPError as exc:
            raw = exc.read(4096).decode("utf-8", "replace")
            try:
                error = json.loads(raw).get("error", {})
            except (ValueError, AttributeError):
                error = {}
            code = str(error.get("code") or error.get("type") or "unspecified")
            message = str(error.get("message") or "")[:300]
            if exc.code == 429 and code != "insufficient_quota" and attempt < 3:
                retry_after = exc.headers.get("retry-after", "")
                try:
                    delay = max(1, min(60, int(retry_after)))
                except (TypeError, ValueError):
                    delay = (10, 20, 40)[attempt]
                print(f"Review API temporarily rate limited ({code}); retrying in {delay}s", flush=True)
                time.sleep(delay)
                continue
            raise RuntimeError(f"Reviewer API HTTP {exc.code} ({code}): {message}") from exc
    raise RuntimeError("Reviewer API retries exhausted")


def images_from_manifest(root: Path, manifest: Path) -> list[Path]:
    expected = [x.strip() for x in manifest.read_text(encoding="utf-8-sig").splitlines()
                if x.strip() and not x.lstrip().startswith("#")]
    if len(expected) != len(set(expected)) or not expected:
        raise ValueError("Screenshot manifest is empty or contains duplicates")
    images = []
    for name in expected:
        if Path(name).name != name or not name.lower().endswith(".png"):
            raise ValueError(f"Invalid manifest screenshot name: {name}")
        path = root / name
        minimum_bytes = 500 if name == "UCR-two-button-full-circles-preview.png" else 5000
        if not path.is_file() or path.stat().st_size < minimum_bytes:
            raise FileNotFoundError(f"Required real WPF screenshot missing or empty: {name}")
        with path.open("rb") as source:
            signature = source.read(8)
        if signature != b"\x89PNG\r\n\x1a\n":
            raise ValueError(f"Invalid PNG signature: {name}")
        images.append(path)
    return images


def user_text(text: str) -> dict:
    return {"type": "input_text", "text": text}


def user_image(path: Path) -> dict:
    b64 = base64.b64encode(path.read_bytes()).decode("ascii")
    return {"type": "input_image", "image_url": "data:image/png;base64," + b64,
            "detail": "original"}


def write_report(dest: Path, outcome: dict) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    (dest / "verdict.json").write_text(json.dumps(outcome, indent=2), encoding="utf-8")
    md = ["# Independent UCR quality-control verdict", "",
          f"**Result: {outcome.get('status', 'REJECTED')}**", "",
          f"Source revision: {outcome.get('source_sha', 'unknown')}", "",
          f"Windows run: {outcome.get('windows_build_run_id', 'unknown')}", ""]
    if "error" in outcome:
        md += ["## Review failure", outcome["error"], ""]
    for index, item in enumerate(outcome.get("reviews", []), 1):
        verdict = item["verdict"]
        md += [f"## {index}. {item['role']} — {'APPROVED' if verdict['approve'] else 'REJECTED'}",
               "", verdict["rationale"], ""]
        for issue in verdict["issues"]:
            md.append(f"- **{issue['severity']} — {issue['location']}**: {issue['problem']} "
                      f"Fix: {issue['recommended_fix']}")
        for gap in verdict["coverage_gaps"]:
            md.append(f"- **Missing verification:** {gap}")
        md.append("")
    (dest / "review.md").write_text("\n".join(md) + "\n", encoding="utf-8")


def run(args: argparse.Namespace) -> int:
    out = Path(args.output)
    outcome: dict = {"status": "NOT APPROVED", "source_sha": "unknown",
                     "windows_build_run_id": "unknown", "reviews": []}
    try:
        metadata = json.loads(Path(args.metadata).read_text(encoding="utf-8"))
        sha = metadata.get("source_sha", "")
        if not isinstance(sha, str) or len(sha) != 40 or any(c not in "0123456789abcdef" for c in sha.lower()):
            raise ValueError("Invalid reviewed source revision")
        if metadata.get("windows_build_conclusion") != "success":
            raise ValueError("Reviewed Windows build was not successful")
        if not metadata.get("windows_build_run_id"):
            raise ValueError("Missing Windows build run ID")
        outcome.update({"source_sha": sha, "windows_build_run_id": metadata["windows_build_run_id"]})
        expected = images_from_manifest(Path(args.screenshots), Path(args.expected))
        constitution = Path(args.constitution).read_text(encoding="utf-8")
        diff = Path(args.diff).read_bytes()
        if len(diff) > MAX_DIFF_BYTES:
            raise ValueError("Commit diff exceeds review budget; split change to avoid incomplete QA")
        difftext = diff.decode("utf-8", "replace")
        if not difftext.strip():
            raise ValueError("No source diff was provided to the independent functional reviewer")
        key = os.environ.get("OPENAI_API_KEY", "")
        if not key:
            raise RuntimeError("OPENAI_API_KEY repository secret not configured; no reviewer ran")
        design_model = os.environ.get("UCR_DESIGN_REVIEW_MODEL", "gpt-6.1-terra")
        function_model = os.environ.get("UCR_FUNCTION_REVIEW_MODEL", "gpt-6.1-sol")
        design_instructions = (
            "You are the independent UCR design custodian. You have not implemented this work. "
            "Examine actual screenshots for poor layout and geometry, clipping, incoherent hierarchy, "
            "unnecessary nesting, visual clutter, hard-to-read labels, inconsistent controls and friction. "
            "Compare states across provided images. Apply the owner's design constitution rigorously. "
            "Do not rubber-stamp. Do not invent invisible details. If a screenshot cannot prove "
            "the relevant state, record a coverage gap. Approve only when the displayed scope "
            "is free from demonstrated defects and material coverage gaps. "
            "A defect must identify its actual image and visible evidence; no speculative complaints.")
        for start in range(0, len(expected), BATCH_SIZE):
            paths = expected[start:start + BATCH_SIZE]
            content = [user_text("DESIGN CONSTITUTION:\n" + constitution +
                                 "\nReview these actual WPF captures. Each image is preceded "
                                 "by its filename. Treat these as UI evidence, not a fully interactive test.")]
            for p in paths:
                content.extend([user_text("Image: " + p.name), user_image(p)])
            verdict = review(design_model, design_instructions, content, key)
            outcome["reviews"].append({"role": "Independent visual reviewer: " +
                                         ", ".join(p.name for p in paths), "verdict": verdict})
            write_report(out, outcome)
        function_instructions = (
            "You are an independent UCR functional-risk reviewer. You did NOT write this patch. "
            "Independently assess changed behaviours, unintended side effects, missing/weak tests, "
            "state transitions, persistence, device mapping risks, and changes lacking WPF screenshot "
            "coverage. The Windows CI reportedly passed including startup and NUnit. That is necessary, "
            "not proof of functional acceptance. Do not claim to have executed interactive hardware tests. "
            "Ground every issue in the provided diff. If material behavior changed without matching "
            "verification evidence, reject and describe the missing test. When changes are purely "
            "documentation, do not invent functional risks. Approve only with adequate evidence.")
        content = [user_text("DESIGN CONSTITUTION:\n" + constitution +
                             "\nBUILD: Windows compile, NUnit, cold startup passed. "
                             "22 WPF screenshots required, but not all interactive states represented. "
                             "\nSOURCE DIFF:\n" + difftext)]
        verdict = review(function_model, function_instructions, content, key)
        outcome["reviews"].append({"role": "Independent functional risk reviewer", "verdict": verdict})
        outcome["status"] = "APPROVED" if all(item["verdict"]["approve"]
                                               for item in outcome["reviews"]) else "REJECTED"
    except (OSError, ValueError, RuntimeError, json.JSONDecodeError) as exc:
        outcome["status"] = "NOT APPROVED"
        outcome["error"] = str(exc)
    write_report(out, outcome)
    print(f"UCR independent review: {outcome['status']}")
    print("Report: " + str(out / "review.md"))
    return 0 if outcome["status"] == "APPROVED" else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ["screenshots", "expected", "constitution", "diff", "metadata", "output"]:
        parser.add_argument("--" + name, required=True)
    return run(parser.parse_args())


if __name__ == "__main__":
    sys.exit(main())
