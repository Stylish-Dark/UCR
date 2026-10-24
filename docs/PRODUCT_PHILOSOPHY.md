# UCR — product philosophy (initial operational draft)

**Authority:** this document records product principles demonstrated in prior UCR feedback. It is not a substitute for new instructions from the product owner. The director may refine implementation decisions within these boundaries, not invent changes in product vision.

## Product experience
- Functionality must be powerful, immediate, predictable and visually coherent. Every affordance should clearly communicate what it does.
- Prioritise direct manipulation over modal prompts. A binding glyph is clickable, visibly enters a listening state, and can be cancelled with Escape, the same glyph or clicking away. No message-box interruptions.
- Preserve the core layout: profile list on the left; devices, filters and mappings in the main workspace; input glyphs left and output glyphs right.
- Mapping cards expand through an obvious, adequately sized, bordered chevron control. Open cards need a distinguishable visual identity. Do not use an unlabelled bare triangle.
- The **NOT** control is an explicit labelled button, not an ambiguous switch. Inversion and negation are distinct semantics; don't conflate them.
- The remove-input button is clearly identifiable with an ×. No hit target may overlap another control or disappear under an overflow menu.
- Binding controls must end cleanly before the three-dot menu and respect padding at different DPI scaling settings.
- Keyboard glyphs use a black background and device-coloured letters/outline, with optically centred labels.
- Inputs and outputs remain independently understandable: AND rows, alternative OR sources, and their bindings must be legible. Do not silently simplify advanced behaviour to achieve a visual change.
- Prefer consistent control borders, spacious enough hit areas, restraint in colour, and no unnecessary UI clutter.

## Engineering and quality
- Prefer narrow changes with explicit regression checks. Don't replace functioning semantics while polishing visuals.
- Device-colour increments, keyboard rendering, toolbar bounds, performance and responsive listening states are part of quality, not optional cosmetic details.
- Windows WPF application: verify both compilation and actual runtime/UI behaviour. GitHub Actions success is not a substitute for inspecting the visual output.
- Never call something finished based solely on code changes, screenshots of old builds, or optimistic assertions.

## Definition of complete
A change is complete only when intended behaviour is demonstrated; both relevant Windows checks are green; independent review has no unresolved blockers; UI changes have current visual evidence at representative scaling; and the project director confirms the original intent was met. Otherwise the work is **in progress** or **blocked**, not done.
