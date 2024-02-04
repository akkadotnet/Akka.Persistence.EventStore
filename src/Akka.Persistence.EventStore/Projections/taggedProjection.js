const forTenant = '[[TENANT_ID]]';
const streamNamePattern = '[[TAGGED_STREAM_NAME_PATTERN]]';

fromAll()
    .when({
        $any: function (s, e) {
            if (e.metadata) {
                if (e.metadata.tenant !== forTenant) {
                    return;
                }
                
                let tags = e.metadata.tags;
                
                if (tags) {
                    if (tags.$values) {
                        tags = tags.$values;
                    }
                    
                    for (let i = 0, len = tags.length; i < len; i++) {
                        linkTo(streamNamePattern.replace('[[TAG]]', tags[i]), e);
                    }
                }
            }
        }
    });