using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1.Server;

public class AsyncTcpServer
{
    private readonly Socket _listener;
    private readonly int _port;
    private readonly ConcurrentDictionary<string,Socket> _clients = new();
    private readonly bool _enableHeartbeat;

    public AsyncTcpServer(int port, bool enableHeartbeat=false)
    {
        _port = port;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _enableHeartbeat = enableHeartbeat;
    }

    public async Task StartAsync()
    {
        //绑定IP
        _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
        //开始监听，最大连接数为100
        _listener.Listen(100);
        Console.WriteLine($"[Server] Listening on port {_port}");

        //循环接收连接
        while (true)
        {
            var client = await _listener.AcceptAsync();
            Console.WriteLine($"[Server] Client connected: {client.RemoteEndPoint}");
            //为连接开启一个异步处理线程
            _ = HandleClientAsync(client);
        }
    }

    /// <summary>
    /// 处理客户端消息
    /// </summary>
    /// <param name="client"></param>
    private async Task HandleClientAsync(Socket client)
    {
        string clientId=client.RemoteEndPoint?.ToString()??Guid.NewGuid().ToString();//防止RemoteEndPoint为空
        _clients[clientId] = client;
        try
        {
            byte[] buffer = new byte[4];//消息长度
            while (true)
            {
                var receiveTask = client.ReceiveAsync(buffer, SocketFlags.None);
                //超时30s
                var timeoutTask = _enableHeartbeat ? Task.Delay(TimeSpan.FromSeconds(30)) : Task.Delay(Timeout.Infinite);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    Console.WriteLine($"[Server] Client {clientId} timeout, closing connection");
                    break;
                }

                int bytesRead = await receiveTask;
                if (bytesRead == 0)
                {
                    break;
                }

                int msgLength = BitConverter.ToInt32(buffer, 0);
                byte[] msgBuffer = new byte[msgLength];

                //msgBuffer中存放的是客户端发送的完整的包
                await ReceiveExactAsync(client, msgBuffer, msgLength);
                string msg = Encoding.UTF8.GetString(msgBuffer);
                if ("[HEARTBEAT]".Equals(msg))
                {
                    Console.WriteLine($"[Server] [HEARTBEAT] from {clientId}");
                    continue;
                }
                Console.WriteLine($"[Server] Received from {clientId}: {msg}");
                await SendMessageAsync(clientId, "Echo: " + msg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Exception occured: {ex}");
        }
        finally
        {
            Console.WriteLine($"[Server] Closing for client {clientId}");
            _clients.TryRemove(clientId, out _);
            try
            {
                client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e){}
            client.Close();
            Console.WriteLine($"[Server] Client closed");
        }
    }

    /// <summary>
    /// 根据数据长度接收完全整的数据
    /// </summary>
    /// <param name="socket"></param>
    /// <param name="buffer"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    /// <exception cref="SocketException"></exception>
    private async Task<int> ReceiveExactAsync(Socket socket, byte[] buffer, int count)
    {
        int received = 0;
        while (received < count)
        {
            int bytes = await socket.ReceiveAsync(buffer.AsMemory(received, count - received), SocketFlags.None);
            if (bytes == 0)
            {
                throw new SocketException((int)SocketError.ConnectionReset);
            }
            received += bytes;
        }
        return received;
    }
    private async Task SendMessageAsync(string clientId, string data)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return;
        byte[] msgBytes=Encoding.UTF8.GetBytes(data);
        byte[] lengthBytes = BitConverter.GetBytes(msgBytes.Length);

        await client.SendAsync(lengthBytes, SocketFlags.None);
        await client.SendAsync(msgBytes, SocketFlags.None);
    }   
}