using System.Text;
using SmartInspectConsole.Core.Packets;
using SmartInspectConsole.Core.Parsing;

namespace SmartInspectConsole.Core.FileIO;

/// <summary>
/// Reads SmartInspect Log files (.sil format).
/// Format: "SILF" magic header (4 bytes) followed by sequential binary packets.
/// </summary>
public class SilFileReader
{
    private const string MagicHeader = "SILF";

    /// <summary>
    /// Reads all packets from a .sil file.
    /// </summary>
    /// <param name="filePath">Path to the .sil file.</param>
    /// <returns>List of parsed packets.</returns>
    public async Task<List<Packet>> ReadFileAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return await ReadFromStreamAsync(stream);
    }

    /// <summary>
    /// Reads all packets from a stream containing .sil format data.
    /// </summary>
    public async Task<List<Packet>> ReadFromStreamAsync(Stream stream)
    {
        // Validate magic header
        var magic = new byte[4];
        var bytesRead = await BinaryPacketReader.ReadExactlyAsync(stream, magic, 4);
        if (bytesRead < 4)
            throw new InvalidDataException("File is too small to be a valid SmartInspect log file.");

        var magicString = Encoding.ASCII.GetString(magic);
        if (magicString != MagicHeader)
            throw new InvalidDataException($"Invalid file header. Expected 'SILF', got '{magicString}'.");

        // Read packets sequentially
        var packets = new List<Packet>();
        var reader = new BinaryPacketReader();

        while (true)
        {
            var header = await BinaryPacketReader.ReadPacketHeaderAsync(stream);
            if (header == null)
                break; // End of file

            var (packetType, payloadSize) = header.Value;

            if (payloadSize < 0 || payloadSize > 100_000_000) // 100MB safety limit
                break;

            var payload = new byte[payloadSize];
            bytesRead = await BinaryPacketReader.ReadExactlyAsync(stream, payload, payloadSize);
            if (bytesRead < payloadSize)
                break; // Truncated file

            var packet = reader.ParsePacket(packetType, payload);
            if (packet != null)
                packets.Add(packet);
        }

        return packets;
    }
}
