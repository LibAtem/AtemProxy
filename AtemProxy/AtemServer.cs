using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LibAtem.Commands;
using LibAtem.Discovery;
using LibAtem.Net;
using log4net;	
using Makaretu.Dns;

namespace AtemProxy
{
    public class AtemServer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AtemServer));

        private readonly ClientConnectionList _connections = new ClientConnectionList();
        private CommandQueue _state;
        private bool _accept = false;

        private Socket _socket;	
        private readonly MulticastService _mdns = new MulticastService();

        // TODO - remove this list, and replace with something more sensible...
        private readonly List<Timer> timers = new List<Timer>();
        
        public delegate void CommandHandler(object sender, List<Tuple<ICommand, byte[]>> commands);

        public event CommandHandler OnReceive;

        public AtemServer(CommandQueue state)
        {
            _state = state;
        }

        public void RejectConnections()
        {
            _accept = false;
            _connections.ClearAll();
        }

        public void AcceptConnections()
        {
            _accept = true;
        }
        
         public void StartAnnounce(string modelName, string deviceId)	
        {	
            _mdns.UseIpv4 = true;	
            _mdns.UseIpv6 = false;	
            var safeModelName = modelName.Replace(' ', '-').ToUpper();	
            var domain = new DomainName($"Mock {modelName}.{AtemDeviceInfo.ServiceName}");	
            var deviceDomain = new DomainName($"MOCK-{safeModelName}-{deviceId}.local");	
            	
            timers.Add(new Timer(o =>	
            {	
                Log.Info("MDNS announce");	
                DoAnnounce(deviceId, modelName, domain, deviceDomain);	
            }, null, 0, 10000));	
            _mdns.QueryReceived += (s, e) =>	
            {	
                var msg = e.Message;	
                if (msg.Questions.Any(q => q.Name == AtemDeviceInfo.ServiceName))	
                {	
                    Log.Debug("MDNS query");	
                    DoAnnounce(deviceId, modelName, domain, deviceDomain);	
                }	
            };	
            _mdns.Start();	
        }	
        private void DoAnnounce(string deviceId, string modelName, DomainName domain, DomainName deviceDomain)	
        {	
            var res = new Message();	
            var addresses = MulticastService.GetIPAddresses()	
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);	
            foreach (var address in addresses)	
            {	
                res.Answers.Add(new PTRRecord	
                {	
                    Name = AtemDeviceInfo.ServiceName,	
                    DomainName = domain	
                });	
                res.AdditionalRecords.Add(new TXTRecord	
                {	
                    Name = domain,	
                    Strings = new List<string>	
                    {	
                        "txtvers=1",	
                        $"name=Blackmagic {modelName}",	
                        "class=AtemSwitcher",	
                        "protocol version=0.0",	
                        "internal version=FAKE",	
                        $"unique id={deviceId}"	
                    }	
                });	
                res.AdditionalRecords.Add(new ARecord	
                {	
                    Address = address,	
                    Name = deviceDomain,	
                });	
                res.AdditionalRecords.Add(new SRVRecord	
                {	
                    Name = domain,	
                    Port = 9910,	
                    Priority = 0,	
                    Target = deviceDomain,	
                    Weight = 0	
                });	
                /*	
                res.AdditionalRecords.Add(new NSECRecord	
                {	
                    Name = domain	
                });*/	
            }	
            _mdns.SendAnswer(res);	
        }
        
        public void StartPingTimer()
        {
            timers.Add(new Timer(o =>
            {
                _connections.QueuePings();
            }, null, 0, AtemConstants.PingInterval));
        }
        
        private static Socket CreateSocket()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 9910);
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        public void StartReceive()
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
                       
                        // Check if we can accept it
                        if (!_accept) continue;

                        AtemServerConnection conn = _connections.FindOrCreateConnection(v.RemoteEndPoint, out _);
                        if (conn == null)
                            continue;

                        byte[] buffer = buff.Array;
                        var packet = new ReceivedPacket(buffer);

                        if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                        {
                            conn.ResetConnStatsInfo();
                            // send handshake back
                            byte[] test =
                            {
                                buffer[0], buffer[1], // flags + length
                                buffer[2], buffer[3], // session id
                                0x00, 0x00, // acked pkt id
                                0x00, 0x00, // retransmit request
                                buffer[8], buffer[9], // unknown2
                                0x00, 0x00, // server pkt id
                                0x02, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00
                            };

                            var sendThread = new Thread(o =>
                            {
                                while (!conn.HasTimedOut)
                                {
                                    conn.TrySendQueued(_socket);
                                    Task.Delay(1).Wait();
                                }	
                                Console.WriteLine("send finished");
                            });
                            sendThread.Name = $"To {conn.Endpoint}";
                            sendThread.Start();

                            await _socket.SendToAsync(new ArraySegment<byte>(test, 0, 20), SocketFlags.None, v.RemoteEndPoint);

                            continue;
                        }

                        if (!conn.IsOpened)
                        {
                            var recvThread = new Thread(o =>
                            {
                                while (!conn.HasTimedOut || conn.HasCommandsToProcess)
                                {
                                    List<ICommand> cmds = conn.GetNextCommands();

                                    Log.DebugFormat("Recieved {0} commands", cmds.Count);
                                    //conn.HandleInner(_state, connection, cmds);
                                }
                            });
                            recvThread.Name = $"Receive {conn.Endpoint}";
                            recvThread.Start();
                        }
                        conn.Receive(_socket, packet);

                        if (conn.ReadyForData)
                            QueueDataDumps(conn);
                    }
                    catch (SocketException)
                    {
                        // Reinit the socket as it is now unavailable
                        //_socket = CreateSocket();
                    }
                }
            });
            thread.Name = "AtemServer";
            thread.Start();
        }
        
        private void QueueDataDumps(AtemConnection conn)
        {
            try
            {
                var queuedCommands = _state.Values();
                var count = queuedCommands.Count;
                var sent = 0;
                while (queuedCommands.Count > 0)
                {
                    var builder = new OutboundMessageBuilder();

                    int removeCount = 0;
                    foreach (byte[] data in queuedCommands)
                    {
                        if (!builder.TryAddData(data))
                            break;

                        removeCount++;
                    }

                    if (removeCount == 0)
                    {
                        throw new Exception("Failed to build message!");
                    }

                    queuedCommands.RemoveRange(0, removeCount);
                    conn.QueueMessage(builder.Create());
                    Log.InfoFormat("Length {0} {1}", builder.currentLength , removeCount);
                    sent++;
                }

                Log.InfoFormat("Sent all {1} commands to {0} in {2} packets", conn.Endpoint, count, sent);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}