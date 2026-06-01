using Microsoft.Extensions.Options;
using NoteworthyMCPSharp.Configuration;
using Noteworthy;

namespace NoteworthyMCPSharp.Services;

/// <summary>
/// Shared filesystem / load+save glue for every MIDI tool. Owns the path-jail (clients
/// can't escape <see cref="NoteworthyOptions.LibraryRoot"/> unless <see cref="NoteworthyOptions.AllowAnyPath"/>
/// is true), the read-only gate, and a small set of MidiFile load/save convenience wrappers
/// so each tool doesn't repeat the same boilerplate.
/// </summary>
public sealed class MidiLibrary
{
    private readonly NoteworthyOptions _options;

    public MidiLibrary(IOptions<NoteworthyOptions> options) { _options = options.Value; }

    public NoteworthyOptions Options => _options;
    public bool IsReadOnly => _options.ReadOnly;

    /// <summary>The effective root directory. Falls back to the launch cwd when LibraryRoot is empty.</summary>
    public string LibraryRoot =>
        string.IsNullOrWhiteSpace(_options.LibraryRoot) ? Directory.GetCurrentDirectory() : _options.LibraryRoot;

    /// <summary>
    /// Resolve a user-supplied path (relative or absolute) into a real path the agent is allowed
    /// to touch. Throws on jail-break attempts unless AllowAnyPath is on. Also enforces the
    /// MaxFileBytes cap up-front so we don't even start loading something huge.
    /// </summary>
    public string ResolveScoped(string path, bool requireExists)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));

        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(LibraryRoot, path));

        if (!_options.AllowAnyPath)
        {
            var root = Path.GetFullPath(LibraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var resolvedNorm = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!resolvedNorm.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(resolvedNorm, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Path '{path}' escapes the configured LibraryRoot '{LibraryRoot}'. " +
                    "Set Noteworthy:AllowAnyPath=true to allow access outside the library.");
            }
        }

        if (requireExists && !File.Exists(resolved))
            throw new FileNotFoundException($"MIDI file not found: {path}", resolved);

        if (requireExists)
        {
            var info = new FileInfo(resolved);
            if (info.Length > _options.MaxFileBytes)
                throw new InvalidOperationException(
                    $"File is {info.Length:N0} bytes which exceeds Noteworthy:MaxFileBytes={_options.MaxFileBytes:N0}.");
        }

        return resolved;
    }

    /// <summary>Friendly path for output — relative to LibraryRoot when inside, absolute otherwise.</summary>
    public string DisplayPath(string absolutePath)
    {
        var root = Path.GetFullPath(LibraryRoot);
        if (absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var rel = absolutePath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? "." : rel.Replace(Path.DirectorySeparatorChar, '/');
        }
        return absolutePath;
    }

    public void EnsureWritable(string operation)
    {
        if (_options.ReadOnly)
            throw new InvalidOperationException(
                $"Operation '{operation}' is blocked: server is running in read-only mode. " +
                "Set Noteworthy:ReadOnly=false to allow writes.");
    }

    public MidiFile Load(string scopedAbsolutePath) => MidiFile.Load(scopedAbsolutePath);

    public Task<MidiFile> LoadAsync(string scopedAbsolutePath, CancellationToken ct) =>
        MidiFile.LoadAsync(scopedAbsolutePath, ct);

    public void Save(MidiFile file, string scopedAbsolutePath)
    {
        var dir = Path.GetDirectoryName(scopedAbsolutePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        file.Save(scopedAbsolutePath);
    }
}
