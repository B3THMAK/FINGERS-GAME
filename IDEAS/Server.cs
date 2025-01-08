using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

public class GameServer
{
    private TcpListener listener;
    private Dictionary<string, int> leaderboard = new Dictionary<string, int>();
    private Queue<TcpClient> waitingPlayers = new Queue<TcpClient>();
    private object leaderboardLock = new object();
    private int roundTime = 15; // Time limit for each round in seconds
    public GameServer(int port = 5000)
    {
        listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task Start()
    {
        listener.Start();
        Console.WriteLine("Server started. Waiting for connections...");

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Console.WriteLine("New client connected!");
            HandleNewConnection(client);
        }
    }
    private void HandleNewConnection(TcpClient client)
    {
        Task.Run(async () =>
        {
            using (NetworkStream stream = client.GetStream())
            using (var reader = new StreamReader(stream))
            using (var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                string playerName = await reader.ReadLineAsync();
                Console.WriteLine($"Player {playerName} joined the game");
                lock (leaderboardLock)

                {
                    if (!leaderboard.ContainsKey(playerName))
                        leaderboard[playerName] = 0;
                }

                // Add player to matchmaking queue
                lock (waitingPlayers)
                {
                    if (waitingPlayers.Count > 0)
                    {
                        var opponent = waitingPlayers.Dequeue();
                        StartMatch(client, opponent, playerName);
                    }
                    else
                    {
                        waitingPlayers.Enqueue(client);
                        await writer.WriteLineAsync("Waiting for an opponent...");
                    }
                }
            }
        });
    }

     private async void StartMatch(TcpClient client1, TcpClient client2, string player1Name)
    {
        Console.WriteLine($"Starting a match between {player1Name} and a new opponent!");

        using (NetworkStream stream1 = client1.GetStream())
        using (NetworkStream stream2 = client2.GetStream())
        using (var reader1 = new StreamReader(stream1))
        using (var writer1 = new StreamWriter(stream1) { AutoFlush = true })
        using (var reader2 = new StreamReader(stream2))
        using (var writer2 = new StreamWriter(stream2) { AutoFlush = true })
        {
            string player2Name = await reader2.ReadLineAsync();
            Console.WriteLine($"Player {player2Name} joined {player1Name} in a match!");

            while (true)
            {
                var moves = await CollectMoves(reader1, reader2, writer1, writer2, player1Name, player2Name);
                if (moves == null) break; // One or both players disconnected

                var (player1Move, player2Move) = moves.Value;
                var results = DetermineResults(player1Move, player2Move);

                // Update leaderboard
                lock (leaderboardLock)
                {
                    leaderboard[player1Name] += results.player1Score;
                    leaderboard[player2Name] += results.player2Score;
                }

                // Notify players
                await writer1.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
                {
                    Type = "RESULT",
                    Content = $"You played {player1Move}, opponent played {player2Move}. {results.player1Message}",
                    Score = leaderboard[player1Name]
                }));

                await writer2.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
                {
                    Type = "RESULT",
                    Content = $"You played {player2Move}, opponent played {player1Move}. {results.player2Message}",
                    Score = leaderboard[player2Name]
                }));

                // Broadcast leaderboard
                BroadcastLeaderboard();
            }

            Console.WriteLine($"{player1Name} and {player2Name} match ended!");
        }
    }
    private async Task<(GameMove, GameMove)?> CollectMoves(StreamReader reader1, StreamReader reader2, StreamWriter writer1, StreamWriter writer2, string player1Name, string player2Name)
    {
        var moveTasks = new[]
        {
            CollectMove(reader1, writer1, player1Name),
            CollectMove(reader2, writer2, player2Name)
        };

        if (await Task.WhenAll(moveTasks) is [GameMove ? move1, GameMove ? move2])
        {
            return (move1.Value, move2.Value);
        }

        return null; // One or both players timed out/disconnected
    }

    private async Task<GameMove?> CollectMove(StreamReader reader, StreamWriter writer, string playerName)
    {
        try
        {
            await writer.WriteLineAsync($"Round starting! Make your move within {roundTime} seconds.");
            var moveTask = reader.ReadLineAsync();
            if (await Task.WhenAny(moveTask, Task.Delay(roundTime * 1000)) == moveTask)
            {
                string move = moveTask.Result;
                return Enum.TryParse<GameMove>(move, true, out var parsedMove) ? parsedMove : (GameMove?)null;
            }

            await writer.WriteLineAsync("You took too long! You forfeit this round.");
        }
        catch
        {
            Console.WriteLine($"{playerName} disconnected.");
        }

        return null;
    }
}      

