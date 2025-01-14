using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RockPaperScissorsClient
{
    public class GameProtocol
    {
        private TcpClient client;
        private NetworkStream stream;


        public async Task ConnectAsync()
        {
            client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 5000);
            stream = client.GetStream();
        }


        public async Task SendAsync(string type, string content = null)
        {
            var message = new GameMessage
            {
                Type = type,
                Content = content
            };

            var json = JsonSerializer.Serialize(message);
            var data = Encoding.UTF8.GetBytes(json);

            await stream.WriteAsync(data, 0, data.Length);
        }


        public async Task<GameMessage> ReceiveAsync()
        {
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return JsonSerializer.Deserialize<GameMessage>(json);
        }


        public void Disconnect()
        {
            stream?.Close();
            client?.Close();
        }
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

    public enum GameResult
    {
        Win,
        Lose,
        Draw
    }
}
