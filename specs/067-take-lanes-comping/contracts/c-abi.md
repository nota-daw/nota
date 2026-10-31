# C ABI: take lanes (specs/067)

All functions are message-thread only.

```c
typedef struct NotaTakeInfo {
    int32_t id;                   /* engine-unique; comp clips carry it */
    int32_t number;               /* "Take N" on its track */
    int32_t group;                /* the loop recording it came from */
    int32_t reserved;
    double  origin_beat;          /* timeline beat of the take audio's frame 0 */
    double  start_beat;           /* recorded range on the timeline (current tempo) */
    double  length_beats;
    double  source_offset_frames; /* recorded range inside the take audio */
    int64_t length_frames;
    int64_t sample_id;            /* the take audio (nota_sample_read) */
} NotaTakeInfo;

int32_t    nota_track_take_count(const NotaEngine*, int32_t track_id);
int32_t    nota_track_take_info(const NotaEngine*, int32_t track_id, int32_t index, NotaTakeInfo* out); /* 1/0 */
int32_t    nota_track_take_peaks(const NotaEngine*, int32_t track_id, int32_t index,
                                 float* out_min_max, int32_t max_points);  /* buckets written */
/* Comp: the take plays over [start_beat, end_beat) (clamped to its range). One undo step. */
NotaResult nota_track_take_comp(NotaEngine*, int32_t track_id, int32_t take_id, double start_beat, double end_beat);
/* Delete a take; its comp sections keep playing. One undo step. */
NotaResult nota_track_take_delete(NotaEngine*, int32_t track_id, int32_t take_id);
/* Flatten every recording's takes on the track into one clip each. One undo step. */
NotaResult nota_track_takes_flatten(NotaEngine*, int32_t track_id);
/* The take a clip plays (0 = none). */
int32_t    nota_clip_take_id(const NotaEngine*, int32_t track_id, int32_t clip_index);

/* Project load (no undo entry). restore returns the new take id or -1; set_take_id tags a
 * restored clip and shares the take's audio with it when they match. */
int32_t    nota_track_restore_take(NotaEngine*, int32_t track_id, const char* path_utf8, int32_t number,
                                   int32_t group, double origin_beat, double source_offset_frames,
                                   int64_t length_frames);
NotaResult nota_clip_set_take_id(NotaEngine*, int32_t track_id, int32_t clip_index, int32_t take_id);

/* Smoke-test hook: record `frames` of interleaved stereo into track_id as a loop take started at
 * start_beat, through the real capture path (ring → drain → stop), without a device. 1 = ok. */
int32_t    nota_take_record_test(NotaEngine*, int32_t track_id, const float* interleaved_stereo,
                                 int32_t frames, double start_beat);
```
