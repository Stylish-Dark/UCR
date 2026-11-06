"""Negative tests: prove bad UCR UI changes are rejected, not just described."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[1]
MODULE = REPO / "build/quality/check_ui_contract.py"
spec = importlib.util.spec_from_file_location("ucr_ui_contract", MODULE)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class UiContractVetoTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.root = Path(folder.name)
        paths = [
            Path("UCR/App.xaml"),
            Path("UCR/Utilities/UiLabels.cs"),
            Path("UCR/ViewModels/ProfileViewModels/DeviceBindingViewModel.cs"),
            Path("UCR/ViewModels/ProfileViewModels/MappingViewModel.cs"),
            Path("UCR/Views/Controls/CompactInputBindingControl.xaml.cs"),
        ]
        paths += [p.relative_to(REPO) for p in (REPO / "UCR/Views").rglob("*.xaml")]
        for path in paths:
            target = self.root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO / path, target)

    def modify(self, relative: str, original: str, replacement: str):
        path = self.root / relative
        text = path.read_text(encoding="utf-8-sig")
        self.assertIn(original, text, f"Fixture anchor disappeared: {relative}")
        path.write_text(text.replace(original, replacement, 1), encoding="utf-8")

    def assert_veto(self, expected: str):
        findings = guard.check(self.root)
        self.assertTrue(
            any(expected in finding for finding in findings),
            f"Expected veto {expected!r}, findings: {findings}")

    def test_unmodified_project_passes(self):
        self.assertEqual(guard.check(self.root), [])

    def test_duplicate_NOT_asset_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            "<UserControl.Resources>",
            '<UserControl.Resources><Style x:Key="MappingNotToggle" '
            'TargetType="{x:Type ToggleButton}"/>')
        self.assert_veto("MappingNotToggle must be defined exactly once")

    def test_bind_style_bypass_rejected_even_if_reference_count_is_preserved(self):
        path = "UCR/Views/Controls/MappingCardControl.xaml"
        self.modify(
            path,
            'BasedOn="{StaticResource UcrBindActionButton}"',
            'BasedOn="{StaticResource MaterialDesignRaisedButton}" '
            'ToolTip="BasedOn={StaticResource UcrBindActionButton}"')
        self.assert_veto("BindDirectionalSlot_OnClick bypasses UcrBindActionButton")

    def test_unapproved_options_button_skin_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/CompactInputBindingControl.xaml",
            'Style="{StaticResource UcrBindOptionsButton}"',
            'Style="{StaticResource MaterialDesignRaisedButton}"')
        self.assert_veto("MenuButton_OnClick bypasses UcrBindOptionsButton")

    def test_compact_bind_minimum_cannot_be_reset_to_zero(self):
        self.modify(
            "UCR/Views/Controls/CompactInputBindingControl.xaml",
            'x:Name="BindButton" MinHeight="36" Padding="2 0"',
            'x:Name="BindButton" MinHeight="36" MinWidth="0" Padding="2 0"')
        self.assert_veto("Compact Bind button resets its minimum width")

    def test_output_type_filter_icon_cannot_be_reintroduced(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            'ToolTip="Axis output" Click="OutputRepresentation_OnClick"',
            'ToolTip="Filter output" Click="OutputRepresentation_OnClick"')
        self.assert_veto("Filter must not appear as an Outputs representation")

    def test_output_compass_cannot_expand_back_to_full_panel_width(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            'HorizontalAlignment="Center" MaxWidth="360"',
            'HorizontalAlignment="Center" MaxWidth="480"')
        self.assert_veto("4-Way output compass must have one compact 360px")

    def test_four_way_direction_invert_cannot_return(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            'x:Key="DirectionalOutputDPadCardTemplate"',
            'x:Key="DirectionalOutputDPadCardTemplate" ToolTip="Invert this output"')
        self.assert_veto("4-Way output directions must not have per-arm Invert")

    def test_NOT_label_drift_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            'Content="{x:Static labels:UiLabels.Not}"',
            'Content="Negate"')
        self.assert_veto("NOT instances using canonical name")

    def test_literal_Block_caption_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/DeviceBindingControl.xaml",
            'Text="{x:Static labels:UiLabels.Block}"',
            'Text="Block"')
        self.assert_veto("Block label bypasses UiLabels.Block")

    def test_redundant_face_button_caption_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/CompactInputBindingControl.xaml.cs",
            'return (title ?? string.Empty).Trim().ToUpperInvariant();',
            'return "Face button " + (title ?? string.Empty);')
        self.assert_veto("unnecessary Face button prefix")

    def test_two_dot_glyph_removal_is_rejected(self):
        self.modify(
            "UCR/Views/Controls/MappingCardControl.xaml",
            'x:Name="TwoDots"',
            'x:Name="InventedTwoGlyph"')
        self.assert_veto("approved two-dot button glyph")


if __name__ == "__main__":
    unittest.main()
