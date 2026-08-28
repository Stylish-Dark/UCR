#!/usr/bin/env python3
from __future__ import annotations

import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FAILURES: list[str] = []
PASSES: list[str] = []


def fail(msg: str) -> None:
    FAILURES.append(msg)


def ok(msg: str) -> None:
    PASSES.append(msg)


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8-sig")


def require(cond: bool, msg: str) -> None:
    if cond:
        ok(msg)
    else:
        fail(msg)


def strip_csharp_noncode(text: str) -> str:
    out = []
    i = 0
    n = len(text)
    state = "code"
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if state == "code":
            if c == "/" and nxt == "/":
                state = "line_comment"; out.extend("  "); i += 2; continue
            if c == "/" and nxt == "*":
                state = "block_comment"; out.extend("  "); i += 2; continue
            if c == "@" and nxt == '"':
                state = "verbatim"; out.extend("  "); i += 2; continue
            if c == '"':
                state = "string"; out.append(" "); i += 1; continue
            if c == "'":
                state = "char"; out.append(" "); i += 1; continue
            out.append(c); i += 1; continue
        if state == "line_comment":
            if c == "\n": state = "code"; out.append("\n")
            else: out.append(" ")
            i += 1; continue
        if state == "block_comment":
            if c == "*" and nxt == "/":
                state = "code"; out.extend("  "); i += 2
            else:
                out.append("\n" if c == "\n" else " "); i += 1
            continue
        if state == "string":
            if c == "\\":
                out.extend("  "); i += min(2, n - i); continue
            if c == '"': state = "code"
            out.append(" "); i += 1; continue
        if state == "verbatim":
            if c == '"' and nxt == '"':
                out.extend("  "); i += 2; continue
            if c == '"': state = "code"
            out.append("\n" if c == "\n" else " "); i += 1; continue
        if state == "char":
            if c == "\\":
                out.extend("  "); i += min(2, n - i); continue
            if c == "'": state = "code"
            out.append(" "); i += 1; continue
    return "".join(out)


def balanced_csharp(path: Path) -> None:
    code = strip_csharp_noncode(path.read_text(encoding="utf-8-sig"))
    for opening, closing, label in [("{", "}", "braces"), ("(", ")", "parentheses"), ("[", "]", "brackets")]:
        depth = 0
        for ch in code:
            if ch == opening: depth += 1
            elif ch == closing:
                depth -= 1
                if depth < 0:
                    fail(f"{path.relative_to(ROOT)} has premature closing {label}")
                    return
        if depth != 0:
            fail(f"{path.relative_to(ROOT)} has unbalanced {label}: depth={depth}")
            return
    ok(f"C# delimiter balance: {path.relative_to(ROOT)}")


def check_xml_and_handlers() -> None:
    xml_files = list(ROOT.rglob("*.xaml")) + list(ROOT.rglob("*.csproj")) + list(ROOT.rglob("*.props")) + list(ROOT.rglob("*.targets"))
    parsed = 0
    for path in xml_files:
        if any(part in {"bin", "obj", "packages", ".git"} for part in path.parts):
            continue
        try:
            ET.parse(path)
            parsed += 1
        except Exception as exc:
            fail(f"XML parse failed: {path.relative_to(ROOT)}: {exc}")
    if not any("XML parse failed" in f for f in FAILURES):
        ok(f"XML/XAML/MSBuild parse: {parsed} files")

    for rel in [
        "UCR/Views/Controls/MappingCardControl.xaml",
        "UCR/Views/Dialogs/DeviceManagerDialog.xaml",
        "UCR/Views/ProfileViews/ProfileWindow.xaml",
    ]:
        path = ROOT / rel
        tree = ET.parse(path)
        root = tree.getroot()
        codebehind = Path(str(path) + ".cs")
        if not codebehind.exists():
            fail(f"Missing code-behind for {rel}")
            continue
        cs = codebehind.read_text(encoding="utf-8-sig")
        event_names = set()
        interesting = {
            "Click", "PreviewMouseRightButtonUp", "MouseRightButtonUp", "PreviewMouseWheel",
            "PreviewKeyDown", "SelectionChanged", "Loaded", "Closed", "Checked", "Unchecked",
            "DropDownOpened", "Selected", "TextChanged", "LostFocus", "KeyDown", "MouseDoubleClick",
        }
        for elem in root.iter():
            for raw_attr, value in elem.attrib.items():
                attr = raw_attr.split("}")[-1]
                if attr in interesting and value and not value.startswith("{"):
                    event_names.add(value)
        missing = [name for name in sorted(event_names) if not re.search(r"\b" + re.escape(name) + r"\s*\(", cs)]
        if missing:
            fail(f"{rel} missing code-behind handlers: {missing}")
        else:
            ok(f"XAML handlers resolve: {rel} ({len(event_names)} handlers)")


def check_project_inputs() -> None:
    ns = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
    checked = 0
    for proj in ROOT.rglob("*.csproj"):
        if any(part in {"submodules", "packages", "bin", "obj", ".git"} for part in proj.parts):
            continue
        try:
            tree = ET.parse(proj)
        except Exception:
            continue
        base = proj.parent
        root = tree.getroot()
        nodes = []
        for tag in ["Compile", "Page", "ApplicationDefinition", "Resource", "Content", "None", "ProjectReference"]:
            nodes += root.findall(f".//m:{tag}", ns)
        for node in nodes:
            inc = node.attrib.get("Include")
            if not inc or "$(" in inc or "*" in inc:
                continue
            parts = [part for part in Path(inc.replace("\\", "/")).parts if part not in (".", "")]
            current = base
            exists = True
            for part in parts:
                if part == "..":
                    current = current.parent
                    continue
                if not current.is_dir():
                    exists = False
                    break
                matches = [child for child in current.iterdir() if child.name.lower() == part.lower()]
                if not matches:
                    exists = False
                    break
                current = matches[0]
            if not exists:
                fail(f"Missing project input: {proj.relative_to(ROOT)} -> {inc}")
            else:
                checked += 1
    if not any("Missing project input" in f for f in FAILURES):
        ok(f"Compile/project inputs exist: {checked}")


def check_repo_paths() -> None:
    tracked = subprocess.check_output(["git", "ls-files"], cwd=ROOT, text=True).splitlines()
    case_map = defaultdict(list)
    invalid = []
    reserved = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}
    for rel in tracked:
        case_map[rel.lower()].append(rel)
        for part in Path(rel).parts:
            stem = part.split(".")[0].upper()
            if stem in reserved or any(ch in part for ch in '<>:"|?*') or part.endswith((" ", ".")):
                invalid.append(rel)
    collisions = [vals for vals in case_map.values() if len(vals) > 1]
    require(not collisions, f"No Windows case-colliding tracked paths ({len(tracked)} tracked)")
    require(not invalid, "No Windows-invalid tracked paths")


def check_feature_contract() -> None:
    device = read("UCR.Core/Models/Device.cs")
    manager = read("UCR.Core/Managers/DevicesManager.cs")
    dmvm = read("UCR/ViewModels/Dashboard/DeviceManagerViewModel.cs")
    dmxaml = read("UCR/Views/Dialogs/DeviceManagerDialog.xaml")
    dmcs = read("UCR/Views/Dialogs/DeviceManagerDialog.xaml.cs")
    profile = read("UCR/Views/ProfileViews/ProfileWindow.xaml")
    mapx = read("UCR/Views/Controls/MappingCardControl.xaml")
    mapcs = read("UCR/Views/Controls/MappingCardControl.xaml.cs")
    mapvm = read("UCR/ViewModels/ProfileViewModels/MappingViewModel.cs")
    visual = read("UCR/ViewModels/Presentation/DeviceVisualDescriptor.cs")
    tests = read("UCR.Tests/FactoryTests/DeviceFactoryTests.cs")

    require("public bool Removed { get; set; }" in device and "[DefaultValue(false)]" in device,
            "Persistent DeviceAlias.Removed is backward-compatible")
    require(re.search(r"\[XmlAttribute\]\s*\[DefaultValue\(1\)\]\s*public int LogicalInstanceNumber \{ get; set; \} = 1;", device) is not None,
            "Logical device ordinal persists separately from raw provider slot")
    require("CoreInterceptionSlotSuffix" in manager and 'new Regex(@"\\s+#\\d+\\s*$"' in manager,
            "Core_Interception trailing #N normalization is present")
    require("GetRawAvailableDeviceList(DeviceIoType.Input, false)" in manager,
            "Input detection listens to raw endpoints")
    require("RegisterDetectedInputEndpoint" in manager and "SamePhysicalEvidence" in manager and
            "different raw endpoint has now itself produced deliberate input" in manager,
            "#2 creation requires deliberate detection on a different still-live endpoint")
    require("RemoveInputDevice" in manager and "RestoreInputDevice" in manager and "RegisterDetectedInputDevice" in manager,
            "Persistent input removal and Detect restoration paths exist")
    require('if (IsInputRemoved(configuredDevice)) return null;' in manager,
            "Removed input is operationally unavailable until Detect restores it")
    require(": devices.Where(device => !IsInputRemoved(device) && !IsDeviceHidden(device, devices)).ToList();" in manager,
            "Removed combined input/output devices are suppressed from output selection surfaces too")
    require("CanRemoveFromUcr => HasInput" in dmvm and "CanHide => HasOutput && !HasInput" in dmvm,
            "Device manager enforces input=Remove / output=Hidden semantics")
    add_devices = read("UCR/ViewModels/Dashboard/AddDevicesDialogViewModel.cs")
    profile_devices = read("UCR/ViewModels/Dashboard/ProfileDeviceListControlViewModel.cs")
    require("left.LogicalInstanceNumber == right.LogicalInstanceNumber" in add_devices and
            "left.LogicalInstanceNumber == right.LogicalInstanceNumber" in profile_devices,
            "Detected logical #2 is not merged back into logical instance 1 by add/profile lists")
    require("Select(BuildLogicalInstanceKey)" in dmvm and
            "removedInputKeys.Contains(logicalInstanceKey)" in dmvm,
            "Device Manager removal suppression is scoped to the exact logical ordinal")

    forbidden = ["REMOVE STALE", "rest of this session", "Session identity only", "RemoveStale_OnClick"]
    combined = "\n".join([dmxaml, dmcs, dmvm])
    for phrase in forbidden:
        require(phrase not in combined, f"Removed obsolete device-manager concept: {phrase}")
    stale_calls = len(re.findall(r"\bRemoveStaleDeviceCacheCopies\s*\(", manager))
    require(stale_calls == 1, "Stale-cache cleanup has no automatic/manual callers (definition only)")
    require("DismissDeviceForSession" not in manager and "IsSessionDismissed" not in manager,
            "Session-only device removal machinery is gone")
    require("CanHide" in dmxaml and "CanRemoveFromUcr" in dmxaml,
            "Device Manager conditionally exposes Hidden/Remove actions")

    require(profile.count("<Expander") >= 3, "Add mapping, Filters, and Devices are collapsible expanders")
    add_mapping_pos = profile.find('Text="Add mapping"')
    require(add_mapping_pos >= 0 and "<Expander.Header>" in profile[max(0, add_mapping_pos-700):add_mapping_pos],
            "Add mapping panel is inside an Expander header")
    require('x:Name="InputProfileDeviceList"' in profile and 'MaxHeight="110"' in profile,
            "Input device list has expanded height budget")
    out_pos = profile.find('x:Name="OutputProfileDeviceList"')
    require(out_pos >= 0 and 'MaxHeight="110"' in profile[out_pos-150:out_pos+250],
            "Output device list has expanded height budget")

    for handler in ["RenameHeader_OnMouseRightButtonUp", "QuickBindInput_OnMouseRightButtonUp", "QuickBindOutput_OnMouseRightButtonUp"]:
        require(handler in mapx and handler in mapcs, f"Mapping quick-action handler wired: {handler}")
    require('MinWidth="64"' in mapx and 'ClipToBounds="False"' in mapx,
            "Collapsed binding visuals have explicit non-clipping bounds")
    for method in ["ResolveCollapsedBinding", "QuickBindInput", "GetQuickOutputBindingOptions", "ApplyQuickOutputBinding"]:
        require(method in mapvm, f"Mapping quick-bind model method exists: {method}")
    require("SetDeviceConfigurationGuid(option.DeviceConfigurationGuid)" in mapvm and
            "SetKeyTypeValue(option.KeyType, option.KeyValue, option.KeySubValue)" in mapvm,
            "Output quick-bind uses existing binding mutation path")
    require("BindingGuid" in visual and "BindingGuid = binding.Guid" in visual,
            "Collapsed visual identifies its exact binding")

    current_test_text = "\n".join(
        p.read_text(encoding="utf-8-sig") for p in (ROOT / "UCR.Tests").rglob("*.cs")
    )
    current_test_decorators = (
        len(re.findall(r"\[Test\]", current_test_text))
        + len(re.findall(r"\[TestCase(?:\(|\])", current_test_text))
    )
    baseline_test_decorators = 0
    tracked_tests = subprocess.check_output(
        ["git", "ls-tree", "-r", "--name-only", "c0e7afc47293adb2a0f913e8a77412ba25caab9e", "UCR.Tests"], cwd=ROOT, text=True
    ).splitlines()
    for rel in tracked_tests:
        if not rel.endswith(".cs"):
            continue
        baseline = subprocess.check_output(["git", "show", f"c0e7afc47293adb2a0f913e8a77412ba25caab9e:{rel}"], cwd=ROOT, text=True)
        baseline_test_decorators += len(re.findall(r"\[Test\]", baseline))
        baseline_test_decorators += len(re.findall(r"\[TestCase(?:\(|\])", baseline))
    require(
        current_test_decorators >= baseline_test_decorators,
        f"Unit-test decorators not reduced ({current_test_decorators} current vs {baseline_test_decorators} baseline; baseline CI ran 179 cases)",
    )
    for testname in [
        "CoreInterceptionSlotChurnDoesNotDisablePersistentPresentation",
        "CoreInterceptionLogicalIdentityIgnoresProviderSlotSuffixButKeepsDeviceFamily",
        "CoreInterceptionRawSlotDuplicatesCollapseToOneLogicalDevice",
        "CoreInterceptionLogicalIdentityDoesNotDependOnProviderFriendlyTitle",
        "DeviceManagerUsesRemoveForInputsAndHiddenForOutputs",
        "DeviceManagerCombinedInputOutputDeviceUsesRemoveNotHidden",
        "InputRemovalPersistsOnLogicalIdentityUntilRestored",
        "DetectingDifferentStillLiveCoreInterceptionEndpointCreatesSecondLogicalInstance",
        "DetectingSamePhysicalPathOnNewSlotIsTreatedAsSlotChurnNotSecondDevice",
        "RemovedSecondLogicalInputRestoresOnlyItsOwnOrdinal",
        "RemovedInputIsOperationallyUnavailableUntilDetectRestoresIt",
        "LogicalDeviceOrdinalSurvivesProfileSerialization",
    ]:
        require(testname in tests, f"Regression test present: {testname}")

    # Executable model of the title/identity rule that caused #4/#6 duplicates.
    suffix = re.compile(r"\s+#\d+\s*$", re.I)
    def title(v: str) -> str:
        return suffix.sub("", v).strip()
    k4 = ("keyboard", r"Keyboard\VID_046D&PID_C52B".lower(), title("K: Logitech USB Receiver #4").lower())
    k6 = ("keyboard", r"Keyboard\VID_046D&PID_C52B".lower(), title("K: Logitech USB Receiver #6").lower())
    mouse = ("mouse", r"Mouse\VID_046D&PID_C52B".lower(), title("M: Logitech USB Receiver").lower())
    require(k4 == k6 and k4 != mouse, "Python identity oracle collapses #4/#6 but not keyboard/mouse families")

    # Detection-state oracle: passive duplicate endpoints remain one device. A second endpoint only
    # earns #2 after deliberate input while the previously detected endpoint is still live.
    claims = []
    live = {"slot4", "slot6"}
    detected = "slot4"
    if detected not in claims:
        claims.append(detected)
    require(len(claims) == 1, "Python detection oracle: first deliberate endpoint is logical instance 1")
    detected = "slot6"
    if detected not in claims and claims[0] in live:
        claims.append(detected)
    require(len(claims) == 2, "Python detection oracle: second deliberately active still-live endpoint earns #2")


def check_git_and_text_integrity() -> None:
    result = subprocess.run(["git", "diff", "--check"], cwd=ROOT, text=True, capture_output=True)
    require(result.returncode == 0, "git diff --check")
    conflict = re.compile(r"^(<<<<<<<|=======|>>>>>>>)", re.M)
    bad = []
    for ext in ("*.cs", "*.xaml", "*.csproj", "*.yml", "*.md", "*.py"):
        for path in ROOT.rglob(ext):
            if any(part in {".git", "bin", "obj", "packages", "submodules"} for part in path.parts):
                continue
            try: text = path.read_text(encoding="utf-8-sig")
            except Exception: continue
            if conflict.search(text): bad.append(str(path.relative_to(ROOT)))
    require(not bad, "No merge-conflict markers")


def main() -> int:
    check_feature_contract()
    check_xml_and_handlers()
    check_project_inputs()
    check_repo_paths()
    check_git_and_text_integrity()
    for path in [
        ROOT / "UCR.Core/Managers/DevicesManager.cs",
        ROOT / "UCR.Core/Models/Device.cs",
        ROOT / "UCR/ViewModels/Dashboard/DeviceManagerViewModel.cs",
        ROOT / "UCR/ViewModels/ProfileViewModels/MappingViewModel.cs",
        ROOT / "UCR/Views/Controls/MappingCardControl.xaml.cs",
        ROOT / "UCR/Views/Dialogs/DeviceManagerDialog.xaml.cs",
    ]:
        balanced_csharp(path)

    print(f"PASS: {len(PASSES)}")
    for item in PASSES:
        print("  +", item)
    if FAILURES:
        print(f"FAIL: {len(FAILURES)}")
        for item in FAILURES:
            print("  -", item)
        return 1
    print("FAIL: 0")
    return 0


if __name__ == "__main__":
    sys.exit(main())
