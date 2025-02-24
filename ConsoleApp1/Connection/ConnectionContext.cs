using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Connection;

public class ConnectionContext:IDisposable
{
    public string ClientId { get; }
    public Socket SocketInstance { get; } 
    //依赖Socket
    public NetworkStream? NetworkStreamInstance { get; }
    //依赖NetworkStream ,负责安全通信，但是读写还是由Socket来负责的
    public SslStream? sslStream { get; }
    public bool IsEncrypted=>sslStream!=null;
    
    //记录上次活动时间
    public DateTime LastActiveTime { get;private set; }

    public ConnectionContext(string clientId, Socket socketInstance, bool enableTls)
    {
        ClientId = clientId;
        SocketInstance = socketInstance;
        //NetworkStream关闭时不会关闭socket链接，ownsSocket：false
        NetworkStreamInstance = new NetworkStream(socketInstance, ownsSocket: false);
        LastActiveTime = DateTime.UtcNow;
        if (enableTls)
        {
            //sslStream关闭时会关闭NetworkStream, leaveInnerStreamOpen:false
            sslStream = new SslStream(NetworkStreamInstance,leaveInnerStreamOpen:false);
        }
    }

    /// <summary>
    /// 执行TLS握手
    /// </summary>
    /// <param name="certificate"></param>
    public async Task AuthenticateTlsAsync(X509Certificate2 certificate)
    {
        if (sslStream == null) return;
        //服务器的TLS证书为形参， 不要求客户端证书，允许TLS兼容的协议(TLS1.2/1.3)
        await sslStream.AuthenticateAsServerAsync(certificate, false, true);
    }

    public void UpdateLastActiveTime()
    {
        LastActiveTime = DateTime.UtcNow;
    }

    private Stream GetStream()
    {
        if(sslStream!=null)return sslStream;
        if(NetworkStreamInstance!=null)return NetworkStreamInstance;
        throw new InvalidOperationException("Both sslStream  and NetworkStream are null.");
    }

    public async Task SendMessageAsync(string message)
    {
        if (SocketInstance != null || !SocketInstance.Connected)
        {
            throw new InvalidOperationException("Cannot send message to non-connected socket.");
        }
        //信息
        byte[] msgBytes=Encoding.UTF8.GetBytes(message);
        //信息长度 
        byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);
        Stream stream =GetStream();
        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
        await stream.WriteAsync(msgBytes, 0, msgBytes.Length);
        await stream.FlushAsync();
        UpdateLastActiveTime();
    }

    public async Task<string?> ReceiveMessageAsync()
    {
        var stream = GetStream();
        byte[] lengthBuffer = new byte[sizeof(int)];
        int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory());
        if (bytesRead == 0) return null;

        int msgLength = BitConverter.ToInt32(lengthBuffer, 0);
        byte[] msgBuffer = new byte[msgLength];
        await ReceiveExactAsync(stream,msgBuffer.AsMemory(),msgLength);

        //更新连接活跃时间
        UpdateLastActiveTime();
        
        return Encoding.UTF8.GetString(msgBuffer);
    }

    /// <summary>
    /// 根据数据长度接收完全整的数据
    /// </summary>
    /// <param name="socket"></param>
    /// <param name="buffer"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    /// <exception cref="SocketException"></exception>
    public async Task ReceiveExactAsync(Stream stream, Memory<byte> buffer, int count)
    {
        int received = 0;
        while (received < count)
        {
            //使用切片，避免手动计算偏移量
            int bytes = await stream.ReadAsync(buffer[received..]);
            if (bytes == 0)
            {
                throw new SocketException((int)SocketError.ConnectionReset);
            }

            received += bytes;
        }
    }

    /// <summary>
    /// 释放资源顺序 TLS资源释放，关闭NetworkStream，停止socket发送和接收，释放socket资源
    /// </summary>
    public void Dispose()
    {
        try{sslStream?.Close();}catch(Exception ex){}
        try{NetworkStreamInstance?.Close();}catch(Exception ex){}
        try{SocketInstance.Shutdown(SocketShutdown.Both);}catch(Exception ex){}
        try{SocketInstance.Close();}catch(Exception ex){}
    }
}