# Device glyph masters

This folder is the editable source set for UCR's monoline device glyphs.

## Source of truth

Edit the files in `masters/`. The runtime copies used by UCR live in
`UCR/Assets/DeviceGlyphs/`.

After changing a glyph, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Sync-DeviceGlyphs.ps1
```

Use `-Check` to verify that the runtime copies still match the masters.

## Artwork contract

- Transparent PNG.
- Monochrome line artwork. White is conventional, but RGB is ignored at runtime.
- Alpha defines the glyph shape; UCR recolours the glyph dynamically.
- No baked background, shadow, glow, or device/player colour.
- Keep the whole controller comfortably inside the canvas so it survives small UI sizes.
- 128x80 is the preferred working canvas. A replacement may be larger; UCR scales it down at render time.
- Keep the filename stable unless `DeviceGlyphControl.AssetFor()` is deliberately changed.

This directory exists specifically so the icon set can be adjusted independently of the UI code.
