# Contract: C ABI

```c
typedef struct NotaSessionClipProps {
    /* … existing fields unchanged … */
    int32_t jump_scene;
    int32_t follow_bypass; /* 1 = Follow configuration held (bypassed), 0 = active (default) */
} NotaSessionClipProps;
```

- `nota_session_get_clip_props` fills `follow_bypass` (0/1).
- `nota_session_set_clip_props` stores `follow_bypass != 0`; one undo step like every other prop.
- No new entry points. The struct size is unchanged at 64 bytes: the field takes the former tail
  padding at offset 60.
