using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;

namespace Akka.Persistence.EventStore.Serialization;

public static class SettingsWithAdapterExtensions
{
    private static readonly IImmutableDictionary<string, Type> AdapterOverrides = new Dictionary<string, Type>
    {
        ["default"] = typeof(DefaultMessageAdapter),
        ["system-text-json"] = typeof(SystemTextJsonMessageAdapter)
    }.ToImmutableDictionary();
    
    public static IMessageAdapter FindEventAdapter(
        this ISettingsWithAdapter settings,
        ActorSystem actorSystem)
    {
        var type = AdapterOverrides.TryGetValue(settings.Adapter, out var adapterType)
            ? adapterType
            : Type.GetType(settings.Adapter);

        return Create(type, settings, actorSystem);
    }

    private static IMessageAdapter Create(
        Type? type,
        ISettingsWithAdapter settings,
        ActorSystem actorSystem)
    {
        var logger = actorSystem.Log;
     
        if (type == null)
        {
            logger.Error(
                "Unable to find type [{0}] Adapter. Is the assembly referenced properly? Falling back to default",
                settings.Adapter);
            
            return Create(AdapterOverrides["default"], settings, actorSystem);
        }
        
        try
        {
            var adapterConstructor =
                type.GetConstructor([typeof(Akka.Serialization.Serialization), typeof(ISettingsWithAdapter)]);

            if ((adapterConstructor != null
                    ? adapterConstructor.Invoke([actorSystem.Serialization, settings])
                    : Activator.CreateInstance(type)) is IMessageAdapter adapter)
                
                return adapter;

            logger.Error(
                $"Unable to create instance of type [{type.AssemblyQualifiedName}] Adapter. Do you have an empty constructor, or one that takes in Akka.Serialization.Serialization and Akka.Persistence.EventStore.Configuration.ISettingsWithAdapter? Falling back to default.");

            return Create(AdapterOverrides["default"], settings, actorSystem);
        }
        catch (Exception e)
        {
            logger.Error(e, "Error loading Adapter. Falling back to default");
            
            return Create(AdapterOverrides["default"], settings, actorSystem);
        }
    }
}