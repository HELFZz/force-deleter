using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ForceDeleter
{
    internal sealed class IpcServer
    {
        private readonly Action<string> handler;
        private Thread thread;
        private volatile bool running;

        internal IpcServer(Action<string> handler)
        {
            this.handler = handler;
        }

        internal void Start()
        {
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "ForceDeleter IPC";
            thread.Start();
        }

        internal void Stop()
        {
            running = false;
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(
                        Program.PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None))
                    {
                        server.WaitForConnection();

                        using (StreamReader reader = new StreamReader(server, Encoding.UTF8))
                        using (StreamWriter writer = new StreamWriter(server, new UTF8Encoding(false)))
                        {
                            writer.AutoFlush = true;
                            string line = reader.ReadLine();
                            if (line != null)
                            {
                                writer.WriteLine("OK");
                                try
                                {
                                    handler(line);
                                }
                                catch (Exception ex)
                                {
                                    AppLog.Write("IPC HANDLER: " + ex);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (running)
                    {
                        AppLog.Write("IPC: " + ex.Message);
                        Thread.Sleep(200);
                    }
                }
            }
        }
    }
}
