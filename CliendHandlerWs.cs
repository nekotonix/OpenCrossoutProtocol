namespace OpenCrossoutProtocol;
using System.Net.WebSockets;

public class ClientHandlerWebSocket : ClientHandlerBase
{
    private readonly WebSocket _webSocket;
    private readonly WebSocketServer _server;
    public Guid Id { get; } = Guid.NewGuid();

    public ClientHandlerWebSocket(WebSocketServer server, WebSocket webSocket,
        OnGetMessage messageHandler, byte[]? pubKey = null, byte[]? privKey = null)
        : base(messageHandler, pubKey, privKey)
    {
        _server = server;
        _webSocket = webSocket;
    }

    public override async Task HandleConnectionAsync(CancellationToken ct)
    {
        _ct = ct;
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested &&
                   _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                clientCounter += 1;
                await ProcessRequest(buffer, result.Count, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.logl($"WebSocket error: {ex}", (byte)Logger.LogType.Error);
        }
        finally
        {
            _server.RemoveClient(this);
            wantDisconnect = true;
            _webSocket.Dispose();
        }
    }

    protected override async Task SendResponseAsync(byte[] data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            Logger.logl($"SEND: {BitConverter.ToString(data)}", (byte)Logger.LogType.Response);
            await _webSocket.SendAsync(new ArraySegment<byte>(data),
                WebSocketMessageType.Binary, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async Task<bool> SendMessage(message_basic message, bool insertAsStart = false)
    {
        try
        {
            if (isRespDone)
            {
                isRespDone = false;
                serverCounter += 1;
                byte[]? nextMessage = cryptoService.cryptSingleMessageClient(serverCounter, message);
                if (nextMessage == null) return false;

                await SendResponseAsync(nextMessage, _ct ?? default);
                isRespDone = true;
            }
            else
            {
                if (insertAsStart)
                    undoneStartMessagesPool.Add(message);
                else
                    undoneMessagesPool.Add(message);
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.logl($"SendMessage Failed: {ex}", (byte)Logger.LogType.Warning);
            return false;
        }
    }

    public override void Dispose()
    {
        wantDisconnect = true;
        _webSocket?.Dispose();
    }

    protected override async Task ProcessRequest(byte[] data, int length, CancellationToken ct)
    {
        // Реализация идентична ClientHandlerTcp
        // ... (код из оригинального ProcessRequest)
    }
}
