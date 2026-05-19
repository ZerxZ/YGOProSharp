using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using YGOProSharp;
using YGOProSharp.Network;
using YGOProSharp.Network.Enums;
using YGOProSharp.Network.Utils;
using YGOProSharp.OCGWrapper;
using YGOProSharp.OCGWrapper.Enums;

Run("CoreMessage reads native payloads from spans", CoreMessageReadsNativePayloadsFromSpans);
Run("Duel rejects too-small query destination buffers", DuelRejectsTooSmallQueryDestinationBuffers);
Run("Duel preserves oversize response behavior", DuelPreservesOversizeResponseBehavior);
Run("PacketFramer handles split and sticky packets", PacketFramerHandlesSplitAndStickyPackets);
Run("PacketFramer handles 4-byte size-included headers", PacketFramerHandlesFourByteSizeIncludedHeaders);
Run("PacketFramer rejects oversize packets", PacketFramerRejectsOversizePackets);
Run("PacketReader reads little-endian and UTF-16 data", PacketReaderReadsLittleEndianAndUtf16Data);
Run("Player parses player info from spans", PlayerParsesPlayerInfoFromSpans);
await RunAsync("NetworkClient loopback send and receive", NetworkClientLoopbackSendAndReceiveAsync);

static void CoreMessageReadsNativePayloadsFromSpans()
{
    byte[] raw =
    [
        0x34, 0x12,
        0x78, 0x56, 0x34, 0x12,
        0xAA, 0xBB, 0xCC
    ];

    CoreMessage message = new(GameMessage.Hint, raw);

    AssertEqual(0x1234, message.Reader.ReadInt16());
    AssertEqual(0x12345678, message.Reader.ReadInt32());
    AssertSequenceEqual(new byte[] { 0xAA, 0xBB }, message.Reader.ReadBytes(2));
    AssertSequenceEqual(raw.AsSpan(0, 8), message.CreateBufferSpan());
}

static void DuelRejectsTooSmallQueryDestinationBuffers()
{
    byte[] source = [1, 2, 3, 4];
    byte[] destination = new byte[3];

    AssertThrows<ArgumentException>(() => Duel.CopyQueryResult(source, source.Length, destination, "test_query"));
}

static void DuelPreservesOversizeResponseBehavior()
{
    byte[] response = new byte[Duel.MaxResponseLength + 1];
    byte[] destination = Enumerable.Repeat((byte)0xCC, Duel.MaxResponseLength).ToArray();

    bool copied = Duel.TryCopyResponse(response, destination);

    AssertFalse(copied);
    AssertSequenceEqual(Enumerable.Repeat((byte)0xCC, Duel.MaxResponseLength).ToArray(), destination);
}

static void PacketFramerHandlesSplitAndStickyPackets()
{
    PacketFramer framer = new();
    byte[] first = framer.Frame([1, 2, 3]);
    byte[] second = framer.Frame([4, 5]);

    framer.Append(first.AsSpan(0, 1));
    AssertFalse(framer.TryReadPacket(out _));

    framer.Append(first.AsSpan(1));
    AssertTrue(framer.TryReadPacket(out byte[] firstPacket));
    AssertSequenceEqual(new byte[] { 1, 2, 3 }, firstPacket);

    byte[] sticky = [.. second, .. framer.Frame([6])];
    framer.Append(sticky);

    AssertTrue(framer.TryReadPacket(out byte[] secondPacket));
    AssertSequenceEqual(new byte[] { 4, 5 }, secondPacket);

    AssertTrue(framer.TryReadPacket(out byte[] thirdPacket));
    AssertSequenceEqual(new byte[] { 6 }, thirdPacket);
}

static void PacketFramerHandlesFourByteSizeIncludedHeaders()
{
    PacketFramer framer = new(headerSize: 4, isHeaderSizeIncluded: true);
    byte[] frame = framer.Frame([7, 8]);

    AssertEqual(6, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4)));

    framer.Append(frame);
    AssertTrue(framer.TryReadPacket(out byte[] packet));
    AssertSequenceEqual(new byte[] { 7, 8 }, packet);
}

static void PacketFramerRejectsOversizePackets()
{
    PacketFramer framer = new(maxPacketLength: 4);
    Span<byte> header = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(header, 5);

    framer.Append(header);

    AssertThrows<InvalidDataException>(() => framer.TryReadPacket(out _));
}

static void PacketReaderReadsLittleEndianAndUtf16Data()
{
    byte[] data = new byte[2 + 4 + 8 + 2];
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0, 2), 0x1234);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2, 4), 0x12345678);
    Encoding.Unicode.GetBytes("Hi\0\0", data.AsSpan(6, 8));
    data[^2] = 0xAA;
    data[^1] = 0xBB;

    PacketReader reader = new(data);

    AssertEqual(0x1234, reader.ReadInt16());
    AssertEqual(0x12345678, reader.ReadInt32());
    AssertEqual("Hi", reader.ReadUnicode(4));
    AssertSequenceEqual(new byte[] { 0xAA, 0xBB }, reader.ReadRemainingBytes());
    AssertEqual(0, reader.Remaining);
}

static void PlayerParsesPlayerInfoFromSpans()
{
    Config.Load([]);
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.Write((byte)CtosMessage.PlayerInfo);
    writer.WriteUnicode("Tester", 20);

    Player player = new(new Game(new CoreServer()), new YGOClient());

    player.Parse(stream.ToArray());

    AssertEqual("Tester", player.Name);
}

static async Task NetworkClientLoopbackSendAndReceiveAsync()
{
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();

    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using NetworkClient client = new();
    TaskCompletionSource<byte[]> clientReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    client.DataReceived += data => clientReceived.TrySetResult(data);

    Task<Socket> acceptTask = listener.AcceptSocketAsync();
    await client.ConnectAsync(IPAddress.Loopback, port);

    using Socket serverSocket = await acceptTask;
    await serverSocket.SendAsync(new byte[] { 1, 2, 3 });

    byte[] receivedByClient = await WaitAsync(clientReceived.Task);
    AssertSequenceEqual(new byte[] { 1, 2, 3 }, receivedByClient);

    await client.SendAsync(new byte[] { 4, 5 });
    byte[] serverBuffer = new byte[2];
    int bytesRead = await serverSocket.ReceiveAsync(serverBuffer);

    AssertEqual(2, bytesRead);
    AssertSequenceEqual(new byte[] { 4, 5 }, serverBuffer);

    client.Close();
    listener.Stop();
}

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
}

static async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
}

static void AssertEqual<T>(T expected, T actual)
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
}

static void AssertFalse(bool value)
{
    if (value)
        throw new InvalidOperationException("Expected false.");
}

static void AssertTrue(bool value)
{
    if (!value)
        throw new InvalidOperationException("Expected true.");
}

static void AssertSequenceEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expected.ToArray())}], actual [{string.Join(", ", actual.ToArray())}].");
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task<T> WaitAsync<T>(Task<T> task)
{
    Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
    if (completed != task)
        throw new TimeoutException("Timed out waiting for asynchronous test operation.");

    return await task;
}
