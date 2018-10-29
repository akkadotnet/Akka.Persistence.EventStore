# Akka.Persistence.EventStore and Akka.Persistence.EventStore.Query

[![NuGet Version](http://img.shields.io/nuget/v/Akka.Persistence.EventStore.svg?style=flat)](https://www.nuget.org/packages/Akka.Persistence.EventStore)

Akka Persistence EventStore Plugin is a plugin for `Akka Persistence` that provides  components:
 - write journal store
 - snapshot store
 - standard [persistence queries](http://getakka.net/articles/persistence/persistence-query.html)

This plugin stores data in a [EventStore](https://eventstore.org) database and based on [EventStore.Client](https://www.nuget.org/packages/EventStore.Client) client library for .net45 and  [EventStore.ClientAPI.NetCore](https://www.nuget.org/packages/EventStore.ClientAPI.NetCore) client library for .netstandard2.0.

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
- `connection-string` - connection string, as described here: https://eventstore.org/docs/dotnet-api/connecting-to-a-server/index.html#connection-string
- `connection-name ` - connection name to tell eventstore server who is connecting
- `read-batch-size ` - when reading back events, how many to bring back at a time
- `adapter ` - controls how the event data and metadata is stored and retrieved. See Adapter section below for more information.

#### Example HOCON Configuration
```
 akka.persistence {
    journal {
        plugin = "akka.persistence.journal.eventstore""
        eventstore {
            connection-string = "ConnectTo=tcp://admin:changeit@localhost:1113; HeartBeatTimeout=500"
            connection-name = "Akka"
        }
    }
}
```
### Adapter

Akka Persistence EventStore Plugin supports changing how data is stored and retrieved. 
By default, it will serialize the data using ```Newtonsoft.Json```, and populate the Metadata with the following information:
```json
{
  "persistenceId": "p-14",
  "occurredOn": "2018-05-03T10:28:06.3437687-06:00",
  "manifest": "",
  "senderPath": "",
  "sequenceNr": 5,
  "writerGuid": "f8706bba-52a7-4326-a760-990c7f657c46",
  "clrEventType": "System.String, System.Private.CoreLib"
}
```

If you are happy with the default serialization and metadata, but want to just augment the metadata or data, or do any of the following:

- Inspect event to add metadata
- Encrypt data
- Change the "type" stored in event store

You can inherit from DefaultAdapter and override the ```ToBytes``` and ```ToEvent``` methods
```C#
public class AltAdapter : DefaultAdapter
{
    protected override byte[] ToBytes(object @event, JObject metadata, out string type, out bool isJson)
    {
        var bytes = base.ToBytes(@event, metadata, out type, out isJson);
        
        //Add some additional metadata:
        metadata["additionalProp"] = true;

        //Do something additional with bytes.
        return bytes;

    }
    protected override object ToEvent(byte[] bytes, JObject metadata)
    {
        //Use the metadata to determine if you need to do something additional to the data
        //Do something additional with bytes before handing it off to be deserialized.
        return base.ToEvent(bytes, metadata);
    }         
}
```

You also have the option of creating a new implemenation of ```Akka.Persistence.EventStore.IAdapter```

```C#
public class CustomAdapter : IAdapter
{
    public DefaultAdapter()
    {
    }

    public EventData Adapt(IPersistentRepresentation persistentMessage)
    {
        //Implement
    }

    public IPersistentRepresentation Adapt(ResolvedEvent resolvedEvent, Func<string, IActorRef> actorSelection = null)
    {
        //Implement
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
            connection-string = "ConnectTo=tcp://admin:changeit@localhost:1113; HeartBeatTimeout=500"
            connection-name = "Akka"
            read-batch-size = 500
            adapter = "Your.Namespace.YourAdapter, Your.Assembly"
        }
    }
}
```

# Akka.Persistence.EventStore.Query

## Installation
From `Nuget Package Manager`
```PowerShell
Install-Package Akka.Persistence.EventStore.Query
```
From `.NET CLI`
```Shell
dotnet add package Akka.Persistence.EventStore.Query
```

Please note that you need to cofigure write jouranl anyways since EventStore 
Persistance Query reuses connection from that journal. Also, it uses 
`IEventStoreConnection.SubscribeToStreamFrom` known as Catch up subscription, 
this means that each subscription will start new connection under the hood. 
This is the way how official EventStore client library works.

## Read Journal plugin
To activate the journal plugin, add the following line to your HOCON config:
```
akka.persistence.query.journal.plugin = "akka.persistence.query.journal.eventstore"
```
This will run the journal with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

#### HOCON Configuration

- `write-plugin` - Absolute path to the write journal plugin configuration entry that this query journal will connect to. If undefined (or "") it will connect to the default journal as specified by the `akka.persistence.journal.plugin` property.
- `max-buffer-size` - How many events to fetch in one query (replay) and keep buffered until they are delivered downstreams. Default value is 500.
- `auto-ack` - Should query journal automaticaly aknowladge delivered events to downstream. Default is `false`. This is reserved for next release for competing consumers query. See [this docs](https://eventstore.org/docs/dotnet-api/competing-consumers/index.html) for more details

#### HOCON Configuration Example
```
akka.persistence.query.journal.eventstore {
    write-plugin = ""
    max-buffer-size = 500
    auto-ack = false
}
```

#### Usage

To use standard queries please refer to documentation about [Persistence Query](http://getakka.net/articles/persistence/persistence-query.html) on getakka.net website.

## TODO

- CompetingBy(string groupName, string streamName)

## Maintainer
- [ryandanthony](https://github.com/ryandanthony)
- [mjaric](https://github.com/mjaric)