using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Utilities
{
    // Apply optional loose-XAML layout settings to the existing compiled controls.
    // This retains the XAML names, bindings, commands, event handlers and default layout.
    internal static class LayoutOverrideManager
    {
        private static readonly Dictionary<string, Dictionary<string, Style>> Layouts =
            new Dictionary<string, Dictionary<string, Style>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, string>> NamedProperties =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static bool _registered;

        internal static void Load()
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Layouts");
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.GetFiles(directory, "*.xaml")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var dictionary = new ResourceDictionary { Source = new Uri(Path.GetFullPath(file)) };
                    var styles = new Dictionary<string, Style>(StringComparer.Ordinal);
                    var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        var key = entry.Key as string;
                        var style = entry.Value as Style;
                        if (style == null)
                        {
                            var literal = entry.Value as string;
                            if (string.IsNullOrEmpty(key) || key.LastIndexOf('.') <= 0 || literal == null)
                                throw new InvalidDataException("Layout entries must be named styles or Element.Property strings.");
                            properties.Add(key, literal);
                            continue;
                        }
                        if (string.IsNullOrEmpty(key))
                            throw new InvalidDataException("Layout styles require a name.");
                        if (style.BasedOn != null || style.Triggers.Count != 0)
                            throw new InvalidDataException("Layout rules support property setters, not BasedOn or triggers: " + key);

                        foreach (SetterBase item in style.Setters)
                        {
                            var setter = item as Setter;
                            if (setter == null || setter.Property == null || !string.IsNullOrEmpty(setter.TargetName))
                                throw new InvalidDataException("Layout rule has an unsupported setter: " + key);
                        }

                        style.Seal();
                        styles.Add(key, style);
                    }

                    var viewName = Path.GetFileNameWithoutExtension(file);
                    if (styles.Count != 0) Layouts[viewName] = styles;
                    if (properties.Count != 0) NamedProperties[viewName] = properties;
                    Logger.Info("Loaded " + (styles.Count + properties.Count) + " layout rules for " + viewName);
                }
                catch (Exception exception)
                {
                    Logger.Warn("Could not load layout overrides from " + file + "; keeping compiled layout.", exception);
                }
            }

            if ((Layouts.Count == 0 && NamedProperties.Count == 0) || _registered) return;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ApplyOnLoaded), true);
            _registered = true;
        }

        private static void ApplyOnLoaded(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null || !ReferenceEquals(sender, e.OriginalSource)) return;

            Dictionary<string, string> properties;
            if (NamedProperties.TryGetValue(element.GetType().Name, out properties))
                ApplyNamedProperties(element, properties);

            if (string.IsNullOrEmpty(element.Name)) return;

            // Nearest matching view wins, even when a view contains another user control.
            for (FrameworkElement owner = element; owner != null; owner = GetParent(owner))
            {
                Dictionary<string, Style> styles;
                Style rule;
                if (!Layouts.TryGetValue(owner.GetType().Name, out styles)
                    || !styles.TryGetValue(element.Name, out rule)) continue;

                try
                {
                    if (!rule.TargetType.IsAssignableFrom(element.GetType()))
                        throw new InvalidDataException("Expected " + rule.TargetType.Name
                            + " but found " + element.GetType().Name);

                    // Convert and validate all values before changing any properties.
                    var updates = new List<KeyValuePair<DependencyProperty, object>>();
                    foreach (Setter setter in rule.Setters)
                    {
                        object value = setter.Value;
                        var raw = value as string;
                        if (raw != null && setter.Property.PropertyType != typeof(string))
                        {
                            var converter = TypeDescriptor.GetConverter(setter.Property.PropertyType);
                            if (!converter.CanConvertFrom(typeof(string)))
                                throw new InvalidDataException("Cannot convert layout value for " + setter.Property.Name);
                            value = converter.ConvertFrom(null, CultureInfo.InvariantCulture, raw);
                        }
                        if (!setter.Property.IsValidValue(value))
                            throw new InvalidDataException("Invalid layout value for " + setter.Property.Name);
                        updates.Add(new KeyValuePair<DependencyProperty, object>(setter.Property, value));
                    }

                    // SetCurrentValue takes precedence over compiled literals without removing
                    // existing bindings, event handlers, templates or interaction logic.
                    foreach (var update in updates)
                        element.SetCurrentValue(update.Key, update.Value);
                }
                catch (Exception exception)
                {
                    var identity = owner.GetType().Name + "." + element.Name;
                    lock (Reported)
                    {
                        if (!Reported.Add(identity)) return;
                    }
                    Logger.Warn("Ignoring invalid external layout for " + identity, exception);
                }
                return;
            }
        }

        private static void ApplyNamedProperties(FrameworkElement view, Dictionary<string, string> properties)
        {
            foreach (var pair in properties)
            {
                try
                {
                    var dot = pair.Key.LastIndexOf('.');
                    var elementName = pair.Key.Substring(0, dot);
                    var propertyName = pair.Key.Substring(dot + 1);
                    var target = view.FindName(elementName) as DependencyObject;
                    if (target == null)
                        throw new InvalidDataException("Named layout target does not exist: " + elementName);

                    var field = target.GetType().GetField(propertyName + "Property",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.FlattenHierarchy);
                    var property = field == null ? null : field.GetValue(null) as DependencyProperty;
                    if (property == null)
                        throw new InvalidDataException("Unknown layout property: " + propertyName);

                    var converter = TypeDescriptor.GetConverter(property.PropertyType);
                    if (!converter.CanConvertFrom(typeof(string)))
                        throw new InvalidDataException("Cannot convert layout property: " + propertyName);
                    var value = converter.ConvertFrom(null, CultureInfo.InvariantCulture, pair.Value);
                    if (!property.IsValidValue(value))
                        throw new InvalidDataException("Invalid layout value for " + pair.Key);

                    target.SetCurrentValue(property, value);
                }
                catch (Exception exception)
                {
                    var identity = view.GetType().Name + "." + pair.Key;
                    lock (Reported)
                    {
                        if (!Reported.Add(identity)) continue;
                    }
                    Logger.Warn("Ignoring invalid named layout property " + identity, exception);
                }
            }
        }

        private static FrameworkElement GetParent(FrameworkElement element)
        {
            return VisualTreeHelper.GetParent(element) as FrameworkElement
                ?? LogicalTreeHelper.GetParent(element) as FrameworkElement;
        }
    }
}
