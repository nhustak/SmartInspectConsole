using System.Drawing;
using System.Text;
using SmartInspectConsole.Core.Enums;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Core.Parsing;

/// <summary>
/// Reads and parses SmartInspect binary protocol packets.
/// </summary>
public class BinaryPacketReader
{
    // OLE Automation timestamp constants
    private const long Ticks = 621355968000000000L;
    private const long MicrosecondsPerDay = 86400000000L;
    private const int DayOffset = 25569;

    private byte[] _buffer = [];
    private int _position;

    /// <summary>
    /// Parses a packet from the given payload bytes.
    /// </summary>
    /// <param name="packetType">The packet type.</param>
    /// <param name="payload">The packet payload (excluding the 6-byte header).</param>
    /// <returns>The parsed packet, or null if parsing fails.</returns>
    public Packet? ParsePacket(PacketType packetType, byte[] payload)
    {
        _buffer = payload;
        _position = 0;

        return packetType switch
        {
            PacketType.LogEntry => ParseLogEntry(),
            PacketType.Watch => ParseWatch(),
            PacketType.ProcessFlow => ParseProcessFlow(),
            PacketType.ControlCommand => ParseControlCommand(),
            PacketType.LogHeader => ParseLogHeader(),
            _ => null
        };
    }

    /// <summary>
    /// Hard upper bound for a single packet payload. Larger values are treated as corrupt
    /// so a bad header cannot force multi-GB allocations that stall the process.
    /// </summary>
    public const int MaxPacketPayloadSize = 16 * 1024 * 1024;

    /// <summary>
    /// Reads a packet header from the stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>A tuple of (PacketType, PayloadSize), or null if end of stream.</returns>
    /// <exception cref="InvalidDataException">Thrown when the declared payload size is invalid.</exception>
    public static async Task<(PacketType Type, int Size)?> ReadPacketHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[Packet.HeaderSize];
        var bytesRead = await ReadExactlyAsync(stream, header, Packet.HeaderSize, cancellationToken);

        if (bytesRead < Packet.HeaderSize)
            return null;

        var packetType = (PacketType)(header[0] | header[1] << 8);
        var size = header[2] | header[3] << 8 | header[4] << 16 | header[5] << 24;

        if (size < 0 || size > MaxPacketPayloadSize)
            throw new InvalidDataException($"Invalid SmartInspect packet size: {size}.");

        return (packetType, size);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the stream.
    /// </summary>
    public static async Task<int> ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }

    private LogEntry ParseLogEntry()
    {
        // Header: 48 bytes
        var logEntryType = ReadLogEntryType();
        var viewerId = (ViewerId)ReadInt();
        var appNameLength = ReadInt();
        var sessionNameLength = ReadInt();
        var titleLength = ReadInt();
        var hostNameLength = ReadInt();
        var dataLength = ReadInt();
        var processId = ReadInt();
        var threadId = ReadInt();
        var timestamp = ReadTimestamp();
        var color = ReadColor();

        // String data
        var appName = ReadString(appNameLength);
        var sessionName = ReadString(sessionNameLength);
        var title = ReadString(titleLength);
        var hostName = ReadString(hostNameLength);

        // Binary data
        byte[]? data = null;
        if (dataLength > 0)
        {
            data = ReadBytes(dataLength);
        }

        return new LogEntry
        {
            LogEntryType = logEntryType,
            ViewerId = viewerId,
            AppName = appName,
            SessionName = sessionName,
            Title = title,
            HostName = hostName,
            Data = data,
            ProcessId = processId,
            ThreadId = threadId,
            Timestamp = timestamp,
            Color = color
        };
    }

    private LogEntryType ReadLogEntryType()
    {
        var protocolValue = ReadInt();

        // Native SmartInspect binary values encode leave before enter.
        // Keep the app model semantic and translate at the protocol boundary.
        return protocolValue switch
        {
            1 => LogEntryType.LeaveMethod,
            2 => LogEntryType.EnterMethod,
            _ => (LogEntryType)protocolValue
        };
    }

    private Watch ParseWatch()
    {
        // Header: 20 bytes
        var nameLength = ReadInt();
        var valueLength = ReadInt();
        var watchType = (WatchType)ReadInt();
        var timestamp = ReadTimestamp();

        // String data
        var name = ReadString(nameLength);
        var value = ReadString(valueLength);

        return new Watch
        {
            Name = name,
            Value = value,
            WatchType = watchType,
            Timestamp = timestamp
        };
    }

    private ProcessFlow ParseProcessFlow()
    {
        // Header: 28 bytes
        var processFlowType = (ProcessFlowType)ReadInt();
        var titleLength = ReadInt();
        var hostNameLength = ReadInt();
        var processId = ReadInt();
        var threadId = ReadInt();
        var timestamp = ReadTimestamp();

        // String data
        var title = ReadString(titleLength);
        var hostName = ReadString(hostNameLength);

        return new ProcessFlow
        {
            ProcessFlowType = processFlowType,
            Title = title,
            HostName = hostName,
            ProcessId = processId,
            ThreadId = threadId,
            Timestamp = timestamp
        };
    }

    private ControlCommand ParseControlCommand()
    {
        // Header: 8 bytes
        var commandType = (ControlCommandType)ReadInt();
        var dataLength = ReadInt();

        byte[]? data = null;
        if (dataLength > 0)
        {
            data = ReadBytes(dataLength);
        }

        return new ControlCommand
        {
            ControlCommandType = commandType,
            Data = data,
            Timestamp = DateTime.Now
        };
    }

    private LogHeader ParseLogHeader()
    {
        var contentLength = ReadInt();
        var content = ReadString(contentLength);

        var header = new LogHeader
        {
            Content = content,
            Timestamp = DateTime.Now
        };

        // Parse the content to extract metadata
        header.ParseContent();

        return header;
    }

    private short ReadShort()
    {
        var value = (short)(_buffer[_position] | _buffer[_position + 1] << 8);
        _position += 2;
        return value;
    }

    private int ReadInt()
    {
        var value = _buffer[_position] |
                   _buffer[_position + 1] << 8 |
                   _buffer[_position + 2] << 16 |
                   _buffer[_position + 3] << 24;
        _position += 4;
        return value;
    }

    private long ReadLong()
    {
        long value = (long)_buffer[_position] |
                    (long)_buffer[_position + 1] << 8 |
                    (long)_buffer[_position + 2] << 16 |
                    (long)_buffer[_position + 3] << 24 |
                    (long)_buffer[_position + 4] << 32 |
                    (long)_buffer[_position + 5] << 40 |
                    (long)_buffer[_position + 6] << 48 |
                    (long)_buffer[_position + 7] << 56;
        _position += 8;
        return value;
    }

    private double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

    private DateTime ReadTimestamp()
    {
        var value = ReadDouble();

        // Convert OLE Automation date to DateTime
        // The integral part is days since 12/30/1899
        // The fractional part is the fraction of a 24-hour day elapsed
        var days = (long)value - DayOffset;
        var fraction = value - (long)value;
        var microseconds = (long)(fraction * MicrosecondsPerDay);
        var ticks = (days * MicrosecondsPerDay + microseconds) * 10L + Ticks;

        try
        {
            return new DateTime(ticks, DateTimeKind.Local);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private Color ReadColor()
    {
        var value = ReadInt();
        var r = (byte)(value & 0xFF);
        var g = (byte)((value >> 8) & 0xFF);
        var b = (byte)((value >> 16) & 0xFF);
        var a = (byte)((value >> 24) & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }

    private string ReadString(int length)
    {
        if (length <= 0)
            return string.Empty;

        var value = Encoding.UTF8.GetString(_buffer, _position, length);
        _position += length;
        return value;
    }

    private byte[] ReadBytes(int length)
    {
        if (length <= 0)
            return [];

        var value = new byte[length];
        Array.Copy(_buffer, _position, value, 0, length);
        _position += length;
        return value;
    }
}
