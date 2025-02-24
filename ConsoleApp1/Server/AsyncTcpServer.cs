using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp1.Connection;

namespace ConsoleApp1.Server;

public class AsyncTcpServer
{
    private readonly Socket _listener;
    private readonly int _port;
    //创建一个线程安全的字典，key为guid，value为一个元组， 元组的元素为socket链接以及最后活跃时间
    private readonly ConcurrentDictionary<string, ConnectionContext> _clients = new();
    private readonly bool _enableHeartbeat;
    private readonly bool _enableTls;
    private readonly X509Certificate2? _certificate;

    public AsyncTcpServer(int port, bool enableHeartbeat=false,bool enableTls=false,X509Certificate2? certificate=null)
    {
        _port = port;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _enableHeartbeat = enableHeartbeat;
        _enableTls = enableTls;
        _certificate = certificate;
    }

    public async Task StartAsync()
    {
        //绑定IP
        _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
        //开始监听，最大连接数为100
        _listener.Listen(100);
        Console.WriteLine($"[Server] Listening on port {_port}");
        _ = Task.Run(CleanupInactiveClients);

        //循环接收连接
        while (true)
        {
            var client = await _listener.AcceptAsync();
            Console.WriteLine($"[Server] Client connected: {client.RemoteEndPoint}");
            //为连接开启一个独立异步处理线程
            string clientId = client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
            var connection = new ConnectionContext(clientId, client, _enableTls);
            _clients[clientId] = connection;
            if (_enableTls && _certificate != null)
            {
                await connection.AuthenticateTlsAsync(_certificate);
            }
            _ = Task.Run(() => HandleClientAsync(connection));
        }
    }

    /// <summary>
    /// 处理客户端消息
    /// </summary>
    /// <param name="client"></param>
    private async Task HandleClientAsync(ConnectionContext connection)
    {
        try
        {
            byte[] buffer = new byte[4];//消息长度
            while (true)
            {
                var receiveTask = connection.ReceiveMessageAsync();
                //超时30s
                var timeoutTask = _enableHeartbeat ? Task.Delay(TimeSpan.FromSeconds(30)) : Task.Delay(Timeout.Infinite);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    Console.WriteLine($"[Server] Client {connection.ClientId} timeout, closing connection");
                    break;
                }

                string? bytesRead = await receiveTask;
                if (string.IsNullOrEmpty(bytesRead))
                {
                    break;
                }

                //msgBuffer中存放的是客户端发送的完整的包
                if ("[HEARTBEAT]".Equals(bytesRead))
                {
                    Console.WriteLine($"[Server] [HEARTBEAT] from {connection.ClientId}");
                    connection.UpdateLastActiveTime();
                    continue;
                }
                Console.WriteLine($"[Server] Received from {connection.ClientId}: {bytesRead}");
                await connection.SendMessageAsync("Echo: " + bytesRead);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Exception occured: {ex}");
        }
        finally
        {
            Console.WriteLine($"[Server] Closing for client {connection.ClientId}");
            _clients.TryRemove(connection.ClientId, out _);
            connection.Dispose();
            Console.WriteLine($"[Server] Client closed");
        }
    }

    /// <summary>
    /// 定期清理长时间不活跃的链接
    /// </summary>
    private async Task CleanupInactiveClients()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            
            var now = DateTime.UtcNow;
            foreach (var (clientId, connection) in _clients)
            {
                if ((now - connection.LastActiveTime).TotalSeconds >= 120)
                {
                    Console.WriteLine($"[Server] Removing inactive client:{clientId} ");
                    _clients.TryRemove(clientId, out _);
                    connection.Dispose();
                }
            }
        }
    }
}