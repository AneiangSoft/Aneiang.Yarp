using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Services;

/// <summary>Describes a built-in adapter that compiles plugin configuration directly to native YARP fields.</summary>
public sealed record NativePluginAdapterDescriptor(string PluginId, string DisplayName, PluginBindingScope Scope);
