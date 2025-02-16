using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Client;

public class AsyncTcpClient
{
    private readonly string _serverIp;
    private readonly int _port;
    private Socket? _client;
    private bool _enableHeartbeat;

    public AsyncTcpClient(string serverIp, int port, bool enableHeartbeat=false)
    {
        _serverIp = serverIp;
        _port = port;
        _enableHeartbeat = enableHeartbeat;
    }

    public async Task ConnectAsync()
    {
        _client= new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
        await _client.ConnectAsync(_serverIp,_port);
        Console.WriteLine($"[Client] Connected to {_serverIp}:{_port}");
        if (_enableHeartbeat)
        {
            _ = StartHeartbeatAsync();
        }
        _ = ReceiveMsgAsync();
    }

    private async Task StartHeartbeatAsync()
    {
        while (_client!=null&&_client.Connected)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            try
            {
                await SendMessageAsync("[HEARTBEAT]");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Client] Exception: {e}, Stopped Heartbeat");
                break;
            }
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (_client == null || !_client.Connected)
        {
            Console.WriteLine($"[Client] Not connected to server.");
            return;
        }

        byte[] msgBytes = Encoding.UTF8.GetBytes(message);
        byte[] lengthBytes=BitConverter.GetBytes(msgBytes.Length);

        await _client.SendAsync(lengthBytes, SocketFlags.None);
        await _client.SendAsync(msgBytes, SocketFlags.None);
    }

    private async Task ReceiveMsgAsync()
    {
        if (_client == null) return;
        try
        {
            byte[] lengthBytes = new byte[4];
            while (true)
            {
                int bytesRead = await _client.ReceiveAsync(lengthBytes, SocketFlags.None); //接收长度
                if (bytesRead == 0) break;
                int msgLength = BitConverter.ToInt32(lengthBytes, 0);
                byte[] msgBuffer = new byte[msgLength];
                bytesRead = await _client.ReceiveAsync(msgBuffer, SocketFlags.None); //接收数据
                if (bytesRead == 0) break;
                string msg = Encoding.UTF8.GetString(msgBuffer, 0, bytesRead);
                Console.WriteLine($"[Client] Received {msg}");
            }

        }
        catch (Exception e)
        {
            Console.WriteLine($"[Client] Error: {e}");
        }
        finally
        {
            await Close();
        }
    }

    public async Task Close()
    {
        //关闭客户端连接时应该先通知服务器，否则会发生RST错误，
        if (_client != null && _client.Connected)
        {
            try
            {
                await SendMessageAsync("[CLIENT_DISCONNECT]");
                await Task.Delay(1000);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                //让tcp执行四次挥手(FIN->ACK->FIN->ACK)
                _client.Shutdown(SocketShutdown.Both);//关闭读写
                _client.Close();
                Console.WriteLine($"[Client] Disconnected from {_serverIp}:{_port}");
            }
        }
    }
}