# UCR visual-release protection — activation and limits

Updated 6 November 2026. The owner commissioned this because prompts and self-QC
did not prevent unsolicited cosmetic changes from reaching their daily program.

## Implemented safeguards

The existing Windows build still compiles UCR, runs NUnit and real WPF tests,
starts UCR.exe, and captures 22 named WPF image states. It now uploads the
executable under **UCR-Unverified-Windows**, explicitly not an approved release.

In the **same GitHub Actions run**, the `visual-approval` job runs only after
Windows succeeds. It retrieves screenshots from the actual preceding build,
runs offline rejection tests and `verify_visual_artifacts.py` (missing,
unexpected, identical or corrupt PNGs and protected-baseline drift), then runs
`independent_review.py` with the design constitution, the exact commit
difference, real screenshots and the Windows run identity. Missing API key,
unavailable reviewers, failed reviews or missing evidence block approval.

The `publish-approved` job depends on Windows and visual-approval success,
and only on a push to `master`. It also queries GitHub's actual branch metadata
and refuses publication if `master` is not protected. A weakly configured
protection rule is not sufficient; the owner must enable the required checks
and remove bypasses as described below. It publishes the verified binary as
`UCR-APPROVED-Windows-<full-commit-SHA>` with an approval file and executable
SHA-256 hash. Failed checks never publish that named approved artifact.

**Do not confuse these labels:** A Windows build passing means the code built.
Visual capture passing means the 22 evidence files are distinct and intact.
Only the independent design/functional approval can authorise publication.

## Activation requirements

1. **OpenAI API review access.** Configure the GitHub Actions secret
   `OPENAI_API_KEY` in repository Settings > Secrets and variables >
   Actions. An API project with actual available billing/credits is required;
   a ChatGPT subscription does not automatically supply API credits.
   Do not send credentials to a chat or commit them. Confirm the configured
   reviewer models actually exist for that API project. Missing credentials
   intentionally keep releases unapproved.

2. **Server-enforced master protection.** GitHub's repository admin must
   create a ruleset targeting `master` under Settings > Rules > Rulesets.
   Require a pull request and successful required status checks, including
   `Build Windows Release` and `Visual Approval (required for release)`.
   Forbid direct and force pushes by implementers and disallow bypasses.
   Verify this manually in GitHub: branch metadata must report
   `protected:true`. The connected GitHub tool lacks permission to make
   repository-admin ruleset changes.

   **Security caveat:** Before making the independent-review status a
   mandatory PR check in an untrusted-contributor environment, move
   secret-bearing API review into a trusted base-branch workflow. The
   current same-workflow reviewer is appropriate to gate approved
   publications but should not receive API secrets from arbitrary
   untrusted pull requests. On a private owner-only repo, protect
   workflow/script edits through rules and code ownership.

## Owner-approved visual baselines

The `quality/approved_visual_baselines/` folder may contain only
screenshots explicitly approved by the owner. No existing screenshot has
been declared owner-approved solely because an AI produced it.
If an approved baseline exists, any byte change fails the visual gate.
Intentional aesthetic changes require an owner-authorised update, rather
than silent baseline re-generation.

## Honest remaining limits

- Repository branch protection is not active until the owner enables it.
  Prior source edits can still be pushed directly to master by accounts
  with permission. The release artifact gate cannot remove those rights.
- The independent reviewer requires working API credentials. A missing
  key or quota failure blocks release; this has not been waived.
- The first trusted baselines must be approved, not invented by the AI.
- The current 22 WPF screenshots and assertions cover finite states,
  not every DPI, popup, hardware device or possible user state.
- SHA-256 baseline equality can be brittle across renderer/environment
  upgrades. Until an intentional new owner-approved baseline is created,
  such changes fail closed. No automatic baseline updates.
- An AI design reviewer is fallible; it is a veto and an escalation
  layer, not a substitute for the owner's taste.

## Operator workflow

AI implementer edits a candidate, runs Windows and visual QA, repairs
rejections without exposing broken candidate layouts to the owner, and
only treats a successful approved publication as a deliverable.
Continue to improve executable visual geometry tests and owner-approved
visual baselines. Do not mark the full protection project complete until
the branch ruleset and API reviewer have been confirmed operational.
