using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Sodium;
using System.Net;
namespace OpenCrossoutProtocol;

//[BETA]
public class WebSocketServer : IHostedService, IDisposable
{
    private readonly int _port;
    private HttpListener? _listener;
    private Task? _acceptLoopTask;
    private readonly ConcurrentDictionary<Guid, ClientHandlerWebSocket> _clients = new();
    private readonly ClientHandlerBase.OnGetMessage _messageHandler;
    public delegate Task OnDisconnect(ClientHandlerWebSocket initial);
    private OnDisconnect _disconnectHandler;
    private CancellationTokenSource _cts = new();

    public KeyPair? _knownKeyPair = null;

    public delegate void ConnectClientAdd(ClientHandlerBase client);
    public static event ConnectClientAdd OnClientAdd;
    public delegate void DisconnectClientRemove(ClientHandlerBase client);
    public static event DisconnectClientRemove OnClientRemove;

    public WebSocketServer(int port, ClientHandlerBase.OnGetMessage handler,
        WebSocketServer.OnDisconnect disconnectHandler, byte[]? knownSeed = null)
    {
        _port = port;
        _messageHandler = handler;
        _disconnectHandler = disconnectHandler;

        if (knownSeed != null)
        {
            _knownKeyPair = PublicKeyAuth.GenerateKeyPair(knownSeed);
            Logger.logl("Known server public key (insert into executable):");
            Logger.loghexl(_knownKeyPair.PublicKey);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{_port}/");
        _listener.Start();

        _acceptLoopTask = AcceptLoop(_cts.Token);
        Logger.logl($"WebSocket server started on port {_port}", (byte)Logger.LogType.Base);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    var client = new ClientHandlerWebSocket(this, webSocketContext.WebSocket,
                        _messageHandler,
                        _knownKeyPair?.PublicKey,
                        _knownKeyPair?.PrivateKey);

                    AddClient(client);
                    _ = client.HandleConnectionAsync(ct);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.logl($"Accept error: {ex.Message}", (byte)Logger.LogType.Error);
            }
        }
    }

    private void AddClient(ClientHandlerWebSocket client)
    {
        _clients[client.Id] = client;
        OnClientAdd?.Invoke(client);
    }

    public async void RemoveClient(ClientHandlerWebSocket client)
    {
        if (_clients.TryRemove(client.Id, out _))
        {
            await _disconnectHandler(client);
            OnClientRemove?.Invoke(client);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listener?.Stop();

        if (_acceptLoopTask != null)
            await _acceptLoopTask;

        foreach (var client in _clients.Values.ToList())
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _listener?.Close();
    }
}