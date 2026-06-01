using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NoteworthyMCPSharp.Services;
using Noteworthy;
using Noteworthy.Events;
using Noteworthy.Events.Channel;
using Noteworthy.Events.Meta;

namespace NoteworthyMCPSharp.Tools;

/// <summary>
/// Tools for creating and adding to MIDI files. All "tick" parameters are ABSOLUTE positions
/// in the file's timeline (ticks since tick 0). Internally we re-sort each track and recompute
/// delta times after every mutation — agents don't have to think about MIDI's delta-time encoding.
/// </summary>
[McpServerToolType]
public static class CompositionTools
{
    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>
    /// Capture (absoluteTick, event) for every event in a track, then sort + rebuild the track
    /// with computed delta times. Stable sort by tick keeps simultaneous events in their original
    /// order, which matters for e.g. NoteOff(prev note) coming before NoteOn(new note at same tick).
    /// </summary>
    private static void RebuildTrackFromAbsolute(MidiTrack track, List<(long Tick, MidiEvent Event)> absoluteEvents)
    {
        absoluteEvents.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        track.Clear();
        long lastTick = 0;
        var hasEndOfTrack = false;
        foreach (var (tick, ev) in absoluteEvents)
        {
            if (ev is EndOfTrackEvent) { hasEndOfTrack = true; continue; } // re-add at the end
            ev.DeltaTime = tick - lastTick;
            track.Add(ev);
            lastTick = tick;
        }
        if (hasEndOfTrack) track.Add(new EndOfTrackEvent(0));
    }

    /// <summary>Extract events with absolute ticks. Skips EndOfTrack because we always re-add it last.</summary>
    private static List<(long Tick, MidiEvent Event)> ToAbsoluteList(MidiTrack track)
    {
        var result = new List<(long, MidiEvent)>();
        long cursor = 0;
        foreach (var ev in track.Events)
        {
            cursor += ev.DeltaTime;
            if (ev is EndOfTrackEvent) continue;
            result.Add((cursor, ev));
        }
        return result;
    }

    private static byte ClampVel(int v) => (byte)Math.Clamp(v, 1, 127);
    private static byte ClampChannel(int ch) => (byte)Math.Clamp(ch, 0, 15);
    private static byte ClampNote(int n) => (byte)Math.Clamp(n, 0, 127);

    // ───────────────────────────── tools ─────────────────────────────

    [McpServerTool(Name = "create_midi_file"),
     Description("Create a new empty MIDI file with a conductor track (tempo + time signature) and one optional empty music track. Returns the resolved path.")]
    public static async Task<string> CreateMidiFile(
        MidiLibrary lib,
        [Description("Path for the new file (relative to LibraryRoot or absolute).")] string path,
        [Description("Tempo in BPM. Defaults to Noteworthy:DefaultTempoBpm.")] double? tempoBpm = null,
        [Description("Time signature numerator. Defaults to Noteworthy:DefaultTimeSignatureNumerator.")] byte? timeSignatureNumerator = null,
        [Description("Time signature denominator (1, 2, 4, 8, 16, …). Defaults to Noteworthy:DefaultTimeSignatureDenominator.")] int? timeSignatureDenominator = null,
        [Description("Ticks per quarter note (PPQ). Defaults to Noteworthy:DefaultPpq.")] int? ppq = null,
        [Description("Optional name for the first music track, e.g. 'Lead'. Empty/null = no music track is created.")] string? initialTrackName = null,
        [Description("Refuse to overwrite an existing file. Default true.")] bool failIfExists = true,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("create_midi_file");
        var resolved = lib.ResolveScoped(path, requireExists: false);
        if (failIfExists && File.Exists(resolved))
            return JsonSerializer.Serialize(new { error = $"file already exists: {lib.DisplayPath(resolved)}" }, JsonOpts.Default);

        var p = ppq ?? lib.Options.DefaultPpq;
        var file = new MidiFile(MidiFormat.MultiTrack, MidiTimeDivision.FromPpq(p));

        // Conductor track (index 0): meta-events only.
        var conductor = file.AddTrack();
        conductor.Add(new TrackNameEvent(0, "Conductor"));
        conductor.Add(TempoEvent.FromBpm(0, tempoBpm ?? lib.Options.DefaultTempoBpm));
        conductor.Add(new TimeSignatureEvent(
            deltaTime: 0,
            numerator: timeSignatureNumerator ?? lib.Options.DefaultTimeSignatureNumerator,
            denominator: timeSignatureDenominator ?? lib.Options.DefaultTimeSignatureDenominator,
            clocksPerMetronomeClick: 24,
            thirtySecondNotesPerQuarter: 8));
        conductor.Add(new EndOfTrackEvent(0));

        if (!string.IsNullOrWhiteSpace(initialTrackName))
        {
            var music = file.AddTrack();
            music.Add(new TrackNameEvent(0, initialTrackName!));
            music.Add(new ProgramChangeEvent(0, channel: 0, program: 0));
            music.Add(new EndOfTrackEvent(0));
        }

        lib.Save(file, resolved);
        await Task.Yield();
        return JsonSerializer.Serialize(new
        {
            created = lib.DisplayPath(resolved),
            ppq = p,
            tempoBpm = tempoBpm ?? lib.Options.DefaultTempoBpm,
            timeSignature = $"{timeSignatureNumerator ?? lib.Options.DefaultTimeSignatureNumerator}/{timeSignatureDenominator ?? lib.Options.DefaultTimeSignatureDenominator}",
            trackCount = file.Tracks.Count,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "add_track"),
     Description("Append a new track to the file. Use this to add instruments (each track typically corresponds to one instrument on one channel).")]
    public static async Task<string> AddTrack(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Track name (shown in DAWs).")] string name,
        [Description("MIDI channel 0–15. Use 9 for percussion (GM drums).")] int channel = 0,
        [Description("General MIDI program number 0–127 (0 = Acoustic Grand Piano). See list_instruments for the enum.")] int program = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("add_track");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);

        var track = file.AddTrack();
        track.Add(new TrackNameEvent(0, name));
        track.Add(new ProgramChangeEvent(0, ClampChannel(channel), (byte)Math.Clamp(program, 0, 127)));
        track.Add(new EndOfTrackEvent(0));

        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), addedTrackIndex = file.Tracks.Count - 1, name, channel, program }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_track_instrument"),
     Description("Set (or change) the instrument on a track. Inserts a ProgramChange at tick 0 of that track.")]
    public static async Task<string> SetTrackInstrument(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("General MIDI program 0–127.")] int program,
        [Description("MIDI channel 0–15. Use 9 for percussion.")] int channel = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("set_track_instrument");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        // Drop any pre-existing ProgramChange at tick 0 on the same channel so we don't pile them up.
        abs.RemoveAll(x => x.Tick == 0 && x.Event is ProgramChangeEvent pc && pc.Channel == ClampChannel(channel));
        abs.Add((0, new ProgramChangeEvent(0, ClampChannel(channel), (byte)Math.Clamp(program, 0, 127))));
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, channel, program }, JsonOpts.Default);
    }

    [McpServerTool(Name = "add_notes"),
     Description("Bulk-insert notes into a track. Each note has absolute startTick, durationTicks, pitch, velocity, channel. Existing events are preserved. This is the main composition tool.")]
    public static async Task<string> AddNotes(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("Notes to add. pitch is a MIDI number 0–127 (use name_to_pitch for 'C4' etc.).")] NoteInput[] notes,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("add_notes");
        if (notes is null || notes.Length == 0) return JsonSerializer.Serialize(new { error = "no notes provided" }, JsonOpts.Default);
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        foreach (var n in notes)
        {
            var ch = ClampChannel(n.Channel ?? 0);
            var pitch = ClampNote(n.Pitch);
            var vel = ClampVel(n.Velocity ?? 100);
            if (n.DurationTicks <= 0) continue;
            abs.Add((n.StartTick, new NoteOnEvent(0, ch, pitch, vel)));
            abs.Add((n.StartTick + n.DurationTicks, new NoteOffEvent(0, ch, pitch, 0)));
        }
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, addedNotes = notes.Length, totalTicks = t.TotalTicks }, JsonOpts.Default);
    }

    [McpServerTool(Name = "add_chord"),
     Description("Convenience: insert N notes that all start at the same tick and have the same duration — a chord.")]
    public static async Task<string> AddChord(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("MIDI pitches that make up the chord, e.g. [60, 64, 67] for C major.")] int[] pitches,
        [Description("Start tick (absolute).")] long startTick,
        [Description("Duration in ticks.")] long durationTicks,
        [Description("Velocity 1–127. Default 100.")] int velocity = 100,
        [Description("MIDI channel 0–15. Default 0.")] int channel = 0,
        CancellationToken ct = default)
    {
        if (pitches is null || pitches.Length == 0) return JsonSerializer.Serialize(new { error = "no pitches provided" }, JsonOpts.Default);
        var notes = pitches.Select(p => new NoteInput
        {
            Pitch = p,
            StartTick = startTick,
            DurationTicks = durationTicks,
            Velocity = velocity,
            Channel = channel,
        }).ToArray();
        return await AddNotes(lib, path, track, notes, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "add_tempo_change"),
     Description("Insert a tempo change on the conductor track (track 0 by default). Use to add accelerandos / ritardandos at specific ticks.")]
    public static async Task<string> AddTempoChange(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("BPM at this tick.")] double bpm,
        [Description("Absolute tick where the new tempo takes effect. Default 0.")] long tick = 0,
        [Description("Track index to insert into. Conductor is normally 0.")] int track = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("add_tempo_change");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        abs.Add((tick, TempoEvent.FromBpm(0, bpm)));
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, tick, bpm }, JsonOpts.Default);
    }

    [McpServerTool(Name = "add_time_signature"),
     Description("Insert a time-signature change on a track at the given tick.")]
    public static async Task<string> AddTimeSignature(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Numerator (top number).")] byte numerator,
        [Description("Denominator (bottom number — must be a power of 2: 1, 2, 4, 8, 16, …).")] int denominator,
        [Description("Absolute tick. Default 0.")] long tick = 0,
        [Description("Track index. Conductor is normally 0.")] int track = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("add_time_signature");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        abs.Add((tick, new TimeSignatureEvent(0, numerator, denominator, clocksPerMetronomeClick: 24, thirtySecondNotesPerQuarter: 8)));
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, tick, signature = $"{numerator}/{denominator}" }, JsonOpts.Default);
    }

    [McpServerTool(Name = "add_control_change"),
     Description("Insert a generic Control Change event (sustain pedal, expression, pan, modulation, …) at a tick. See list_controllers for common controller numbers.")]
    public static async Task<string> AddControlChange(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("Controller number 0–127 (64 = sustain, 7 = volume, 10 = pan, …).")] int controller,
        [Description("Controller value 0–127.")] int value,
        [Description("Absolute tick.")] long tick = 0,
        [Description("MIDI channel 0–15.")] int channel = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("add_control_change");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        abs.Add((tick, new ControlChangeEvent(0, ClampChannel(channel), (byte)Math.Clamp(controller, 0, 127), (byte)Math.Clamp(value, 0, 127))));
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, controller, value, tick, channel }, JsonOpts.Default);
    }

    [McpServerTool(Name = "rename_track"),
     Description("Replace a track's name (TrackNameEvent at tick 0).")]
    public static async Task<string> RenameTrack(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("New track name.")] string name,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("rename_track");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = ToAbsoluteList(t);
        abs.RemoveAll(x => x.Event is TrackNameEvent);
        abs.Add((0, new TrackNameEvent(0, name)));
        RebuildTrackFromAbsolute(t, abs);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, name }, JsonOpts.Default);
    }
}

/// <summary>Public DTO used by <c>add_notes</c>. MCP tools surface this as a JSON-schema object array.</summary>
public sealed class NoteInput
{
    public int Pitch { get; set; }
    public long StartTick { get; set; }
    public long DurationTicks { get; set; }
    public int? Velocity { get; set; }
    public int? Channel { get; set; }
}
