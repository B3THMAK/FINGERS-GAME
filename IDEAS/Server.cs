using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RockPaperScissorsServer
{
    class Program
    {
        private static readonly ConcurrentDictionary<TcpClient, Player> ConnectedPlayers = new();
        private static readonly List<KeyValuePair<string, int>> Leaderboard = new();
        private static readonly Random Random = new();

        static async Task Main(string[] args)
        {
            TcpListener server = new TcpListener(IPAddress.Any, 5000);
            server.Start();
            Console.WriteLine("Server started on port 5000...");

            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                Console.WriteLine("New client connected.");
                _ = HandleClient(client);
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) { AutoFlush = true };

            Player player = null;

            try
            {
                while (client.Connected)
                {
                    string messageJson = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(messageJson)) break;

                    var message = JsonSerializer.Deserialize<GameMessage>(messageJson);

                    switch (message.Type)
                    {
                        case "INIT":
                            player = new Player
                            {
                                Name = message.PlayerName,
                                IsVsComputer = message.Content == "COMPUTER",
                                Client = client,
                                Writer = writer,
                                Score = 0
                            };
                            ConnectedPlayers.TryAdd(client, player);
                            Console.WriteLine($"Player '{player.Name}' joined the game.");
                            if (player.IsVsComputer)
                            {
                                await StartGameVsComputer(player);
                            }
                            else
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
                                {
                                    Type = "WAITING",
                                    Content = "Waiting for another player..."
                                }));
                            }
                            break;

                        case "MOVE":
                            player.Move = message.PlayerMove;
                            if (player.IsVsComputer)
                            {
                                await ProcessGameVsComputer(player);
                            }
                            else
                            {
                                var opponent = ConnectedPlayers.Values.FirstOrDefault(p =>
                                    p != player && p.Opponent == player);
                                if (opponent != null && opponent.Move.HasValue)
                                {
                                    await ProcessGameVsPlayer(player, opponent);
                                }
                            }
                            break;

                        case "LEADERBOARD_REQUEST":
                            await writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
                            {
                                Type = "LEADERBOARD",
                                Leaderboard = Leaderboard
                            }));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
            finally
            {
                if (player != null)
                {
                    ConnectedPlayers.TryRemove(client, out _);
                    Console.WriteLine($"Player '{player.Name}' disconnected.");
                }
                client.Close();
            }
        }

        private static async Task StartGameVsComputer(Player player)
        {
            await player.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "MOVE_REQUEST",
                Content = "Your turn!"
            }));
        }

        private static async Task ProcessGameVsComputer(Player player)
        {
            GameMove computerMove = (GameMove)Random.Next(0, 3);
            string resultMessage = GetGameResult(player.Move.Value, computerMove, out bool playerWins);

            if (playerWins) player.Score++;

            await player.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "RESULT",
                Content = $"You chose {player.Move}, Computer chose {computerMove}. {resultMessage}",
                Score = player.Score,
                OpponentScore = player.Score // For single-player, opponent score can mirror player score
            }));

            Leaderboard.Add(new KeyValuePair<string, int>(player.Name, player.Score));
        }

        private static async Task ProcessGameVsPlayer(Player player1, Player player2)
        {
            string result1 = GetGameResult(player1.Move.Value, player2.Move.Value, out bool player1Wins);
            string result2 = player1Wins ? "You lose!" : "You win!";

            if (player1Wins)
            {
                player1.Score++;
            }
            else if (!player1Wins && player1.Move.Value != player2.Move.Value)
            {
                player2.Score++;
            }

            // Send results to both players
            await player1.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "RESULT",
                Content = result1,
                Score = player1.Score,
                OpponentScore = player2.Score
            }));

            await player2.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "RESULT",
                Content = result2,
                Score = player2.Score,
                OpponentScore = player1.Score
            }));

            // Reset moves for next round
            player1.Move = null;
            player2.Move = null;

            // Prompt for the next move
            await player1.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "MOVE_REQUEST",
                Content = "Your turn!"
            }));

            await player2.Writer.WriteLineAsync(JsonSerializer.Serialize(new GameMessage
            {
                Type = "MOVE_REQUEST",
                Content = "Your turn!"
            }));
        }

        private static string GetGameResult(GameMove playerMove, GameMove opponentMove, out bool playerWins)
        {
            if (playerMove == opponentMove)
            {
                playerWins = false;
                return "It's a tie!";
            }

            if ((playerMove == GameMove.Rock && opponentMove == GameMove.Scissors) ||
                (playerMove == GameMove.Paper && opponentMove == GameMove.Rock) ||
                (playerMove == GameMove.Scissors && opponentMove == GameMove.Paper))
            {
                playerWins = true;
                return "You win!";
            }

            playerWins = false;
            return "You lose!";
        }

        private class Player
        {
            public string Name { get; set; }
            public bool IsVsComputer { get; set; }
            public TcpClient Client { get; set; }
            public StreamWriter Writer { get; set; }
            public GameMove? Move { get; set; }
            public int Score { get; set; }
            public Player Opponent { get; set; }
        }

        public class GameMessage
        {
            public string Type { get; set; }
            public string Content { get; set; }
            public GameMove? PlayerMove { get; set; }
            public int? Score { get; set; }
            public int? OpponentScore { get; set; }
            public string PlayerName { get; set; }
            public List<KeyValuePair<string, int>> Leaderboard { get; set; }
        }

        public enum GameMove
        {
            Rock,
            Paper,
            Scissors
        }
    }
}
