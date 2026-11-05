# Owner-approved visual baselines

This directory intentionally contains no screenshot baselines yet.

Only the owner can approve a screenshot as an authoritative aesthetic
reference. The implementer and an independent AI reviewer must not
promote an automatically generated or historically passing screenshot
to owner-approved status.

Once approved, place its real WPF PNG here using the exact filename in
`build/quality/expected_screenshots.txt`. The visual evidence guard
will reject any byte-for-byte change to that protected rendered view.

To change a protected visual design intentionally, secure the owner's
approval for the new appearance first; do not update a golden merely
because a candidate fails. Mechanical geometry checks and independent
reviews remain mandatory even when baseline matches.
