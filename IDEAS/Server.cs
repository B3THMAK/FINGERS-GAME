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
    private async Task HandleNewConnection(TcpClient client)
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


}      

