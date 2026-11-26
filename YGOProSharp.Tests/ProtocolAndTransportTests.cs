namespace YGOProSharp.Tests;

public sealed class ProtocolAndTransportTests
{
    [Fact]
    public void ProtocolMessageEnumsIncludeReferenceValues()
    {
        AssertEqual(0x30, (int)CtosMessage.RequestField);
        AssertEqual(0x09, (int)StocMessage.DeckCount);
        AssertEqual(0x30, (int)StocMessage.FieldFinish);
        AssertEqual(0x31, (int)StocMessage.SrvproRoomlist);
    }

    [Fact]
    public void PacketFramerHandlesSplitAndStickyPackets()
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

    [Fact]
    public void PacketFramerHandlesFourByteSizeIncludedHeaders()
    {
        PacketFramer framer = new(headerSize: 4, isHeaderSizeIncluded: true);
        byte[] frame = framer.Frame([7, 8]);

        AssertEqual(6, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4)));

        framer.Append(frame);
        AssertTrue(framer.TryReadPacket(out byte[] packet));
        AssertSequenceEqual(new byte[] { 7, 8 }, packet);
    }

    [Fact]
    public void PacketFramerRejectsOversizePackets()
    {
        PacketFramer framer = new(maxPacketLength: 4);
        Span<byte> header = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 5);

        framer.Append(header);

        AssertThrows<InvalidDataException>(() => framer.TryReadPacket(out _));
    }

    [Fact]
    public void PacketReaderReadsLittleEndianAndUtf16Data()
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

        PacketReader emptyReader = new(new byte[4]);
        AssertEqual("", emptyReader.ReadUnicode(2));
    }

    [Fact]
    public void BinaryExtensionsReadAndWriteFixedUtf8Strings()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.WriteUtf8("room", 8);
        writer.WriteUtf8("大厅", 12);

        stream.Position = 0;
        using BinaryReader reader = new(stream);

        AssertEqual("room", reader.ReadUtf8(8));
        AssertEqual("大厅", reader.ReadUtf8(12));

        PacketReader packetReader = new(stream.ToArray());
        AssertEqual("room", packetReader.ReadUtf8(8));
        AssertEqual("大厅", packetReader.ReadUtf8(12));
    }

    [Fact]
    public void PacketFramerCompactsInPlaceAcrossMultipleRounds()
    {
        PacketFramer framer = new();
        byte[] payloadA = Enumerable.Range(0, 128).Select(i => (byte)(i & 0xFF)).ToArray();
        byte[] payloadB = Enumerable.Range(0, 300).Select(i => (byte)((i * 3) & 0xFF)).ToArray();

        for (int round = 0; round < 40; round++)
        {
            byte[] framedA = framer.Frame(payloadA);
            byte[] framedB = framer.Frame(payloadB);

            // 先写入半包，再追加剩余部分和第二个完整粘包，覆盖多次压缩与扩容路径。
            int split = 17;
            framer.Append(framedA.AsSpan(0, split));
            AssertFalse(framer.TryReadPacket(out _));

            byte[] combined = new byte[(framedA.Length - split) + framedB.Length];
            framedA.AsSpan(split).CopyTo(combined);
            framedB.CopyTo(combined.AsSpan(framedA.Length - split));
            framer.Append(combined);

            AssertTrue(framer.TryReadPacket(out byte[]? readA));
            AssertSequenceEqual(payloadA, readA!);
            AssertTrue(framer.TryReadPacket(out byte[]? readB));
            AssertSequenceEqual(payloadB, readB!);
            AssertFalse(framer.TryReadPacket(out _));
        }
    }

    [Fact]
    public void BinaryExtensionsRoundTripsStackAllocAndPooledBuffers()
    {
        string shortCjk = "青眼白龙 Blue-Eyes";
        string largeText = new string('龙', 600); // > 1024 bytes in UTF-16 and UTF-8, exercises ArrayPool path

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUnicode(string.Empty, 16);
            writer.WriteUnicode(shortCjk, 32);
            writer.WriteUnicode(largeText, 700);
            writer.WriteUnicodeAutoLength(shortCjk, 64);
            writer.WriteUnicodeAutoLength(largeText, 700);
            writer.WriteUtf8(shortCjk, 64);
            writer.WriteUtf8(largeText, 2048);
        }

        stream.Position = 0;
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        AssertEqual(string.Empty, reader.ReadUnicode(16));
        AssertEqual(shortCjk, reader.ReadUnicode(32));
        AssertEqual(largeText, reader.ReadUnicode(700));
        AssertEqual(shortCjk, reader.ReadUnicode(shortCjk.Length + 1));
        AssertEqual(largeText, reader.ReadUnicode(largeText.Length + 1));
        AssertEqual(shortCjk, reader.ReadUtf8(64));
        AssertEqual(largeText, reader.ReadUtf8(2048));
    }

    [Fact]
    public async Task NetworkClientLoopbackSendAndReceiveAsync()
    {
        TestLog.Provider.Clear();
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

        AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Network client connected"));
        AssertTrue(TestLog.Provider.Contains(LogLevel.Debug, "Received 3 bytes"));
        AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Network client disconnected"));
    }

    [Fact]
    public async Task YgoClientStackAllocSendWritesCtosPacketsAsync()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using NetworkClient networkClient = new();
        YGOClient client = new(networkClient);
        Task<Socket> acceptTask = listener.AcceptSocketAsync();

        await networkClient.ConnectAsync(IPAddress.Loopback, port);
        using Socket serverSocket = await acceptTask;

        client.Send(CtosMessage.HsReady);
        client.Send(CtosMessage.Response, 0x12345678);

        byte[] received = new byte[3 + 7];
        int totalRead = 0;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        while (totalRead < received.Length)
        {
            int read = await serverSocket.ReceiveAsync(received.AsMemory(totalRead), cts.Token);
            if (read == 0)
                break;
            totalRead += read;
        }

        AssertEqual(received.Length, totalRead);
        AssertSequenceEqual(
            new byte[]
            {
                0x01, 0x00, (byte)CtosMessage.HsReady,
                0x05, 0x00, (byte)CtosMessage.Response, 0x78, 0x56, 0x34, 0x12
            },
            received);

        client.Close();
        listener.Stop();
    }

    [Fact]
    public async Task YgoClientWebSocketRoundTripsBothFramingModesAsync()
    {
        await VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode.LengthPrefixed);
        await VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode.MessagePerPacket);
    }

    [Fact]
    public async Task InMemoryNetworkTransportRoundTripsAndPropagatesDisconnectAsync()
    {
        // 1. 验证 connected:false -> Connect() 触发两端 Connected 事件与缓冲派发
        (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
            InMemoryNetworkTransport.CreatePair(connected: false);

        bool clientConnected = false;
        bool serverConnected = false;
        bool serverDisconnected = false;

        YGOClient client = new(clientTransport);
        YGOClient server = new(serverTransport);
        client.Connected += () => clientConnected = true;
        server.Connected += () => serverConnected = true;
        server.Disconnected += _ => serverDisconnected = true;

        List<byte[]> serverReceived = new();
        List<byte[]> clientReceived = new();
        server.PacketReceivedRaw += p => serverReceived.Add(p.ToArray());
        client.PacketReceivedRaw += p => clientReceived.Add(p.ToArray());

        clientTransport.Connect();
        client.Update();
        server.Update();

        AssertTrue(clientConnected);
        AssertTrue(serverConnected);

        client.Send(CtosMessage.HsReady);
        await client.SendAsync(new byte[] { (byte)CtosMessage.HandResult, 2, 0, 0, 0 });
        server.Update();

        AssertEqual(2, serverReceived.Count);
        AssertSequenceEqual(new byte[] { (byte)CtosMessage.HsReady }, serverReceived[0]);
        AssertSequenceEqual(new byte[] { (byte)CtosMessage.HandResult, 2, 0, 0, 0 }, serverReceived[1]);

        server.Send(new byte[] { (byte)StocMessage.SelectHand });
        client.Update();

        AssertEqual(1, clientReceived.Count);
        AssertSequenceEqual(new byte[] { (byte)StocMessage.SelectHand }, clientReceived[0]);

        // 2. 验证一端 Close 后对端同步断开
        client.Close();
        server.Update();
        AssertTrue(serverDisconnected);
        AssertFalse(server.IsConnected);

        // 3. 验证 YGOClient.CreateInMemoryPair 便捷工厂
        (YGOClient pairClient, YGOClient pairServer) = YGOClient.CreateInMemoryPair();
        byte[]? receivedFromPair = null;
        pairServer.PacketReceivedRaw += p => receivedFromPair = p.ToArray();
        pairClient.Send(CtosMessage.Surrender);
        pairServer.Update();
        AssertTrue(receivedFromPair is not null);
        AssertSequenceEqual(new byte[] { (byte)CtosMessage.Surrender }, receivedFromPair!);
        pairClient.Close();
    }

}
