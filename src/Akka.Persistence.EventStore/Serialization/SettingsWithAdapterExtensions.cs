using Akka.Actor;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;

namespace Akka.Persistence.EventStore.Serialization;

public static class SettingsWithAdapterExtensions
{
    public static IMessageAdapter FindEventAdapter(
        this ISettingsWithAdapter settings,
        ActorSystem actorSystem)
    {
        if (settings.Adapter == "default")
            return GetDefaultAdapter();
        
        var logger = actorSystem.Log;
        
        try
        {
            var journalMessageAdapterType = Type.GetType(settings.Adapter);
         
            if (journalMessageAdapterType == null)
            {
                logger.Error(
                    $"Unable to find type [{settings.Adapter}] Adapter. Is the assembly referenced properly? Falling back to default");
                
                return GetDefaultAdapter();
            }

            var adapterConstructor =
                journalMessageAdapterType.GetConstructor([typeof(Akka.Serialization.Serialization), typeof(ISettingsWithAdapter)]);

            if ((adapterConstructor != null
                    ? adapterConstructor.Invoke([actorSystem.Serialization, settings])
                    : Activator.CreateInstance(journalMessageAdapterType)) is IMessageAdapter adapter)
                
                return adapter;

            logger.Error(
                $"Unable to create instance of type [{journalMessageAdapterType.AssemblyQualifiedName}] Adapter. Do you have an empty constructor, or one that takes in Akka.Serialization.Serialization? Falling back to default.");
            
            return GetDefaultAdapter();
        }
        catch (Exception e)
        {
            logger.Error(e, "Error loading Adapter. Falling back to default");
            
            return GetDefaultAdapter();
        }
        
        IMessageAdapter GetDefaultAdapter()
        {
            return new DefaultMessageAdapter(actorSystem.Serialization, settings);
        }
    }
}