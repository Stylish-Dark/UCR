# Provider source patches

`Apply-IOWrapperInputOrdering.ps1` restores the established stuck-input fix in the exact pinned IOWrapper source: physical subscription callbacks execute synchronously instead of being launched as independent scheduled tasks. This preserves press/release ordering for providers that use the shared device handler.

`CoreViGEmOutput.patch` serializes acquisition, release and report writes for each virtual Xbox 360 or DualShock 4 controller. New targets begin with neutral reports. Stale subscribers cannot write to a reacquired target, and successful writes return success to UCR. Xbox trigger conversion covers the signed input range in equal-width byte steps.

The former pinned `Core_ViGEm.dll` override is intentionally not used, because it would overwrite the patched provider produced from source. Core Interception keeps its approved DLL override.
