
// Program.cs
class Program
{
    static async Task Main()
    {
        Console.WriteLine("Starting Rock Paper Scissors Server...");
        var server = new GameServer();
        await server.Start();
    }
}