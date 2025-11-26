using OpenCrossoutProtocol;

public abstract class ClientHandlerBase : IDisposable
{
    protected ulong clientCounter = 0;
    protected ulong serverCounter = 0;
    public int maxChunkSize { get; set; } = 4096;
    protected bool wantDisconnect = false;
    protected bool isRespDone = true;
    protected Crypto cryptoService;
    protected List<message_basic> undoneMessagesPool = new();
    protected List<message_basic> undoneStartMessagesPool = new();
    protected SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    protected CancellationToken? _ct;

    public delegate Task<message_basic[]?> OnGetMessage(ClientHandlerBase initial, message_basic message);
    protected OnGetMessage _handler;

    protected ClientHandlerBase(OnGetMessage messageHandler, byte[]? pubKey, byte[]? privKey)
    {
        _handler = messageHandler;
        cryptoService = new Crypto(pubKey, privKey);
    }
    /// <summary>
    /// do not use from your code!! per-server connections
    /// </summary>
    public abstract Task HandleConnectionAsync(CancellationToken ct);
    /// <summary>
    /// Process request from connection loop
    /// </summary>
    protected abstract Task ProcessRequest(byte[] data, int length, CancellationToken ct);
    /// <summary>
    /// Sends message to client
    /// </summary>
    public abstract Task<bool> SendMessage(message_basic message, bool insertAsStart = false);
    /// <summary>
    /// Sends message to client
    /// </summary>
    protected abstract Task SendResponseAsync(byte[] data, CancellationToken ct);
    /// <summary>
    /// Disconnect client from server
    /// </summary>
    public abstract void Dispose();

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