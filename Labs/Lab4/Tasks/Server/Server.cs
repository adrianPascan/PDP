﻿using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Server
{
    public class StateObject
    {
        public const int BufferSize = 1024;
        public byte[] buffer = new byte[BufferSize];
        public StringBuilder sb = new StringBuilder();
        public Socket workSocket = null;
    }

    public class AsynchronousSocketListener
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);

        public AsynchronousSocketListener()
        {
        }

        public static void StartListening()
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 11000);

            Socket listener = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    allDone.Reset();

                    Console.WriteLine("Waiting for a connection...");

                    Task<StateObject> future = Accept(listener);
                    future.ContinueWith((Task<StateObject> f) => Read(f.Result));

                    allDone.WaitOne();
                }

            }
            catch (Exception e) 
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();

        }

        public static Task<StateObject> Accept(Socket listener)
        {
            TaskCompletionSource<StateObject> promise = new TaskCompletionSource<StateObject>();
            StateObject state = new StateObject();

            listener.BeginAccept(
                        (IAsyncResult ar) =>
                        {
                            Socket handler = listener.EndAccept(ar);
                            state.workSocket = handler;

                            allDone.Set();

                            promise.SetResult(state);
                        },
                        null);

            return promise.Task;
        }

        public static Task<StateObject> Read(StateObject state)
        {
            Task<StateObject> future = ReadCallback(state);
            future.ContinueWith((Task<StateObject> f) => Process(f.Result));

            return future;
        }

        public static Task<StateObject> ReadCallback(StateObject state)
        {
            TaskCompletionSource<StateObject> promise = new TaskCompletionSource<StateObject>();

            string content = String.Empty;

            Socket handler = state.workSocket;

            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                (IAsyncResult ar) =>
                {
                    int bytesRead = handler.EndReceive(ar);

                    if (bytesRead > 0)
                    {
                        state.sb.Append(Encoding.ASCII.GetString(
                            state.buffer, 0, bytesRead));

                        content = state.sb.ToString();
                        if (content.IndexOf("\n\n") > -1)
                        {
                            Console.WriteLine("Read {0} bytes from socket.\nData :\n{1}",
                                content.Length, content);

                            promise.SetResult(Send(state, "Download completed!").Result);
                        }
                        else
                        {
                            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            (IAsyncResult ar) => promise.SetResult(ReadCallback(state).Result),
                            null);
                        }
                    }
                }, 
                null);          

            return promise.Task;
        }

        private static Task<StateObject> Process(StateObject stateObject)
        {
            TaskCompletionSource<StateObject> promise = new TaskCompletionSource<StateObject>();

            string content = stateObject.sb.ToString();
            List<string> tokens = new List<string>(content.Split("\n"));

            string uri = tokens[0];
            string[] headerLines = tokens.GetRange(1, tokens.Count - 1).ToArray();


            Header(headerLines);
            Download(uri);

            promise.SetResult(stateObject);
            return promise.Task;
        }

        private static void Header(string[] headerLines)
        {
            int index = 1;
            bool found = false;
            while (index < headerLines.Length && !found)
            {
                string headerLine = headerLines[index++].Trim();

                if (headerLine.StartsWith("Content-length:"))
                {
                    string contentLength = headerLine.Substring(15);

                    Console.WriteLine("Content-length: {0}", contentLength);
                }
            }
        }

        private static void Download(string uri)
        {
            string[] tokens = uri.Split(@"/");
            string fileName = tokens[tokens.Length - 1];

            WebClient webClient = new WebClient();
            webClient.DownloadFile(uri, fileName);

            Console.WriteLine("Downloaded file {0}", fileName);
        }

        private static Task<StateObject> Send(StateObject state, String data)
        {
            TaskCompletionSource<StateObject> promise = new TaskCompletionSource<StateObject>();

            Socket handler = state.workSocket;

            byte[] byteData = Encoding.ASCII.GetBytes(data);

            handler.BeginSend(byteData, 0, byteData.Length, 0,
                (IAsyncResult ar) =>
                {
                    int bytesSent = handler.EndSend(ar);
                    Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();

                    promise.SetResult(state);
                },
                null);

            return promise.Task;
        }

        public static int Main(String[] args)
        {
            Console.Title = "Server";
            StartListening();
            return 0;
        }
    }
}
