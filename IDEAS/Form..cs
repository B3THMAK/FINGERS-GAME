using System;
using System.Drawing;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GameClient
{
    public partial class GameForm : Form
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _serverIp = "127.0.0.1"; // Change to match server IP.
        private int _serverPort = 5000;         // Change to match server port.
        private string _playerName;

        public GameForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Rock, Paper, Scissors Game";
            this.ClientSize = new Size(500, 400);

            Label lblStatus = new Label
            {
                Text = "Status: Disconnected",
                Location = new Point(20, 20),
                AutoSize = true,
                Name = "lblStatus"
            };
            this.Controls.Add(lblStatus);

            Button btnConnect = new Button
            {
                Text = "Connect to Server",
                Location = new Point(20, 60),
                AutoSize = true
            };
            btnConnect.Click += async (sender, e) =>
            {
                _playerName = PromptForName();
                if (string.IsNullOrWhiteSpace(_playerName)) return;

                if (await ConnectToServer())
                {
                    lblStatus.Text = "Status: Connected to Server";
                    await SendMessageAsync(new GameMessage { Type = "PlayerJoin", Content = _playerName });
                    await StartListening();
                }
                else
                {
                    lblStatus.Text = "Status: Connection Failed";
                }
            };
            this.Controls.Add(btnConnect);

            Button btnRock = new Button
            {
                Text = "Rock",
                Location = new Point(20, 120),
                AutoSize = true
            };
            btnRock.Click += async (sender, e) => await SendMove(GameClient.GameMove.Rock);
            this.Controls.Add(btnRock);

            Button btnPaper = new Button
            {
                Text = "Paper",
                Location = new Point(120, 120),
                AutoSize = true
            };
            btnPaper.Click += async (sender, e) => await SendMove(GameClient.GameMove.Paper);
            this.Controls.Add(btnPaper);

            Button btnScissors = new Button
            {
                Text = "Scissors",
                Location = new Point(220, 120),
                AutoSize = true
            };
            btnScissors.Click += async (sender, e) => await SendMove(GameClient.GameMove.Scissors);
            this.Controls.Add(btnScissors);
        }

        private async Task<bool> ConnectToServer()
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_serverIp, _serverPort);
                _stream = _client.GetStream();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task StartListening()
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (_client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string json = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        GameMessage message = JsonSerializer.Deserialize<GameMessage>(json);

                        if (message != null)
                        {
                            if (message.Type == "Result")
                            {
                                Invoke(new Action(() =>
                                {
                                    var lblResult = this.Controls["lblResult"] as Label;
                                    lblResult.Text = $"Result: {message.Content}";
                                }));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private async Task SendMove(GameMove move)
        {
            await SendMessageAsync(new GameMessage { Type = "Move", PlayerMove = move, Content = move.ToString() });
        }

        private async Task SendMessageAsync(GameMessage message)
        {
            try
            {
                string json = JsonSerializer.Serialize(message);
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                await _stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending message: {ex.Message}");
            }
        }

        private string PromptForName()
        {
            using (Form inputForm = new Form())
            {
                inputForm.Text = "Enter Your Name";
                inputForm.Width = 300;
                inputForm.Height = 150;

                TextBox textBox = new TextBox { Left = 20, Top = 20, Width = 240 };
                Button okButton = new Button { Text = "OK", Left = 20, Top = 50, Width = 100, DialogResult = DialogResult.OK };

                inputForm.Controls.Add(textBox);
                inputForm.Controls.Add(okButton);
                inputForm.AcceptButton = okButton;

                if (inputForm.ShowDialog() == DialogResult.OK)
                {
                    return textBox.Text.Trim();
                }
                return null;
            }
        }
    }
}
