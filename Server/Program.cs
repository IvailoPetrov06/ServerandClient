using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json; 
using System.Threading;

namespace ServerApp
{
    class Program
    {
        private static bool active = false;
        private static Thread listener = null;
        private static long id = 0;
        private static string key = ""; 

        private struct MyClient
        {
            public long id;
            public StringBuilder username;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        }

        private static ConcurrentDictionary<long, MyClient> clients = new ConcurrentDictionary<long, MyClient>();

        static void Main(string[] args)
        {
            Console.WriteLine("Enter IP address:");
            string address = Console.ReadLine();

            Console.WriteLine("Enter port number:");
            string portInput = Console.ReadLine();
            int port = int.Parse(portInput);

            Console.WriteLine("Enter server key:");
            key = Console.ReadLine(); 

            IPAddress ip = IPAddress.Parse(address);

            listener = new Thread(() => Listener(ip, port));
            listener.Start();
            listener.Join();
        }

        private static void Log(string msg = "")
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                Console.WriteLine($"[ {DateTime.Now:HH:mm} ] {msg}");
            }
        }

        private static void Active(bool status)
        {
            active = status;
            if (status)
            {
                Log("SYSTEM: Server has started");
            }
            else
            {
                Log("SYSTEM: Server has stopped");
            }
        }

        private static void Read(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                }
            }

            if (bytes > 0)
            {
                obj.data.Append(Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    }
                    else
                    {
                        string msg = $"{obj.username}: {obj.data}";
                        Log(msg);
                        Send(msg, obj.id);
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log($"ERROR: {ex.Message}");
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
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;

            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                }
            }

            if (bytes > 0)
            {
                obj.data.Append(Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), obj);
                    }
                    else
                    {
                        
                        Dictionary<string, string> data = JsonSerializer.Deserialize<Dictionary<string, string>>(obj.data.ToString());

                        if (!data.ContainsKey("username") || data["username"].Length < 1 || !data.ContainsKey("key") || !data["key"].Equals(key))
                        {
                            obj.client.Close();
                        }
                        else
                        {
                            obj.username.Append(data["username"].Length > 200 ? data["username"].Substring(0, 200) : data["username"]);
                            Send("{\"status\": \"authorized\"}", obj);
                        }

                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log($"ERROR: {ex.Message}");
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private static bool Authorize(MyClient obj)
        {
            bool success = false;

            while (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), obj);
                    obj.handle.WaitOne();

                    if (obj.username.Length > 0)
                    {
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                }
            }

            return success;
        }

        private static void Connection(MyClient obj)
        {
            if (Authorize(obj))
            {
                clients.TryAdd(obj.id, obj);
                string msg = $"{obj.username} has connected";
                Log($"SYSTEM: {msg}");
                Send($"SYSTEM: {msg}", obj.id);

                while (obj.client.Connected)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                        obj.handle.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: {ex.Message}");
                    }
                }

                obj.client.Close();
                clients.TryRemove(obj.id, out MyClient tmp);
                msg = $"{tmp.username} has disconnected";
                Log($"SYSTEM: {msg}");
                Send($"SYSTEM: {msg}", tmp.id);
            }
        }

        private static void Listener(IPAddress ip, int port)
        {
            TcpListener listener = null;

            try
            {
                listener = new TcpListener(ip, port);
                listener.Start();
                Active(true);

                while (active)
                {
                    if (listener.Pending())
                    {
                        try
                        {
                            MyClient obj = new MyClient
                            {
                                id = id,
                                username = new StringBuilder(),
                                client = listener.AcceptTcpClient(), 
                                stream = null,  
                                buffer = new byte[8192],
                                data = new StringBuilder(),
                                handle = new EventWaitHandle(false, EventResetMode.AutoReset)
                            };

                            obj.stream = obj.client.GetStream(); 

                            Thread th = new Thread(() => Connection(obj)) { IsBackground = true };
                            th.Start();
                            id++;
                        }
                        catch (Exception ex)
                        {
                            Log($"ERROR: {ex.Message}");
                        }
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }

                Active(false);
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
            }
            finally
            {
                listener?.Server.Close();
            }
        }

        private static void Send(string msg, MyClient obj)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);

            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                }
            }
        }

        private static void Send(string msg, long id = -1)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);

            foreach (KeyValuePair<long, MyClient> obj in clients)
            {
                if (id != obj.Value.id && obj.Value.client.Connected)
                {
                    try
                    {
                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: {ex.Message}");
                    }
                }
            }
        }

        private static void Write(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;

            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                }
            }
        }
    }
}
