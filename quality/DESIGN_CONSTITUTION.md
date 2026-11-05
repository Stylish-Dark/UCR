# UCR design constitution — owner-defined, reviewer-enforced

## Accountability

The owner commissions behaviour and product direction. The project director owns delivery quality. The developer may not approve its own work. Independent reviewers reject weak work without consulting the owner on basic craftsmanship. Escalate genuine product trade-offs only.

## Design principles

1. **Coherence:** Controls, terminology, hierarchy, spacing and geometry express consistent meaning across the entire UI. Understand nearby conventions before making changes.
2. **Frictionlessness:** Prefer the shortest understandable interaction. Eliminate redundant clicks, pointless nesting, duplicative selectors and visual noise. For example, a POV menu whose only item is POV1 is unwarranted unless it represents a meaningful distinction.
3. **Functional elegance:** Preserve flexibility without imposing complexity on simple use cases. Advanced capabilities should be discoverable, not intrusive.
4. **Geometry follows meaning:** WASD keycaps form W above A/S/D; a D-pad depicts a directional cross; opposing directions appear as an appropriate pair. Use proportionate dimensions, precise alignment, and consistent spacing.
5. **Visual restraint:** Avoid gratuitous menus, panels, labels, popups and features. Follow existing styling unless a deviation demonstrably improves usability.
6. **Stability:** Binding-type changes, conditions, expansion/collapse and device changes must not cause arbitrary layout shifts, clipping, hidden actions or broken states.
7. **Semantics and persistence:** Mappings, device selections, saved profiles, compound bindings and filtering must remain functional. Visual quality never excuses a functional regression.
8. **Scope discipline:** Fix related quality defects discovered during implementation but avoid unrelated redesign or reinterpretation of the owner's requirements.
9. **No unsolicited explanation of self-evident symbols:** Do not expand a controller X symbol to "Face button X" or add the word "Arrows" next to an obvious directional arrow glyph without specific owner authorisation. Labels distinguish genuinely ambiguous controls, not decorate or restate their glyphs.
10. **Settled visual decisions are protected:** A change request is not permission to adjust neighbouring iconography, button chrome, colours, labels, or spacing. Visual discretion is restricted to implementing the specific owner-authorised outcome and proven geometric repairs.

## Rejection criteria

Reject work for clipping, overlapping, unjustified nesting, misalignment, inconsistent visual grammar, missing controls or labels, wasted space that impairs comprehension, unintended state changes, needless interaction friction and demonstrable regressions. Examine actual rendered UI screenshots for each affected state. Recorded screenshots alone do not equal visual inspection; green tests alone do not establish design quality.

An independent reviewer gets the owner's criteria and actual evidence, not the developer's explanation of why the work is supposedly good. The reviewer should distinguish clearly evidenced defects from unverified states and possible false alarms.

## Delivery integrity

- Do not label work approved if evidence is missing, review failed, or a reviewer has not run.
- Report the exact commit SHA, Windows build run, review outcome and uncovered interaction states.
- Preserve the owner's existing work, configuration and unrelated code.
- Do not make the owner debug straightforward craftsmanship failures.
- Save a detailed checkpoint before execution limits threaten continuity; do not silently misrepresent incomplete work as finished.
