using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Windows;
using HidWizards.UCR.Core.Utilities;

namespace HidWizards.UCR.Utilities
{
    public class ResourceLoader
    {
        private CompositionContainer _Container;

        [ImportMany(typeof(ResourceDictionary))]
        private IEnumerable<ResourceDictionary> _resourceDictionaries { get; set; }

        public void Load()
        {
            var catalog = new AggregateCatalog();

            foreach (var path in Directory.EnumerateDirectories(@".\Plugins", "*", SearchOption.TopDirectoryOnly))
            {
                var folderName = path.Remove(0, path.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                if (File.Exists(Path.Combine(path, folderName + ".dll")))
                {
                    catalog.Catalogs.Add(new DirectoryCatalog(path, folderName + ".dll"));
                }
            }

            _Container = new CompositionContainer(catalog);
            _Container.ComposeParts(this);

            foreach (var resourceDictionary in _resourceDictionaries)
            {
                Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);
            }

            LoadExternalStyles();
        }

        private static void LoadExternalStyles()
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Styles");
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.GetFiles(directory, "*.xaml")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var dictionary = new ResourceDictionary { Source = new Uri(Path.GetFullPath(file)) };
                    // Validate an entire file before changing any live application resources.
                    // A malformed replacement must leave every built-in style intact.
                    var overrides = new List<KeyValuePair<string, Style>>();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        var key = entry.Key as string;
                        var style = entry.Value as Style;
                        if (key == null || style == null) continue;

                        var fallback = Application.Current.Resources[key] as Style;
                        if (fallback != null && fallback.TargetType != style.TargetType)
                            throw new InvalidDataException("External style " + key
                                + " has an incompatible target type; expected " + fallback.TargetType);

                        style.Seal();
                        overrides.Add(new KeyValuePair<string, Style>(key, style));
                    }

                    foreach (var item in overrides)
                        Application.Current.Resources[item.Key] = item.Value;

                    Logger.Info("Loaded " + overrides.Count + " external UI style(s) from " + file);
                }
                catch (Exception exception)
                {
                    Logger.Warn("Could not load external UI styles from " + file + "; using built-in styles.", exception);
                }
            }
        }
    }
}