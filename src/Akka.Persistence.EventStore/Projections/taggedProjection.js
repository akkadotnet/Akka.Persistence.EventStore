fromAll()
    .when({
        $any: function (s, e) {
            if (e.metadata) {
                const tags = e.metadata.tags;
                
                if (tags) {
                    for (let i = 0, len = tags.length; i < len; i++) {
                        linkTo(`[[TAGGED_STREAM_PREFIX]]${tags[i]}`, e);
                    }
                }
            }
        }
    });