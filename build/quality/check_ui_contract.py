#!/usr/bin/env python3
"""Enforce UCR's canonical UI components and concise naming at source level.

No API key, AI reviewer, screenshots, or remote service is required. This is a
structural veto: a violating change fails CI before UCR is compiled.
"""
from __future__ import annotations

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
STYLE_KEYS = (
    "MappingNotToggle", "CompactBlockSwitch",
    "UcrBindActionButton", "UcrBindOptionsButton",
)
XAMLNS = "{http://schemas.microsoft.com/winfx/2006/xaml}"
PRESENT = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"


def check(root: Path = ROOT) -> list[str]:
    errors = []
    app = root / "UCR/App.xaml"
    all_xaml = [app] + sorted((root / "UCR/Views").rglob("*.xaml"))
    nodes = {}
    for path in all_xaml:
        try:
            nodes[path] = ET.parse(path).getroot()
        except ET.ParseError as exc:
            errors.append(f"{path.relative_to(root)}: malformed XAML: {exc}")

    # A shared asset is not shared if a view can silently shadow it.
    for key in STYLE_KEYS:
        owners = []
        for path, tree in nodes.items():
            for node in tree.iter():
                if node.tag == PRESENT + "Style" and node.attrib.get(XAMLNS + "Key") == key:
                    owners.append(path)
        if owners != [app]:
            errors.append(f"{key} must be defined exactly once in App.xaml; "
                          f"actual owners: {[str(x.relative_to(root)) for x in owners]}")

    # Every binding action must inherit the SAME visual chrome, not merely
    # increment a file-wide style-reference counter to make CI happy.
    required_buttons = {
        "UCR/Views/Controls/MappingCardControl.xaml": {
            "BindDirectionalSlot_OnClick": (2, "UcrBindActionButton"),
            "CompoundPrimaryBind_OnClick": (1, "UcrBindActionButton"),
            "CompassOutputBind_OnClick": (1, "UcrBindActionButton"),
            "CompoundOutput_OnClick": (1, "UcrBindActionButton"),
        },
        "UCR/Views/Controls/CompactInputBindingControl.xaml": {
            "BindButton_OnClick": (1, "UcrBindActionButton"),
            "MenuButton_OnClick": (1, "UcrBindOptionsButton"),
        },
        "UCR/Views/Controls/DeviceBindingControl.xaml": {
            "BindButton_OnClick": (1, "UcrBindActionButton"),
            "BindMenuButton_OnClick": (1, "UcrBindOptionsButton"),
        },
    }
    for relative, controls in required_buttons.items():
        xaml_path = root / relative
        tree = nodes.get(xaml_path)
        if tree is None:
            errors.append(f"{relative}: missing parsed UI to check canonical binding actions")
            continue
        for handler, (required_count, required_style) in controls.items():
            buttons = [element for element in tree.iter()
                       if element.tag == PRESENT + "Button"
                       and element.attrib.get("Click") == handler]
            if len(buttons) != required_count:
                errors.append(f"{relative}: expected {required_count} button(s) for {handler}, "
                              f"found {len(buttons)}")
            for button in buttons:
                direct = button.attrib.get("Style") == (
                    "{StaticResource " + required_style + "}")
                inherited = any(style.attrib.get("BasedOn") == (
                    "{StaticResource " + required_style + "}")
                    for container in button.findall(PRESENT + "Button.Style")
                    for style in container
                    if style.tag == PRESENT + "Style")
                if not (direct or inherited):
                    errors.append(f"{relative}: {handler} bypasses {required_style}")

    compact = (root / "UCR/Views/Controls/CompactInputBindingControl.xaml").read_text(encoding="utf-8-sig")
    mapping = (root / "UCR/Views/Controls/MappingCardControl.xaml").read_text(encoding="utf-8-sig")
    device = (root / "UCR/Views/Controls/DeviceBindingControl.xaml").read_text(encoding="utf-8-sig")
    binding_vm = (root / "UCR/ViewModels/ProfileViewModels/DeviceBindingViewModel.cs").read_text(encoding="utf-8-sig")
    mapping_vm = (root / "UCR/ViewModels/ProfileViewModels/MappingViewModel.cs").read_text(encoding="utf-8-sig")
    picker = (root / "UCR/Views/Controls/CompactInputBindingControl.xaml.cs").read_text(encoding="utf-8-sig")
    names = (root / "UCR/Utilities/UiLabels.cs").read_text(encoding="utf-8-sig")

    # Size and layout invariants are assets, not suggestions. The 82px
    # minimum belongs to the canonical Bind style and its compact grid cell.
    app_source = app.read_text(encoding="utf-8-sig")
    if '<system:Double x:Key="UcrBindMinWidth">82</system:Double>' not in app_source:
        errors.append("Shared Bind minimum width token must remain 82px")
    if '<Setter Property="MinWidth" Value="{StaticResource UcrBindMinWidth}"/>' not in app_source:
        errors.append("Canonical Bind style bypasses its minimum-width token")

    output_compasses = [
        element for element in nodes.get(root / "UCR/Views/Controls/MappingCardControl.xaml", [])
        .iter() if element.attrib.get(XAMLNS + "Name") == "OutputDirectionalCompassGrid"
    ] if root / "UCR/Views/Controls/MappingCardControl.xaml" in nodes else []
    if len(output_compasses) != 1 or output_compasses[0].attrib.get("MaxWidth") != "360":
        errors.append("4-Way output compass must have one compact 360px geometry limit")
    if any(element.tag == PRESENT + "RadioButton"
           and (element.attrib.get("Click") == "FilterOutput_OnClick" or
                element.attrib.get("ToolTip") == "Filter output")
           for tree in nodes.values() for element in tree.iter()):
        errors.append("Filter must not appear as an Outputs representation type")

    # Explicitly preserve the owner's good glyph and modifier controls.
    if "x:Name=\"TwoDots\"" not in mapping or 'Value="••"' not in mapping:
        errors.append("The approved two-dot button glyph is missing or replaced")
    if "CompactBindSurfaceButton" in compact or "CompactBindSurfaceButton" in (root / "UCR/App.xaml").read_text(encoding="utf-8-sig"):
        errors.append("A second, local Bind surface template has been introduced")
    if 'MinWidth="{StaticResource UcrBindMinWidth}"' not in compact or (
            'x:Name="BindingActionGroup"' not in compact or
            'HorizontalAlignment="Left" MinWidth="124"' not in compact):
        errors.append("Compact Bind layout bypasses the shared 82px minimum target")
    if 'x:Name="BindButton" MinHeight="36" MinWidth="0"' in compact:
        errors.append("Compact Bind button resets its minimum width to zero")
    dpad_start = '<DataTemplate x:Key="DirectionalOutputDPadCardTemplate">'
    dpad_end = '<DataTemplate x:Key="ButtonMappingEditorTemplate">'
    if mapping.count(dpad_start) != 1 or mapping.count(dpad_end) != 1:
        errors.append("Canonical 4-Way output template is missing or duplicated")
    else:
        dpad = mapping.split(dpad_start, 1)[1].split(dpad_end, 1)[0]
        if 'ToolTip="Invert this output"' in dpad:
            errors.append("4-Way output directions must not have per-arm Invert switches")
    if 'BasedOn="{StaticResource UcrBindActionButton}"' not in compact:
        errors.append("Compact Bind control is not derived from the shared Bind asset")
    if 'Style="{StaticResource UcrBindActionButton}"' not in device:
        errors.append("Standard device Bind control is not using the shared Bind asset")
    if mapping.count('BasedOn="{StaticResource UcrBindActionButton}"') < 2:
        errors.append("Directional Bind variants must derive from the shared Bind asset")
    if mapping.count('Style="{StaticResource UcrBindActionButton}"') < 3:
        errors.append("Compound/output bindings must use the shared Bind asset")

    for path, tree in nodes.items():
        for node in tree.iter():
            if node.tag != PRESENT + "ToggleButton":
                continue
            content = node.attrib.get("Content", "")
            if content != "NOT" and "UiLabels.Not" not in content:
                continue
            style = node.attrib.get("Style", "")
            if content != "{x:Static labels:UiLabels.Not}" or style != "{StaticResource MappingNotToggle}":
                errors.append(f"{path.relative_to(root)}: NOT button bypasses canonical label/style")

    if mapping.count('Content="{x:Static labels:UiLabels.Not}"') != 4:
        errors.append("Expected four NOT instances using canonical name and style")
    # Every Block caption is sourced from UiLabels rather than a local literal.
    expected_block = (
        (mapping, 'Text="{x:Static labels:UiLabels.Block}"',
         "MappingCardControl.xaml"),
        (compact, 'Value="{x:Static labels:UiLabels.Block}"',
         "CompactInputBindingControl.xaml"),
        (device, 'Text="{x:Static labels:UiLabels.Block}"',
         "DeviceBindingControl.xaml"),
    )
    for source, reference, filename in expected_block:
        if source.count(reference) != 1:
            errors.append(f"{filename}: Block label must reference UiLabels.Block exactly once")
    for path, tree in nodes.items():
        for node in tree.iter():
            literal_caption = node.attrib.get("Text") == "Block"
            literal_setter = (node.tag == PRESENT + "Setter"
                              and node.attrib.get("Property") == "Text"
                              and node.attrib.get("Value") == "Block")
            if literal_caption or literal_setter:
                errors.append(f"{path.relative_to(root)}: Block label bypasses UiLabels.Block")

    if "x:Key=\"CompactBlockSwitch\"" in compact:
        errors.append("Local Block template shadows the canonical application asset")
    for constant in ("Bind", "Not", "Block", "Output", "Input", "Axis",
                     "Button", "FourWay", "Filter", "Capture"):
        if not re.search(r"public const string " + constant + r"\s*=", names):
            errors.append(f"Missing canonical name: UiLabels.{constant}")
    if '"Select 4-Way ▾"' in mapping_vm or '"Choose stick / D-pad ▾"' in mapping_vm:
        errors.append("A compound Bind action has a redundant explanatory label")
    if "UiLabels.Bind" not in mapping_vm or "UiLabels.Bind" not in binding_vm:
        errors.append("Input/output unassigned Bind actions bypass the naming index")
    if 'return "Output";' in binding_vm:
        errors.append("Unassigned output cannot invent a separate Output action label")
    if '"Face button " +' in picker:
        errors.append("Output-picker caption adds an unnecessary Face button prefix")
    if '" X axis"' in picker or '" Y axis"' in picker:
        errors.append("Output-picker caption repeats axis information")
    return errors


def main() -> int:
    problems = check()
    if problems:
        for problem in problems:
            print("UI CONTRACT REJECTED: " + problem, file=sys.stderr)
        return 1
    print("UI CONTRACT PASSED: canonical Bind, NOT, Block and naming invariants")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
