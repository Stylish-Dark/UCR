using System;
using System.IO;
using HidWizards.UCR.Utilities;

namespace UCR.FileHandler
{
    class Program
    {
        static int Main(string[] args)
        {
            var directory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            var nonInteractive = args.Length > 1 &&
                string.Equals(args[1], "--non-interactive", StringComparison.Ordinal);
            if (!FileUnblockManager.UnblockAllProgramFiles(directory)) return -1;

            Console.WriteLine("Successfully unblocked all files in " + directory);
            if (!nonInteractive)
            {
                Console.WriteLine("Press any key to close...");
                Console.ReadKey();
            }

            return 0;
        }
    }
}
