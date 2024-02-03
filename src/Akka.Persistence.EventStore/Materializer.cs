using System;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Implementation;
using Akka.Util;

namespace Akka.Persistence.EventStore;

internal static class Materializer
{
    private static ActorSystem ActorSystemOf(IActorRefFactory context)
    {
        return context switch
        {
            null => throw new ArgumentNullException(nameof(context), "IActorRefFactory must be defined"),
            ExtendedActorSystem system => system,
            IActorContext actorContext => actorContext.System,
            _ => throw new ArgumentException(
                $"ActorRefFactory context must be a ActorSystem or ActorContext, got [{context.GetType()}]"),
        };
    }

    /// <summary>
    ///     Creates a Materializer under the /system/ hierarchy instead of /user/
    ///     This ensures that we don't accidentally get torn down before the rest of the system.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="settings"></param>
    /// <param name="namePrefix"></param>
    /// <returns></returns>
    public static ActorMaterializer CreateSystemMaterializer(
        ExtendedActorSystem context,
        ActorMaterializerSettings? settings = null,
        string? namePrefix = null)
    {
        var haveShutDown = new AtomicBoolean();
        var system = ActorSystemOf(context);
        system.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
        settings ??= ActorMaterializerSettings.Create(system);

        return new ActorMaterializerImpl(
            system: system,
            settings: settings,
            dispatchers: system.Dispatchers,
            supervisor: context.SystemActorOf(
                props: StreamSupervisor.Props(settings, haveShutDown).WithDispatcher(settings.Dispatcher),
                name: StreamSupervisor.NextName()),
            haveShutDown: haveShutDown,
            flowNames: EnumerableActorName.Create(namePrefix ?? "Flow"));
    }
}
