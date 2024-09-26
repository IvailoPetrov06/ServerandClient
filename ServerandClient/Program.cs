using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace Client
{
    class Program
    {
        private static bool connected = false;
        private static Thread clientThread = null;
        private static MyClient obj;
        private static Task sendTask = null;
        private static bool exit = false;

        private struct MyClient
        {
            public string username;
            public string key;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        }

        static void Main(string[] args)
        {

            while (!exit)
            {
                Console.WriteLine("Enter the server IP address:");
                string address = Console.ReadLine().Trim();
                Console.WriteLine("Enter the server port:");
                string portStr = Console.ReadLine().Trim();
                Console.WriteLine("Enter your username:");
                string username = Console.ReadLine().Trim();
                Console.WriteLine("Enter your encryption key (optional, press Enter to skip):");

                string key = Console.ReadLine().Trim();

                if (IPAddress.TryParse(address, out IPAddress ip) && int.TryParse(portStr, out int port) && port >= 0 && port <= 65535)
                {
                    if (clientThread == null || !clientThread.IsAlive)
                    {
                        clientThread = new Thread(() => Connection(ip, port, username, key))
                        {
                            IsBackground = true
                        };
                        clientThread.Start();
                    }
                }
                else
                {
                    Console.WriteLine("Invalid IP or port. Please try again.");
                }

                while (connected)
                {
                    string message = Console.ReadLine();
                    if (message.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        exit = true;
                        obj.client.Close();
                        break;
                    }
                    else if (message.Length > 0)
                    {
                        Log($"{obj.username} (You): {message}");
                        Send(message);
                    }
                }
            }
        }

        private static void Log(string msg = "")
        {
            if (!exit)
            {
                if (msg.Length > 0)
                {
                    Console.WriteLine($"[ {DateTime.Now:HH:mm} ] {msg}");
                }
            }
        }

        private static string ErrorMsg(string msg)
        {
            return $"ERROR: {msg}";
        }

        private static string SystemMsg(string msg)
        {
            return $"SYSTEM: {msg}";
        }

        private static void Connected(bool status)
        {
            connected = status;
            if (status)
            {
                Console.WriteLine(SystemMsg("You are now connected"));
            }
            else
            {
                Console.WriteLine(SystemMsg("You are now disconnected"));
            }
        }

        private static void Read(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }

            if (bytes > 0)
            {
                obj.data.Append(Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                    }
                    else
                    {
                        Log(obj.data.ToString());
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private static void ReadAuth(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }

            if (bytes > 0)
            {
                obj.data.Append(Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);
                    }
                    else
                    {
                        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(obj.data.ToString());
                        if (data.ContainsKey("status") && data["status"] == "authorized")
                        {
                            Connected(true);
                        }
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private static bool Authorize()
        {
            bool success = false;
            var data = new Dictionary<string, string>
            {
                { "username", obj.username },
                { "key", obj.key }
            };
            string jsonData = JsonSerializer.Serialize(data);
            Send(jsonData);

            while (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);
                    obj.handle.WaitOne();

                    if (connected)
                    {
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }

            if (!connected)
            {
                Log(SystemMsg("Unauthorized"));
            }
            return success;
        }

        private static void Connection(IPAddress ip, int port, string username, string key)
        {
            try
            {
                obj = new MyClient
                {
                    username = username,
                    key = key,
                    client = new TcpClient()
                };
                obj.client.Connect(ip, port);
                obj.stream = obj.client.GetStream();
                obj.buffer = new byte[obj.client.ReceiveBufferSize];
                obj.data = new StringBuilder();
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);

                if (Authorize())
                {
                    while (obj.client.Connected)
                    {
                        try
                        {
                            obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                            obj.handle.WaitOne();
                        }
                        catch (Exception ex)
                        {
                            Log(ErrorMsg(ex.Message));
                        }
                    }
                    obj.client.Close();
                    Connected(false);
                }
            }
            catch (Exception ex)
            {
                Log(ErrorMsg(ex.Message));
            }
        }

        private static void Write(IAsyncResult result)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private static void BeginWrite(string msg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private static void Send(string msg)
        {
            if (sendTask == null || sendTask.IsCompleted)
            {
                sendTask = Task.Factory.StartNew(() => BeginWrite(msg));
            }
            else
            {
                sendTask.ContinueWith(t => BeginWrite(msg));
            }
        }
    }
}
