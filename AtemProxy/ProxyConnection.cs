using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Commands.DeviceProfile;
using LibAtem.Net;
using log4net;

namespace AtemProxy
{
    public class ProxyConnection
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyConnection));

        private readonly ConcurrentQueue<LogItem> _logQueue;

        private readonly Socket _serverSocket;
        private readonly EndPoint _clientEndPoint;

        public IPEndPoint AtemEndpoint { get; }
        public UdpClient AtemConnection { get; }

        public ProxyConnection(ConcurrentQueue<LogItem> logQueue, string address, Socket serverSocket, EndPoint clientEndpoint)
        {
            _logQueue = logQueue;
            _serverSocket = serverSocket;
            _clientEndPoint = clientEndpoint;

            AtemEndpoint = new IPEndPoint(IPAddress.Parse(address), 9910);
            AtemConnection = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

            StartReceivingFromAtem(address);
        }
        
        private bool MutateServerCommand(ICommand cmd)
        {
            /*
            if (cmd is MultiviewPropertiesGetCommand mvpCmd)
            {
                // mvpCmd.SafeAreaEnabled = false;
                return true;
            }
            if (cmd is MultiviewerConfigCommand mvcCmd)
            {
                // mvcCmd.Count = 1;
                // mvcCmd.WindowCount = 9; // < 10 works, no effect?
                // mvcCmd.Tmp2 = 0;
                // mvcCmd.CanRouteInputs = false; // Confirmed
                // mvcCmd.CanToggleSafeArea = 0; // Breals
                // mvcCmd.SupportsVuMeters = 0; // 
                return true;
            }
            else if (cmd is MixEffectBlockConfigCommand meCmd)
            {
                meCmd.KeyCount = 1;
                return true;
            }
            else if (cmd is TopologyV8Command top8Cmd)
            {
                // topCmd.SuperSource = 2; // Breaks
                // topCmd.TalkbackOutputs = 8;
                // topCmd.SerialPort = 0; // < 1 Works
                // topCmd.DVE = 2; // > 1 Works
                top8Cmd.MediaPlayers = 1; // < 2 Works
                // topCmd.Stingers = 0; // < 1 Works
                top8Cmd.DownstreamKeyers = 1; // < 1 Works
                // topCmd.Auxiliaries = 4; // Breaks
                // topCmd.HyperDecks = 2; // Works
                // topCmd.TalkbackOverSDI = 4; //
                // top8Cmd.Tmp11 = 2; // < 1 breaks. > 1 is ok
                // top8Cmd.Tmp12 = 0; // All work
                // topCmd.Tmp14 = 1; // Breaks
                // top8Cmd.Tmp20 = 1;

                Console.WriteLine("{0}", JsonConvert.SerializeObject(top8Cmd));
                return true;
            }
            else if (cmd is TopologyCommand topCmd)
            {
                // topCmd.SuperSource = 2; // Breaks
                // topCmd.TalkbackOutputs = 8;
                // topCmd.SerialPort = 0; // < 1 Works
                // topCmd.DVE = 2; // > 1 Works
                // topCmd.MediaPlayers = 1; // < 2 Works
                // topCmd.Stingers = 0; // < 1 Works
                topCmd.DownstreamKeyers = 1; // < 1 Works
                // topCmd.Auxiliaries = 4; // Breaks
                // topCmd.HyperDecks = 2; // Works
                // topCmd.TalkbackOverSDI = 4; //
                // topCmd.Tmp11 = 2; // < 1 breaks. > 1 is ok
                // topCmd.Tmp12 = 0; // All work
                // topCmd.Tmp14 = 1; // Breaks
                // topCmd.Tmp20 = 0;
                return true;
            }*/
            return false;
        }

        private byte[] ParsedCommandToBytes(ParsedCommandSpec cmd)
        {
            var build = new CommandBuilder(cmd.Name);
            build.AddByte(cmd.Body);
            return build.ToByteArray();
        }
        
        private byte[] CompileMessage(ReceivedPacket origPacket, byte[] payload)
        {
            byte opcode = (byte)origPacket.CommandCode;
            byte len1 = (byte)((ReceivedPacket.HeaderLength + payload.Length) / 256 | opcode << 3); // opcode 0x08 + length
            byte len2 = (byte)((ReceivedPacket.HeaderLength + payload.Length) % 256);

            byte[] buffer =
            {
                len1, len2, // Opcode & Length
                (byte)(origPacket.SessionId / 256),  (byte)(origPacket.SessionId % 256), // session id
                0x00, 0x00, // ACKed Pkt Id
                0x00, 0x00, // Unknown
                0x00, 0x00, // unknown2
                (byte)(origPacket.PacketId / 256),  (byte)(origPacket.PacketId % 256), // pkt id
            };

            // If no payload, dont append it
            if (payload.Length == 0)
                return buffer;

            return buffer.Concat(payload).ToArray();
        }

        private void StartReceivingFromAtem(string address)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        IPEndPoint ep = AtemEndpoint;
                        byte[] data = AtemConnection.Receive(ref ep);

                        //Log.InfoFormat("Got message from atem. {0} bytes", data.Length);
                        
                        var packet = new ReceivedPacket(data);
                        if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest) &&
                            !packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                        {
                            // Handle this further
                            var newPayload = new byte[0];
                            bool changed = false;
                            foreach (var rawCmd in packet.Commands)
                            {
                                var cmd = CommandParser.Parse(ProxyServer.Version, rawCmd);
                                if (cmd != null)
                                {
                                    if (cmd is VersionCommand vcmd)
                                    {
                                        ProxyServer.Version = vcmd.ProtocolVersion;
                                    }

                                    var name = CommandManager.FindNameAndVersionForType(cmd);
                                    // Log.InfoFormat("Recv {0} {1}", name.Item1, JsonConvert.SerializeObject(cmd));

                                    if (MutateServerCommand(cmd))
                                    {
                                        changed = true;
                                        newPayload = newPayload.Concat(cmd.ToByteArray()).ToArray();
                                    }
                                    else
                                    {
                                        newPayload = newPayload.Concat(ParsedCommandToBytes(rawCmd)).ToArray();
                                    }

                                }
                                else
                                {
                                    newPayload = newPayload.Concat(ParsedCommandToBytes(rawCmd)).ToArray();
                                }
                            }

                            if (changed)
                            {
                                data = CompileMessage(packet, newPayload);
                            }
                        }

                        _logQueue.Enqueue(new LogItem()
                        {
                            IsSend = false,
                            Payload = data
                        });

                        try
                        {
                            _serverSocket.SendTo(data, SocketFlags.None, _clientEndPoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", _clientEndPoint);
                        }
                    }
                    catch (SocketException)
                    {
                        Log.ErrorFormat("Socket Exception");
                    }
                }
            });
            thread.Start();
        }
    }
}