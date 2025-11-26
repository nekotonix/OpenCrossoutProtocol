# This project is only for educational purposes
This library allows you to create crossout-game servers (not battle, just for hangar and so on) and create API to live servers
Important: I do no want to create crossout-servers anymore and possible i will not update this project. I know that code in this project now is bad, but everything in your hands. You can ask me a question "how to do" in discord @nekotonix (https://discord.gg/r5XbPeaR5p)

*Tested on new (2.7, 2.17, 2.32), 0.10, 0.9, 0.8. Maybe some issues on <0.8 and
*To create a sever you need to mod at least 128 bytes in client (keys)

# Usage
1) Open your project in visual studio (i used 2022)
2) Add to solution project OpenCrossoutProtocol
3) Link this project to your main project's dependencies
4) You can use all this project in your

Some tips that might be able to help:
1) To create a server or client use class "TcpServer" (there alse a "WebsocketServer" that original game uses, but i did not test how it works)
   *Some old crossout versions does not support WebsockerServer
2) To decode packets from new versions (>0.12) use namespace OpenCrossoutProtocol.NewRealizer, for old OpenCrossoutProtocol.TRealizer (<0.12)
3) This server uses built-in class Logger. Use ``OpenCrossoutProtocol.Logger.OnLogMessage += (message, logType) => log(message, logType);``

# Known Issues
This library do not like huge packets from client. For example: saving game car with more than 400 carparts
*In-game limit is about 500 carparts per player in battle

# Example usage
I will publish "CarProtoREST" project (Crossout api in REST) in some time.
It does not use this library as link, there just included code from it (Folder Core/_net/)
//some code parts of server
```cs
//server creation
sp = new TcpServer(Const.ChatPort, MessageRouter.HandleMessageChat, ServiceLocator.ChatClients.RemoveSessionClient, seed)

public static async Task<message_basic[]?> HandleMessage(ClientHandlerBase client, message_basic message)
{
  if (message.requestChannel == (byte)BinaryConst.containers.ChannelId.HandshakesP){ //message.requestChannel == 0xFF
    switch (message.requestType)
    {
        case (ushort)BinaryConst.containers.Handshake.Message.HelloAck: //0
            return await OnClientHello(session, message);
        case (ushort)BinaryConst.containers.Handshake.Message.HelloSign: //2
            return await VerifyClientSign(session, message);
    
        case (ushort)BinaryConst.containers.Handshake.Message.PingPong1: //4
        case (ushort)BinaryConst.containers.Handshake.Message.PingPong2: //5
            return await HandlePing(session, message);
    }
  }
}

private static async Task<message_basic[]?> OnClientHello(SessionContext session, message_basic msg)
{
    try
    {
        Logger.loghexl(msg.data);
        handshakeClientHello? clientHello = Deserializer.Deserialize<handshakeClientHello>(msg.data);
        //...
    }
}

public class handshakeClientHello
{
    public UInt32 clientPeerId;
    [Fixed(32)]
    public byte[] ed_pubkey_client = Array.Empty<byte>();
    [Fixed(32)]
    public byte[] x_pubkey_client = Array.Empty<byte>();
    [Fixed]
    public clientChannel[] channels = Array.Empty<clientChannel>();
    public UInt32 reqPeerId;
    public UInt16 protoVersion;//idk
}
public class handshakeServerHello
{
    public required UInt32 serverPeerId;
    [Fixed(32)]
    public required byte[] ed_pubkey_serv;
    [Fixed(64)]
    public required byte[] ed_sign_serv;
    [Fixed(32)]
    public required byte[] x_pubkey_serv;
    [Fixed]
    public required serverChannel[] channels;
    [Fixed]
    public required serverAllowedServices[] EnabledServices;
    public required string peerServerName;
    public required string peerServerAddr;
}
public class handshakeClientSign
{
    [Fixed(64)]
    public byte[] ed_sign_client;
    public string clientGotAddr;
}
```





if targem see this message, pls dont be angry, i really love your game, but i think that it became a донатная-помойка
