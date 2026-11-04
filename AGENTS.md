# Repository instructions for AI coding assistants

## Owner-directed development workflow (highest priority for this repository)

This repository is the user's single active UCR project. **For user-requested implementation and fixes, work directly on the existing `master` branch.** Do not create feature branches, draft pull requests, review-only changes, or copies as an alternative to updating the program.

- Read and follow the owner's exact feature requirements. Implement them in `master`, using small, traceable commits.
- Run the available build/tests and correct failures. Clearly distinguish tested behaviour from unverified GUI interactions.
- The user tests changes through the actual program built from `master`; merely opening a pull request is **not** delivery.
- Preserve unrelated code, existing profiles, configuration, device mappings and work. Do not force-push, reset history, or overwrite concurrent edits.
- Check the current `master` head before changes and reconcile concurrent work as necessary.
- No branch or PR unless the owner **explicitly asks for one**. Do not assume conventional GitHub/GitFlow collaboration requirements override this instruction.
- Keep explanations concise and focus on delivering the change rather than making the owner perform debugging or QA.
- Older instructions in `CONTRIBUTING.md` describing forks, GitFlow and mandatory pull requests are historical upstream contributor guidance, **not** the owner's workflow for this repository.

When unable to safely deliver directly to `master`, explain the specific blocker; never silently substitute a PR or branch.

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
2. Implement the smallest coherent change on `master` (small traceable commits). Check for collateral effects on Button, Axis, Trigger, 4-Way, AND/OR groups, input/output devices, multiple outputs, filters, and persisted assignments as relevant.
3. **Run the real Windows GitHub Actions build** (`.github/workflows/build.yml`). Check the *complete* run through tests, actual `UCR.exe` startup, screenshot generation, and build artifact upload. A commit or a successful compilation step alone is **not** completion. If the run fails, read the specific failing job logs, correct the underlying issue and rerun until green; never stop at “the build is running.”
4. **Fetch and inspect the actual WPF screenshot artifacts** from the completed build. Inspect each affected mapping mode and relevant states—not merely that screenshots were produced. Check geometry, alignment, size, clipping, text legibility, labels, controls, device selectors, menus, colour/state feedback, unnecessary whitespace and cross-mode consistency. Compare to the owner's screenshots and explicit instructions. Generate or expand WPF QA screenshots when an affected state is not represented.
5. **Act as the QC reviewer:** identify and correct additional defects yourself, including problems the owner has not individually enumerated. Add meaningful regression assertions when possible. Do **not** rewrite tests merely to make them pass if functionality or design has genuinely regressed; tests must verify the intended actual behavior and measured UI.
6. Repeat build → test → screenshot inspection → reject/fix until the implementation meets the stated acceptance criteria and the latest `master` commit is verified. Report a concise outcome and link the successful build and directly usable artifact. Be transparent about any remaining defects or hardware interactions that cannot be verified.

### Acceptance standard for UCR interface work

- **Consistent visual grammar:** the same device selectors, Bind buttons, ellipsis menus (only where warranted), Block/NOT/Invert switches, removal controls, quick-select menus and addition actions should retain their established appearance and meaning.
- **Geometry follows meaning:** one control looks like one control, opposing directions form a pair, and four directions use correct spatial/D-pad geometry. Input and output panes should have clean, proportionate, aligned boundaries and no truncation or unexplained dead space. Prefer content-driven shared WPF layout over hardcoded heights that conceal mismatches.
- **Preserve power without imposing complexity:** adding inputs, AND/OR groups, additional outputs, presets where appropriate, filters and detailed binding options must remain available and operable without cluttering simple mappings.
- **Function is not just appearance:** test actual events, bindings and relevant state transitions; binding-capture controls must visibly indicate the active capture state.
- **No false completion claims:** distinguish verified code compilation, automated tests, WPF visual inspection, real application startup, and physical-device testing. Do not call work finished while relevant known defects remain.

### Example of the successful workflow (November 2026)

The 4-Way/Axis/Button/Trigger mapping-editor overhaul improved after the assistant stopped treating commits as finished, inspected *actual GitHub Actions WPF screenshots*, removed arbitrary minimum heights, corrected clipped 4-Way output controls and missing input/output actions, used shared content-sized rows, expanded screenshot coverage, and iterated on build failures. The verified Windows run was [41844030574](https://github.com/Stylish-Dark/UCR/actions/runs/41844030574) with 406/406 main tests passing, separate WPF checks and application startup passing. This run is an **example of method, not proof that all UI issues are resolved**.

The essential rule: **the owner judges the product; the assistant owns quality control, including finding and fixing what is wrong before presenting it.**
