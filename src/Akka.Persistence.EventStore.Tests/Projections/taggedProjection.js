options({ //option
    $includeLinks: false,
    reorderEvents: false
});

fromAll()
    .when({
        $init: function (s, e) {
            return {streams: ["a", "b", "c", "d", "f", "g", "h"]};
        },
        "string": function (s, e) {
            if (s.streams.includes(e.streamId) && e.data)
                if (e.data.includes("{{COLOR}}")) {
                    linkTo("{{COLOR}}", e);
                }
        }
    });