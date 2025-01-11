namespace GameClient
{
    public class GameMessage
    {
        public string Type { get; set; }     
        public string Content { get; set; }     
        public GameMove? PlayerMove { get; set; }
        public int? Score { get; set; }          
    }

    public enum GameMove
    {
        Rock,
        Paper,
        Scissors
    }

    public enum GameResults
    {
        Win,
        Lose,
        Draw
    }
}
