# Multi-profile activation and mapping groups design

## Goal
Replace child profiles with two simpler concepts: multiple ordinary profiles may run simultaneously, and each profile may contain optional local mapping groups such as "Player 2" or "Extras".

## Runtime
UCR keeps one backend SubscriptionState, but that state is composed from zero or more active profiles. Starting or stopping one profile tears down the current composite state and rebuilds it from the new active set. This preserves existing provider/subscription behavior and the hotplug safety work while removing the single-active-profile restriction.

Each profile contributes its main mappings plus any enabled mapping groups. Mappings from later layers with the same title override earlier mappings in the same profile, preserving the old child-profile override behavior for migrated data. Profiles do not override one another merely because mapping titles match.

Filters are profile-scoped at runtime so two simultaneously active profiles may use the same filter names without affecting one another.

## Mapping groups
A MappingGroup belongs to exactly one Profile. It has a title, enabled state, and mappings. Main mappings remain directly on Profile for backward compatibility. Groups are local organization/runtime layers, not global reusable objects.

Groups can be added, renamed, enabled/disabled while the profile is stopped, duplicated, copied, pasted, and deleted. Newly created groups start enabled because grouping a set of mappings should not silently disable them; migrated legacy children and duplicated/pasted groups start disabled so copying or migration cannot unexpectedly add live behavior. The profile page renders Main plus each group as a separate section.

## Legacy child migration
Legacy child profiles are converted when loaded. A direct child becomes a disabled mapping group on its root parent; its child-specific devices are merged into the parent and its child mappings become the group's mappings. Deeper descendants become independent groups named by their relative breadcrumb and contain the effective descendant-layer mappings needed to reproduce the former activation path. Child collections are then cleared.

## UI
The dashboard is flat: only ordinary top-level profiles appear. Each row can independently be active. Selecting an active profile shows Stop rather than Play. Global Stop still stops everything.

The profile editor retains the existing active/edit-lock explanation. Mapping groups appear below Main with an enable checkbox and group actions. No child-profile add/import/expand UI remains.

## Auto-activation
Application rules activate/deactivate only the profile they belong to. Multiple auto profiles may therefore coexist with manually started profiles. Manual deactivation of an auto-owned profile suppresses that profile until its matching applications exit, without disturbing other profiles.

## Compatibility
Existing context XML remains loadable. ChildProfiles remains deserializable for migration but is no longer created by the UI. Existing main mappings and device configuration GUIDs are preserved.
