# Akka.Persistence.EventStore

[![NuGet Version](http://img.shields.io/nuget/v/Akka.Persistence.EventStore.svg?style=flat)](https://www.nuget.org/packages/Akka.Persistence.EventStore)

Akka Persistence EventStore Plugin is a plugin for `Akka Persistence` that provides  components:
 - Write journal store
 - Snapshot store
 - Standard [persistence queries](http://getakka.net/articles/persistence/persistence-query.html)
 - Akka Streams source for [persistent subscriptions](https://developers.eventstore.com/clients/grpc/persistent-subscriptions.html)

This plugin stores data in a [EventStoreDB](https://www.eventstore.com/) database and based on [EventStore.Client.Grpc](https://www.nuget.org/packages/EventStore.Client.Grpc) client library.

# Getting started

## The Easy Way, Using `Akka.Hosting`

```csharp
var host = new HostBuilder()
    .ConfigureServices((context, services) => {
        services.AddAkka("my-system-name", (builder, provider) =>
        {
            builder.WithEventStorePersistence(connectionString: _myConnectionString)
        });
    })
```

## The Classic Way, Using HOCON

These are the minimum HOCON configuration you need to start using Akka.Persistence.EventStore:
```hocon
akka.persistence {
    journal {
      plugin = "akka.persistence.journal.eventstore"
      
      eventstore {
            class = "Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore"
            connection-string = "{database-connection-string}"
        }
    }
  
    query.journal.eventstore {
        class = "Akka.Persistence.EventStore.Query.EventStoreReadJournalProvider, Akka.Persistence.EventStore"
        write-plugin = "akka.persistence.journal.eventstore"
    }
  
    snapshot-store {
        plugin = "akka.persistence.snapshot-store.eventstore"
        
        eventstore {
            class = "Akka.Persistence.EventStore.Snapshot.EventStoreSnapshotStore, Akka.Persistence.EventStore"
            connection-string = "{database-connection-string}"
        }
    }
}
```

# Configuration options

## Journal
- `connection-string` - Connection string, as described here: https://developers.eventstore.com/clients/grpc/#connection-string.
- `materializer-dispatcher` - Dispatcher used to drive journal actor
- `adapter ` - Controls how the event data and metadata is stored and retrieved. See Adapter section below for more information.
- `auto-initialize` - Whether or not the plugin should create projections to support read journal on startup. See Projections section below for more information.
- `prefix` - A optional prefix that will be added to streams.
- `tenant` - A optional tenant that should be used to support multi-tenant environments.
- `tagged-stream-name-pattern` - A pattern used when creating a stream name for a tags-stream. The name `[[TAG]]` will be replaced by the actual tag used.
- `persistence-ids-stream-name` - A name for the stream that stores all persistence id's (to support read journal).
- `persisted-events-stream-name` - A name for the stream that stores all events (to support read journal).

## Snapshot store
- `connection-string` - Connection string, as described here: https://developers.eventstore.com/clients/grpc/#connection-string.
- `materializer-dispatcher` - Dispatcher used to drive journal actor
- `adapter ` - Controls how the event data and metadata is stored and retrieved. See Adapter section below for more information.- `prefix` - A optional prefix that will be added to streams.
- `tenant` - A optional tenant that should be used to support multi-tenant environments.

## Query journal
- `write-plugin` - Absolute path to the write journal plugin configuration entry that this query journal will connect to.

# Adapter

Akka Persistence EventStore Plugin supports changing how data is stored and retrieved.
By default, it will serialize the data using the configured serializer in Akka, and populate the Metadata with the
following information:
```json
{
  "persistenceId": "p-14",
  "occurredOn": "2018-05-03T10:28:06.3437687-06:00",
  "manifest": "",
  "sender": "",
  "sequenceNr": 5,
  "writerGuid": "f8706bba-52a7-4326-a760-990c7f657c46",
  "journalType": "WriteJournal",
  "timestamp": 123456789,
  "tenant": "",
  "tags": []
}
```

If you are happy with the default serialization and metadata, but want to just augment the metadata or data, or do any of the following:

- Inspect event to add metadata
- Encrypt data
- Change the "type" stored in event store

You can inherit from DefaultAdapter and override the `Serialize`, `DeSerialize`, `GetEventMetadata`, `GetSnapshotMetadata`
, `GetEventMetadataFrom` and `GetSnapshotMetadataFrom` methods

You also have the option of creating a new implemenation of ```Akka.Persistence.EventStore.Serialization.IMessageAdapter```.
Everything is DIY in this case, including correct handling of internal Akka types if they appear in
events. Make use of the supplied `Akka.Serialization.Serialization` to help with this.

```csharp
public class CustomAdapter : IMessageAdapter
{
    public CustomAdapter(Akka.Serialization.Serialization serialization, ISettingsWithAdapter settings)
    {
    }

    public Task<EventData> Adapt(IPersistentRepresentation persistentMessage)
    {
        // Implement
    }

    public Task<EventData> Adapt(SnapshotMetadata snapshotMetadata, object snapshot)
    {
        // Implement
    }
    
    public Task<IPersistentRepresentation?> AdaptEvent(ResolvedEvent evnt)
    {
    	// Implement
    }
    
    public Task<SelectedSnapshot?> AdaptSnapshot(ResolvedEvent evnt)
    {
    	// Implement
    }
    
    public string GetManifest(Type type)
    {
    	// Implement
    }
}
```

Whichever direction you go, you will need to override the configuration

**Using Akka Hosting:**
```csharp
var host = new HostBuilder()
    .ConfigureServices((context, services) => {
        services.AddAkka("my-system-name", (builder, provider) =>
        {
            builder.WithEventStorePersistence(
                connectionString: _myConnectionString,
                adapter: "Your.Namespace.YourAdapter, Your.Assembly")
        });
    })
```

**Using Hocon:**
```
akka.persistence {
    journal {
        plugin = "akka.persistence.journal.eventstore""
        eventstore {
            class = "Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore"
            connection-string = "esdb://admin:changeit@localhost:2113"
            adapter = "Your.Namespace.YourAdapter, Your.Assembly"
        }
    }
}
```

# Projections

To support the Read Journal the plugin takes advantage of the [projections](https://developers.eventstore.com/server/v23.10/projections.html#introduction) feature
of EventStoreDB. If you setup `auto-initialize` on the Journal the required projections will be created for you on startup. You can also use `Akka.Persistence.EventStore.Projections.EventStoreProjectionsSetup`
to create the projections yourself if you want.

# Persistent subscriptions
[Persistent subscriptions](https://developers.eventstore.com/clients/grpc/persistent-subscriptions.html) can be used to subscribe to events stored in EventStoreDb and let the database handle offsets.
This plugin gives you a Akka streams source to make it easy to work with within Akka.net.

```csharp
var clientSettings = EventStoreClientSettings.Create(eventStoreContainer.ConnectionString ?? "");
        
var subscriptionClient = new EventStorePersistentSubscriptionsClient(clientSettings);

EventStoreSource
    .ForPersistentSubscription(
        subscriptionClient,
        "your-stream-name",
        "your-subscriptions-group-name",
        keepReconnecting: true) //true if you want the client to reconnect if it's disconnected, otherwise false (default). 
    .RunForeach(x =>
    {
        Console.WriteLine(x.Event.Event.EventType);

        x.Ack();
    }, _actorSystem.Materializer());
```

# Release Notes, Version Numbers, Etc

This project will automatically populate its release notes in all of its modules via the entries written inside [`RELEASE_NOTES.md`](RELEASE_NOTES.md) and will automatically update the versions of all assemblies and NuGet packages via the metadata included inside [`Directory.Build.props`](src/Directory.Build.props).

# Breaking Changes in 1.5

This is a complete rewrite of the plugin to use EventStore.Client.Grpc. This means that the plugin is not compatible with previous versions.

# Maintainer
- [MattiasJakobsson](https://github.com/MattiasJakobsson)
