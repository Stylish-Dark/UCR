# Independent AI delivery — operating procedure

**Intent:** one request from the product owner; three separate agent invocations; zero self-certification.

The runner `tools/ai_delivery.py` uses three fresh Codex CLI sessions: **director → developer → independent reviewer**, followed by **director final acceptance**. It iterates when reviewers or Windows CI reject the output. All agent responses and diagnostic evidence are written outside the working checkout to a retained audit folder.

## Usage (Windows development computer)
Prerequisites: Python 3, Git, authenticated Codex CLI, authenticated GitHub CLI (`gh auth login`), and repository access. From a clean `master` checkout:

```powershell
python tools/ai_delivery.py "Fix the six mapping-card defects without regressions"
```

This creates an **isolated Git worktree** and development branch; it does not edit your original checkout. A director writes testable acceptance criteria. The developer implements, then a fresh read-only reviewer inspects the entire delta. Failed review loops back to the developer. If review passes, the runner creates/updates **one draft PR** and waits for GitHub Actions; failing CI returns logs to the developer. On success, a separate director pass checks evidence and original intent before marking the PR ready for consideration. No auto-merge.

The initial role prompts are codified in `AGENTS.md` and `docs/PRODUCT_PHILOSOPHY.md`; the runner also provides task-scoped instructions.

**Deliberate fail-closed rule:** UI changes need actual current visual evidence. In a headless agent session without an instrumented screenshot process, the reviewer/director must decline final approval. The script reports a blocked draft rather than asserting the interface looks correct. A Windows interactive verification/screenshot process is a necessary subsequent integration.

**Existing workflows:** the repository already contains `.github/workflows/build.yml` and `.github/workflows/windows-build.yml`. This change adds no CI workflows. Their existing triggers may run more than one check per pushed commit; the runner does not create additional workflow definitions.

This is a local, opt-in agent runner: committing these files does **not** start persistent agents or make ChatGPT execute asynchronously. Authentication and a Windows runtime remain prerequisites. The process is separate from ordinary ChatGPT conversations.
