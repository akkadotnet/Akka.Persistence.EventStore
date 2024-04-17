using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Messages;

public record NewPersistenceIdFound([PublicAPI]string PersistenceId);