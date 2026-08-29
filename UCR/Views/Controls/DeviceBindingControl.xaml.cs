using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Models.Binding;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities.Commands;
using HidWizards.UCR.ViewModels;
using HidWizards.UCR.ViewModels.ProfileViewModels;

namespace HidWizards.UCR.Views.Controls
{
    /// <summary>
    /// Interaction logic for DeviceBindingControl.xaml
    /// </summary>
    public partial class DeviceBindingControl : UserControl
    {
        public static readonly DependencyProperty DeviceBindingProperty = DependencyProperty.Register("DeviceBinding", typeof(DeviceBinding), typeof(DeviceBindingControl), new PropertyMetadata(default(DeviceBinding)));
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register("Label", typeof(string), typeof(DeviceBindingControl), new PropertyMetadata(default(string)));
        public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register("Category", typeof(DeviceBindingCategory?), typeof(DeviceBindingControl), new PropertyMetadata(default(DeviceBindingCategory?)));
        
        /* ContextMenu */
        private ObservableCollection<ContextMenuItem> BindMenu { get; set; }

        private bool HasLoaded = false;

        public DeviceBindingControl()
        {
            BindMenu = new ObservableCollection<ContextMenuItem>();
            InitializeComponent();
            Loaded += UserControl_Loaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DeviceBinding == null) return; // TODO Error logging
            ReloadGui();
            HasLoaded = true;
        }

        private void ReloadGui()
        {
            LoadContextMenu();
        }

        private void LoadContextMenu()
        {
            if (DeviceBinding == null) return;
            BuildContextMenu();
            Ddl.ItemsSource = BindMenu;
        }

        private void BuildContextMenu()
        {
            BindMenu = new ObservableCollection<ContextMenuItem>();
            var deviceConfiguration = GetSelectedDeviceConfiguration();
            if (deviceConfiguration?.Device == null || DeviceBinding?.Profile?.Context == null) return;
            BindMenu = BuildMenu(
                deviceConfiguration.Device.GetDeviceBindingMenu(DeviceBinding.Profile.Context, DeviceBinding.DeviceIoType),
                deviceConfiguration.Guid);
            BindMenu.Add(CreateClearCommandMenuItem());
        }

        private ObservableCollection<ContextMenuItem> BuildMenu(List<DeviceBindingNode> deviceBindingNodes, Guid deviceConfigurationGuid)
        {
            var menuList = new ObservableCollection<ContextMenuItem>();
            if (deviceBindingNodes == null) return menuList;

            foreach (var deviceBindingNode in deviceBindingNodes)
            {
                if (IsKeyboardKeyGroup(deviceBindingNode))
                {
                    foreach (var categoryMenu in BuildMenu(BuildKeyboardCategories(deviceBindingNode.ChildrenNodes), deviceConfigurationGuid))
                    {
                        menuList.Add(categoryMenu);
                    }
                    continue;
                }

                RelayCommand cmd = null;
                if (deviceBindingNode.IsBinding)
                {
                    if (Category != null && deviceBindingNode.DeviceBindingInfo.DeviceBindingCategory != Category) continue;
                    cmd = new RelayCommand(c =>
                    {
                        DeviceBinding.SetDeviceConfigurationGuid(deviceConfigurationGuid);
                        DeviceBinding.SetKeyTypeValue(deviceBindingNode.DeviceBindingInfo.KeyType, deviceBindingNode.DeviceBindingInfo.KeyValue, deviceBindingNode.DeviceBindingInfo.KeySubValue);
                    });
                }

                var menu = new ContextMenuItem(deviceBindingNode.Title, BuildMenu(deviceBindingNode.ChildrenNodes, deviceConfigurationGuid), cmd);
                if (deviceBindingNode.IsBinding || !deviceBindingNode.IsBinding && menu.Children.Count > 0)
                {
                    menuList.Add(menu);
                }
                
            }

            return menuList;
        }

        private static bool IsKeyboardKeyGroup(DeviceBindingNode node)
        {
            return node != null &&
                   node.ChildrenNodes != null &&
                   node.ChildrenNodes.Count >= 20 &&
                   string.Equals(node.Title, "Keys", StringComparison.OrdinalIgnoreCase);
        }

        private static List<DeviceBindingNode> BuildKeyboardCategories(List<DeviceBindingNode> keyboardNodes)
        {
            var order = new[]
            {
                "Letters",
                "Number row",
                "Function keys",
                "Modifiers & locks",
                "Navigation",
                "Numpad",
                "Editing & whitespace",
                "Punctuation",
                "Media & system",
                "Other"
            };

            var groups = new Dictionary<string, List<DeviceBindingNode>>();
            foreach (var name in order) groups.Add(name, new List<DeviceBindingNode>());

            foreach (var node in keyboardNodes)
            {
                if (node == null) continue;
                var category = GetKeyboardCategory(node.Title);
                groups[category].Add(node);
            }

            var result = new List<DeviceBindingNode>();
            foreach (var name in order)
            {
                if (groups[name].Count == 0) continue;
                groups[name].Sort((left, right) => CompareKeyboardBindingTitles(left?.Title, right?.Title));
                result.Add(new DeviceBindingNode
                {
                    Title = name,
                    ChildrenNodes = groups[name]
                });
            }

            return result;
        }


        public static int CompareKeyboardBindingTitles(string left, string right)
        {
            int leftFunction;
            int rightFunction;
            var leftIsFunction = TryGetFunctionKeyNumber(left, out leftFunction);
            var rightIsFunction = TryGetFunctionKeyNumber(right, out rightFunction);
            if (leftIsFunction && rightIsFunction) return leftFunction.CompareTo(rightFunction);
            if (leftIsFunction != rightIsFunction) return leftIsFunction ? -1 : 1;
            return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool TryGetFunctionKeyNumber(string title, out int number)
        {
            number = 0;
            var name = (title ?? string.Empty).Trim();
            if (name.Length < 2 || (name[0] != 'F' && name[0] != 'f')) return false;
            return int.TryParse(name.Substring(1), out number) && number >= 1 && number <= 24;
        }

        private static string GetKeyboardCategory(string title)
        {
            var name = (title ?? string.Empty).Trim();
            if (name.Length == 1 && char.IsLetter(name[0])) return "Letters";
            if (name.Length == 1 && char.IsDigit(name[0])) return "Number row";

            if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f'))
            {
                int functionNumber;
                if (int.TryParse(name.Substring(1), out functionNumber) && functionNumber >= 1 && functionNumber <= 24)
                {
                    return "Function keys";
                }
            }

            if (ContainsAny(name, "shift", "ctrl", "control", "alt", "windows", "caps lock", "num lock", "scroll lock"))
            {
                return "Modifiers & locks";
            }

            if (ContainsAny(name, "numpad", "num ", "numeric", "keypad", "divide", "multiply", "decimal"))
            {
                return "Numpad";
            }

            if (EqualsAny(name, "Left", "Right", "Up", "Down", "Home", "End", "Page Up", "Page Down", "PgUp", "PgDn", "Insert", "Delete"))
            {
                return "Navigation";
            }

            if (EqualsAny(name, "Backspace", "Tab", "Enter", "Return", "Space", "Spacebar", "Esc", "Escape"))
            {
                return "Editing & whitespace";
            }

            if (name.Length == 1 && !char.IsLetterOrDigit(name[0])) return "Punctuation";
            if (ContainsAny(name, "semicolon", "comma", "period", "slash", "quote", "bracket", "backslash", "minus", "equals", "grave", "oem"))
            {
                return "Punctuation";
            }

            if (ContainsAny(name, "volume", "media", "browser", "launch", "print screen", "pause", "break", "sleep", "power", "application", "menu"))
            {
                return "Media & system";
            }

            return "Other";
        }

        private static bool EqualsAny(string value, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(value, candidate, StringComparison.CurrentCultureIgnoreCase)) return true;
            }
            return false;
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                if (value.IndexOf(fragment, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private ContextMenuItem CreateClearCommandMenuItem()
        {
            var clearCommand = new RelayCommand(c => { DeviceBinding.ClearBinding(); });
            return new ContextMenuItem("Clear binding", null, clearCommand);
        }

        public DeviceBinding DeviceBinding
        {
            get { return (DeviceBinding)GetValue(DeviceBindingProperty); }
            set { SetValue(DeviceBindingProperty, value); }
        }

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public DeviceBindingCategory? Category
        {
            get { return (DeviceBindingCategory?) GetValue(CategoryProperty); }
            set { SetValue(CategoryProperty, value); }
        }

        private void DeviceNumberBox_OnSelected(object sender, RoutedEventArgs e)
        {
            if (!HasLoaded) return;
            if (DeviceSelectionBox.SelectedItem == null) return;

            var selectedDeviceConfiguration = GetSelectedDeviceConfiguration();
            if (selectedDeviceConfiguration == null) return;

            var viewModel = DataContext as DeviceBindingViewModel;
            viewModel?.ChangeDeviceConfiguration(selectedDeviceConfiguration.Guid);
            LoadContextMenu();
        }

        private DeviceConfiguration GetSelectedDeviceConfiguration()
        {
            var selectedItem = DeviceSelectionBox.SelectedItem as ComboBoxItemViewModel;
            if (selectedItem == null || DeviceBinding?.Profile == null) return null;
            return DeviceBinding.Profile.GetDeviceConfiguration(DeviceBinding.DeviceIoType, selectedItem.Value);
        }

        private void BindButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DeviceBinding.DeviceIoType.Equals(DeviceIoType.Input))
                {
                    if (DeviceBinding.IsInBindMode) return;
                    if (Category.HasValue) DeviceBinding.DeviceBindingCategory = Category.Value;
                    DeviceBinding.EnterBindMode();
                }
                else
                {
                    OpenContextMenu();
                }
            }
            catch (Exception exception)
            {
                Logger.Error("Failed to enter device bind mode", exception);
                HidWizards.UCR.Utilities.DarkMessageBox.Show("UCR could not start input detection for this binding. The error has been written to the log.",
                    "Unable to bind input", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BindMenuButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenContextMenu();
        }

        private void OpenContextMenu()
        {
            if (DeviceBinding.IsInBindMode) return;
            var contextMenu = BindButton.ContextMenu;
            contextMenu.PlacementTarget = BindButton;
            contextMenu.IsOpen = true;
        }
    }
}
