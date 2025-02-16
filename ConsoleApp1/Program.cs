using System;
using System.Threading.Tasks;
using ConsoleApp1.Client;
using ConsoleApp1.Server;

class Program
{
    static async Task Main(string[] args)
    {
        var type = Console.ReadLine();
        if ("server".Equals(type))
        {
            var server = new AsyncTcpServer(5050,true);
            await server.StartAsync();
        }
        else
        {
            var client=new AsyncTcpClient("127.0.0.1", 5050,true);
            await client.ConnectAsync();
            while (true)
            {
                string? msg=Console.ReadLine();
                if (msg == "quit")
                {
                    await client.Close();
                    break;
                }
                await  client.SendMessageAsync(msg);
            }
        }
        
    }
}