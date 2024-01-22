fromAll()
    .when({
        $any: function (s, e) {
            if (e.metadata) {
                const persistenceId = e.metadata.persistenceId;
                
                if (persistenceId && persistenceId.length > 0 && e.metadata.skipPersistenceId !== true) {
                    linkTo('[[ALL_EVENT_STREAM_NAME]]', e);
                }
            }
        }
    });