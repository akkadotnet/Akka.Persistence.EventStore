namespace Akka.Persistence.EventStore
{
    public static class Constants
    {
        public static class EventMetadata
        {
            public const string ClrEventType = "clrEventType";
            public const string PersistenceId = "persistenceId";
            public const string Manifest = "manifest";
            public const string OccurredOn = "occurredOn";
            public const string SenderPath = "senderPath";
            public const string SequenceNr = "sequenceNr";
            public const string WriterGuid = "writerGuid";
            public const string Timestamp = "timestamp";
            public const string JournalType = "journalType";
        }

        public static class JournalTypes
        {
            public const string WriteJournal = "WriteJournal";
            public const string SnapshotJournal = "SnapshotJournal";
        }
    }
}
