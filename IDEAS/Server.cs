using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

public class GameServer
{
    private TcpListener listener;
    private Dictionary<string, int> topScores = new Dictionary<string, int>();
    private List<Task> activeGames = new List<Task>();
    private object scoreLock = new object();
    private object playerName;

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

            activeGames.RemoveAll(t => t.IsCompleted);
        }
    }
    private async Task HandleClientAsync(TcpClient client)
    {
        using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(stream))
        using (var writer = new StreamWriter(stream) { AutoFlush = true })
        {
            string playerName = await reader.ReadLineAsync();
            Console.WriteLine($"Player {playerName} joined the game");

            while (true)
            {
                try
                {
                    string messageJson = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(messageJson)) break;

                    var message = JsonSerializer.Deserialize<GameMessage>(messageJson);
                    Console.WriteLine($"Received move from {playerName}: {message.PlayerMove}");
                }
                

           }
        }
    }
}
