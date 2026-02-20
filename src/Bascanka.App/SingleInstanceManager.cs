using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Bascanka.App;

/// <summary>
/// Manages single-instance behavior using a global mutex and named pipes.
/// Only one Bascanka process may run at a time. Subsequent launches forward
/// their file arguments to the existing instance and exit.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Global\\Bascanka_SingleInstance";
    private const string PipeName = "Bascanka_Pipe";
    private const int ConnectTimeoutMs = 1000;

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns <c>true</c> if this is the first instance (ownership acquired).
    /// Returns <c>false</c> if another instance already owns the mutex.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the mutex — release our handle.
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }

    /// <summary>
    /// Signals an already-running Bascanka instance.
    /// If <paramref name="files"/> contains paths they are sent for opening;
    /// otherwise a simple activate signal is sent.
    /// Returns <c>true</c> if the signal was delivered (caller should exit).
    /// Returns <c>false</c> if no instance is listening.
    /// </summary>
    public static bool TrySignalExisting(string[] files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            // An empty payload means "just activate the window".
            string payload = files.Length > 0 ? string.Join("\n", files) : "";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            client.Write(data, 0, data.Length);
            client.Flush();
            return true;
        }
        catch
        {
            // No server listening or connection failed.
            return false;
        }
    }

    /// <summary>
    /// Starts a background named pipe server that listens for file paths
    /// from other Bascanka instances. The callback is invoked (on a background thread)
    /// with the received file paths.
    /// </summary>
    public void StartListening(Action<string[]> onFilesReceived)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _listenTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string content = await reader.ReadToEndAsync(token);

                    string[] files = content
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Always invoke: empty array means "just activate the window".
                    onFilesReceived(files);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Transient error; retry after a short delay.
                    try { await Task.Delay(100, token); }
                    catch (OperationCanceledException) { break; }
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();

        // Unblock WaitForConnectionAsync by connecting and immediately closing.
        try
        {
            using var dummy = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            dummy.Connect(100);
        }
        catch
        {
            // Ignore — server may have already stopped.
        }

        try { _listenTask?.Wait(500); }
        catch { /* ignore */ }

        _cts?.Dispose();

        // Release the single-instance mutex.
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
