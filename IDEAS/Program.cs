
using System;
using System.Threading;
using System.Threading.Tasks;
class Program
{
    static async Task Main()
    {
        Console.WriteLine("Starting Rock Paper Scissors Server...");
        using var server = new GameServer();
        using var cts = new CancellationTokenSource();

        // Setup shutdown handling
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            cts.Cancel();
        };

        try
        {

            var serverTask = server.StartAsync(cts.Token);

            Console.WriteLine("Server is running. Press Ctrl+C to shut down.");


            await serverTask;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            Console.WriteLine("\nShutdown requested. Stopping server gracefully...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server encountered an error: {ex.Message}");
            throw;
        }
        finally
        {
            await server.ShutdownAsync();
            Console.WriteLine("Server stopped.");
        }
    }
}