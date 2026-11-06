# UCR visual-quality architecture — no API key

Updated 6 November 2026. These safeguards enforce the owner's
design authority without an external AI reviewer or paid API service.

## What is enforced

The Windows workflow first runs
`build/quality/check_ui_contract.py`, **before compilation**.
It rejects duplicated NOT/Block/Bind styles, unauthorised local Bind
button skins, redundant action wording, and drift from the canonical
labels in `UCR/Utilities/UiLabels.cs`. The app uses shared assets
from `UCR/App.xaml`; see `quality/UI_CONTRACT.md`.

The Windows workflow then compiles the real app, runs NUnit and WPF
geometry assertions, checks startup, and uploads actual rendered WPF
screenshots. The separate offline evidence job rejects missing, corrupt,
duplicated or unexpected screenshots; it additionally checks any
existing owner-approved baselines for byte-for-byte stability.
No OpenAI API key is read or required.

The Windows build artifact is called **UCR-Unverified-Windows**.
It is a test candidate, not an approval.

An artifact labelled **UCR-APPROVED-Windows-<SHA>** is permitted
only after: successful Windows build; successful offline evidence/contract
job; GitHub reports `master` protected; and ALL listed screenshots
have actual owner-approved baseline images in
`quality/approved_visual_baselines/`. The last requirement is
currently unmet by design: no AI may invent owner approval.

## Owner's one-time GitHub safeguard

In repository Settings > Rules > Rulesets, protect `master`:
require pull requests and the successful Windows build and offline
visual evidence/component contract checks. Disallow direct/force pushes
and bypasses. Verify actual rules rather than treating AGENTS.md as
server-enforced permission. The GitHub connector here cannot install
this ruleset.

## What is not proven

A source contract makes *specified* inconsistencies structurally
impossible without also altering the contract. It does not prove all
pixel geometry, UX quality, or every possible interaction. The offline
evidence checks verify screenshot integrity, not aesthetic judgement.
No AI auto-approval is implied. The owner's approved reference
screenshots are the authority for intended appearances.

## Implementation discipline

Change shared components once, never patch each appearance separately.
Keep unrelated features and the two-dot button glyph untouched.
Commit bounded, testable batches on candidate branches and checkpoint
before a potentially risky next task. Never gamble with execution or
context limits, and never rely on automatic resumption.
