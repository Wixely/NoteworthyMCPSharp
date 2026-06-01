namespace NoteworthyMCPSharp.Configuration;

/// <summary>
/// Domain options for the Noteworthy MIDI MCP server. Controls where the agent is allowed
/// to read/write MIDI files, default musical parameters for newly-created files, and a
/// master read-only switch that disables every mutating tool.
/// </summary>
public sealed class NoteworthyOptions
{
    public const string SectionName = "Noteworthy";

    /// <summary>
    /// Root directory the agent is jailed to. Empty = the server's working directory at launch
    /// (typically the install dir). Set this to your music collection in production.
    /// </summary>
    public string LibraryRoot { get; set; } = "";

    /// <summary>
    /// When true, file-path tools accept absolute paths outside <see cref="LibraryRoot"/>.
    /// Off by default — keeps the agent from being talked into editing /etc or your home dir.
    /// </summary>
    public bool AllowAnyPath { get; set; } = false;

    /// <summary>
    /// Master safety switch. When true, every mutating tool (<c>create_midi_file</c>,
    /// <c>add_*</c>, <c>transpose</c>, <c>quantize</c>, …) refuses with a clear error.
    /// Read/inspect/list tools stay available. Default true — flip explicitly to compose.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// When true, the <c>delete_midi_file</c> tool is exposed (requires ReadOnly=false too).
    /// Default false. Don't enable on a shared library you care about.
    /// </summary>
    public bool AllowDelete { get; set; } = false;

    /// <summary>Largest MIDI file in bytes the server will open. Guards against huge stems.</summary>
    public int MaxFileBytes { get; set; } = 50_000_000;

    /// <summary>Default ticks-per-quarter-note (PPQ) for new files. 480 is industry standard.</summary>
    public int DefaultPpq { get; set; } = 480;

    /// <summary>Default tempo (BPM) inserted at tick 0 of new files.</summary>
    public double DefaultTempoBpm { get; set; } = 120.0;

    /// <summary>Default time-signature numerator for new files.</summary>
    public byte DefaultTimeSignatureNumerator { get; set; } = 4;

    /// <summary>Default time-signature denominator for new files (1, 2, 4, 8, 16, …).</summary>
    public int DefaultTimeSignatureDenominator { get; set; } = 4;

    /// <summary>Cap on the number of notes <c>list_notes</c> returns in one shot.</summary>
    public int MaxNotesPerListing { get; set; } = 2000;

    /// <summary>Cap on <c>list_midi_files</c> / <c>search_midi_files</c> results.</summary>
    public int MaxSearchResults { get; set; } = 500;
}

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5711;
    public string Path { get; set; } = "/mcp";

    /// <summary>Service name when running as a Windows Service.</summary>
    public string WindowsServiceName { get; set; } = "NoteworthyMCPSharp";

    /// <summary>Optional MCP endpoint password. Blank disables MCP password auth.</summary>
    public string Password { get; set; } = string.Empty;
}
