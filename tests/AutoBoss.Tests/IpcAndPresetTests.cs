using Xunit;
using AutoBossShared;
using AutoBossManager.Services;
using Newtonsoft.Json;
using System.Diagnostics;

namespace AutoBoss.Tests;

/// <summary>
/// IPC reliability integration test (spec task 27.3):
/// Server + raw TcpClient mo phong plugin, do latency cua COMMAND->ACK
/// qua 30 vong, muc tieu <50ms (loopback thuc te ~1ms).
/// </summary>
[Collection("PluginEnv")]
public class IpcReliabilityTests
{
    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    [Fact]
    public async Task Server_CommandAckRoundTrip_Under50msAverage()
    {
        var originalPort = IpcConfig.ServerPort;
        try
        {
            IpcConfig.ServerPort = FreePort();
            using var server = new SocketServer();
            server.Start();

            var statusReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.OnStatusUpdate += (_, _) => statusReceived.TrySetResult(true);

            var clientIdReceived = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.OnClientConnected += (_, e) => clientIdReceived.TrySetResult(e.InstanceId);

            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(IpcConfig.ServerHost, IpcConfig.ServerPort);
            var stream = client.GetStream();
            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            var writerLock = new object();

            // Background reader: when server sends COMMAND, reply with ACK
            var cts = new System.Threading.CancellationTokenSource();
            var readerTask = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;
                        var msg = JsonConvert.DeserializeObject<IpcMessage>(line);
                        if (msg?.Type == MessageTypes.COMMAND)
                        {
                            var ack = new IpcMessage(MessageTypes.ACK);
                            if (msg.Payload.TryGetValue("command", out var cmdObj))
                                ack.Payload["acknowledgedType"] = cmdObj.ToString();
                            else
                                ack.Payload["acknowledgedType"] = "";
                            lock (writerLock)
                            {
                                writer.WriteLine(JsonConvert.SerializeObject(ack));
                            }
                        }
                    }
                }
                catch { }
            });

            // Heartbeat sender to keep connection alive
            var heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(2000, cts.Token);
                        lock (writerLock)
                        {
                            writer.WriteLine(JsonConvert.SerializeObject(new IpcMessage(MessageTypes.HEARTBEAT)));
                        }
                    }
                }
                catch { }
            });

            // 1) Client gui STATUS_UPDATE -> server phai nhan duoc
            var hello = new IpcMessage(MessageTypes.STATUS_UPDATE);
            hello.Payload["state"] = "Idle";
            hello.Payload["map"] = "TestMap";
            await writer.WriteLineAsync(JsonConvert.SerializeObject(hello));
            var gotStatus = await Task.WhenAny(statusReceived.Task, Task.Delay(3000)) == statusReceived.Task;
            Assert.True(gotStatus, "Server khong nhan STATUS_UPDATE");

            // Wait for client ID
            var connectedId = await Task.WhenAny(clientIdReceived.Task, Task.Delay(3000)) == clientIdReceived.Task
                ? await clientIdReceived.Task
                : Guid.Empty;
            Assert.NotEqual(Guid.Empty, connectedId);

            // 2) N vong COMMAND -> ACK, do latency via server's OnAckReceived
            const int rounds = 30;
            var latencies = new double[rounds];
            for (int i = 0; i < rounds; i++)
            {
                var ackTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void AckHandler(object? s, AckEventArgs e)
                {
                    if (e.InstanceId == connectedId)
                        ackTcs.TrySetResult(e.AckedType);
                }
                server.OnAckReceived += AckHandler;
                try
                {
                    var sw = Stopwatch.StartNew();
                    server.SendCommand(connectedId, Commands.START_FARMING);

                    var completed = await Task.WhenAny(ackTcs.Task, Task.Delay(3000));
                    Assert.True(completed == ackTcs.Task, $"Round {i}: ACK not received within timeout");

                    sw.Stop();
                    latencies[i] = sw.Elapsed.TotalMilliseconds;
                    Assert.Equal(Commands.START_FARMING, await ackTcs.Task);
                }
                finally
                {
                    server.OnAckReceived -= AckHandler;
                }
            }

            cts.Cancel();
            var avg = latencies.Average();
            var max = latencies.Max();
            Assert.True(avg < 50, $"Avg latency {avg:F1}ms >= 50ms (max={max:F1})");
        }
        finally
        {
            IpcConfig.ServerPort = originalPort;
        }
    }
}

/// <summary>Strategy preset apply tests (task 19.4).</summary>
[Collection("PluginEnv")]
public class StrategyPresetTests
{
    [Fact]
    public void Apply_Aggressive_SetsFastSettings()
    {
        var p = new BotProfile();
        global::AutoBossManager.Services.StrategyPresetManager.Apply(p, global::AutoBossManager.Services.StrategyPresetManager.Aggressive);

        Assert.Equal(StrategyPreset.Aggressive, p.Strategy);
        Assert.Equal(15, p.MaxZoneAttempts);
        Assert.Equal(45f, p.CombatTimeoutSec);
        Assert.Equal(2, p.RandomizationIntensity);
    }

    [Fact]
    public void Apply_Safe_SetsConservativeSettings()
    {
        var p = new BotProfile();
        global::AutoBossManager.Services.StrategyPresetManager.Apply(p, global::AutoBossManager.Services.StrategyPresetManager.Safe);

        Assert.Equal(20, p.MaxZoneAttempts);
        Assert.Equal(90f, p.CombatTimeoutSec);
        Assert.Equal(30f, p.RetreatHpPct);
        Assert.Equal(0, p.RandomizationIntensity);
    }

    [Fact]
    public void Apply_PreservesIdentity_Fields()
    {
        var p = new BotProfile { AccountName = "tester", Password = "secret" };
        global::AutoBossManager.Services.StrategyPresetManager.Apply(p, global::AutoBossManager.Services.StrategyPresetManager.Balanced);

        Assert.Equal("tester", p.AccountName);
        Assert.Equal("secret", p.Password);
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndNullForUnknown()
    {
        Assert.NotNull(global::AutoBossManager.Services.StrategyPresetManager.Find("aggressive"));
        Assert.Null(global::AutoBossManager.Services.StrategyPresetManager.Find("khong ton tai"));
    }
}
