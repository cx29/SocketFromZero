using System;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using ConsoleApp1.Connection;

namespace ConsoleApp1.Client;

public class AsyncTcpClient
{
    private readonly string _serverIp;
    private readonly int _port;
    private readonly bool _enableHeartbeat;
    private readonly bool _enableTls;
    private ConnectionContext? _connection;
    private bool _isClosed=false;

    public AsyncTcpClient(string serverIp, int port, bool enableHeartbeat = false, bool enableTls = false)
    {
        _serverIp = serverIp;
        _port = port;
        _enableHeartbeat = enableHeartbeat;
        _enableTls = enableTls;
    }

    public async Task ConnectAsync(X509Certificate2? clientCertificate = null)
    {
        var socket= new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
        await socket.ConnectAsync(_serverIp,_port);
        Console.WriteLine($"[Client] Connected to {_serverIp}:{_port}");
        _connection=new ConnectionContext(Guid.NewGuid().ToString(),socket,_enableTls);
        if (_enableTls && clientCertificate != null)
        {
            await _connection.AuthenticateTlsAsync(clientCertificate);
        }
        
        if (_enableHeartbeat)
        {
            _ = StartHeartbeatAsync();
        }
        _ = ReceiveMsgAsync();
    }

    private async Task StartHeartbeatAsync()
    {
        while (_connection!=null)
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
        if (_connection==null)
        {
            Console.WriteLine($"[Client] Not connected to server.");
            return;
        }

        await _connection.SendMessageAsync(message);
    }

    private async Task ReceiveMsgAsync()
    {
        if (_connection == null) return;
        try
        {
            while (true)
            {
                var msg=await _connection.ReceiveMessageAsync();
                if (msg == null) break;
                Console.WriteLine($"[Client] Received {msg}");
            }

        }
        catch (Exception e)
        {
            Console.WriteLine($"[Client] Error: {e}");
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task CloseAsync()
    {
        if (_isClosed) return;
        _isClosed = true;
        //关闭客户端连接时应该先通知服务器，否则会发生RST错误，
        if(_connection!=null)
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
                _connection.Dispose();
                _connection=null;
                Console.WriteLine($"[Client] Disconnected from {_serverIp}:{_port}");
            }
        }
    }
}