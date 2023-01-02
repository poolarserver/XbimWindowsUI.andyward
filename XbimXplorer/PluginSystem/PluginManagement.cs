﻿using Microsoft.Extensions.Logging;
using NuGet;
using NuGet.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace XbimXplorer.PluginSystem
{
    public enum PluginChannelOption
    {
        Installed,
        LatestStable,
        LatestIncludingDevelopment,
        AllCompatibleVersions
    }

    internal class PluginManagement
    {
        private readonly Dictionary<string, PluginInformation> _diskPlugins =
           new Dictionary<string, PluginInformation>();

        public PluginManagement(Microsoft.Extensions.Logging.ILogger logger = null)
        {
            Logger = logger ?? XplorerMainWindow.LoggerFactory.CreateLogger<PluginManagement>();
        }

        protected static Microsoft.Extensions.Logging.ILogger Logger { get; private set; }

        internal static IEnumerable<DirectoryInfo> GetPluginDirectories()
        {
            var di = GetPluginsDirectory();
            return !di.Exists
                ? Enumerable.Empty<DirectoryInfo>()
                : di.GetDirectories();
        }

        internal IEnumerable<PluginInformation> DiskPlugins => _diskPlugins.Values;

        internal void RefreshLocalPlugins()
        {
            _diskPlugins.Clear();
            var dirs = PluginManagement.GetPluginDirectories();
            foreach (var directoryInfo in dirs)
            {
                var pc = new PluginInformation(directoryInfo);
                _diskPlugins.Add(pc.PluginId, pc);
            }
        }

        internal static DirectoryInfo GetPluginsDirectory()
        {
            var path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrWhiteSpace(path))
                path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (path == null)
                return null;
            path = Path.Combine(path, "Plugins");
            var di = new DirectoryInfo(path);
            return di;
        }

        internal static ManifestMetadata GetManifestMetadata(DirectoryInfo path)
        {
            var tmp = new ManifestMetadata() { Id = path.Name, Version = new NuGet.Versioning.NuGetVersion("0.0.0") };
            var file = path.GetFiles("*.manifest").FirstOrDefault();
            if (file == null)
            {
                return tmp;
            }
            using (var stream = file.OpenRead())
            {
                try
                {
                    var rd = Manifest.ReadFrom(stream, false);
                    return rd.Metadata;
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, ex, "Error loading plugin manifest from {path}", path);
                }
            }
            return tmp;
        }

        internal static string SelectedRepoUrl => "https://www.myget.org/F/xbim-plugins/api/v2";

        internal IEnumerable<PluginInformation> GetPlugins(PluginChannelOption option, string winUiNugetVersion)
        {
#if nuge
            RefreshLocalPlugins();
            var repo = PackageRepositoryFactory.Default.CreateRepository(SelectedRepoUrl);
            var allowDevelop = option != PluginChannelOption.LatestStable;
            var invokingVerion = new SemanticVersion(winUiNugetVersion);

            var fnd = repo.Search("XplorerPlugin", allowDevelop);
            var tmpPackages = new List<IPackage>();
            foreach (var package in fnd)
            {
                // drop develop if latest stable
                System.Diagnostics.Debug.WriteLine($"Evaluating {package}");
                if (option == PluginChannelOption.LatestStable && !string.IsNullOrEmpty(package.Version.SpecialVersion))
                {
                    continue;
                }
                // check it is compatible
                var sel = package.DependencySets.SelectMany(x => x.Dependencies.Where(y => y.Id.StartsWith("Xbim.WindowsUI"))).FirstOrDefault();
                if (sel != null && sel.VersionSpec.Satisfies(invokingVerion))
                {
                    tmpPackages.Add(package);
                }
            }

            if (option == PluginChannelOption.LatestStable || option ==  PluginChannelOption.LatestIncludingDevelopment)
            {
                // only one per ID
                var selPackages = new List<IPackage>();
                var grouped = tmpPackages.GroupBy(x => x.Id);
                foreach (var element in grouped)
                {
                    var maxVersion = element.Max(x => x.Version);
                    selPackages.Add(element.FirstOrDefault(x=>x.Version == maxVersion));
                }
                tmpPackages = selPackages;
            }
            
            
            foreach (var package in tmpPackages)
            {
                var pv = new PluginInformation(package);
                if (_diskPlugins.ContainsKey(package.Id))
                {
                    pv.SetDirectoryInfo(_diskPlugins[package.Id]);
                }
                yield return pv;
            }
#endif
            return Enumerable.Empty<PluginInformation>();
        }

        public static string GetEntryFile(DirectoryInfo dir, string filename = null)
        {
            var f = filename == null 
                ? Path.Combine(dir.FullName, dir.Name + ".exe") 
                : Path.Combine(dir.FullName, filename);
            return f;
        }

        internal static string GetStartupFileConfig(DirectoryInfo dir)
        {
            if (dir == null)
                return "";
            return Path.Combine(dir.FullName, "PluginConfig.xml");
        }

        public static void SetStartup(DirectoryInfo dir, PluginConfiguration.StartupBehaviour behaviour)
        {
            var p = new PluginConfiguration {OnStartup = behaviour};
            p.WriteXml(GetStartupFileConfig(dir));
        }

        public static PluginConfiguration GetConfiguration(DirectoryInfo dir)
        {
            var read = PluginConfiguration.ReadXml(GetStartupFileConfig(dir));
            return read;
        }
    }
}