using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.TCK;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreDatabaseSpec")]
public sealed class EventStoreSnapshotStoreSpec : PluginSpec
{
    public EventStoreSnapshotStoreSpec(DatabaseFixture databaseFixture)
        : base(FromConfig(CreateSpecConfig(databaseFixture)).WithFallback(Config),
            nameof(EventStoreSnapshotStoreSpec))
    {
        _senderProbe = CreateTestProbe();
        Initialize();
    }

    private static readonly string SpecConfigTemplate = @"
        akka.persistence.publish-plugin-commands = on
        akka.persistence.snapshot-store {
            plugin = ""akka.persistence.snapshot-store.my""
            my {
                class = ""TestPersistencePlugin.MySnapshotStore, TestPersistencePlugin""
                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
            }
        }";

    private static readonly Config Config = ConfigurationFactory.ParseString(SpecConfigTemplate);
    private readonly TestProbe _senderProbe;
    private List<SnapshotMetadata> _metadata;

    private IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);

    /// <summary>
    ///     The limit defines a number of bytes persistence plugin can support to store the snapshot.
    ///     If plugin does not support persistence of the snapshots of 10000 bytes or may support more than default size,
    ///     the value can be overriden by the SnapshotStoreSpec implementation with a note in a plugin documentation.
    /// </summary>
    private static int SnapshotByteSizeLimit => 100000;

    /// <summary>
    ///     Initializes a snapshot store with set of predefined snapshots.
    /// </summary>
    private void Initialize()
    {
        _metadata = WriteSnapshots().ToList();
    }

    private IEnumerable<SnapshotMetadata> WriteSnapshots()
    {
        var snapshotStoreSpec = this;
        for (var i = 1; i <= 5; ++i)
        {
            var metadata = new SnapshotMetadata(snapshotStoreSpec.Pid, i + 10);
            snapshotStoreSpec
                .SnapshotStore
                .Tell(
                    new SaveSnapshot(
                        metadata,
                        $"s-{i}"
                    ),
                    snapshotStoreSpec._senderProbe.Ref
                );
            yield return snapshotStoreSpec
                ._senderProbe
                .ExpectMsg<SaveSnapshotSuccess>(new TimeSpan?())
                .Metadata;
        }
    }


    private static Config CreateSpecConfig(DatabaseFixture databaseFixture)
    {
        var specString = @"
                akka.test.single-expect-default = 10s
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.eventstore""
                        eventstore {
                            class = ""Akka.Persistence.EventStore.Snapshot.EventStoreSnapshotStore, Akka.Persistence.EventStore""
                            connection-string = """ + databaseFixture.ConnectionString + @"""
                            tenant = ""es-snapshots""
                        }
                    }
                }";

        return ConfigurationFactory.ParseString(specString);
    }

    [Fact(Skip = "Not supported by EventStore, it has simpler retention policy")]
    public void SnapshotStore_should_delete_a_single_snapshot_identified_by_SequenceNr_in_snapshot_metadata()
    {
        var snapshotMetadata = _metadata[2];
        var metadata =
            new SnapshotMetadata(snapshotMetadata.PersistenceId, snapshotMetadata.SequenceNr);
        var message = new DeleteSnapshot(metadata);
        var testProbe = CreateTestProbe();
        Subscribe<DeleteSnapshot>(testProbe.Ref);
        SnapshotStore.Tell(message, _senderProbe.Ref);
        testProbe.ExpectMsg(message);
        _senderProbe.ExpectMsg<DeleteSnapshotSuccess>();
        SnapshotStore.Tell(
            new LoadSnapshot(Pid, new SnapshotSelectionCriteria(metadata.SequenceNr),
                long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg((Predicate<LoadSnapshotResult>)(result =>
        {
            if (result.ToSequenceNr == long.MaxValue && result.Snapshot != null &&
                result.Snapshot.Metadata.Equals(_metadata[1]))
                return result.Snapshot.Snapshot.ToString() == "s-2";
            return false;
        }));
    }

    [Fact]
    public void SnapshotStore_should_delete_all_snapshots_matching_upper_sequence_number_and_timestamp_bounds()
    {
        var snapshotMetadata = _metadata[2];
        var message = new DeleteSnapshots(Pid,
            new SnapshotSelectionCriteria(snapshotMetadata.SequenceNr, snapshotMetadata.Timestamp));
        var testProbe = CreateTestProbe();
        Subscribe<DeleteSnapshots>(testProbe.Ref);
        SnapshotStore.Tell(message, _senderProbe.Ref);
        testProbe.ExpectMsg(message);
        _senderProbe.ExpectMsg<DeleteSnapshotsSuccess>();

        SnapshotStore.Tell(
            new LoadSnapshot(Pid,
                new SnapshotSelectionCriteria(snapshotMetadata.SequenceNr, snapshotMetadata.Timestamp), long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.Snapshot == null)
                return result.ToSequenceNr == long.MaxValue;
            return false;
        });

        SnapshotStore.Tell(
            new LoadSnapshot(Pid,
                new SnapshotSelectionCriteria(_metadata[3].SequenceNr, _metadata[3].Timestamp), long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.ToSequenceNr == long.MaxValue && result.Snapshot != null &&
                result.Snapshot.Metadata.Equals(_metadata[3]))
                return result.Snapshot.Snapshot.ToString() == "s-4";
            return false;
        });
    }

    [Fact]
    public void SnapshotStore_should_load_the_most_recent_snapshot()
    {
        var msg = new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue);
        SnapshotStore.Tell(msg, _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            var isMaxSeqNo = result.ToSequenceNr == long.MaxValue;
            var snapshotNotNull = result.Snapshot != null;
            var matchMetadata = result.Snapshot.Metadata.Equals(_metadata[4]);
            if (isMaxSeqNo && snapshotNotNull && matchMetadata)
                return result.Snapshot.Snapshot.ToString() == "s-5";
            return false;
        });
    }

    [Fact]
    public void
        SnapshotStore_should_load_the_most_recent_snapshot_matching_an_upper_sequence_number_and_timestamp_bound()
    {
        SnapshotStore.Tell(
            new LoadSnapshot(Pid,
                new SnapshotSelectionCriteria(13L, _metadata[2].Timestamp, 0L, new DateTime?()),
                long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            var isMaxSeqNo = result.ToSequenceNr == long.MaxValue;
            var snapshotNotNull = result.Snapshot != null;
            var matchMetadata = result.Snapshot.Metadata.Equals(_metadata[2]);
            if (isMaxSeqNo && snapshotNotNull && matchMetadata)
                return result.Snapshot.Snapshot.ToString() == "s-3";
            return false;
        }, new TimeSpan?());

        SnapshotStore.Tell(
            new LoadSnapshot(Pid,
                new SnapshotSelectionCriteria(long.MaxValue, _metadata[2].Timestamp, 0L, new DateTime?()),
                13L), _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            var isMaxSeqNo = result.ToSequenceNr == 13L;
            var snapshotNotNull = result.Snapshot != null;
            var matchMetadata = result.Snapshot.Metadata.Equals(_metadata[2]);
            if (isMaxSeqNo && snapshotNotNull && matchMetadata)
                return result.Snapshot.Snapshot.ToString() == "s-3";
            return false;
        }, new TimeSpan?());
    }

    [Fact]
    public void SnapshotStore_should_load_the_most_recent_snapshot_matching_an_upper_sequence_number_bound()
    {
        SnapshotStore.Tell(
            new LoadSnapshot(Pid, new SnapshotSelectionCriteria(13L), long.MaxValue), _senderProbe.Ref
        );
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.ToSequenceNr == long.MaxValue && result.Snapshot != null &&
                result.Snapshot.Metadata.Equals(_metadata[2]))
                return result.Snapshot.Snapshot.ToString() == "s-3";
            return false;
        });
        SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, 13L),
            _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.ToSequenceNr == 13L && result.Snapshot != null &&
                result.Snapshot.Metadata.Equals(_metadata[2]))
                return result.Snapshot.Snapshot.ToString() == "s-3";
            return false;
        }, new TimeSpan?());
    }

    [Fact]
    public void SnapshotStore_should_not_delete_snapshots_with_non_matching_upper_timestamp_bounds()
    {
        var snapshotMetadata = _metadata[3];
        var criteria = new SnapshotSelectionCriteria(snapshotMetadata.SequenceNr,
            snapshotMetadata.Timestamp.Subtract(TimeSpan.FromTicks(1L)), 0L, new DateTime?());
        var message = new DeleteSnapshots(Pid, criteria);
        var testProbe = CreateTestProbe();
        Subscribe<DeleteSnapshots>(testProbe.Ref);
        SnapshotStore.Tell(message, _senderProbe.Ref);
        testProbe.ExpectMsg(message, new TimeSpan?());
        _senderProbe.ExpectMsg<DeleteSnapshotsSuccess>(m => m.Criteria.Equals(criteria), new TimeSpan?());
        SnapshotStore.Tell(
            new LoadSnapshot(Pid,
                new SnapshotSelectionCriteria(snapshotMetadata.SequenceNr, snapshotMetadata.Timestamp, 0L,
                    new DateTime?()), long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg((Predicate<LoadSnapshotResult>)(result =>
        {
            if (result.ToSequenceNr == long.MaxValue && result.Snapshot != null &&
                result.Snapshot.Metadata.Equals(_metadata[3]))
                return result.Snapshot.Snapshot.ToString() == "s-4";
            return false;
        }), new TimeSpan?());
    }

    [Fact]
    public void SnapshotStore_should_not_load_a_snapshot_given_an_invalid_persistence_id()
    {
        SnapshotStore.Tell(
            new LoadSnapshot("invalid", SnapshotSelectionCriteria.Latest, long.MaxValue),
            _senderProbe.Ref
        );
        _senderProbe.ExpectMsg<LoadSnapshotResult>(
            result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue, new TimeSpan?());
    }

    [Fact]
    public void SnapshotStore_should_not_load_a_snapshot_given_non_matching_sequence_number_criteria()
    {
        SnapshotStore.Tell(
            new LoadSnapshot(Pid, new SnapshotSelectionCriteria(7L), long.MaxValue),
            _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.Snapshot == null)
                return result.ToSequenceNr == long.MaxValue;
            return false;
        }, new TimeSpan?());

        SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, 7L),
            _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
        {
            if (result.Snapshot == null)
                return result.ToSequenceNr == 7L;
            return false;
        }, new TimeSpan?());
    }

    [Fact]
    public void SnapshotStore_should_not_load_a_snapshot_given_non_matching_timestamp_criteria()
    {
        var criteria = new SnapshotSelectionCriteria(
            long.MaxValue,
            new DateTime(100000L),
            0L,
            new DateTime?()
        );
        SnapshotStore.Tell(new LoadSnapshot(Pid, criteria, long.MaxValue), _senderProbe.Ref);
        _senderProbe.ExpectMsg<LoadSnapshotResult>(
            result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue, new TimeSpan?());
    }

    [Fact(Skip = "Not supported in EventStore, it is append only database")]
    public void SnapshotStore_should_save_and_overwrite_snapshot_with_same_sequence_number()
    {
        var metadata1 = _metadata[4];
        SnapshotStore.Tell(new SaveSnapshot(metadata1, "s-5-modified"),
            _senderProbe.Ref);
        var metadata2 =
            _senderProbe.ExpectMsg<SaveSnapshotSuccess>(new TimeSpan?()).Metadata;
        Assert.Equal(metadata1.SequenceNr, metadata2.SequenceNr);
        SnapshotStore.Tell(
            new LoadSnapshot(Pid, new SnapshotSelectionCriteria(metadata1.SequenceNr),
                long.MaxValue), _senderProbe.Ref);
        var loadSnapshotResult =
            _senderProbe.ExpectMsg<LoadSnapshotResult>(new TimeSpan?());
        Assert.Equal("s-5-modified", loadSnapshotResult.Snapshot.Snapshot.ToString());
        Assert.Equal(metadata1.SequenceNr, loadSnapshotResult.Snapshot.Metadata.SequenceNr);
    }

    [Fact]
    public void SnapshotStore_should_save_bigger_size_snapshot()
    {
        var metadata = new SnapshotMetadata(Pid, 100L);
        var buffer = new byte[SnapshotByteSizeLimit];
        new Random().NextBytes(buffer);
        SnapshotStore.Tell(new SaveSnapshot(metadata, buffer), _senderProbe.Ref);
        _senderProbe.ExpectMsg<SaveSnapshotSuccess>(new TimeSpan?());
    }
}