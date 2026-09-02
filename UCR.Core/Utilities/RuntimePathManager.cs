using System;
using System.IO;

namespace HidWizards.UCR.Core.Utilities
{
    public static class RuntimePathManager
    {
        public static string NormalizeWorkingDirectory()
        {
            var applicationDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            Directory.SetCurrentDirectory(applicationDirectory);
            return applicationDirectory;
        }
    }
}
