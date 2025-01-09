using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

public class GameServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly ConcurrentDictionary<string, int> leaderboard = new();
    private readonly ConcurrentQueue<(TcpClient client, string name)> waitingPlayers = new();
    private readonly CancellationTokenSource shutdownToken = new();
    private readonly SemaphoreSlim connectionLimiter;
    private readonly int maxConnections = 100;
    private readonly int roundTime = 15;
    private bool isDisposed;

    public GameServer(int port = 5000)
    {
        listener = new TcpListener(IPAddress.Any, port);
        connectionLimiter = new SemaphoreSlim(maxConnections);
    }

    public async Task StartAsync(CancellationToken externalCancellation = default)
    {
        try
        {
            // Create a linked token that combines our internal shutdown token with the external one
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                shutdownToken.Token,
                externalCancellation);

            listener.Start();
            Console.WriteLine("Server started. Waiting for connections...");

            while (!linkedCts.Token.IsCancellationRequested)
            {
                if (!await connectionLimiter.WaitAsync(1000, linkedCts.Token))
                    continue;

                try
                {
                    var acceptTask = listener.AcceptTcpClientAsync();
                    if (await Task.WhenAny(acceptTask, Task.Delay(30000)) == acceptTask)
                    {
                        var client = await acceptTask;
                        _ = HandleNewConnectionAsync(client, linkedCts.Token)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                    Console.WriteLine($"Connection handler failed: {t.Exception}");
                            }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    else
                    {
                        // Timeout occurred
                        connectionLimiter.Release();
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Error accepting client: {ex.Message}");
                    connectionLimiter.Release();
                    continue;
                }
                catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
                {
                    connectionLimiter.Release();
                    break;
                }
            }
        }
        finally
        {
            await ShutdownAsync();
        }
    }


    private async Task HandleNewConnectionaAsync(TcpClient client)
    {

        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;

            // Receive initial player name
            var playerName = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(playerName) || playerName.Length > 50)
                return;

            Console.WriteLine($"Player {playerName} has joined!");
            leaderboard.TryAdd(playerName, 0);

            // Send waiting message
            await SendGameMessageAsync(writer, new GameMessage
            {
                Type = "WAITING",
                Content = "Waiting for an opponent..."
            });

            await MatchmakePlayerAsync(client, playerName, reader, writer);
        }
        finally
        {
            connectionLimiter.Release();
            client.Dispose();
        }
    }

    public async Task ShutdownAsync()
    {
        if (isDisposed)
            return;

        shutdownToken.Cancel();
        listener.Stop();

        while (waitingPlayers.TryDequeue(out var player))
        {
            player.client.Dispose();
        }

        connectionLimiter.Dispose();
        shutdownToken.Dispose();
        isDisposed = true;
    }

    public void Dispose()
    {
        ShutdownAsync().GetAwaiter().GetResult();
    }

    internal async Task Start()
    {
        throw new NotImplementedException();
    }

}
