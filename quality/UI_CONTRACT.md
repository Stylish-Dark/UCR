# UCR canonical UI contract — enforced components and vocabulary

This is the *readable index*. The **source of truth** for text is
`UCR/Utilities/UiLabels.cs`; the **source of truth** for control appearance
is `UCR/App.xaml`. The Windows build runs
`build/quality/check_ui_contract.py` and rejects known deviations
before compilation. Changing this document alone changes nothing.

## Shared assets

| Function | Required asset | Permitted variation | Not permitted |
| --- | --- | --- | --- |
| NOT | `MappingNotToggle` | Host-specific position/size where required | Another NOT template, new colours or glyph |
| Block | `CompactBlockSwitch` | Position/visibility appropriate to inputs | Local shadow copies or invented switch styles |
| Ordinary Bind | `UcrBindActionButton` | Height, width and arrangement to fit single, paired, directional and compound bindings | An independent Bind chrome/template in a view |
| Bind options | `UcrBindOptionsButton` | Host width and location | A competing menu-button design |
| Two-button type glyph | `TwoDots` in the directional type template | None without owner permission | Replacing the approved two solid dots |
| Double-axis type glyph | `StackedAxes` in the directional type template | Requires separate owner visual approval | Treating an unapproved sketch as settled design |

## UI naming index

| Meaning | Canonical source | Display |
| --- | --- | --- |
| Unassigned input/output, including compound | `UiLabels.Bind` | Bind |
| Negation action | `UiLabels.Not` | NOT |
| Input blocking | `UiLabels.Block` | Block |
| Capture in progress | `UiLabels.Capture` | ●  PRESS INPUT |
| Axis/device group | `UiLabels.Axis` | Axis |
| Button/device group | `UiLabels.Button` | Button |
| Compound mapping type | `UiLabels.FourWay` | 4-Way |
| Filter function | `UiLabels.Filter` | Filter |
| Stick names | `UiLabels.LeftStick` / `UiLabels.RightStick` | Left stick / Right stick |

Bound controls display their actual control code or name. A face-button
glyph **X** is labelled **X**, not “Face button X”. Axis options already
sit in an axes context: **Left stick X**, not “Left stick X axis”.
“Select 4-Way” is not another name for the same **Bind** action.
Extra contextual words are only allowed when required to distinguish
two different user actions. Do not globally rename persisted
device identifiers or preset IDs just to shorten visible labels.

## Geometry authority

The *component* owns visual chrome, colour, border, pressed/hover state
and basic typography. The *host layout* owns width, height and position.
This permits a pair of Bind actions to form a horizontal pair, or four
directional buttons to form a D-pad, without creating unrelated button
appearances. Any new control template requires explicit owner approval
and a contract/test update. Correct NOT and Block appearances are
protected; do not redesign them.

## Source precedence

For Bind visual decisions, start with the actual 24 October 2026
implementation (`a22c41b387e47ae12bd4137bdea6ea9d55397055`),
not the later independently invented blue compact template. Do not
blindly roll back unrelated functions, routing, persistence or layouts.

## Hard limit of enforcement

Source-level tests prevent *specified* structural mistakes. They do
not prove every rendered pixel is well arranged. Real WPF geometry tests
and screenshot checks must still reject clipping, overlap and bad
spatial geometry. Never assert that a screenshot has owner approval
because an AI or a build generated it.

## Execution checkpoints

All implementation work must be atomic, isolated, and committed after
each coherent verified batch. Do not start the next batch when the
remaining execution budget is uncertain. Stop with a commit SHA and
explicit pass/fail status instead of running into the execution limit.
No false claims of automatic resumption or a measurable 99% guarantee.
