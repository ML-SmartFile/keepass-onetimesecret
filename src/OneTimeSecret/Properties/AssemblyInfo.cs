using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("OneTimeSecret")]
// MUST be exactly "KeePass Plugin". KeePass's PluginManager.LoadPlugins compares
// FileVersionInfo.ProductName against AppDefs.PluginProductName and silently
// `continue`s on any mismatch -- before hashing the file or calling Assembly.LoadFrom.
// Changing this string makes the plugin invisible in Tools -> Plugins with no error.
[assembly: AssemblyProduct("KeePass Plugin")]
[assembly: AssemblyDescription("Share a KeePass entry as a one-time link via OneTimeSecret.")]
[assembly: AssemblyCompany("SmartFile GmbH")]
[assembly: AssemblyCopyright("Copyright (c) 2026 SmartFile GmbH")]
[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
