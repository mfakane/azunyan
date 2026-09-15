using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Azunote;

/// <summary>
/// One command line forwarded from a later launch to the running Azunote
/// instance.
/// </summary>
public sealed record SingleInstanceCommand(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput);

/// <summary>
/// How the running instance finished a forwarded command. The value is the
/// process exit code the command-line client reports.
/// </summary>
public enum SingleInstanceStatus : byte
{
    Completed = 0,
    Canceled = 1,
    Failed = 2
}

/// <summary>
/// The result of a forwarded command. The payload is held as UTF-8 because
/// that is what both ends do with it: the editor encodes the document once,
/// and the client copies the bytes to standard output without decoding them.
/// </summary>
public sealed class SingleInstanceResponse
{
    private static readonly SingleInstanceResponse CanceledResponse = new(SingleInstanceStatus.Canceled, ReadOnlyMemory<byte>.Empty);
    private static readonly SingleInstanceResponse FailedResponse = new(SingleInstanceStatus.Failed, ReadOnlyMemory<byte>.Empty);

    public SingleInstanceResponse(SingleInstanceStatus status, ReadOnlyMemory<byte> payload)
    {
        Status = status;
        Payload = payload;
    }

    public SingleInstanceStatus Status { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public int ExitCode => (int)Status;

    public string PayloadText => SingleInstanceProtocol.Utf8.GetString(Payload.Span);

    public static SingleInstanceResponse Completed(string payload = "") =>
        new(SingleInstanceStatus.Completed, SingleInstanceProtocol.Utf8.GetBytes(payload));

    public static SingleInstanceResponse Canceled() => CanceledResponse;

    public static SingleInstanceResponse Failed() => FailedResponse;
}

/// <summary>
/// The wire format shared by <c>azu.exe</c> and the running Azunote instance.
/// Every message is one length-prefixed frame, written with a single write and
/// read with a single read, then parsed in memory. Strings inside a frame are
/// length-prefixed UTF-8.
/// </summary>
public static class SingleInstanceProtocol
{
    public const string DefaultInstanceName = "Azunote";
    public const int MaxArgumentCount = 64;
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string GetPipeName(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        return $"{instanceName}.CommandLine";
    }

    public static string GetMutexName(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        return $@"Local\{instanceName}.SingleInstance";
    }

    public static Task WriteCommandAsync(
        Stream stream,
        SingleInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Assigned in a statement rather than a conditional expression: a null
        // byte array converts to an empty ReadOnlyMemory, which would turn
        // absent standard input into empty standard input.
        ReadOnlyMemory<byte>? standardInput = null;
        if (command.StandardInput is { } text)
        {
            standardInput = Utf8.GetBytes(text);
        }

        return WriteCommandAsync(
            stream,
            command.Arguments,
            command.WorkingDirectory,
            standardInput,
            cancellationToken);
    }

    /// <summary>
    /// Writes a command whose standard input is already UTF-8. The client
    /// reads standard input as bytes, so this keeps the text out of a decode
    /// and re-encode round trip on the way to the editor.
    /// </summary>
    public static async Task WriteCommandAsync(
        Stream stream,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ReadOnlyMemory<byte>? standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (arguments.Count > MaxArgumentCount)
        {
            throw new InvalidOperationException("Too many command-line arguments.");
        }

        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, arguments.Count);
        foreach (var argument in arguments)
        {
            WriteString(writer, argument);
        }

        WriteString(writer, workingDirectory);
        if (standardInput is { } input)
        {
            writer.GetSpan(1)[0] = 1;
            writer.Advance(1);
            WriteInt32(writer, input.Length);
            writer.Write(input.Span);
        }
        else
        {
            writer.GetSpan(1)[0] = 0;
            writer.Advance(1);
        }

        await WriteFrameAsync(stream, writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SingleInstanceCommand> ReadCommandAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        var offset = 0;
        var span = frame.AsSpan();
        var count = ReadInt32(span, ref offset);
        if (count < 0 || count > MaxArgumentCount)
        {
            throw new InvalidDataException("The forwarded command contains an invalid argument count.");
        }

        var arguments = new string[count];
        for (var index = 0; index < count; index++)
        {
            arguments[index] = ReadString(span, ref offset);
        }

        var workingDirectory = ReadString(span, ref offset);
        return ReadByte(span, ref offset) switch
        {
            0 => new SingleInstanceCommand(arguments, workingDirectory, null),
            1 => new SingleInstanceCommand(arguments, workingDirectory, ReadString(span, ref offset)),
            _ => throw new InvalidDataException("The forwarded command contains an invalid standard-input flag.")
        };
    }

    public static async Task WriteResponseAsync(
        Stream stream,
        SingleInstanceResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(response);

        var frame = new byte[sizeof(int) + 1 + response.Payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length - sizeof(int));
        frame[sizeof(int)] = (byte)response.Status;
        response.Payload.Span.CopyTo(frame.AsSpan(sizeof(int) + 1));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SingleInstanceResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame.Length < 1)
        {
            throw new InvalidDataException("The response frame is empty.");
        }

        return new SingleInstanceResponse(
            (SingleInstanceStatus)frame[0],
            frame.AsMemory(1));
    }

    private static async Task WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.Span.CopyTo(frame.AsSpan(sizeof(int)));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"The message frame length is invalid: {length}");
        }

        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(sizeof(int)), value);
        writer.Advance(sizeof(int));
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var length = Utf8.GetByteCount(value);
        WriteInt32(writer, length);
        Utf8.GetBytes(value, writer.GetSpan(length));
        writer.Advance(length);
    }

    private static byte ReadByte(ReadOnlySpan<byte> frame, ref int offset)
    {
        if (offset >= frame.Length)
        {
            throw new InvalidDataException("The message frame ended early.");
        }

        return frame[offset++];
    }

    private static int ReadInt32(ReadOnlySpan<byte> frame, ref int offset)
    {
        if (offset + sizeof(int) > frame.Length)
        {
            throw new InvalidDataException("The message frame ended early.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(frame[offset..]);
        offset += sizeof(int);
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> frame, ref int offset)
    {
        var length = ReadInt32(frame, ref offset);
        if (length < 0 || offset + length > frame.Length)
        {
            throw new InvalidDataException("The message frame contains an invalid string length.");
        }

        var value = Utf8.GetString(frame.Slice(offset, length));
        offset += length;
        return value;
    }
}
