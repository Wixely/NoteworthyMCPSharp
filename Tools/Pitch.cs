namespace NoteworthyMCPSharp.Tools;

/// <summary>
/// MIDI note-number ↔ scientific-pitch-name conversions. Used by both reference tools and
/// the note-creation tools so agents can express pitches as "C4", "F#5", "Bb3" interchangeably
/// with raw 0–127 integers.
/// </summary>
internal static class Pitch
{
    private static readonly string[] NamesSharp = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>60 → "C4", 61 → "C#4", … (using MIDI standard where C4 = note 60).</summary>
    public static string ToName(int midi)
    {
        if (midi < 0 || midi > 127) return midi.ToString();
        var octave = (midi / 12) - 1;
        return NamesSharp[midi % 12] + octave;
    }

    /// <summary>"C4" / "Cb4" / "F#5" / "60" → MIDI note number. Returns null on garbage input.</summary>
    public static int? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (int.TryParse(s, out var direct) && direct is >= 0 and <= 127) return direct;

        var letter = char.ToUpperInvariant(s[0]);
        var idx = letter switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11,
            _ => -1
        };
        if (idx < 0) return null;

        var i = 1;
        if (i < s.Length && (s[i] == '#' || s[i] == 's')) { idx++; i++; }
        else if (i < s.Length && (s[i] == 'b' || s[i] == 'B') && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || s[i + 1] == '-'))
        { idx--; i++; }

        if (i >= s.Length || !int.TryParse(s.AsSpan(i), out var octave)) return null;
        var midi = (octave + 1) * 12 + idx;
        return midi is >= 0 and <= 127 ? midi : null;
    }
}
