using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Net;
using log4net;
using Newtonsoft.Json;

namespace AtemProxy
{
    public class LogItem
    {
        public bool IsSend { get; set; }
        public byte[] Payload { get; set; }
    }

    public class ProxyServer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyServer));

        private Socket _socket;

        private Dictionary<string, ProxyConnection> _clients = new Dictionary<string, ProxyConnection>();
        public static ProtocolVersion Version = ProtocolVersion.Minimum;

        private ConcurrentQueue<LogItem> _logQueue = new ConcurrentQueue<LogItem>();

        public ProxyServer(string address)
        {
            // TODO - need to clean out stale clients

            StartLogWriter();
            StartReceivingFromClients(address);
        }

        private void StartLogWriter()
        {
            var thread = new Thread(() =>
            {
                while(true)
                {
                    if (!_logQueue.TryDequeue(out LogItem item))
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    var packet = new ReceivedPacket(item.Payload);
                    if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest) &&
                        !packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                    {
                        string dirStr = item.IsSend ? "Send" : "Recv";
                        // Handle this further
                        foreach (var rawCmd in packet.Commands)
                        {
                            var cmd = CommandParser.Parse(Version, rawCmd);
                            if (cmd != null)
                            {
                                Log.InfoFormat("{0} {1} {2} ({3})", dirStr, rawCmd.Name, JsonConvert.SerializeObject(cmd), BitConverter.ToString(rawCmd.Body));
                            } else
                            {
                                Log.InfoFormat("{0} unknown {1} {2}", dirStr, rawCmd.Name, BitConverter.ToString(rawCmd.Body));
                            }
                        }
                    }
                }
            });
            thread.Start();
        }

        private static Socket CreateSocket()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 9910);
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        private void StartReceivingFromClients(string address)
        {
            _socket = CreateSocket();
            
            var thread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        //Start receiving data
                        ArraySegment<byte> buff = new ArraySegment<byte>(new byte[2500]);
                        var end = new IPEndPoint(IPAddress.Any, 0);
                        SocketReceiveFromResult v = await _socket.ReceiveFromAsync(buff, SocketFlags.None, end);

                        string epStr = v.RemoteEndPoint.ToString();
                        if (!_clients.TryGetValue(epStr, out ProxyConnection client))
                        {
                            Log.InfoFormat("Got connection from new client: {0}", epStr);
                            client = new ProxyConnection(_logQueue, address, _socket, v.RemoteEndPoint);
                            _clients.Add(epStr, client);
                        }
                        
                        //Log.InfoFormat("Got message from client. {0} bytes", v.ReceivedBytes);

                        var resBuff = buff.ToArray();
                        var resSize = v.ReceivedBytes;

                        _logQueue.Enqueue(new LogItem()
                        {
                            IsSend = true,
                            Payload = resBuff
                        });
                        
                        try
                        {
                            client.AtemConnection.Send(resBuff, resSize, client.AtemEndpoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", client.AtemEndpoint);
                        }
                    }
                    catch (SocketException)
                    {
                        // Reinit the socket as it is now unavailable
                        //_socket = CreateSocket();
                    }
                }
            });
            thread.Start();
        }
    }
}