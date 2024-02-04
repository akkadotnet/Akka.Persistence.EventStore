# Akka.Persistence.EventStore

[![NuGet Version](http://img.shields.io/nuget/v/Akka.Persistence.EventStore.svg?style=flat)](https://www.nuget.org/packages/Akka.Persistence.EventStore)

Akka Persistence EventStore Plugin is a plugin for `Akka Persistence` that provides  components:
 - write journal store
 - snapshot store
 - standard [persistence queries](http://getakka.net/articles/persistence/persistence-query.html)

This plugin stores data in a [EventStoreDB](https://www.eventstore.com/) database and based on [EventStore.Client.Grpc](https://www.nuget.org/packages/EventStore.Client.Grpc) client library.

## Installation
From `Nuget Package Manager`
```PowerShell
Install-Package Akka.Persistence.EventStore
```
From `.NET CLI`
```Shell
dotnet add package Akka.Persistence.EventStore
```

## Write Journal plugin
To activate the journal plugin, add the following line to your HOCON config:
```
akka.persistence.journal.plugin = "akka.persistence.journal.eventstore"
```
This will run the journal with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

#### Configuration
- `connection-string` - Connection string, as described here: https://developers.eventstore.com/clients/grpc/#connection-string.
- `adapter ` - Controls how the event data and metadata is stored and retrieved. See Adapter section below for more information.
- `auto-initialize` - Whether or not the plugin should create projections to support read journal on startup. See Projections section below for more information.
- `prefix` - A optional prefix that will be added to streams.
- `tenant` - A optional tenant that should be used to support multi-tenant environments.
- `tagged-stream-name-pattern` - A pattern used when creating a stream name for a tags-stream. The name `[[TAG]]` will be replaced by the actual tag used.
- `persistence-ids-stream-name` - A name for the stream that stores all persistence id's (to support read journal).
- `persisted-events-stream-name` - A name for the stream that stores all events (to support read journal).

#### Example HOCON Configuration
```
 akka.persistence {
    journal {
        plugin = "akka.persistence.journal.eventstore""
        eventstore {
            connection-string = "esdb://admin:changeit@localhost:2113"
	    adapter = "default"
	    auto-initialize = false
	    
	    prefix = ""
	    tenant = ""

	    tagged-stream-name-pattern = "tagged-[[TAG]]"
	    persistence-ids-stream-name = "persistenceids"
	    persisted-events-stream-name = "persistedevents"
        }
    }
}
```
### Adapter

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
  "journalType": "WriteJournal"
  "timestamp": 123456789,
  "tenant": "",
  "tags": []
}
```

If you are happy with the default serialization and metadata, but want to just augment the metadata or data, or do any of the following:

- Inspect event to add metadata
- Encrypt data
- Change the "type" stored in event store

You can inherit from DefaultAdapter and override the ```Serialize```, ```DeSerialize```, ```GetEventMetadata```, ```GetSnapshotMetadata```
, ```GetEventMetadataFrom``` and ```GetSnapshotMetadataFrom``` methods

You also have the option of creating a new implemenation of ```Akka.Persistence.EventStore.Serialization.IMessageAdapter```.
Everything is DIY in this case, including correct handling of internal Akka types if they appear in 
events. Make use of the supplied ```Akka.Serialization.Serialization``` to help with this.

```C#
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

Whichever direction you go, you will need to override the HOCON to use your new adapter
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

## Snapshot plugin

To activate the snapshot plugin, add the following line to your HOCON config:
```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.eventstore"
```
This will run the snapshot store with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

#### Configuration
- `connection-string` - Connection string, as described here: https://developers.eventstore.com/clients/grpc/#connection-string.
- `adapter ` - Controls how the event data and metadata is stored and retrieved. See Adapter section below for more information.
- `prefix` - A optional prefix that will be added to streams.
- `tenant` - A optional tenant that should be used to support multi-tenant environments.

#### Example HOCON Configuration
```
 akka.persistence {
    snapshot-store {
        plugin = "akka.persistence.snapshot-store.eventstore""
        eventstore {
            connection-string = "esdb://admin:changeit@localhost:2113"
	    adapter = "default"
	    
	    prefix = ""
	    tenant = ""
        }
    }
}
```

## Read Journal plugin

Please note that you need to cofigure write jouranl anyways since EventStore 
Persistance Query reuses connection from that journal.

To activate the journal plugin, add the following line to your HOCON config:
```
akka.persistence.query.journal.plugin = "akka.persistence.query.journal.eventstore"
```
This will run the journal with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

#### HOCON Configuration

- `write-plugin` - Absolute path to the write journal plugin configuration entry that this query journal will connect to. If undefined (or "") it will connect to the default journal as specified by the `akka.persistence.journal.plugin` property.
- `refresh-interval` - The amount of time the plugin will wait between queries when it didn't find any events.

#### HOCON Configuration Example
```
akka.persistence.query.journal.eventstore {
    write-plugin = ""
    refresh-interval = 5s
}
```

#### Usage

To use standard queries please refer to documentation about [Persistence Query](http://getakka.net/articles/persistence/persistence-query.html) on getakka.net website.

#### Projections

To support the Read Journal the plugin takes advantage of the [projections](https://developers.eventstore.com/server/v23.10/projections.html#introduction) feature
of EventStoreDB. If you setup `auto-initialize` on the Journal the required projections will be created for you on startup. You can also use `Akka.Persistence.EventStore.Projections.EventStoreProjectionsSetup`
to create the projections yourself if you want.

## Breaking Changes in 1.5

1. The legacy adapter has been removed.
2. The default adapter will now use the serializer used by akka.
3. The query plugin has been changed to use eventstore projections that needs to be created.

## Breaking Changes in 1.4

1. The `DefaultEventAdapter` does not support internal Akka types (e.g. actor refs) and thus specs are 
failing. This has been updated to use `Akka.Serialization.NewtonSoftJsonSerializer`. If this does not 
affect you, or you have projections that depend on old configuration, use the following keys:

```
akka.persistence.journal.eventstore.adapter = legacy
akka.persistence.snapshot-store.eventstore.adapter = legacy
```

2. Adapter API has been changed to more correctly serialize the Sender actor ref.

Derived event adapter classes requires a minor interface change, namely removed
`Func<string, IActorRef>` argument. The Akka built-in Persistent serialization mechanism 
uses `System.Provider.ResolveActorRef` and `Akka.Serialization.Serialization.SerializedActorPath` 
to accomplish this. Legacy behavior is preserved by using `System.ActorSelection` rather 
than the journal's actor context.

## Maintainer
- [ryandanthony](https://github.com/ryandanthony)
- [mjaric](https://github.com/mjaric)
- [ptjhuang](https://github.com/ptjhuang)
- [MattiasJakobsson](https://github.com/MattiasJakobsson)
