fromAll()
    .when({
        $any: function (s, e) {
            if (e.metadata) {
                const persistenceId = e.metadata.persistenceId;
                const sequenceNr = e.metadata.sequenceNr;
                
                if (persistenceId 
                    && persistenceId.length > 0 
                    && sequenceNr 
                    && sequenceNr === 1 
                    && e.metadata.skipPersistenceId !== true) {
                    emit(
                        '[[ALL_PERSISTENCE_IDS_STREAM_NAME]]', 
                        '[[EVENT_NAME]]', 
                        {'persistenceId': persistenceId}, 
                        {
                            'persistenceId': persistenceId,
                            'occurredOn': e.metadata.occurredOn,
                            'manifest': '[[EVENT_MANIFEST]]',
                            'sequenceNr': 1,
                            'writerGuid': e.metadata.writerGuid,
                            'journalType': '[[JOURNAL_TYPE]]',
                            'tags': [],
                            'skipPersistenceId': true
                        });
                }
            }
        }
    });