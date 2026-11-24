# Quickstart: check spec 114 by hand

1. Build and run Nota; turn Remote on and connect a phone (or `npm run mock` in `src/web/remote`).
2. Add a Volt track with Arp before it and Reverb after it; open Macros on the phone.
3. Device ◀ ▶ walk Arp → Volt → Reverb; page buttons show Main · Osc · Filter · LFO on Volt.
4. Play notes: Volt Main shows the filter curve with a moving spectrum; turn Cutoff — the corner
   moves, the header shows "FILTER 1 FREQ" and the value for a second.
5. Reverb Main: raise Decay, the tail reaches −60 dB later; hit a drum: the measured tail draws
   under the set line.
6. Connect a controller (or the test port), Follow on: Page ▶ on the controller moves the phone;
   Track ▶ on the phone selects the next track in Nota.
7. Leave the phone on a still page with nothing playing: no `led` traffic after the first frame
   (browser dev tools → WS frames).
8. Every built-in device: the smoke test renders each page; open a few by hand (EQ-8, Compressor,
   Delay, Chorus, Scale, Chord, Rhythm, Operator) and compare with the card.
