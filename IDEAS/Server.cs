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

    private async Task HandleNewConnectionAsync(TcpClient client, CancellationToken token)

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
    private async Task MatchmakePlayerAsync(TcpClient client, string playerName,
       StreamReader reader, StreamWriter writer)
    {
        if (waitingPlayers.TryDequeue(out var opponent))
        {
            if (opponent.client.Connected)
            {
                await StartMatchAsync(
                    (client, reader, writer, playerName),
                    opponent);
                return;
            }
        }

        waitingPlayers.Enqueue((client, playerName));
    }
    private async Task StartMatchAsync(
       (TcpClient client, StreamReader reader, StreamWriter writer, string name) player1,
       (TcpClient client, string name) player2)
    {
        using var stream2 = player2.client.GetStream();
        using var reader2 = new StreamReader(stream2);
        using var writer2 = new StreamWriter(stream2) { AutoFlush = true };

        try
        {
            while (!shutdownToken.Token.IsCancellationRequested)
            {
                // Request moves from both players
                var moveRequest = new GameMessage
                {
                    Type = "MOVE_REQUEST",
                    Content = $"Make your move within {roundTime} seconds."
                };

                await Task.WhenAll(
                    SendGameMessageAsync(player1.writer, moveRequest),
                    SendGameMessageAsync(writer2, moveRequest)
                );

                // Collect moves with timeout
                var moves = await CollectMovesAsync(player1, (player2.client, reader2, writer2, player2.name));
                if (!moves.HasValue)
                    break;

                var (move1, move2) = moves.Value;
                var result = DetermineResult(move1, move2);

                // Update scores and send results
                await ProcessRoundResultAsync(
                    player1.name, player1.writer, move1, move2, result,
                    player2.name, writer2);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Match error: {ex.Message}");
        }
    }
    private async Task<(GameMove move1, GameMove move2)?> CollectMovesAsync(
        (TcpClient client, StreamReader reader, StreamWriter writer, string name) player1,
        (TcpClient client, StreamReader reader, StreamWriter writer, string name) player2)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(roundTime));

        try
        {
            var move1Task = GetMoveAsync(player1.reader);
            var move2Task = GetMoveAsync(player2.reader);

            await Task.WhenAll(move1Task, move2Task);
            return (move1Task.Result, move2Task.Result);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
    private async Task<GameMove> GetMoveAsync(StreamReader reader)
    {
        var moveJson = await reader.ReadLineAsync();
        var moveMessage = JsonSerializer.Deserialize<GameMessage>(moveJson);

        if (moveMessage?.PlayerMove == null)
            throw new InvalidOperationException("Invalid move received");

        return moveMessage.PlayerMove.Value;
    }

    private GameResults DetermineResult(GameMove move1, GameMove move2)
    {
        if (move1 == move2)
            return GameResults.Draw;

        return (move1, move2) switch
        {
            (GameMove.Rock, GameMove.Scissors) => GameResults.Win,
            (GameMove.Paper, GameMove.Rock) => GameResults.Win,
            (GameMove.Scissors, GameMove.Paper) => GameResults.Win,
            _ => GameResults.Lose
        };
    }
    private async Task ProcessRoundResultAsync(
        string player1Name, StreamWriter writer1, GameMove move1, GameMove move2,
        GameResults result, string player2Name, StreamWriter writer2)
    {
        // Update leaderboard
        if (result == GameResults.Win)
            leaderboard.AddOrUpdate(player1Name, 1, (_, score) => score + 1);
        else if (result == GameResults.Lose)
            leaderboard.AddOrUpdate(player2Name, 1, (_, score) => score + 1);

        // Send results to players
        await SendGameMessageAsync(writer1, new GameMessage
        {
            Type = "RESULT",
            Content = $"You played {move1}, opponent played {move2}. {result}!",
            PlayerMove = move1,
            Score = leaderboard[player1Name]
        });

        var player2Result = result switch
        {
            GameResults.Win => GameResults.Lose,
            GameResults.Lose => GameResults.Win,
            _ => GameResults.Draw
        };

        await SendGameMessageAsync(writer2, new GameMessage
        {
            Type = "RESULT",
            Content = $"You played {move2}, opponent played {move1}. {player2Result}!",
            PlayerMove = move2,
            Score = leaderboard[player2Name]
        });
    }

    private static async Task SendGameMessageAsync(StreamWriter writer, GameMessage message)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));
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
