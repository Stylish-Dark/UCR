# Repository instructions for AI coding assistants

## Owner-directed development and visual release workflow (6 November 2026)

The owner explicitly commissioned enforceable prevention of AI-introduced aesthetic
regressions, not another promise to exercise judgement. This supersedes the older
direct-to-master/no-PR instruction in this repository.

- Do not treat a source commit, green compilation, NUnit success, or screenshot upload
  as aesthetic approval. Do not send the owner unreviewed screenshots as a debugging task.
- For future UCR interface changes use a short-lived candidate branch. A pull request
  is an internal development mechanism, not a finished deliverable. The owner must
  receive the actual approved executable; a PR by itself is not delivery.
- Keep one active candidate per affected UI component. Re-read latest branch SHA
  before editing; do not let independent chats redesign the same surface in parallel.
- Do not publish or present an executable as approved without a successful build,
  complete unique WPF capture set, all geometry/behavior assertions, and a completed
  independent review for the **same exact SHA**.
- A failed or unavailable independent reviewer means NOT APPROVED. Never re-label
  an internal QA artifact as approved, delete a failing test, regenerate reference
  screenshots to match a bad candidate, or waive a visual rejection.
- The only owner-facing GitHub artifact to identify as approved has the
  `UCR-APPROVED-Windows-<SHA>` prefix and comes from the `publish-approved`
  job after both the Windows build and the visual-approval job succeed.
- Preserve profile files, device configurations, unrelated code and existing
  visual conventions. Do not force-push or reset shared history.
- The connector cannot configure GitHub repository rules. Until branch protection
  is activated by the repository owner, candidate-branch discipline is a written
  instruction, **not** an unbypassable GitHub permission barrier. State this clearly.

### Execution-budget stop rule — mandatory

Do not gamble with a tool, execution or context limit. The available
budget cannot be measured to a mathematical 99% certainty; when uncertain,
stop **before** starting another substantial operation. Work in bounded,
coherent batches, commit each completed batch to the isolated candidate
branch, record the last commit SHA and test status, then return control
to the owner. Never leave an uncommitted multi-file refactor waiting for
automatic continuation. Do not promise background work or automatic
resumption. A deliberately incomplete but clean checkpoint is preferable
to a stuck session.

### Canonical architecture — mandatory

All shared Bind, NOT, Block and Bind-options controls must reference the
single application-level assets in `UCR/App.xaml`. All common button labels
must originate from `UCR/Utilities/UiLabels.cs`, not independently invented
strings in individual views. Consult `quality/UI_CONTRACT.md`. Run the
offline `build/quality/check_ui_contract.py` **before** Windows compilation.
An assistant may not silence a failing contract by duplicating styles,
changing the checker, or reintroducing descriptive labels. Only the
owner may authorise a new UI component variant or wording.

### Limits on aesthetic discretion

The owner specifies design. Implementers may repair measurable defects but may not
invent new labels, icons, control families, spacing rules, hierarchy, or geometry
for aesthetic preference. Derive each change from an explicit requirement or
the existing approved visual convention. Example forbidden expansions: adding
"Arrows" next to an unambiguous arrow glyph, or expanding a self-explanatory
X face-button label to "Face button X" without the owner's direction. Correct
WASD, D-pad, side-by-side pairs, delete X controls, and icon semantics are
design constraints, not opportunities for artistic reinterpretation.

Do not add goldens under `quality/approved_visual_baselines/` unless the owner
actually approved those captures. Never infer approval from a green build,
an AI verdict, or previously generated WPF screenshots.

## Non-negotiable delivery standard: owner-directed, AI-owned quality control

**The owner is the product director, not the debugger or QA inspector.** The assistant owns the result, including implementation, independent review, Windows build, automated tests, actual rendered WPF inspection, correcting defects, and re-verification. Do not hand the owner unfinished work and ask them to discover or explain UI mistakes.

The working hierarchy is:

1. **Owner (product authority):** defines intended behavior, design philosophy and acceptance criteria. Do not reinterpret or redesign the request.
2. **AI project director:** translates the request into observable, testable outcomes and ensures consistency across mapping types and existing workflows.
3. **Independent AI quality reviewer:** critically evaluates the actual result against the owner's intent, actively seeks regressions and rejects inadequate work. Do not confuse passing code tests with satisfactory visual quality.
4. **AI implementer:** fixes the rejected work and repeats the cycle.

These roles may be executed by one assistant, but the reviewer must adopt an independent, adversarial quality-check mindset rather than rubber-stamping implementation.

### Required implementation and verification loop

1. Read the request, examine the relevant existing UI/code, and understand nearby mapping types. Preserve the owner's established visual language and functional conventions. Avoid invented controls, gratuitous redesigns, arbitrary fixed heights, and changes outside scope.
2. Implement the smallest coherent change on an isolated candidate branch (small traceable commits). Check for collateral effects on Button, Axis, Trigger, 4-Way, AND/OR groups, input/output devices, multiple outputs, filters, and persisted assignments as relevant.
3. **Run the real Windows GitHub Actions build** (`.github/workflows/build.yml`). Check the *complete* run through tests, actual `UCR.exe` startup, screenshot generation, and build artifact upload. A commit or a successful compilation step alone is **not** completion. If the run fails, read the specific failing job logs, correct the underlying issue and rerun until green; never stop at “the build is running.”
4. **Fetch and inspect the actual WPF screenshot artifacts** from the completed build. Inspect each affected mapping mode and relevant states—not merely that screenshots were produced. Check geometry, alignment, size, clipping, text legibility, labels, controls, device selectors, menus, colour/state feedback, unnecessary whitespace and cross-mode consistency. Compare to the owner's screenshots and explicit instructions. Generate or expand WPF QA screenshots when an affected state is not represented.
5. **Act as the QC reviewer:** identify and correct additional defects yourself, including problems the owner has not individually enumerated. Add meaningful regression assertions when possible. Do **not** rewrite tests merely to make them pass if functionality or design has genuinely regressed; tests must verify the intended actual behavior and measured UI.
6. Repeat build → test → screenshot inspection → reject/fix until the candidate meets the stated acceptance criteria. Promote only an independently approved exact revision; never substitute a PR for a usable final executable. Report a concise outcome and link the successful build and directly usable artifact. Be transparent about any remaining defects or hardware interactions that cannot be verified.

### Acceptance standard for UCR interface work

- **Consistent visual grammar:** the same device selectors, Bind buttons, ellipsis menus (only where warranted), Block/NOT/Invert switches, removal controls, quick-select menus and addition actions should retain their established appearance and meaning.
- **Geometry follows meaning:** one control looks like one control, opposing directions form a pair, and four directions use correct spatial/D-pad geometry. Input and output panes should have clean, proportionate, aligned boundaries and no truncation or unexplained dead space. Prefer content-driven shared WPF layout over hardcoded heights that conceal mismatches.
- **Preserve power without imposing complexity:** adding inputs, AND/OR groups, additional outputs, presets where appropriate, filters and detailed binding options must remain available and operable without cluttering simple mappings.
- **Function is not just appearance:** test actual events, bindings and relevant state transitions; binding-capture controls must visibly indicate the active capture state.
- **No false completion claims:** distinguish verified code compilation, automated tests, WPF visual inspection, real application startup, and physical-device testing. Do not call work finished while relevant known defects remain.

### Example of the successful workflow (November 2026)

The 4-Way/Axis/Button/Trigger mapping-editor overhaul improved after the assistant stopped treating commits as finished, inspected *actual GitHub Actions WPF screenshots*, removed arbitrary minimum heights, corrected clipped 4-Way output controls and missing input/output actions, used shared content-sized rows, expanded screenshot coverage, and iterated on build failures. The verified Windows run was [41844030574](https://github.com/Stylish-Dark/UCR/actions/runs/41844030574) with 406/406 main tests passing, separate WPF checks and application startup passing. This run is an **example of method, not proof that all UI issues are resolved**.

The essential rule: **the owner judges the product; the assistant owns quality control, including finding and fixing what is wrong before presenting it.**
