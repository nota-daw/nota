# Quickstart: validating the Assistant panel (098)

Run from the repo root. Smoke flags run the built dll directly (`dotnet run -- <flag>` loses
the flag):

```bash
dotnet build tests/Nota.SmokeTest -c Release --nologo
SMOKE="dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll"
```

## 1. Build the manual index

```bash
scripts/build-manual-index.sh ../nota-docs
```

Expect `assets/manual/manual-index.json.gz` rewritten, a summary line (pages, sections per
language, how-tos, anchored, explanatory), and exit 0. Errors list the page and section.

## 2. Logic checks

```bash
$SMOKE --assistant
```

Expect `ASSISTANT PASSED`: index loads (en + ru); anchors known; every how-to anchored; EN/RU
anchor parity; tokenizer goldens; BM25 finds the fixed probes; Markdown model covers every
FR-012 element; `view.assistant` bound and listed in Shortcuts; toolbar order includes
Assistant.

## 3. Retrieval quality (SC-001)

```bash
$SMOKE --assistant-eval                 # words only: expect ≥ 80 % top-3
$SMOKE --assistant-eval <model dir>     # with e5: expect ≥ 90 % top-3
```

`<model dir>` holds `model_quantized.onnx`, `tokenizer.json` and the ONNX Runtime library (an
installed model store folder works).

## 4. In the app

1. Launch Nota offline (Wi-Fi off), no AI models installed.
2. Click the Assistant key on the toolbar → the panel opens on the right, full height, input focused.
3. Ask "how do I duck the bass when the kick hits" → reply under 1 s quoting the Compressor's
   sidechain section, with *Open in the manual* and *Show me*.
4. Ask the same in Russian ("как приглушить бас под бочку") → the Russian section.
5. Press *Show me* on a fresh project → step 1 says to add a Compressor first and highlights
   the browser's Audio effects; add one, press Next → the Sidechain switch is outlined.
6. Rebind Toggle Assistant in Preferences ▸ Shortcuts; narrow the window until the key moves
   into ⋯; switch theme and Classic look; restart → width and open state persist.
7. Preferences ▸ AI models → install *Manual search*; ask "how to make a part repeat" (no
   shared words with "Loop") → the loop section is in the reply.

## 5. Gate

```bash
scripts/build.sh
```

Must pass (includes `--assistant`).
