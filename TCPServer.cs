using Microsoft.Extensions.Hosting;
using Sodium;
using System.Net;
using System.Net.Sockets;
namespace OpenCrossoutProtocol;

/// <summary>
/// Класс реализующий создание базового TCP сервера для работы клиента. 
/// [19.06.2025] Если ищете функции по типу отправки то заглядывайте в ClientHandler
/// [12.09.2025] Реализация изменена на более подходящий IHostedService
/// </summary>
public class TcpServer : IHostedService, IDisposable
{
    private readonly int _port;
    private TcpListener _listener;
    private Task? _acceptLoopTask;

    private readonly List<ClientHandlerTcp> _clients = new List<ClientHandlerTcp>();
    private readonly ClientHandlerTcp.OnGetMessage _messageHandler;

    public delegate Task OnDisconnect(ClientHandlerTcp initial);
    private OnDisconnect _disconnectHandler;

    public delegate void ConnectClientAdd(ClientHandlerTcp client);
    public event ConnectClientAdd OnClientAdd;
    public delegate void DisconnectClientRemove(ClientHandlerTcp client);
    public event DisconnectClientRemove OnClientRemove;

    public KeyPair? _knownKeyPair = null;
    private readonly object _clientsLock = new();
    CancellationTokenSource _cts = new();

    /// <summary>
    /// Creates TCP server. To start use StartAsync()
    /// </summary>
    /// <param name="port">Net Port to access tcp server</param>
    /// <param name="handler">Task message_basic[]  function(ClientHandler client, message_basic InputMessage)</param>
    /// <param name="disconnectHandler">Обработчик дисконнекта. Task function(ClientHandler client)</param>
    /// <param name="knownSeed">32-байтный сид для создания известного публичного ключа. TCPServ._knownKeyPair</param>
    public TcpServer(int port, ClientHandlerTcp.OnGetMessage handler, OnDisconnect disconnectHandler, byte[]? knownSeed = null)
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
    //ihosted
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            _acceptLoopTask = AcceptLoop(_cts.Token);
            Logger.logl($"Server started on port {_port}", (byte)Logger.LogType.Base);
        }
        catch (Exception ex)
        {
            Logger.logl($"TCP server: {ex.Message}", (byte)Logger.LogType.Error);
            throw;
        }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var client = new ClientHandlerTcp(this, tcpClient, _messageHandler, (_knownKeyPair != null ? _knownKeyPair.PublicKey : null), (_knownKeyPair != null ? _knownKeyPair.PrivateKey : null));
                AddClient(client);
                _ = client.HandleConnectionAsync(ct); // Запуск без ожидания
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
    public void AddClient(ClientHandlerTcp client) //chatapi
    {
        OnClientAdd?.Invoke(client);
        lock (_clientsLock) _clients.Add(client);
    }

    public async void RemoveClient(ClientHandlerTcp client) //chatapi
    {
        await _disconnectHandler(client);
        OnClientRemove?.Invoke(client);
        lock (_clientsLock) _clients.Remove(client);
    }


    /// <summary>
    /// Stops the TCP server
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listener?.Stop();

        // Ожидаем завершения accept-цикла
        if (_acceptLoopTask != null)
            await _acceptLoopTask;

        // Отключаем всех клиентов
        lock (_clientsLock)
        {
            foreach (var client in _clients.ToList())
            {
                client.Dispose();
            }
            _clients.Clear();
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _listener?.Stop();
    }
}