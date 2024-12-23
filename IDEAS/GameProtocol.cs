using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

public enum GameMove { Rock,Paper ,Scissors}
public enum GameResults { Win, Lose, Draw}
public class GameMessage

{
    public string Type { get; set; }
    public string Content { get; set; }
    public GameMove? PlayerMove { get; set; }  
    public int? Score { get; set; }
}

