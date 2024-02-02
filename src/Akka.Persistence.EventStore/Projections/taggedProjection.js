fromAll()
    .when({
        $any: function (s, e) {
            if (e.metadata) {
                let tags = e.metadata.tags;
                
                if (tags) {
                    if (tags.$values) {
                        tags = tags.$values;
                    }
                    
                    for (let i = 0, len = tags.length; i < len; i++) {
                        linkTo(`[[TAGGED_STREAM_PREFIX]]${tags[i]}`, e);
                    }
                }
            }
        }
    });