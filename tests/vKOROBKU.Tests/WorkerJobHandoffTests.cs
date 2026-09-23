extern alias worker;
using System.Threading.Channels;
using WorkerProgram = worker::vKOROBKU.Worker.Program;
using WorkerMessage = worker::vKOROBKU.Protocol.WorkerMessage;

namespace vKOROBKU.Tests;

public sealed class WorkerJobHandoffTests
{
    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("error")]
    public async Task TerminalResponseHasNoCompetingReader_ForImmediateNextCommand(string outcome)
    {
        var inbox = Channel.CreateUnbounded<string>();
        var reader = new TrackingReader(inbox.Reader);
        const string next = "{\"type\":\"shutdown\"}";
        var shutdown = await WorkerProgram.RunJobAsync(
            _ => outcome switch
            {
                "cancelled" => Task.FromException<WorkerMessage>(new OperationCanceledException()),
                "error" => Task.FromException<WorkerMessage>(new IOException("fixture")),
                _ => Task.FromResult(new WorkerMessage("completed"))
            }, reader,
            async message =>
            {
                Assert.Equal(outcome, message.Type);
                // Deterministic: a response cannot be published while the previous
                // job still owns a pending read, regardless of scheduling speed.
                Assert.Equal(0, reader.PendingReads);
                await inbox.Writer.WriteAsync(next);
            });
        Assert.False(shutdown);
        Assert.True(inbox.Reader.TryRead(out var command));
        Assert.Equal(next, command);
    }

    [Theory]
    [InlineData("shutdown", true)]
    [InlineData("cancel", false)]
    public async Task InFlightShutdownEndsSession_WhileCancelOnlyEndsJob(string command, bool expectedShutdown)
    {
        var inbox = Channel.CreateUnbounded<string>();
        await inbox.Writer.WriteAsync($"{{\"type\":\"{command}\"}}");
        var shutdown = await WorkerProgram.RunJobAsync(
            token => Task.FromCanceled<WorkerMessage>(token), inbox.Reader,
            message => { Assert.Equal("cancelled", message.Type); return Task.CompletedTask; });
        Assert.Equal(expectedShutdown, shutdown);
    }

    private sealed class TrackingReader(ChannelReader<string> inner) : ChannelReader<string>
    {
        public int PendingReads { get; private set; }
        public override async ValueTask<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            PendingReads++;
            try { return await inner.ReadAsync(cancellationToken); }
            finally { PendingReads--; }
        }
        public override bool TryRead(out string item) => inner.TryRead(out item!);
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            inner.WaitToReadAsync(cancellationToken);
    }
}
