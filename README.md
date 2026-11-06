# Universal Control Remapper — Stylish Edition

Windows desktop input remapping application, built on the [Universal Control Remapper](https://github.com/Snoothy/UCR) project.

## Download

The [Windows build workflow](https://github.com/Stylish-Dark/UCR/actions/workflows/build.yml) publishes a `UCR-Windows` artifact containing the application, plugins, providers and editable assets. Keep the extracted directory together.

Hardware and virtual output devices may require their respective drivers. Supported providers include Interception, ViGEm, vJoy, XInput and DirectInput.

## Source

- `UCR/` — WPF application and replaceable `Assets/`
- `UCR.Core/` — device, mapping and profile logic
- `UCR.Plugins/` — built-in remapping plugins
- `UCR.FileHandler/` — Windows file integration
- `build/ProviderPatches/` — fixes applied while building the pinned IOWrapper dependency
- `.github/workflows/build.yml` — reproducible Windows build

The workflow obtains its pinned IOWrapper source during the build. No local submodule checkout or obsolete `build.ps1` initialization is required.

## License and attribution

Based on Universal Control Remapper by the original UCR contributors. Licensed under the [MIT License](LICENSE).
