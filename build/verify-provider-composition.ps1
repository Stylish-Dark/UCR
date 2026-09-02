param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimePath
)

$ErrorActionPreference = 'Stop'

$runtime = (Resolve-Path -LiteralPath $RuntimePath).Path
$providersPath = Join-Path $runtime 'Providers'
if (-not (Test-Path -LiteralPath $providersPath -PathType Container)) {
    throw "Final runtime Providers directory is missing: $providersPath"
}

# IOWrapper's GenericMEFPluginLoader enumerates .\Providers with System.IO. Constructing
# IOController inside powershell.exe proved unreliable on the x86 GitHub runner because the
# host process can expose PowerShell's own directory as the effective .NET working directory.
# Use a tiny x86 .NET Framework process instead, pin its CWD immediately, then construct the
# exact deployed IOController from UCR's runtime files.
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw "32-bit .NET Framework C# compiler is unavailable: $csc"
}

$hostExe = Join-Path $runtime 'ProviderCompositionSmokeHost.exe'
$hostSource = Join-Path $env:RUNNER_TEMP 'ProviderCompositionSmokeHost.cs'
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $hostSource = Join-Path ([System.IO.Path]::GetTempPath()) 'ProviderCompositionSmokeHost.cs'
}

$source = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class ProviderCompositionSmokeHost
{
    private static int Main()
    {
        object controller = null;
        try
        {
            var runtime = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            Environment.CurrentDirectory = runtime;
            var effectiveCurrentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            Console.WriteLine("Smoke host runtime: " + runtime);
            Console.WriteLine("Smoke host CWD:     " + effectiveCurrentDirectory);

            if (!string.Equals(runtime, effectiveCurrentDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Provider smoke host failed to pin its process working directory to the UCR runtime.");
            }

            var providersPath = Path.Combine(runtime, "Providers");
            if (!Directory.Exists(providersPath))
            {
                throw new DirectoryNotFoundException("Final runtime Providers directory is missing: " + providersPath);
            }

            Assembly.LoadFrom(Path.Combine(runtime, "lib", "IOWrapper.DTOs.dll"));
            Assembly.LoadFrom(Path.Combine(runtime, "lib", "IOWrapper.IProvider.dll"));
            var coreAssembly = Assembly.LoadFrom(Path.Combine(runtime, "lib", "IOWrapper.Core.dll"));
            var controllerType = coreAssembly.GetType("HidWizards.IOWrapper.Core.IOController", true);

            // Re-assert immediately before construction: GenericMEFPluginLoader resolves .\Providers
            // during IOController's constructor, so this is the boundary that matters.
            Environment.CurrentDirectory = runtime;
            controller = Activator.CreateInstance(controllerType);

            var providersField = controllerType.GetField(
                "_providers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (providersField == null)
            {
                throw new MissingFieldException(
                    "Unable to inspect IOWrapper provider composition: _providers field was not found.");
            }

            var providers = providersField.GetValue(controller);
            if (providers == null)
            {
                throw new InvalidOperationException("IOWrapper provider dictionary is null.");
            }

            var keysProperty = providers.GetType().GetProperty("Keys");
            if (keysProperty == null)
            {
                throw new MissingMemberException("IOWrapper provider dictionary has no Keys property.");
            }

            var keys = keysProperty.GetValue(providers, null) as IEnumerable;
            if (keys == null)
            {
                throw new InvalidOperationException("Unable to enumerate composed IOWrapper providers.");
            }

            var providerNames = new List<string>();
            foreach (var key in keys)
            {
                providerNames.Add(Convert.ToString(key));
            }

            Console.WriteLine("Composed providers: " + string.Join(", ", providerNames.ToArray()));

            if (!providerNames.Contains("Core_Interception"))
            {
                throw new InvalidOperationException(
                    "Final runtime could not compose Core_Interception through IOWrapper.");
            }
            if (!providerNames.Contains("Core_ViGEm"))
            {
                throw new InvalidOperationException(
                    "Final runtime could not compose Core_ViGEm through IOWrapper.");
            }

            Console.WriteLine("Verified final deployed IOWrapper can compose Core_Interception and Core_ViGEm.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            var disposable = controller as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }
}
'@

try {
    [System.IO.File]::WriteAllText($hostSource, $source, [System.Text.UTF8Encoding]::new($false))

    & $csc /nologo /target:exe /platform:x86 "/out:$hostExe" $hostSource
    if ($LASTEXITCODE -ne 0) {
        throw "Provider-composition smoke host failed to compile with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw "Provider-composition smoke host compiler produced no executable: $hostExe"
    }

    & $hostExe
    if ($LASTEXITCODE -ne 0) {
        throw "Final provider-composition smoke test failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $hostExe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $hostSource -Force -ErrorAction SilentlyContinue
}
