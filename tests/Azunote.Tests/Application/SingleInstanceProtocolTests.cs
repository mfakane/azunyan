using System.Text;
using Xunit;

namespace Azunote.Tests;

public sealed class SingleInstanceProtocolTests
{
    [Fact]
    public async Task A_command_survives_the_round_trip()
    {
        using var stream = new MemoryStream();
        var command = new SingleInstanceCommand(
            ["--output", "document", "-"],
            @"C:\work",
            "パイプで渡した本文\r\n");

        await SingleInstanceProtocol.WriteCommandAsync(stream, command);
        stream.Position = 0;
        var restored = await SingleInstanceProtocol.ReadCommandAsync(stream);

        Assert.Equal(command.Arguments, restored.Arguments);
        Assert.Equal(command.WorkingDirectory, restored.WorkingDirectory);
        Assert.Equal(command.StandardInput, restored.StandardInput);
    }

    [Fact]
    public async Task Standard_input_written_as_utf8_arrives_as_the_same_text()
    {
        using var stream = new MemoryStream();
        var text = "パイプで渡した本文\r\n";

        await SingleInstanceProtocol.WriteCommandAsync(
            stream,
            ["-"],
            @"C:\work",
            new UTF8Encoding(false).GetBytes(text));
        stream.Position = 0;

        Assert.Equal(text, (await SingleInstanceProtocol.ReadCommandAsync(stream)).StandardInput);
    }

    [Fact]
    public async Task A_command_without_standard_input_stays_null()
    {
        using var stream = new MemoryStream();

        await SingleInstanceProtocol.WriteCommandAsync(
            stream,
            new SingleInstanceCommand(["notes.txt"], @"C:\work", null));
        stream.Position = 0;

        Assert.Null((await SingleInstanceProtocol.ReadCommandAsync(stream)).StandardInput);
    }

    [Fact]
    public async Task A_response_carries_its_status_and_payload()
    {
        using var stream = new MemoryStream();

        await SingleInstanceProtocol.WriteResponseAsync(
            stream,
            SingleInstanceResponse.Completed("alpha\r\nbravo"));
        stream.Position = 0;
        var response = await SingleInstanceProtocol.ReadResponseAsync(stream);

        Assert.Equal(SingleInstanceStatus.Completed, response.Status);
        Assert.Equal("alpha\r\nbravo", response.PayloadText);
        Assert.Equal(0, response.ExitCode);
    }

    [Fact]
    public async Task Each_message_is_one_length_prefixed_frame()
    {
        using var stream = new MemoryStream();

        await SingleInstanceProtocol.WriteResponseAsync(
            stream,
            SingleInstanceResponse.Completed("abc"));

        var bytes = stream.ToArray();
        Assert.Equal(sizeof(int) + 1 + 3, bytes.Length);
        Assert.Equal(1 + 3, BitConverter.ToInt32(bytes, 0));
    }

    [Fact]
    public async Task An_oversized_frame_is_rejected_before_it_is_allocated()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(SingleInstanceProtocol.MaxFrameBytes + 1));
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await SingleInstanceProtocol.ReadResponseAsync(stream));
    }

    [Fact]
    public void Each_status_is_the_exit_code_it_reports()
    {
        Assert.Equal(0, SingleInstanceResponse.Completed().ExitCode);
        Assert.Equal(1, SingleInstanceResponse.Canceled().ExitCode);
        Assert.Equal(2, SingleInstanceResponse.Failed().ExitCode);
    }
}
