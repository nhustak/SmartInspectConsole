using System.Text;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Core.FileIO;

/// <summary>
/// Writes SmartInspect Log files (.sil format).
/// Format: "SILF" magic header (4 bytes) followed by sequential binary packets.
/// </summary>
public class SilFileWriter
{
    private static readonly byte[] MagicHeader = "SILF"u8.ToArray();

    /// <summary>
    /// Writes packets to a .sil file.
    /// </summary>
    /// <param name="filePath">Path to write the .sil file.</param>
    /// <param name="packets">Packets to write.</param>
    public async Task WriteFileAsync(string filePath, IEnumerable<Packet> packets)
    {
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await WriteToStreamAsync(stream, packets);
    }

    /// <summary>
    /// Writes packets to a stream in .sil format.
    /// </summary>
    public async Task WriteToStreamAsync(Stream stream, IEnumerable<Packet> packets)
    {
        // Write magic header
        await stream.WriteAsync(MagicHeader);

        // Write packets
        var writer = new BinaryPacketWriter();
        foreach (var packet in packets)
        {
            writer.WritePacket(stream, packet);
        }

        await stream.FlushAsync();
    }
}
