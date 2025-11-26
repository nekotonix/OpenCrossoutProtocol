using System.Net.Sockets;
namespace OpenCrossoutProtocol;

/// <summary>
/// Класс реализующий основную работу с клиентом.
/// </summary>
public class ClientHandlerTcp : ClientHandlerBase
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly TcpServer _tcpServer;

    /// <summary>
    /// Базовая схемка:
    /// хендшейк, ключи client.ed, client.x
    /// InitializeCryptoService()
    /// работа с данными
    /// </summary>
    public ClientHandlerTcp(TcpServer tcpServer, TcpClient client, OnGetMessage messageHandler, byte[]? pubKey = null, byte[]? privKey = null) : base(messageHandler, pubKey, privKey)
    {
        _tcpServer = tcpServer;
        _client = client;
        _stream = client.GetStream();
        _handler = messageHandler;

        cryptoService = new Crypto(pubKey, privKey);
    }

    /// <summary>
    /// do not use from your code!!
    /// </summary>
    public override async Task HandleConnectionAsync(CancellationToken ct)
    {
        _ct = ct;
        try
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                int bytesRead = 0;
                try
                {
                    if (wantDisconnect) break;
                    bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break;
                    //Logger.logl($"GET {string.Join(" ", BitConverter.ToString(buffer[..bytesRead]).Split('-'))}", (byte)Logger.LogType.Request);
                    clientCounter += 1;
                    await ProcessRequest(buffer, bytesRead, ct);
                }
                catch (OperationCanceledException) { }
                catch (System.IO.IOException)
                {
                    Logger.logl($"Client lost connection in result of critical", (byte)Logger.LogType.Error);
                }
                catch (Exception ex)
                {
                    Logger.logl($"CLIENTHANDLER {ex}", (byte)Logger.LogType.Error);
                }
            }
        }
        catch (Exception ex) { Logger.logl($"SERVER ERROR: {ex}", (byte)Logger.LogType.Error); }
        finally
        {
            _tcpServer.RemoveClient(this);
            wantDisconnect = true;
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
    protected override async Task SendResponseAsync(byte[] data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sent = string.Join(" ", BitConverter.ToString(data).Split('-'));
            Logger.logl($"SEND: {sent}", (byte)Logger.LogType.Response);

            _stream.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);

        }
        finally
        {
            _sendLock.Release();
        }
    }
    /// <inheritdoc/>
    public override void Dispose()
    {
        wantDisconnect = true;
        //[UPD] если мы сами отключаем клиент то вызывается finally которое само удаляет
        //_tcpServer.RemoveClient(this);
        _stream?.Dispose();
        _client?.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ProcessRequest(byte[] data, int length, CancellationToken ct)
    {
        isRespDone = false;
        List<message_basic> messages;
        ulong nonceChange = 0;
        byte[] dataFormatted = new byte[length];
        Array.Copy(data, dataFormatted, length);

        (messages, nonceChange) = cryptoService.decryptMultipleMessagesClient(clientCounter, dataFormatted);
        clientCounter = clientCounter + nonceChange;

        byte[] response_c = new byte[] { };
        foreach (message_basic msgDecrypted in messages)
        {
            Logger.loghexl(msgDecrypted.data,(byte)Logger.LogType.Request);
            if (msgDecrypted == null) continue; //Не выкидываем иначе может быть блябля
            message_basic[]? msgResponses = await _handler(this, msgDecrypted);
            if (wantDisconnect) return; //Может быть после вызова хандлера
            if (msgResponses == null) msgResponses = Array.Empty<message_basic>();

            //ТЕСТОВОЕ
            msgResponses = undoneStartMessagesPool.Concat(msgResponses).ToArray();
            undoneStartMessagesPool.Clear();

            foreach (message_basic msgResponse in msgResponses)
            {
                serverCounter += 1; //ТОООЛЬКО если сообщение составлено
                byte[] nextMessage = cryptoService.cryptSingleMessageClient(serverCounter, msgResponse);
                if (response_c.Length + nextMessage.Length > maxChunkSize)
                {
                    await SendResponseAsync(response_c, ct);
                    response_c = new byte[] { };
                }
                response_c = response_c.Concat(nextMessage).ToArray(); //Добавляем каждое обработанное сообщение в пул
            }
        }
        //await SendDisconnectAsync(0x31,ct);
        await SendResponseAsync(response_c, ct);
        isRespDone = true;
        for (int i = 0; i < undoneMessagesPool.Count; ++i)
        {
            await SendMessage(undoneMessagesPool[i]);
        }
        undoneMessagesPool.Clear();

        return;
    }

    private List<message_basic> undoneMessagesPool = new List<message_basic>();
    private List<message_basic> undoneStartMessagesPool = new List<message_basic>();

    /// <summary>
    /// Send no-response message to client
    /// </summary>
    /// <param name="message"></param>
    /// <param name="insertAsStart">false = wait for done previous request. true = send NOW (Use only in replicas that not in ghosting!!)</param>
    /// <returns></returns>
    public override async Task<bool> SendMessage(message_basic message, bool insertAsStart = false)
    {
        try
        {
            if (isRespDone)
            {
                isRespDone = false;
                serverCounter += 1;
                byte[]? nextMessage = cryptoService.cryptSingleMessageClient(serverCounter, message);
                if (nextMessage == null)
                {
                    Logger.logl("msg is null!", (byte)Logger.LogType.Warning);
                    return false;
                }
                if (_ct == null)
                {
                    Logger.logl("_ct is null!", (byte)Logger.LogType.Warning);
                    return false;
                }
                await SendResponseAsync(nextMessage, (CancellationToken)_ct);
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
            Logger.logl($"SendMessage Failed! Err: {ex}", (byte)Logger.LogType.Warning);
            return false;
        }
    }

    #region cryptoSafe
    public byte[] edClientPublicKey
    {
        set { cryptoService.ed25519_public_client = value; }
    }
    public byte[] xClientPublicKey
    {
        set { cryptoService.x25519_public_client = value; }
    }
    public byte[] edServerPublicKey
    {
        get { return cryptoService.GetServerPublicEd25519(); }
    }
    public byte[] xServerPublicKey
    {
        get { return cryptoService.GetServerPublicX25519(); }
    }
    public byte[] edClientSign
    {
        set { cryptoService.ed25519_client_sign = value; }
    }
    /// <summary>
    /// For handshake only. Server sign. Uses for client validation. Required to set edClientPublicKey before using
    /// </summary>
    public byte[] edSign()
    {
        return cryptoService.ed25519_sign(
            cryptoService.ed25519_public_client
            .Concat(cryptoService.GetServerPublicX25519())
            .ToArray()
        );
    }
    /// <summary>
    /// For handshake only. Verifies client's sign
    /// </summary>
    public bool VerifyClientSign()
    {
        return cryptoService.ed25519_verifDetached(cryptoService.ed25519_client_sign, cryptoService.GetCombinedDataForVerif());
    }

    /// <summary>
    /// For handshake only. Initializes the crypto service between server and client. Call after verify client's sign
    /// </summary>
    public void InitializeCryptoService()
    {
        cryptoService.CalculateChachaKeys();
    }
    #endregion
}