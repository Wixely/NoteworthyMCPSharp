using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Noteworthy.Enums;

namespace NoteworthyMCPSharp.Tools;

/// <summary>
/// Read-only helpers: GM instrument list, drum kit map, controller numbers, pitch ↔ name
/// conversion, tempo arithmetic. Always available regardless of ReadOnly. The agent uses
/// these to map between human-readable musical concepts and the numeric MIDI primitives
/// the composition tools take.
/// </summary>
[McpServerToolType]
public static class ReferenceTools
{
    [McpServerTool(Name = "list_instruments"),
     Description("Full General MIDI Level 1 instrument map (program 0-127 → human-readable name).")]
    public static string ListInstruments()
    {
        var rows = new List<object>();
        foreach (var v in Enum.GetValues<GeneralMidiInstrument>())
            rows.Add(new { program = (int)v, name = v.ToString(), category = CategoryFor((int)v) });
        return JsonSerializer.Serialize(rows, JsonOpts.Default);
    }

    [McpServerTool(Name = "list_drum_sounds"),
     Description("General MIDI percussion map: note numbers 35-81 on channel 9. Each entry pairs a MIDI note with the drum/percussion sound it triggers.")]
    public static string ListDrumSounds()
    {
        // GM percussion notes start at 35 (Acoustic Bass Drum) and run through 81 (Open Triangle).
        var notes = new[]
        {
            (35,"AcousticBassDrum"),(36,"BassDrum1"),(37,"SideStick"),(38,"AcousticSnare"),
            (39,"HandClap"),(40,"ElectricSnare"),(41,"LowFloorTom"),(42,"ClosedHiHat"),
            (43,"HighFloorTom"),(44,"PedalHiHat"),(45,"LowTom"),(46,"OpenHiHat"),
            (47,"LowMidTom"),(48,"HiMidTom"),(49,"CrashCymbal1"),(50,"HighTom"),
            (51,"RideCymbal1"),(52,"ChineseCymbal"),(53,"RideBell"),(54,"Tambourine"),
            (55,"SplashCymbal"),(56,"Cowbell"),(57,"CrashCymbal2"),(58,"Vibraslap"),
            (59,"RideCymbal2"),(60,"HiBongo"),(61,"LowBongo"),(62,"MuteHiConga"),
            (63,"OpenHiConga"),(64,"LowConga"),(65,"HighTimbale"),(66,"LowTimbale"),
            (67,"HighAgogo"),(68,"LowAgogo"),(69,"Cabasa"),(70,"Maracas"),
            (71,"ShortWhistle"),(72,"LongWhistle"),(73,"ShortGuiro"),(74,"LongGuiro"),
            (75,"Claves"),(76,"HiWoodBlock"),(77,"LowWoodBlock"),(78,"MuteCuica"),
            (79,"OpenCuica"),(80,"MuteTriangle"),(81,"OpenTriangle"),
        };
        return JsonSerializer.Serialize(notes.Select(p => new { note = p.Item1, name = p.Item2 }), JsonOpts.Default);
    }

    [McpServerTool(Name = "list_controllers"),
     Description("Common MIDI Control Change controller numbers (sustain, volume, pan, modulation, etc.) with human names.")]
    public static string ListControllers()
    {
        var rows = new List<object>();
        foreach (var v in Enum.GetValues<MidiController>())
            rows.Add(new { controller = (int)v, name = v.ToString() });
        return JsonSerializer.Serialize(rows, JsonOpts.Default);
    }

    [McpServerTool(Name = "pitch_to_name"),
     Description("Convert MIDI note number to scientific pitch name (e.g. 60 → C4).")]
    public static string PitchToName(
        [Description("MIDI note 0–127.")] int pitch) =>
        JsonSerializer.Serialize(new { pitch, name = Pitch.ToName(pitch) }, JsonOpts.Default);

    [McpServerTool(Name = "name_to_pitch"),
     Description("Convert a scientific pitch name to a MIDI note number. Accepts sharps (C#4), flats (Db4), and bare numbers.")]
    public static string NameToPitch(
        [Description("Pitch like 'C4', 'F#5', 'Bb3', or '60'.")] string name)
    {
        var pitch = Pitch.Parse(name);
        return pitch is null
            ? JsonSerializer.Serialize(new { error = $"could not parse '{name}'" }, JsonOpts.Default)
            : JsonSerializer.Serialize(new { name, pitch = pitch.Value, canonical = Pitch.ToName(pitch.Value) }, JsonOpts.Default);
    }

    [McpServerTool(Name = "bpm_to_microseconds"),
     Description("Convert BPM to microseconds-per-quarter-note (the unit Standard MIDI Files use for tempo).")]
    public static string BpmToMicroseconds(
        [Description("Tempo in beats per minute.")] double bpm)
    {
        if (bpm <= 0) return JsonSerializer.Serialize(new { error = "bpm must be > 0" }, JsonOpts.Default);
        var usec = (int)Math.Round(60_000_000.0 / bpm);
        return JsonSerializer.Serialize(new { bpm, microsecondsPerQuarter = usec }, JsonOpts.Default);
    }

    [McpServerTool(Name = "tick_helper"),
     Description("Compute the tick durations of common note values (quarter, eighth, sixteenth, …) for a given PPQ. Use these to populate `durationTicks` in add_notes.")]
    public static string TickHelper(
        [Description("Ticks per quarter note of the file. 480 is industry standard.")] int ppq = 480) =>
        JsonSerializer.Serialize(new
        {
            ppq,
            whole = ppq * 4,
            half = ppq * 2,
            quarter = ppq,
            eighth = ppq / 2,
            sixteenth = ppq / 4,
            thirtySecond = ppq / 8,
            quarterTriplet = (int)Math.Round(ppq * 2.0 / 3.0),
            eighthTriplet = (int)Math.Round(ppq / 3.0),
            dotted = new
            {
                half = (int)(ppq * 3),
                quarter = (int)(ppq * 1.5),
                eighth = (int)(ppq * 0.75),
                sixteenth = (int)(ppq * 0.375),
            }
        }, JsonOpts.Default);

    private static string CategoryFor(int program) => program switch
    {
        >= 0 and <= 7 => "Piano",
        >= 8 and <= 15 => "Chromatic Percussion",
        >= 16 and <= 23 => "Organ",
        >= 24 and <= 31 => "Guitar",
        >= 32 and <= 39 => "Bass",
        >= 40 and <= 47 => "Strings",
        >= 48 and <= 55 => "Ensemble",
        >= 56 and <= 63 => "Brass",
        >= 64 and <= 71 => "Reed",
        >= 72 and <= 79 => "Pipe",
        >= 80 and <= 87 => "Synth Lead",
        >= 88 and <= 95 => "Synth Pad",
        >= 96 and <= 103 => "Synth Effects",
        >= 104 and <= 111 => "Ethnic",
        >= 112 and <= 119 => "Percussive",
        >= 120 and <= 127 => "Sound Effects",
        _ => "Unknown",
    };
}
