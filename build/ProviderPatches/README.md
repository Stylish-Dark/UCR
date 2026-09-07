# Provider source patches

`Apply-IOWrapperInputOrdering.ps1` restores the established stuck-input fix in the exact pinned IOWrapper source: physical subscription callbacks execute synchronously instead of being launched as independent scheduled tasks. This preserves press/release ordering for providers that use the shared device handler.

`Apply-CoreViGEmDs4Serialization.ps1` is applied to the exact pinned IOWrapper source before the provider build.

It changes only the ViGEm DS4 handler: axis, button, and D-pad report mutation/transmission are kept synchronous and serialized through a per-controller lock. This prevents concurrent DS4 report writes from interleaving while leaving the Xbox 360 handler unchanged.

The former pinned `Core_ViGEm.dll` override is intentionally not used, because it would overwrite the patched provider produced from source. Core Interception keeps its approved DLL override.
