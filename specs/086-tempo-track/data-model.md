# Data model

TempoPoint: beat >= 0, BPM 1..1000, outgoing shape step/linear/smooth,
normalized handles x1,y1,x2,y2 in [0,1], x1 <= x2. Strictly increasing beats
with minimum separation 1e-6; first beat exactly zero. Reject nonfinite data.

TempoMap: immutable points plus compiled beat/seconds nodes. Positive slopes
give unique forward/inverse mapping; extend the last point at its BPM. Store
by immutable shared pointer in Graph, inherited on every structural edit.

Gesture: original/preview points, selected indices, bar beat, previous anchor
and target physical seconds. No project mutation until commit.

Persistence: optional tempoPoints in manifest v31; absent means legacy BPM.
All shapes and handles round-trip without loss.
