using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class AsyncTcpServer
{
    private readonly Socket _listener;
    private readonly int _port;

    public AsyncTcpServer(int port)
    {
        _port = port;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public async Task StartAsync()
    {
        _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
        _listener.Listen(100);
        Console.WriteLine($"[Server] Listening on port {_port}");

        while (true)
        {
            var client = await _listener.AcceptAsync();
            Console.WriteLine($"[Server] Client connected: {client.RemoteEndPoint}");
            _=HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(Socket client)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int bytesRead = await client.ReceiveAsync(buffer, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"[Server] Received: {msg}");
                await client.SendAsync(Encoding.UTF8.GetBytes($"Echo: {msg}"), SocketFlags.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Exception occured: {ex}");
        }
        finally
        {
            client.Close();
            Console.WriteLine($"[Server] Client closed");
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        var server = new AsyncTcpServer(5050);
        await server.StartAsync();
    }
}