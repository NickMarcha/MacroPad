using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MacroPad.App;

/// <summary>
/// One MacroPad per user. A second start hands its command line to the running one and exits, so
/// <c>macropad --profile next</c> works from a desktop shortcut, a script or a keyboard shortcut. A named pipe on
/// Windows, a Unix socket on Linux (.NET picks).
/// </summary>
public static class SingleInstance
{
    static readonly string PipeName = "MacroPad-" + Environment.UserName;

    /// <summary>Passes the arguments to a running MacroPad. False when none is running.</summary>
    public static bool TrySendToRunning(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(300);
            using var writer = new StreamWriter(client);
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for later starts and calls <paramref name="received"/> with their arguments (on a pool thread).</summary>
    public static void Listen(Action<string[]> received, CancellationToken stop)
    {
        Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    await server.WaitForConnectionAsync(stop);
                    using var reader = new StreamReader(server);
                    var text = await reader.ReadToEndAsync(stop);
                    received(text.Length == 0 ? [] : text.Split('\n'));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, stop);
    }
}
