using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OctaShift.Properties
{
    internal sealed class SingleFileSettingsProvider : SettingsProvider, IApplicationSettingsProvider
    {
        private const string ProviderName = "SingleFileSettingsProvider";
        private const string ApplicationNameConst = "OctaShift";
        private const string FileName = "user.config";

        private readonly string _configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationNameConst,
            FileName);

        private Dictionary<string, string> _values = new();
        private bool _loaded = false;
        private bool _legacyCleanupAttempted = false;

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(string.IsNullOrWhiteSpace(name) ? ProviderName : name, config);

            var dir = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }

        public override string ApplicationName
        {
            get => ApplicationNameConst;
            set { /* ignore */ }
        }

        public override string Name => ProviderName;

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            EnsureLoaded();

            var result = new SettingsPropertyValueCollection();

            foreach (SettingsProperty property in collection)
            {
                var value = new SettingsPropertyValue(property)
                {
                    SerializedValue = _values.TryGetValue(property.Name, out var stored)
                        ? stored
                        : property.DefaultValue
                };

                result.Add(value);
            }

            return result;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            EnsureLoaded();

            foreach (SettingsPropertyValue value in collection)
            {
                _values[value.Name] = value.SerializedValue?.ToString() ?? string.Empty;
            }

            Save();
        }

        SettingsPropertyValue IApplicationSettingsProvider.GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            return new SettingsPropertyValue(property)
            {
                SerializedValue = property.DefaultValue
            };
        }

        void IApplicationSettingsProvider.Reset(SettingsContext context)
        {
            EnsureLoaded();
            _values.Clear();
            Save();
        }

        void IApplicationSettingsProvider.Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        {
            // No-op: storage is not versioned, so nothing to migrate per version.
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;

            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(_configFilePath))
            {
                TryLoadNewFormat(_configFilePath);
            }
            else
            {
                var imported = TryImportLegacyConfig();
                Save();

                if (imported)
                {
                    CleanupLegacyConfigs();
                }
            }

            _loaded = true;
        }

        private void TryLoadNewFormat(string path)
        {
            try
            {
                var doc = XDocument.Load(path);
                var settings = doc.Root?.Elements("Setting");

                if (settings != null)
                {
                    foreach (var setting in settings)
                    {
                        var name = setting.Attribute("Name")?.Value;
                        var value = setting.Element("Value")?.Value ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(name))
                            _values[name] = value;
                    }
                }
            }
            catch
            {
                // Ignore corrupt file; start fresh.
            }
        }

        private bool TryImportLegacyConfig()
        {
            try
            {
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationNameConst);
                if (!Directory.Exists(baseDir)) return false;

                var candidates = Directory.GetFiles(baseDir, "user.config", SearchOption.AllDirectories);
                var latest = candidates
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest == null)
                    return false;

                var doc = XDocument.Load(latest.FullName);
                var settingsSection = doc.Root?
                    .Element("userSettings")?
                    .Element("OctaShift.Properties.Settings");

                if (settingsSection == null)
                {
                    settingsSection = doc.Root?
                        .Element("configuration")?
                        .Element("userSettings")?
                        .Element("OctaShift.Properties.Settings");
                }

                if (settingsSection == null)
                    return false;

                foreach (var setting in settingsSection.Elements("setting"))
                {
                    var name = setting.Attribute("name")?.Value;
                    var value = setting.Element("value")?.Value ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(name))
                        _values[name] = value;
                }

                return true;
            }
            catch
            {
                // Ignore legacy import failures; Start with defaults.
            }

            return false;
        }

        private void Save()
        {
            var doc = new XDocument(
                new XElement("Settings",
                    _values.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                           .Select(kvp =>
                               new XElement("Setting",
                                   new XAttribute("Name", kvp.Key),
                                   new XAttribute("SerializeAs", "String"),
                                   new XElement("Value", kvp.Value ?? string.Empty)))));

            doc.Save(_configFilePath);
        }

        private void CleanupLegacyConfigs()
        {
            if (_legacyCleanupAttempted)
                return;

            _legacyCleanupAttempted = true;

            try
            {
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationNameConst);
                if (!Directory.Exists(baseDir)) return;

                var currentDir = Path.GetDirectoryName(_configFilePath)?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty;

                var legacyCandidates = Directory.GetFiles(baseDir, FileName, SearchOption.AllDirectories)
                    .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_configFilePath), StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .ToList();

                foreach (var fi in legacyCandidates)
                {
                    try
                    {
                        var dir = fi.Directory;
                        if (dir == null) continue;

                        fi.Delete();

                        var cursor = dir;
                        while (cursor != null && cursor.FullName.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.Equals(cursor.FullName.TrimEnd(Path.DirectorySeparatorChar), currentDir, StringComparison.OrdinalIgnoreCase))
                                break;

                            if (Directory.Exists(cursor.FullName) && !Directory.EnumerateFileSystemEntries(cursor.FullName).Any())
                            {
                                Directory.Delete(cursor.FullName, false);
                                cursor = cursor.Parent;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore failures; continue cleaning the rest.
                    }
                }
            }
            catch
            {
                // Ignore cleanup failures; they are best-effort only.
            }
        }
    }
}

