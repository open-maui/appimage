using System.Text;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Minimal Unix "ar" archive writer (the container format of a .deb).
/// Common format: global header "!&lt;arch&gt;\n", then per member a 60-byte
/// ASCII header (name 16, mtime 12, uid 6, gid 6, octal mode 8, size 10,
/// terminator "`\n") followed by the data, padded with "\n" to an even offset.
/// Member names must be at most 15 characters (all .deb members are).
/// </summary>
public sealed class ArArchiveWriter : IDisposable
{
    public const string GlobalHeader = "!<arch>\n";

    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public ArArchiveWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        var header = Encoding.ASCII.GetBytes(GlobalHeader);
        _stream.Write(header, 0, header.Length);
    }

    public void AddEntry(string name, byte[] data, DateTimeOffset modified, int mode = 0b110_100_100 /* 0644 */)
    {
        using var ms = new MemoryStream(data, writable: false);
        AddEntry(name, ms, data.LongLength, modified, mode);
    }

    /// <summary>Adds a member whose content is read from <paramref name="data"/> (exactly <paramref name="length"/> bytes).</summary>
    public void AddEntry(string name, Stream data, long length, DateTimeOffset modified, int mode = 0b110_100_100 /* 0644 */)
    {
        if (name.Length == 0 || name.Length > 15 || name.Contains('/') || name.Contains(' '))
            throw new ArgumentException($"ar member name '{name}' must be 1-15 characters without '/' or spaces.", nameof(name));

        var header = new StringBuilder(60);
        header.Append(name.PadRight(16));
        header.Append(modified.ToUnixTimeSeconds().ToString().PadRight(12));
        header.Append("0".PadRight(6));   // uid root
        header.Append("0".PadRight(6));   // gid root
        header.Append(("100" + Convert.ToString(mode, 8).PadLeft(3, '0')).PadRight(8));
        header.Append(length.ToString().PadRight(10));
        header.Append("`\n");

        var bytes = Encoding.ASCII.GetBytes(header.ToString());
        if (bytes.Length != 60)
            throw new InvalidOperationException("ar header must be 60 bytes.");

        _stream.Write(bytes, 0, bytes.Length);
        var copied = CopyExactly(data, _stream, length);
        if (copied != length)
            throw new InvalidOperationException($"ar member '{name}': expected {length} bytes, got {copied}.");
        if (length % 2 != 0)
            _stream.WriteByte((byte)'\n');
    }

    private static long CopyExactly(Stream source, Stream destination, long length)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (total < length)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length - total));
            if (read == 0)
                break;
            destination.Write(buffer, 0, read);
            total += read;
        }
        return total;
    }

    public void Dispose()
    {
        _stream.Flush();
        if (!_leaveOpen)
            _stream.Dispose();
    }
}
