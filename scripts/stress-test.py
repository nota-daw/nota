#!/usr/bin/env python3
"""
Nota stress test: ramp from 1 to 40 tracks, each with Volt instrument
+ EQ-8 + Compressor + Auto Filter. Records DSP load + process CPU at
each step while transport is playing.

Requires: Nota running with MCP enabled on 127.0.0.1:3900.
Launch the app first:
    dotnet run --project src/managed/Nota.App -c Release
"""

import json
import random
import subprocess
import sys
import time
import urllib.request
import urllib.error

MCP_URL = "http://127.0.0.1:3900/"
MAX_TRACKS = 80
SETTLE_SECONDS = 3.0
RESULTS_CSV = "scripts/stress-test-results.csv"

# MIDI pattern generation: a random melodic phrase in each track's clip.
SCALE = [0, 2, 3, 5, 7, 10, 12]  # minor pentatonic-ish, one octave
BASE_PITCH = 48  # C3

REQ_ID = 0

def mcp_call(method, params=None):
    global REQ_ID
    REQ_ID += 1
    body = json.dumps({
        "jsonrpc": "2.0",
        "id": REQ_ID,
        "method": method,
        "params": params or {},
    }).encode()
    req = urllib.request.Request(
        MCP_URL,
        data=body,
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode()
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
    # Response may be SSE (lines: data: {...}) or plain JSON.
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        if line.startswith("data:"):
            line = line[5:].strip()
        if line.startswith("{"):
            obj = json.loads(line)
            if "result" in obj:
                return obj["result"]
            if "error" in obj:
                raise RuntimeError(f"MCP error: {obj['error']}")
    raise RuntimeError(f"Unparseable MCP response: {raw[:200]}")

def mcp_init():
    return mcp_call("initialize", {
        "protocolVersion": "2025-06-18",
        "capabilities": {},
        "clientInfo": {"name": "stress-test", "version": "1"},
    })

def mcp_tool(name, arguments=None):
    return mcp_call("tools/call", {
        "name": name,
        "arguments": arguments or {},
    })

def parse_tool_result(result):
    """MCP tool result: {content: [{type: text, text: "<json>"}]} -> parsed JSON or raw text."""
    contents = result.get("content", [])
    if not contents:
        return None
    text = contents[0].get("text", "")
    try:
        return json.loads(text)
    except (json.JSONDecodeError, ValueError):
        return text

def wait_for_mcp(timeout=60):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            mcp_init()
            return True
        except Exception:
            time.sleep(1)
    return False

def get_process_cpu(pid):
    """Sample process CPU % over 1 second using ps."""
    try:
        out1 = subprocess.check_output(
            ["ps", "-p", str(pid), "-o", "%cpu="], text=True
        ).strip()
        time.sleep(1.0)
        out2 = subprocess.check_output(
            ["ps", "-p", str(pid), "-o", "%cpu="], text=True
        ).strip()
        return (float(out1) + float(out2)) / 2.0
    except Exception:
        return -1

def find_nota_pid():
    """Find the actual Nota.App process (not the 'dotnet run' parent)."""
    try:
        out = subprocess.check_output(
            ["pgrep", "-f", "Nota.App/bin/.*Nota.App"], text=True
        ).strip().splitlines()
        return int(out[0]) if out else None
    except Exception:
        pass
    try:
        out = subprocess.check_output(
            ["pgrep", "-x", "Nota.App"], text=True
        ).strip().splitlines()
        return int(out[0]) if out else None
    except Exception:
        return None

def gen_random_notes(seed, clip_length=4.0):
    """Generate a random MIDI phrase: 8-24 notes within the clip, beat-relative."""
    rng = random.Random(seed)
    n_notes = rng.randint(8, 24)
    notes = []
    for _ in range(n_notes):
        pitch = BASE_PITCH + rng.choice(SCALE) + rng.choice([0, 12, -12])
        start = round(rng.uniform(0, clip_length - 0.25), 3)
        length = round(rng.choice([0.25, 0.5, 0.5, 1.0, 1.0, 2.0]), 3)
        velocity = round(rng.uniform(0.5, 1.0), 3)
        notes.append({"pitch": pitch, "start": start, "length": length, "velocity": velocity})
    notes.sort(key=lambda n: n["start"])
    return notes

def main():
    print("==> Waiting for Nota MCP server on 127.0.0.1:3900 ...")
    if not wait_for_mcp():
        print("ERROR: MCP server not reachable. Start Nota first:")
        print("  dotnet run --project src/managed/Nota.App -c Release")
        sys.exit(1)
    print("    MCP connected.")

    pid = find_nota_pid()
    if pid:
        print(f"    Nota PID: {pid}")
    else:
        print("    WARNING: could not find Nota PID; process CPU will be skipped.")

    # Clear existing tracks via get_overview + remove_track.
    print("==> Clearing existing tracks ...")
    ov = parse_tool_result(mcp_tool("get_overview"))
    if ov and "tracks" in ov:
        for t in ov["tracks"]:
            mcp_tool("remove_track", {"trackId": t["id"]})
        print(f"    Removed {len(ov['tracks'])} existing tracks.")

    # Start transport.
    mcp_tool("play")
    print("==> Transport: play")

    rows = []
    print(f"\n{'Tracks':>6} {'DSP%':>8} {'ProcCPU%':>10} {'Xruns':>7} {'Notes':>7}  {'TrackId':>8}")
    print("-" * 55)

    for n in range(1, MAX_TRACKS + 1):
        # Add Volt instrument track (kind 6).
        track_id = parse_tool_result(mcp_tool("add_instrument_track", {"kind": 6}))
        if not isinstance(track_id, int) or track_id <= 0:
            print(f"  ERROR at track {n}: add_instrument_track returned {track_id}")
            break

        # Add EQ-8 (0), Compressor (1), Auto Filter (7).
        for kind in (0, 1, 7):
            mcp_tool("add_device", {"trackId": track_id, "kind": kind})

        # Add a random MIDI clip with random notes.
        clip_idx = parse_tool_result(mcp_tool("add_midi_clip", {"trackId": track_id, "startBeat": 0, "lengthBeats": 4}))
        notes = gen_random_notes(seed=track_id * 1000 + n)
        mcp_tool("set_clip_notes", {"trackId": track_id, "clipIndex": clip_idx, "notes": notes})

        # Ensure transport is playing.
        mcp_tool("play")

        # Let DSP settle.
        time.sleep(SETTLE_SECONDS)

        # Read CPU load.
        cpu = parse_tool_result(mcp_tool("get_cpu_load"))
        dsp_load = cpu.get("dspLoad", -1) if isinstance(cpu, dict) else -1
        dsp_pct = dsp_load * 100.0 if dsp_load >= 0 else -1

        info = parse_tool_result(mcp_tool("get_engine_info"))
        xruns = info.get("xruns", -1) if isinstance(info, dict) else -1

        proc_cpu = get_process_cpu(pid) if pid else -1
        note_count = len(notes)

        row = {
            "tracks": n,
            "dsp_pct": round(dsp_pct, 2),
            "proc_cpu_pct": round(proc_cpu, 2),
            "xruns": xruns,
            "notes": note_count,
            "track_id": track_id,
        }
        rows.append(row)
        print(f"{n:>6} {dsp_pct:>8.2f} {proc_cpu:>10.2f} {xruns:>7} {note_count:>7}  {track_id:>8}")

        # Safety: stop if DSP load > 95%.
        if dsp_pct > 95:
            print(f"\n!! DSP load {dsp_pct:.1f}% exceeded 95% — stopping early at {n} tracks.")
            break

    # Stop transport.
    mcp_tool("stop")
    print("\n==> Transport: stop")

    # Write CSV.
    with open(RESULTS_CSV, "w") as f:
        f.write("tracks,dsp_pct,proc_cpu_pct,xruns,notes,track_id\n")
        for r in rows:
            f.write(f"{r['tracks']},{r['dsp_pct']},{r['proc_cpu_pct']},{r['xruns']},{r['notes']},{r['track_id']}\n")
    print(f"\n==> Results written to {RESULTS_CSV}")

    # Summary.
    if rows:
        print(f"\nSummary: {len(rows)} tracks tested.")
        print(f"  DSP load:  {rows[0]['dsp_pct']:.2f}% -> {rows[-1]['dsp_pct']:.2f}%")
        print(f"  Proc CPU:  {rows[0]['proc_cpu_pct']:.2f}% -> {rows[-1]['proc_cpu_pct']:.2f}%")
        print(f"  Xruns:     {rows[0]['xruns']} -> {rows[-1]['xruns']}")

if __name__ == "__main__":
    main()