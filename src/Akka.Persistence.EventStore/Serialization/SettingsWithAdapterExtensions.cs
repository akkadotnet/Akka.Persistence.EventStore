using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;

namespace Akka.Persistence.EventStore.Serialization;

public static class SettingsWithAdapterExtensions
{
    public static IJournalMessageSerializer FindSerializer(
        this ISettingsWithAdapter settings,
        ActorSystem actorSystem)
    {
        if (settings.Adapter == "default")
            return GetDefaultSerializer();
        
        var logger = actorSystem.Log;
        
        try
        {
            var journalSerializerType = Type.GetType(settings.Adapter);
         
            if (journalSerializerType == null)
            {
                logger.Error(
                    $"Unable to find type [{settings.Adapter}] Serializer. Is the assembly referenced properly? Falling back to default");
                
                return GetDefaultSerializer();
            }

            var serializerConstructor =
                journalSerializerType.GetConstructor([typeof(Akka.Serialization.Serialization)]);

            if ((serializerConstructor != null
                    ? serializerConstructor.Invoke([actorSystem.Serialization])
                    : Activator.CreateInstance(journalSerializerType)) is IJournalMessageSerializer journalSerializer)
                
                return journalSerializer;

            logger.Error(
                $"Unable to create instance of type [{journalSerializerType.AssemblyQualifiedName}] Serializer. Do you have an empty constructor, or one that takes in Akka.Serialization.Serialization? Falling back to default.");
            
            return GetDefaultSerializer();
        }
        catch (Exception e)
        {
            logger.Error(e, "Error loading Serializer. Falling back to default");
            
            return GetDefaultSerializer();
        }
        
        IJournalMessageSerializer GetDefaultSerializer()
        {
            return new DefaultJournalMessageSerializer(actorSystem.Serialization);
        }
    }
}