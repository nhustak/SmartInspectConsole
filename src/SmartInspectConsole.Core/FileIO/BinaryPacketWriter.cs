using System.Drawing;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Core.FileIO;

/// <summary>
/// Serializes SmartInspect packets to binary format.
/// Reverse of BinaryPacketReader.
/// </summary>
public class BinaryPacketWriter
{
    // OLE Automation timestamp constants (same as BinaryPacketReader)
    private const long Ticks = 621355968000000000L;
    private const long MicrosecondsPerDay = 86400000000L;
    private const int DayOffset = 25569;

    /// <summary>
    /// Writes a complete packet (6-byte header + payload) to the stream.
    /// </summary>
    public void WritePacket(Stream stream, Packet packet)
    {
        var payload = SerializePayload(packet);
        WritePacketHeader(stream, packet.PacketType, payload.Length);
        stream.Write(payload);
    }

    /// <summary>
    /// Writes the 6-byte packet header (2-byte type + 4-byte size).
    /// </summary>
    private static void WritePacketHeader(Stream stream, PacketType type, int payloadSize)
    {
        var header = new byte[Packet.HeaderSize];
        var typeValue = (short)type;
        header[0] = (byte)(typeValue & 0xFF);
        header[1] = (byte)((typeValue >> 8) & 0xFF);
        header[2] = (byte)(payloadSize & 0xFF);
        header[3] = (byte)((payloadSize >> 8) & 0xFF);
        header[4] = (byte)((payloadSize >> 16) & 0xFF);
        header[5] = (byte)((payloadSize >> 24) & 0xFF);
        stream.Write(header);
    }

    /// <summary>
    /// Serializes a packet's payload to bytes (excluding the 6-byte header).
    /// </summary>
    public byte[] SerializePayload(Packet packet)
    {
        return packet switch
        {
            LogEntry logEntry => SerializeLogEntry(logEntry),
            Watch watch => SerializeWatch(watch),
            ProcessFlow processFlow => SerializeProcessFlow(processFlow),
            ControlCommand controlCommand => SerializeControlCommand(controlCommand),
            LogHeader logHeader => SerializeLogHeader(logHeader),
            _ => throw new ArgumentException($"Unknown packet type: {packet.GetType().Name}")
        };
    }

    private byte[] SerializeLogEntry(LogEntry entry)
    {
        var appNameBytes = Encoding.UTF8.GetBytes(entry.AppName);
        var sessionNameBytes = Encoding.UTF8.GetBytes(entry.SessionName);
        var titleBytes = Encoding.UTF8.GetBytes(entry.Title);
        var hostNameBytes = Encoding.UTF8.GetBytes(entry.HostName);
        var dataBytes = entry.Data ?? [];

        // 48-byte fixed header + variable data
        var size = 48 + appNameBytes.Length + sessionNameBytes.Length +
                   titleBytes.Length + hostNameBytes.Length + dataBytes.Length;
        var buffer = new byte[size];
        var pos = 0;

        WriteInt(buffer, ref pos, (int)entry.LogEntryType);
        WriteInt(buffer, ref pos, (int)entry.ViewerId);
        WriteInt(buffer, ref pos, appNameBytes.Length);
        WriteInt(buffer, ref pos, sessionNameBytes.Length);
        WriteInt(buffer, ref pos, titleBytes.Length);
        WriteInt(buffer, ref pos, hostNameBytes.Length);
        WriteInt(buffer, ref pos, dataBytes.Length);
        WriteInt(buffer, ref pos, entry.ProcessId);
        WriteInt(buffer, ref pos, entry.ThreadId);
        WriteTimestamp(buffer, ref pos, entry.Timestamp);
        WriteColor(buffer, ref pos, entry.Color);

        WriteBytes(buffer, ref pos, appNameBytes);
        WriteBytes(buffer, ref pos, sessionNameBytes);
        WriteBytes(buffer, ref pos, titleBytes);
        WriteBytes(buffer, ref pos, hostNameBytes);
        WriteBytes(buffer, ref pos, dataBytes);

        return buffer;
    }

    private byte[] SerializeWatch(Watch watch)
    {
        var nameBytes = Encoding.UTF8.GetBytes(watch.Name);
        var valueBytes = Encoding.UTF8.GetBytes(watch.Value);

        var size = 20 + nameBytes.Length + valueBytes.Length;
        var buffer = new byte[size];
        var pos = 0;

        WriteInt(buffer, ref pos, nameBytes.Length);
        WriteInt(buffer, ref pos, valueBytes.Length);
        WriteInt(buffer, ref pos, (int)watch.WatchType);
        WriteTimestamp(buffer, ref pos, watch.Timestamp);

        WriteBytes(buffer, ref pos, nameBytes);
        WriteBytes(buffer, ref pos, valueBytes);

        return buffer;
    }

    private byte[] SerializeProcessFlow(ProcessFlow processFlow)
    {
        var titleBytes = Encoding.UTF8.GetBytes(processFlow.Title);
        var hostNameBytes = Encoding.UTF8.GetBytes(processFlow.HostName);

        var size = 28 + titleBytes.Length + hostNameBytes.Length;
        var buffer = new byte[size];
        var pos = 0;

        WriteInt(buffer, ref pos, (int)processFlow.ProcessFlowType);
        WriteInt(buffer, ref pos, titleBytes.Length);
        WriteInt(buffer, ref pos, hostNameBytes.Length);
        WriteInt(buffer, ref pos, processFlow.ProcessId);
        WriteInt(buffer, ref pos, processFlow.ThreadId);
        WriteTimestamp(buffer, ref pos, processFlow.Timestamp);

        WriteBytes(buffer, ref pos, titleBytes);
        WriteBytes(buffer, ref pos, hostNameBytes);

        return buffer;
    }

    private byte[] SerializeControlCommand(ControlCommand command)
    {
        var dataBytes = command.Data ?? [];

        var size = 8 + dataBytes.Length;
        var buffer = new byte[size];
        var pos = 0;

        WriteInt(buffer, ref pos, (int)command.ControlCommandType);
        WriteInt(buffer, ref pos, dataBytes.Length);
        WriteBytes(buffer, ref pos, dataBytes);

        return buffer;
    }

    private byte[] SerializeLogHeader(LogHeader header)
    {
        var contentBytes = Encoding.UTF8.GetBytes(header.Content);

        var size = 4 + contentBytes.Length;
        var buffer = new byte[size];
        var pos = 0;

        WriteInt(buffer, ref pos, contentBytes.Length);
        WriteBytes(buffer, ref pos, contentBytes);

        return buffer;
    }

    private static void WriteInt(byte[] buffer, ref int pos, int value)
    {
        buffer[pos] = (byte)(value & 0xFF);
        buffer[pos + 1] = (byte)((value >> 8) & 0xFF);
        buffer[pos + 2] = (byte)((value >> 16) & 0xFF);
        buffer[pos + 3] = (byte)((value >> 24) & 0xFF);
        pos += 4;
    }

    private static void WriteTimestamp(byte[] buffer, ref int pos, DateTime timestamp)
    {
        // Convert DateTime to OLE Automation date (reverse of ReadTimestamp)
        var ticks = timestamp.Ticks - Ticks;
        var totalMicroseconds = ticks / 10L;
        var days = totalMicroseconds / MicrosecondsPerDay + DayOffset;
        var remainderMicroseconds = totalMicroseconds % MicrosecondsPerDay;
        var fraction = (double)remainderMicroseconds / MicrosecondsPerDay;
        var oleDate = days + fraction;

        var longValue = BitConverter.DoubleToInt64Bits(oleDate);
        buffer[pos] = (byte)(longValue & 0xFF);
        buffer[pos + 1] = (byte)((longValue >> 8) & 0xFF);
        buffer[pos + 2] = (byte)((longValue >> 16) & 0xFF);
        buffer[pos + 3] = (byte)((longValue >> 24) & 0xFF);
        buffer[pos + 4] = (byte)((longValue >> 32) & 0xFF);
        buffer[pos + 5] = (byte)((longValue >> 40) & 0xFF);
        buffer[pos + 6] = (byte)((longValue >> 48) & 0xFF);
        buffer[pos + 7] = (byte)((longValue >> 56) & 0xFF);
        pos += 8;
    }

    private static void WriteColor(byte[] buffer, ref int pos, Color color)
    {
        // Stored as RGBA in little-endian int (R in lowest byte)
        var value = color.R | (color.G << 8) | (color.B << 16) | (color.A << 24);
        WriteInt(buffer, ref pos, value);
    }

    private static void WriteBytes(byte[] buffer, ref int pos, byte[] data)
    {
        if (data.Length > 0)
        {
            Array.Copy(data, 0, buffer, pos, data.Length);
            pos += data.Length;
        }
    }
}
