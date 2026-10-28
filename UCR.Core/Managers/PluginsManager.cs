using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HidWizards.UCR.Core.Models;
using NLog;

namespace HidWizards.UCR.Core.Managers
{
    public class PluginsManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private CompositionContainer _Container;

        [ImportMany(typeof(Plugin))]
        public List<Plugin> Plugins { get; set; }

        public static string BuiltInPluginPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins",
                "UCR.Plugins", "UCR.Plugins.dll");

        [DllImport("kernel32.dll", EntryPoint = "DeleteFileW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteAlternateDataStream(string path);

        // Downloaded Windows archives can propagate Zone.Identifier to every
        // extracted DLL. Delete ONLY the bundled plugin's download marker,
        // after explicit user approval. Do not rely on another DLL to repair
        // blocked DLL loading.
        public static bool TryUnblockBuiltInPlugin()
        {
            var dll = BuiltInPluginPath;
            if (!File.Exists(dll)) return false;
            try
            {
                if (DeleteAlternateDataStream(dll + ":Zone.Identifier")) return true;
                var win32 = Marshal.GetLastWin32Error();
                if (win32 != 2 && win32 != 3)
                    Logger.Warn("Could not remove mapping plugin download marker. Windows error: " + win32);
                return false;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Unable to unblock the installed mapping plugin: " + dll);
                return false;
            }
        }

        public PluginsManager(string basePath)
        {
            Plugins = new List<Plugin>();
            var catalog = new AggregateCatalog();
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, basePath);
            try
            {
                if (Directory.Exists(directory))
                {
                    foreach (var path in Directory.EnumerateDirectories(directory, "*",
                        SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(path);
                        var dll = Path.Combine(path, name + ".dll");
                        if (File.Exists(dll))
                            catalog.Catalogs.Add(new DirectoryCatalog(path, name + ".dll"));
                        else
                            Logger.Warn("Mapping plugin folder has no matching DLL: " + dll);
                    }
                }
                else
                    Logger.Error("Plugins directory not found: " + directory);

                _Container = new CompositionContainer(catalog);
                _Container.ComposeParts(this);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Mapping plugin composition failed in: " + directory);
            }

            if (Plugins == null) Plugins = new List<Plugin>();

            // The built-in plugin is essential for loading existing profiles.
            // MEF may silently exclude an assembly when Windows has blocked it.
            // A narrowly scoped reflection fallback avoids incorrectly
            // treating valid saved mappings as corrupt.
            if (!Plugins.Any(plugin => plugin.GetType().FullName ==
                "HidWizards.UCR.Plugins.Remapper.ButtonToButton"))
            {
                var dll = BuiltInPluginPath;
                if (File.Exists(dll))
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(dll);
                        foreach (var type in assembly.GetExportedTypes().Where(type =>
                            typeof(Plugin).IsAssignableFrom(type) &&
                            !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null))
                        {
                            if (Plugins.Any(plugin => plugin.GetType() == type)) continue;
                            Plugins.Add((Plugin)Activator.CreateInstance(type));
                        }
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        Logger.Error(exception, "Bundled mapping plugin dependency failed to load: " +
                            string.Join("; ", exception.LoaderExceptions.Where(error => error != null)
                                .Select(error => error.Message)));
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Bundled mapping plugin could not load: " + dll);
                    }
                }
                else
                    Logger.Error("Required bundled mapping plugin is missing: " + dll);
            }
            Logger.Info("Mapping plugins available: " + Plugins.Count +
                "; ButtonToButton installed: " + Plugins.Any(plugin =>
                    plugin.GetType().FullName == "HidWizards.UCR.Plugins.Remapper.ButtonToButton"));
        }

        public Plugin GetNewPlugin(Plugin plugin)
        {
            return (Plugin)Activator.CreateInstance(plugin.GetType());
        }
    }
}
