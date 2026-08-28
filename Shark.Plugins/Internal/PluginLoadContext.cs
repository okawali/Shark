using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Shark.Plugins.Internal
{
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _assemblyPath;
        private readonly string _directory;
        private readonly AssemblyDependencyResolver _resolver;
        private readonly AssemblyLoadContext _hostContext;

        public PluginLoadContext(string assemblyPath)
            : base(Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: false)
        {
            _assemblyPath = Path.GetFullPath(assemblyPath);
            _directory = Path.GetDirectoryName(_assemblyPath);
            _resolver = new AssemblyDependencyResolver(_assemblyPath);
            _hostContext = GetLoadContext(typeof(PluginLoadContext).Assembly) ?? Default;
        }

        public Assembly LoadPluginAssembly() => LoadFromAssemblyPath(_assemblyPath);

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.Name))
            {
                return null;
            }

            // Share every dependency the host can bind, not just the plugin contract.
            try
            {
                return _hostContext.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // The host may be missing this dependency or have an incompatible version.
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null && File.Exists(path))
            {
                return LoadFromAssemblyPath(path);
            }

            // Also support loose assemblies without a deps.json, including satellite assemblies.
            var culture = assemblyName.CultureName;
            var directory = string.IsNullOrEmpty(culture) || culture == "neutral"
                ? _directory
                : Path.Combine(_directory, culture);
            path = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null && File.Exists(path))
            {
                return LoadUnmanagedDllFromPath(path);
            }

            foreach (var name in GetNativeLibraryNames(unmanagedDllName))
            {
                path = Path.Combine(_directory, name);
                if (File.Exists(path))
                {
                    return LoadUnmanagedDllFromPath(path);
                }
            }

            // Let the runtime continue with its normal system-library search.
            return IntPtr.Zero;
        }

        private static IEnumerable<string> GetNativeLibraryNames(string name)
        {
            if (Path.IsPathRooted(name))
            {
                yield return name;
                yield break;
            }

            if (OperatingSystem.IsWindows())
            {
                yield return name;
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    yield return name + ".dll";
                }
                yield break;
            }

            var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            var prefixAllowed = !name.Contains(Path.DirectorySeparatorChar);
            var hasSoSuffix = OperatingSystem.IsLinux()
                && (name.EndsWith(".so", StringComparison.Ordinal) || name.Contains(".so.", StringComparison.Ordinal));

            string[] candidates = hasSoSuffix
                ? [name, name + extension]
                : [name + extension, name];

            foreach (var candidate in candidates)
            {
                yield return candidate;
                if (prefixAllowed) yield return "lib" + candidate;
            }
        }
    }
}
