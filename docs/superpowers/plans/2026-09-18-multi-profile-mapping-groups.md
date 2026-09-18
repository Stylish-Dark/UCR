# Multi-profile activation and mapping groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow multiple ordinary UCR profiles to run together and replace child-profile usage with optional profile-local mapping groups.

**Architecture:** Keep one composite SubscriptionState and rebuild it whenever the active profile set changes. Keep Profile.Mappings as the Main section and add MappingGroup collections for optional layers. Migrate legacy child trees into groups on load and flatten the dashboard.

**Tech Stack:** C#/.NET Framework, WPF, XML serialization, NUnit, IOWrapper.

**Spec:** `docs/superpowers/specs/2026-09-18-multi-profile-mapping-groups-design.md`

## Global Constraints
- Preserve the existing hotplug crash fix and stable crash logging.
- Active profiles remain locked for editing.
- Existing context/profile XML must remain loadable.
- No provider/backend redesign; one composite SubscriptionState remains the backend unit.

---

### Task 1: Mapping group model and legacy migration
**Files:** Create `UCR.Core/Models/MappingGroup.cs`; modify `UCR.Core/Models/Profile.cs`, `UCR.Core/UCR.Core.csproj`; test `UCR.Tests/ModelTests/ProfileTests.cs`, `UCR.Tests/ModelTests/PersistenceTests.cs`.
**Interfaces:** Produces `MappingGroup`, `Profile.MappingGroups`, `Profile.GetRuntimeMappings()`, group CRUD/copy helpers, legacy child-to-group migration.
- [ ] Write tests for group ownership, runtime inclusion, copying, and legacy child migration.
- [ ] Verify tests fail because MappingGroup does not exist.
- [ ] Implement model and migration.
- [ ] Verify tests pass on a Windows/.NET test runner when available; otherwise perform serializer/static verification locally and leave CI as compile/test gate.

### Task 2: Composite multi-profile runtime
**Files:** Modify `UCR.Core/Context.cs`, `UCR.Core/Models/Subscription/SubscriptionState.cs`, `UCR.Core/Managers/SubscriptionsManager.cs`, `UCR.Core/Models/Mapping.cs`, `UCR.Core/Models/Plugin.cs`; test `UCR.Tests/ModelTests/SubscriptionTest.cs`.
**Interfaces:** Produces `Context.ActiveProfiles`, `SubscriptionsManager.DeactivateProfile(Profile)`, composite `SubscriptionState.ActiveProfiles` and profile-scoped filter keys.
- [ ] Write tests that two profiles can be active, stopping one keeps the other active, and same-named filters are isolated.
- [ ] Verify tests fail under the single-profile runtime.
- [ ] Implement composite rebuild and profile-scoped filter state.
- [ ] Verify tests/structural checks.

### Task 3: Auto-activation and active-state UI semantics
**Files:** Modify `UCR/Utilities/AutoProfileMonitor.cs`, `UCR/ViewModels/Dashboard/ProfileItem.cs`, `UCR/ViewModels/Dashboard/DashboardViewModel.cs`, `UCR/ViewModels/ProfileViewModels/ProfileViewModel.cs`, profile page/window code-behind and `UCR/Views/MainWindow.xaml(.cs)`; update tests.
**Interfaces:** Per-profile active state and per-profile stop while retaining global stop-all.
- [ ] Add tests for multiple active ProfileItem states and auto-owned profile independence.
- [ ] Implement event refresh and per-profile toggle behavior.
- [ ] Remove child-profile tree UI/actions and flatten profile rows.
- [ ] Verify XAML parses and static event-handler references resolve.

### Task 4: Mapping group editor UI and copy/paste
**Files:** Create `UCR/ViewModels/ProfileViewModels/MappingGroupViewModel.cs`; modify `ProfileViewModel.cs`, `ProfilePage.xaml(.cs)`, `ProfileWindow.xaml(.cs)`, `UCR/UCR.csproj`; update tests.
**Interfaces:** Main section plus MappingGroups, add/rename/toggle/duplicate/copy/paste/delete group commands.
- [ ] Add view-model tests for local groups and copy independence.
- [ ] Implement group view models and section rendering.
- [ ] Keep existing mapping-card controls and edit-lock semantics.
- [ ] Verify XAML and static C# structure.

### Task 5: Persistence/import cleanup and final verification
**Files:** Modify persistence/profile-transfer validation paths and affected tests/docs.
- [ ] Ensure imported legacy child profiles migrate through the same path.
- [ ] Remove UI-only assumptions that profiles have parents.
- [ ] Run repository integrity, XML/XAML parsing, source-reference checks, and available tests/build.
- [ ] Commit the complete change and package a full push-ready repository with `.git` intact.
