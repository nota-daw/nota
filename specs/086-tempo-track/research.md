# Research

- Decision: extend Transport and Graph. This checkout has constant BPM and no
  existing tempo map; changing BPM periodically would shift earlier events.
- Decision: compile Bezier ramps into cumulative seconds/beat lookup nodes on
  the authoring thread. Binary search and interpolation need no render allocation.
- Decision: local samples-per-beat continues to drive device/session block clocks;
  absolute arrangement event timestamps use exact map conversions.
- Decision: reproject unwarped audio placement from its old seconds on map edits
  and play it at its natural rate. Warped material continues to follow music.
- Decision: gesture preview lives in the lane; release replaces the map once,
  through existing undo publication. Cancellation changes no project data.
- Decision: existing numerator/denominator determine bars. Creating signature
  markers is outside approved #535. Snap reuses available transient data.
