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
XAML_DIR = ROOT / "UCR" / "Views"
STYLE_KEYS = (
    "MappingNotToggle", "CompactBlockSwitch",
    "UcrBindActionButton", "UcrBindOptionsButton",
)
XAMLNS = "{http://schemas.microsoft.com/winfx/2006/xaml}"
PRESENT = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"


def check() -> list[str]:
    errors = []
    app = ROOT / "UCR/App.xaml"
    all_xaml = [app] + sorted(XAML_DIR.rglob("*.xaml"))
    nodes = {}
    for path in all_xaml:
        try:
            nodes[path] = ET.parse(path).getroot()
        except ET.ParseError as exc:
            errors.append(f"{path.relative_to(ROOT)}: malformed XAML: {exc}")

    # A shared asset is not shared if a view can silently shadow it.
    for key in STYLE_KEYS:
        owners = []
        for path, root in nodes.items():
            for node in root.iter():
                if node.tag == PRESENT + "Style" and node.attrib.get(XAMLNS + "Key") == key:
                    owners.append(path)
        if owners != [app]:
            errors.append(f"{key} must be defined exactly once in App.xaml; "
                          f"actual owners: {[str(x.relative_to(ROOT)) for x in owners]}")

    compact = (ROOT / "UCR/Views/Controls/CompactInputBindingControl.xaml").read_text(encoding="utf-8-sig")
    mapping = (ROOT / "UCR/Views/Controls/MappingCardControl.xaml").read_text(encoding="utf-8-sig")
    device = (ROOT / "UCR/Views/Controls/DeviceBindingControl.xaml").read_text(encoding="utf-8-sig")
    binding_vm = (ROOT / "UCR/ViewModels/ProfileViewModels/DeviceBindingViewModel.cs").read_text(encoding="utf-8-sig")
    mapping_vm = (ROOT / "UCR/ViewModels/ProfileViewModels/MappingViewModel.cs").read_text(encoding="utf-8-sig")
    picker = (ROOT / "UCR/Views/Controls/CompactInputBindingControl.xaml.cs").read_text(encoding="utf-8-sig")
    names = (ROOT / "UCR/Utilities/UiLabels.cs").read_text(encoding="utf-8-sig")

    # Explicitly preserve the owner's good glyph and modifier controls.
    if "x:Name=\"TwoDots\"" not in mapping or 'Value="••"' not in mapping:
        errors.append("The approved two-dot button glyph is missing or replaced")
    if "CompactBindSurfaceButton" in compact or "CompactBindSurfaceButton" in (ROOT / "UCR/App.xaml").read_text(encoding="utf-8-sig"):
        errors.append("A second, local Bind surface template has been introduced")
    if 'BasedOn="{StaticResource UcrBindActionButton}"' not in compact:
        errors.append("Compact Bind control is not derived from the shared Bind asset")
    if 'Style="{StaticResource UcrBindActionButton}"' not in device:
        errors.append("Standard device Bind control is not using the shared Bind asset")
    if mapping.count('BasedOn="{StaticResource UcrBindActionButton}"') < 2:
        errors.append("Directional Bind variants must derive from the shared Bind asset")
    if mapping.count('Style="{StaticResource UcrBindActionButton}"') < 3:
        errors.append("Compound/output bindings must use the shared Bind asset")

    for path, root in nodes.items():
        for node in root.iter():
            if node.tag != PRESENT + "ToggleButton":
                continue
            content = node.attrib.get("Content", "")
            if content != "NOT" and "UiLabels.Not" not in content:
                continue
            style = node.attrib.get("Style", "")
            if content != "{x:Static labels:UiLabels.Not}" or style != "{StaticResource MappingNotToggle}":
                errors.append(f"{path.relative_to(ROOT)}: NOT button bypasses canonical label/style")

    if mapping.count('Content="{x:Static labels:UiLabels.Not}"') != 4:
        errors.append("Expected four NOT instances using canonical name and style")
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
