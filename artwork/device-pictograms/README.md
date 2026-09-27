# Compact device pictograms

UCR's compact device symbols are precision-built vectors, not downscaled controller images.

The source geometry lives in:

`UCR/Views/Controls/DevicePictogramControl.cs`

## Rules

- 56 x 32 design grid.
- Flat fill only: no glow, no surrounding chip and no K1/X1/P2 text.
- Shape communicates device family; configured colour communicates the individual device.
- Repeated elements are mechanical geometry. Keyboard keys, face-button clusters and stick circles use fixed coordinates.
- Keep only high-value identity anchors. These are pictograms, not miniature illustrations.
- Normal display target: about 46-48 px wide by 28-30 px high.
- Arcade stick = wide control deck, offset stick on the left, button bank on the right. Never a centred ball-and-base silhouette that can read as a person.

Edit the named builder method for a family to change its geometry. All compact UCR surfaces consume the same source automatically.
