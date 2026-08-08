using System.IO.Pipes;
using PickWeight.ClassIsland.Protocol;

namespace PickWeight.ClassIsland.Protocol.Tests;

public sealed class ClassIslandProtocolTests
{
    [Fact]
    public void CreateDrawResultRequest_TrimsNames_AndFormatsCombinedMessage()
    {
        var request = ClassIslandProtocol.CreateDrawResultRequest([" 张三 ", "李四"]);

        Assert.Null(ClassIslandProtocol.Validate(request));
        Assert.Equal(["张三", "李四"], request.Names);
        Assert.Equal("张三、李四被抽中", ClassIslandProtocol.FormatDrawResult(request.Names));
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Validate_RejectsInvalidRequests(DrawResultRequest request)
    {
        Assert.NotNull(ClassIslandProtocol.Validate(request));
    }

    [Fact]
    public async Task ReadAndWrite_RoundTripsRequest()
    {
        await using var stream = new MemoryStream();
        var request = ClassIslandProtocol.CreateDrawResultRequest(["Alice"]);

        await ClassIslandProtocol.WriteAsync(stream, request);
        stream.Position = 0;
        var loaded = await ClassIslandProtocol.ReadAsync<DrawResultRequest>(stream);

        Assert.NotNull(loaded);
        Assert.Equal(request.Version, loaded.Version);
        Assert.Equal(request.Type, loaded.Type);
        Assert.Equal(request.Names, loaded.Names);
    }

    [Fact]
    public async Task PipeClient_SendsRequest_AndReceivesAcknowledgement()
    {
        var pipeName = $"PickWeight.ClassIsland.Tests.{Guid.NewGuid():N}";
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync();
            var request = await ClassIslandProtocol.ReadAsync<DrawResultRequest>(server);
            await ClassIslandProtocol.WriteAsync(server, new ClassIslandResponse(
                ClassIslandProtocol.Validate(request) is null));
            return request;
        });

        var result = await new ClassIslandPipeClient(TimeSpan.FromSeconds(5), pipeName)
            .SendDrawResultAsync(["Alice", "Bob"]);
        var received = await serverTask;

        Assert.True(result.Succeeded);
        Assert.Equal(["Alice", "Bob"], received!.Names);
    }

    [Fact]
    public async Task PipeClient_WhenPluginIsUnavailable_ReturnsFailure()
    {
        var pipeName = $"PickWeight.ClassIsland.Tests.{Guid.NewGuid():N}";
        var result = await new ClassIslandPipeClient(TimeSpan.FromMilliseconds(100), pipeName)
            .SendDrawResultAsync(["Alice"]);

        Assert.False(result.Succeeded);
    }

    public static TheoryData<DrawResultRequest> InvalidRequests => new()
    {
        new DrawResultRequest(99, ClassIslandProtocol.DrawResultType, ["Alice"]),
        new DrawResultRequest(ClassIslandProtocol.Version, "unknown", ["Alice"]),
        new DrawResultRequest(ClassIslandProtocol.Version, ClassIslandProtocol.DrawResultType, []),
        new DrawResultRequest(ClassIslandProtocol.Version, ClassIslandProtocol.DrawResultType, [""]),
        new DrawResultRequest(
            ClassIslandProtocol.Version,
            ClassIslandProtocol.DrawResultType,
            [new string('a', ClassIslandProtocol.MaximumNameLength + 1)])
    };
}

public sealed class ClassIslandContextProtocolTests
{
    [Fact]
    public void CreateContextQueryRequest_IsValid()
    {
        var request = ClassIslandProtocol.CreateContextQueryRequest();

        Assert.Null(ClassIslandProtocol.Validate(request));
        Assert.Equal(ClassIslandProtocol.ContextQueryType, request.Type);
        Assert.Equal(ClassIslandProtocol.Version, request.Version);
    }

    [Fact]
    public void Validate_ContextQuery_RejectsWrongVersion()
    {
        var request = new ContextQueryRequest(1, ClassIslandProtocol.ContextQueryType);

        Assert.Equal(ClassIslandProtocol.VersionMismatchError, ClassIslandProtocol.Validate(request));
    }

    [Fact]
    public async Task ReadRequestEnvelopeAsync_ExtractsTypeField()
    {
        await using var stream = new MemoryStream();
        var request = ClassIslandProtocol.CreateContextQueryRequest();
        await ClassIslandProtocol.WriteAsync(stream, request);
        stream.Position = 0;

        var envelope = await ClassIslandProtocol.ReadRequestEnvelopeAsync(stream);

        Assert.Equal(ClassIslandProtocol.ContextQueryType, envelope.Type);
        var deserialized = envelope.Deserialize<ContextQueryRequest>();
        Assert.NotNull(deserialized);
        Assert.Equal(ClassIslandProtocol.Version, deserialized!.Version);
    }

    [Fact]
    public async Task ReadRequestEnvelopeAsync_HandlesLowercaseTypeField()
    {
        await using var stream = new MemoryStream();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { version = 2, type = "draw-result", names = new[] { "张三" } });
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length);
        await stream.WriteAsync(payload);
        stream.Position = 0;

        var envelope = await ClassIslandProtocol.ReadRequestEnvelopeAsync(stream);

        Assert.Equal("draw-result", envelope.Type);
        var deserialized = envelope.Deserialize<DrawResultRequest>();
        Assert.NotNull(deserialized);
        Assert.Equal(["张三"], deserialized!.Names);
    }

    [Fact]
    public async Task PipeClient_QueryContext_ReceivesContextResponse()
    {
        var pipeName = $"PickWeight.ClassIsland.ContextTests.{Guid.NewGuid():N}";
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync();
            var envelope = await ClassIslandProtocol.ReadRequestEnvelopeAsync(server);
            Assert.Equal(ClassIslandProtocol.ContextQueryType, envelope.Type);
            var request = envelope.Deserialize<ContextQueryRequest>();
            Assert.Null(ClassIslandProtocol.Validate(request));
            await ClassIslandProtocol.WriteAsync(
                server,
                new ClassContextResponse(true, new ClassContext("语文", 3, true, DateTimeOffset.Now)),
                CancellationToken.None);
        });

        var client = new ClassIslandPipeClient(TimeSpan.FromSeconds(5), pipeName);
        var result = await client.QueryContextAsync();
        await serverTask;

        Assert.True(result.Succeeded);
        Assert.Equal("语文", result.Context?.Subject);
        Assert.Equal(3, result.Context?.PeriodIndex);
    }
}
