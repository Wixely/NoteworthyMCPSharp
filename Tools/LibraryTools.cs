using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NoteworthyMCPSharp.Services;
using Noteworthy;
using Noteworthy.Analysis;
using Noteworthy.Events.Meta;
using Noteworthy.Validation;

namespace NoteworthyMCPSharp.Tools;

[McpServerToolType]
public static class LibraryTools
{
    [McpServerTool(Name = "list_midi_files"),
     Description("List MIDI files (.mid / .midi / .smf) under a directory. Path is resolved inside the configured LibraryRoot.")]
    public static string ListMidiFiles(
        MidiLibrary lib,
        [Description("Relative or absolute directory. Empty/null = LibraryRoot.")] string? directory = null,
        [Description("If true, descend into subdirectories. Default true.")] bool recursive = true,
        CancellationToken ct = default)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? lib.LibraryRoot : lib.ResolveScoped(directory, requireExists: false);
        if (!Directory.Exists(dir)) return JsonSerializer.Serialize(new { error = $"not a directory: {dir}" }, JsonOpts.Default);

        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var rows = new List<object>();
        foreach (var ext in new[] { "*.mid", "*.midi", "*.smf" })
        {
            foreach (var path in Directory.EnumerateFiles(dir, ext, opt))
            {
                ct.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                rows.Add(new
                {
                    path = lib.DisplayPath(path),
                    sizeBytes = info.Length,
                    modifiedUtc = info.LastWriteTimeUtc,
                });
                if (rows.Count >= lib.Options.MaxSearchResults) break;
            }
            if (rows.Count >= lib.Options.MaxSearchResults) break;
        }
        return JsonSerializer.Serialize(new { directory = lib.DisplayPath(dir), count = rows.Count, files = rows }, JsonOpts.Default);
    }

    [McpServerTool(Name = "search_midi_files"),
     Description("Find MIDI files whose filename (or path) contains the given substring. Case-insensitive.")]
    public static string SearchMidiFiles(
        MidiLibrary lib,
        [Description("Substring to look for, e.g. \"sonata\", \"happy birthday\".")] string query,
        [Description("Relative or absolute directory to search under. Empty/null = LibraryRoot.")] string? directory = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return JsonSerializer.Serialize(new { error = "query is required" }, JsonOpts.Default);
        var dir = string.IsNullOrWhiteSpace(directory) ? lib.LibraryRoot : lib.ResolveScoped(directory, requireExists: false);
        if (!Directory.Exists(dir)) return JsonSerializer.Serialize(new { error = $"not a directory: {dir}" }, JsonOpts.Default);

        var rows = new List<object>();
        foreach (var ext in new[] { "*.mid", "*.midi", "*.smf" })
        {
            foreach (var path in Directory.EnumerateFiles(dir, ext, SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (!path.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(path);
                rows.Add(new
                {
                    path = lib.DisplayPath(path),
                    sizeBytes = info.Length,
                    modifiedUtc = info.LastWriteTimeUtc,
                });
                if (rows.Count >= lib.Options.MaxSearchResults) break;
            }
            if (rows.Count >= lib.Options.MaxSearchResults) break;
        }
        return JsonSerializer.Serialize(new { query, count = rows.Count, files = rows }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_library_info"),
     Description("Show the configured library root, whether the server is in read-only mode, and the default musical settings for new files.")]
    public static string GetLibraryInfo(MidiLibrary lib) =>
        JsonSerializer.Serialize(new
        {
            libraryRoot = lib.LibraryRoot,
            allowAnyPath = lib.Options.AllowAnyPath,
            readOnly = lib.IsReadOnly,
            allowDelete = lib.Options.AllowDelete,
            defaults = new
            {
                ppq = lib.Options.DefaultPpq,
                tempoBpm = lib.Options.DefaultTempoBpm,
                timeSignature = $"{lib.Options.DefaultTimeSignatureNumerator}/{lib.Options.DefaultTimeSignatureDenominator}",
            }
        }, JsonOpts.Default);

    [McpServerTool(Name = "get_midi_info"),
     Description("Summarise a MIDI file: format, time division, track count, total ticks, tempo, time signature, instruments used, and approximate duration.")]
    public static async Task<string> GetMidiInfo(
        MidiLibrary lib,
        [Description("Path to the MIDI file (relative to LibraryRoot or absolute).")] string path,
        CancellationToken ct = default)
    {
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);

        // Pull header metadata events out of the conductor / first track.
        TempoEvent? tempo = null;
        TimeSignatureEvent? timeSig = null;
        KeySignatureEvent? keySig = null;
        var instruments = new SortedSet<int>();
        var trackNames = new List<string?>();

        foreach (var track in file.Tracks)
        {
            string? name = null;
            foreach (var ev in track.Events)
            {
                switch (ev)
                {
                    case TempoEvent t when tempo is null: tempo = t; break;
                    case TimeSignatureEvent ts when timeSig is null: timeSig = ts; break;
                    case KeySignatureEvent ks when keySig is null: keySig = ks; break;
                    case TrackNameEvent tn when name is null: name = tn.Text; break;
                    case Noteworthy.Events.Channel.ProgramChangeEvent pc: instruments.Add(pc.Program); break;
                }
            }
            trackNames.Add(name);
        }

        var notes = NoteExtraction.ExtractNotes(file);
        TimeSpan duration = TimeSpan.Zero;
        try
        {
            var map = TempoMap.Build(file);
            long maxTick = 0;
            foreach (var track in file.Tracks) if (track.TotalTicks > maxTick) maxTick = track.TotalTicks;
            duration = map.TicksToTime(maxTick);
        }
        catch { /* SMPTE divisions can throw on TempoMap; best-effort */ }

        return JsonSerializer.Serialize(new
        {
            path = lib.DisplayPath(resolved),
            format = file.Format.ToString(),
            timeDivision = file.TimeDivision.IsPpq
                ? $"PPQ {file.TimeDivision.PulsesPerQuarterNote}"
                : $"SMPTE {file.TimeDivision.SmpteFramesPerSecond}fps × {file.TimeDivision.SmpteTicksPerFrame}",
            trackCount = file.Tracks.Count,
            tempoBpm = tempo?.BeatsPerMinute,
            timeSignature = timeSig is null ? null : $"{timeSig.Numerator}/{timeSig.Denominator}",
            keySignature = keySig is null ? null : $"{keySig.SharpsOrFlats} {(keySig.IsMinor ? "minor" : "major")}",
            durationSeconds = Math.Round(duration.TotalSeconds, 3),
            noteCount = notes.Count,
            instrumentPrograms = instruments,
            trackNames,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "list_tracks"),
     Description("Per-track summary: index, name, event count, total ticks, instrument programs seen, channels used, note count.")]
    public static async Task<string> ListTracks(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        CancellationToken ct = default)
    {
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var notes = NoteExtraction.ExtractNotes(file);

        var rows = new List<object>();
        for (int i = 0; i < file.Tracks.Count; i++)
        {
            var track = file.Tracks[i];
            string? name = null;
            var programs = new SortedSet<int>();
            var channels = new SortedSet<int>();
            foreach (var ev in track.Events)
            {
                if (ev is TrackNameEvent tn && name is null) name = tn.Text;
                if (ev is Noteworthy.Events.Channel.ProgramChangeEvent pc) { programs.Add(pc.Program); channels.Add(pc.Channel); }
                if (ev is Noteworthy.Events.ChannelEvent ce) channels.Add(ce.Channel);
            }
            var noteCount = 0;
            foreach (var n in notes) if (n.TrackIndex == i) noteCount++;
            rows.Add(new
            {
                index = i,
                name,
                events = track.Count,
                totalTicks = track.TotalTicks,
                noteCount,
                programs,
                channels,
            });
        }
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), tracks = rows }, JsonOpts.Default);
    }

    [McpServerTool(Name = "list_notes"),
     Description("Extract notes from a file (or a single track), returning absolute-tick (StartTick, DurationTicks, NoteNumber, Channel, Velocity, TrackIndex). Capped by Noteworthy:MaxNotesPerListing.")]
    public static async Task<string> ListNotes(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        [Description("Optional 0-based track index. Omit to list notes across all tracks.")] int? track = null,
        [Description("Optional start tick filter (inclusive).")] long? fromTick = null,
        [Description("Optional end tick filter (exclusive).")] long? toTick = null,
        [Description("If true, include scientific pitch names alongside MIDI numbers.")] bool includeNoteNames = true,
        CancellationToken ct = default)
    {
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var notes = NoteExtraction.ExtractNotes(file);

        var cap = lib.Options.MaxNotesPerListing;
        var rows = new List<object>();
        var skipped = 0;
        foreach (var n in notes)
        {
            if (track is int t && n.TrackIndex != t) continue;
            if (fromTick is long f && n.StartTick < f) continue;
            if (toTick is long e && n.StartTick >= e) continue;
            if (rows.Count >= cap) { skipped++; continue; }
            rows.Add(new
            {
                trackIndex = n.TrackIndex,
                channel = (int)n.Channel,
                pitch = (int)n.NoteNumber,
                name = includeNoteNames ? Pitch.ToName(n.NoteNumber) : null,
                startTick = n.StartTick,
                durationTicks = n.DurationTicks,
                velocity = (int)n.Velocity,
            });
        }
        return JsonSerializer.Serialize(new { path = lib.DisplayPath(resolved), returned = rows.Count, skipped, notes = rows }, JsonOpts.Default);
    }

    [McpServerTool(Name = "validate_midi"),
     Description("Run the Noteworthy validator over a file. Returns a list of issues with severity (Info / Warning / Error), track index, tick, code, message.")]
    public static async Task<string> ValidateMidi(
        MidiLibrary lib,
        [Description("Path to the MIDI file.")] string path,
        CancellationToken ct = default)
    {
        var resolved = lib.ResolveScoped(path, requireExists: true);
        var file = await lib.LoadAsync(resolved, ct).ConfigureAwait(false);
        var report = MidiValidator.Validate(file);
        return JsonSerializer.Serialize(new
        {
            path = lib.DisplayPath(resolved),
            hasErrors = report.HasErrors,
            hasWarnings = report.HasWarnings,
            issues = report.Issues.Select(i => new
            {
                severity = i.Severity.ToString(),
                trackIndex = i.TrackIndex,
                tick = i.AbsoluteTick,
                code = i.Code,
                message = i.Message,
            }).ToList(),
        }, JsonOpts.Default);
    }
}
