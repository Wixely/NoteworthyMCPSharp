# NoteworthyMCPSharp

A Model Context Protocol (MCP) server that lets AI agents read, create, and edit Standard
MIDI Files. Built on the [Noteworthy](https://www.nuget.org/packages/Noteworthy) library and
the MCPSharp product line.

## What an agent can do

**Discover**
- `list_midi_files` — list MIDI files under a directory
- `search_midi_files` — substring search across the library
- `get_library_info` — show configured root + defaults

**Inspect**
- `get_midi_info` — format, time division, track count, tempo, key, time signature, duration, instruments
- `list_tracks` — per-track summary
- `list_notes` — extract notes as `{trackIndex, channel, pitch, startTick, durationTicks, velocity}`
- `validate_midi` — run the Noteworthy validator

**Compose**
- `create_midi_file` — new file with conductor track (tempo + time signature) and optional music track
- `add_track` — append an instrument track
- `set_track_instrument` / `rename_track` — fix up an existing track
- `add_notes` — bulk-insert notes at absolute ticks (the main composition tool)
- `add_chord` — N notes at the same tick
- `add_tempo_change` / `add_time_signature` / `add_control_change` — automation events
- `set_tempo` — replace the tempo at tick 0

**Edit**
- `transpose` — shift everything by N semitones (optionally skipping drums)
- `quantize_track` — snap NoteOn ticks to a grid
- `merge_to_single_track` — flatten to Format 0
- `convert_format` — Format 0 ↔ Format 1
- `delete_track` / `delete_notes_in_range` / `delete_midi_file`

**Reference**
- `list_instruments` — full General MIDI Level 1 list with categories
- `list_drum_sounds` — channel 9 percussion map
- `list_controllers` — common Control Change controller numbers
- `pitch_to_name` / `name_to_pitch` — `60 ↔ "C4"`, accepts sharps + flats
- `bpm_to_microseconds` — tempo arithmetic
- `tick_helper` — duration in ticks of common note values for a given PPQ

## Configuration

`NoteworthyMCPSharp.json` next to the executable. Override per-key via
`NOTEWORTHYMCP_` environment variables (e.g. `NOTEWORTHYMCP_Noteworthy__ReadOnly=false`).

| Section / key | Purpose |
| --- | --- |
| `Noteworthy.LibraryRoot` | Directory the agent is jailed to. Empty = launch cwd. |
| `Noteworthy.AllowAnyPath` | Permit paths outside the library. Default false. |
| `Noteworthy.ReadOnly` | Master safety: block every mutating tool. **Default true.** |
| `Noteworthy.AllowDelete` | Expose `delete_midi_file`. Default false. |
| `Noteworthy.MaxFileBytes` | Refuse to open files larger than this. Default 50 MB. |
| `Noteworthy.DefaultPpq` | Ticks-per-quarter for new files. Default 480. |
| `Noteworthy.DefaultTempoBpm` | Initial tempo for new files. Default 120. |
| `Noteworthy.DefaultTimeSignatureNumerator` / `Denominator` | New-file time sig. Default 4/4. |
| `Server.Host` / `Port` / `Path` | HTTP listener. Default `localhost:5710/mcp`. |
| `Server.Password` | Optional MCP endpoint password. Blank = open. |

## Endpoints

- `GET /healthz` — readiness + posture
- MCP HTTP transport at `Server.Path` (default `/mcp`)

## Safety posture

`ReadOnly=true` by default. Browsing the library and inspecting files is always allowed.
Composition / editing / delete tools refuse with a clear error until `ReadOnly=false`.
`AllowDelete` adds a second gate specifically for the destructive `delete_midi_file` tool.

## Running

```pwsh
dotnet run --project NoteworthyMCPSharp -c Release
```

Or publish single-file and run the produced `NoteworthyMCPSharp.exe`. The JSON config is
read from next to the executable so a single-file deployment keeps tunables external.

## Demos

[`demos/funky_fusion.mid`](demos/funky_fusion.mid) — ~60 s funky jazz fusion in C dorian
(Cm7 ↔ F7 vamp, 112 BPM swing) with drums, electric bass, Rhodes, alto sax, and a sawtooth
lead. Composed end-to-end through the MCP tools as a smoke test of the composition surface.
