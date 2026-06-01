using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NoteworthyMCPSharp.Services;
using Noteworthy;
using Noteworthy.Editing;
using Noteworthy.Events;
using Noteworthy.Events.Channel;
using Noteworthy.Events.Meta;

namespace NoteworthyMCPSharp.Tools;

/// <summary>
/// Transformations that operate on existing MIDI files: transpose, quantize, delete tracks,
/// merge/split, convert format, change tempo, delete notes in a range. All write the result
/// back to the original path (or to a chosen output path).
/// </summary>
[McpServerToolType]
public static class EditingTools
{
    [McpServerTool(Name = "transpose"),
     Description("Shift every NoteOn/NoteOff in the file by N semitones (positive = up). Optionally skip channel 10 (drums) so percussion isn't transposed into unrelated sounds.")]
    public static async Task<string> Transpose(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Semitones to shift. Negative = down.")] int semitones,
        [Description("If true, channel 9 (a.k.a. GM 'channel 10') is left untouched. Default true.")] bool skipDrums = true,
        [Description("Optional output path. Empty = overwrite in place.")] string? output = null,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("transpose");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var transposed = MidiEditing.Transpose(file, semitones, skipDrums);
        var dest = string.IsNullOrWhiteSpace(output) ? resolved : lib.ResolveScoped(output, requireExists: false);
        lib.Save(transposed, dest);
        return JsonSerializer.Serialize(new { source = lib.DisplayPath(resolved), output = lib.DisplayPath(dest), semitones, skipDrums }, JsonOpts.Default);
    }

    [McpServerTool(Name = "quantize_track"),
     Description("Snap NoteOn ticks on a track to a fixed grid (e.g. gridTicks = ppq/4 quantizes to sixteenth notes).")]
    public static async Task<string> QuantizeTrack(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("Grid spacing in ticks. Use ppq for quarters, ppq/2 for eighths, ppq/4 for sixteenths, etc.")] int gridTicks,
        [Description("Optional output path. Empty = overwrite in place.")] string? output = null,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("quantize_track");
        if (gridTicks <= 0) return JsonSerializer.Serialize(new { error = "gridTicks must be > 0" }, JsonOpts.Default);
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        // MidiEditing.Quantize returns a NEW track; swap it in.
        var quantized = MidiEditing.Quantize(file.Tracks[track], gridTicks);
        var rebuilt = new MidiFile(file.Format, file.TimeDivision);
        for (int i = 0; i < file.Tracks.Count; i++)
        {
            rebuilt.AddTrack(i == track ? quantized : file.Tracks[i]);
        }
        var dest = string.IsNullOrWhiteSpace(output) ? resolved : lib.ResolveScoped(output, requireExists: false);
        lib.Save(rebuilt, dest);
        return JsonSerializer.Serialize(new { source = lib.DisplayPath(resolved), output = lib.DisplayPath(dest), track, gridTicks }, JsonOpts.Default);
    }

    [McpServerTool(Name = "merge_to_single_track"),
     Description("Flatten every channel/track into one combined track (preserves channel info so DAWs still know what's what). Useful for export to systems that only accept Format 0.")]
    public static async Task<string> MergeToSingleTrack(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Optional output path. Empty = overwrite in place.")] string? output = null,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("merge_to_single_track");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var merged = MidiEditing.MergeToSingleTrack(file);
        var dest = string.IsNullOrWhiteSpace(output) ? resolved : lib.ResolveScoped(output, requireExists: false);
        lib.Save(merged, dest);
        return JsonSerializer.Serialize(new { source = lib.DisplayPath(resolved), output = lib.DisplayPath(dest), trackCount = merged.Tracks.Count }, JsonOpts.Default);
    }

    [McpServerTool(Name = "convert_format"),
     Description("Convert between Format 0 (single-track) and Format 1 (multi-track with conductor) without losing musical content.")]
    public static async Task<string> ConvertFormat(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Target format: 0 (single-track) or 1 (multi-track).")] int targetFormat,
        [Description("Optional output path. Empty = overwrite in place.")] string? output = null,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("convert_format");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var converted = targetFormat switch
        {
            0 => FormatConversion.ConvertToFormat0(file),
            1 => FormatConversion.ConvertToFormat1(file),
            _ => throw new ArgumentException("targetFormat must be 0 or 1.", nameof(targetFormat))
        };
        var dest = string.IsNullOrWhiteSpace(output) ? resolved : lib.ResolveScoped(output, requireExists: false);
        lib.Save(converted, dest);
        return JsonSerializer.Serialize(new { source = lib.DisplayPath(resolved), output = lib.DisplayPath(dest), targetFormat, trackCount = converted.Tracks.Count }, JsonOpts.Default);
    }

    [McpServerTool(Name = "delete_track"),
     Description("Remove a track by index. Other tracks are renumbered to fill the gap.")]
    public static async Task<string> DeleteTrack(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index to remove.")] int track,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("delete_track");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));
        file.RemoveTrackAt(track);
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), deletedTrack = track, remainingTracks = file.Tracks.Count }, JsonOpts.Default);
    }

    [McpServerTool(Name = "delete_notes_in_range"),
     Description("Delete every NoteOn/NoteOff on a track whose absolute start tick falls within [fromTick, toTick). Other events on the track (tempo, program changes, …) are preserved.")]
    public static async Task<string> DeleteNotesInRange(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("0-based track index.")] int track,
        [Description("Inclusive start tick.")] long fromTick,
        [Description("Exclusive end tick.")] long toTick,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("delete_notes_in_range");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        // Re-build with absolute ticks; drop NoteOn/NoteOff inside [fromTick, toTick).
        var abs = new List<(long Tick, MidiEvent Event)>();
        long cursor = 0;
        var removed = 0;
        foreach (var ev in t.Events)
        {
            cursor += ev.DeltaTime;
            if (ev is EndOfTrackEvent) continue;
            if ((ev is NoteOnEvent || ev is NoteOffEvent) && cursor >= fromTick && cursor < toTick) { removed++; continue; }
            abs.Add((cursor, ev));
        }
        abs.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        t.Clear();
        long last = 0;
        foreach (var (tick, ev) in abs)
        {
            ev.DeltaTime = tick - last;
            t.Add(ev);
            last = tick;
        }
        t.Add(new EndOfTrackEvent(0));
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, fromTick, toTick, removedEvents = removed }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_tempo"),
     Description("Replace the tempo event at tick 0 of the conductor track with the given BPM. Use add_tempo_change for tempo changes at non-zero ticks.")]
    public static async Task<string> SetTempo(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("New tempo in BPM.")] double bpm,
        [Description("Conductor track index. Default 0.")] int track = 0,
        CancellationToken ct = default)
    {
        lib.EnsureWritable("set_tempo");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        if (track < 0 || track >= file.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(track));

        var t = file.Tracks[track];
        var abs = new List<(long Tick, MidiEvent Event)>();
        long cursor = 0;
        foreach (var ev in t.Events)
        {
            cursor += ev.DeltaTime;
            if (ev is EndOfTrackEvent) continue;
            if (ev is TempoEvent && cursor == 0) continue;   // drop the existing tick-0 tempo
            abs.Add((cursor, ev));
        }
        abs.Add((0, TempoEvent.FromBpm(0, bpm)));
        abs.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        t.Clear();
        long last = 0;
        foreach (var (tick, ev) in abs)
        {
            ev.DeltaTime = tick - last;
            t.Add(ev);
            last = tick;
        }
        t.Add(new EndOfTrackEvent(0));
        lib.Save(file, resolved);
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), track, bpm }, JsonOpts.Default);
    }

    [McpServerTool(Name = "delete_midi_file"),
     Description("Permanently delete a MIDI file from disk. Requires Noteworthy:AllowDelete=true. There's no undo.")]
    public static string DeleteMidiFile(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path)
    {
        lib.EnsureWritable("delete_midi_file");
        if (!lib.Options.AllowDelete)
            throw new InvalidOperationException("Deletion is disabled. Set Noteworthy:AllowDelete=true to enable.");
        var resolved = lib.ResolveScoped(path, requireExists: true);
        File.Delete(resolved);
        return JsonSerializer.Serialize(new { deleted = lib.DisplayPath(resolved) }, JsonOpts.Default);
    }
}
