using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Commands.Audio.Fairlight;
using LibAtem.Commands.DataTransfer;
using LibAtem.Commands.DeviceProfile;
using LibAtem.Net;
using log4net;

namespace AtemProxy
{
    public class UpstreamConnection
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(UpstreamConnection));
        
        private readonly AtemClient _atem;
        private readonly Thread _thread;
        private readonly ConcurrentQueue<ReceivedPacket> _pktQueue = new ConcurrentQueue<ReceivedPacket>();

        private ProtocolVersion _version = ProtocolVersion.Minimum;

        private static readonly Type[] AudioLevelCommands = new[]
        {
            typeof(FairlightMixerMasterLevelsCommand), typeof(FairlightMixerSourceLevelsCommand),
            // typeof(FairlightMixerSendLevelsCommand)
        };

        private static readonly Type[] TransferCommands = new[]
        {
            typeof(DataTransferAbortCommand), typeof(DataTransferAckCommand),
            typeof(DataTransferCompleteCommand), typeof(DataTransferDataCommand), typeof(DataTransferErrorCommand),
            typeof(DataTransferDownloadRequestCommand), typeof(DataTransferUploadContinueCommand),
            typeof(DataTransferUploadRequestCommand)
        };

        private static readonly Type[] LockCommands = new[] {typeof(LockObtainedCommand), typeof(LockStateSetCommand)};
        
        public event AtemClient.ConnectedHandler OnConnection
        {
            add => _atem.OnConnection += value;
            remove => _atem.OnConnection -= value;
        }
        public event AtemClient.DisconnectedHandler OnDisconnection
        {
            add => _atem.OnDisconnect += value;
            remove => _atem.OnDisconnect -= value;
        }
        
        public delegate void CommandHandler(object sender, List<Tuple<ICommand, byte[]>> commands);
        public delegate void AudioLevelsHandler(object sender, List<ICommand> levels);

        public event CommandHandler OnReceive;
        public event AudioLevelsHandler OnAudioLevels;
        
        private static byte[] ParsedCommandToBytes(ParsedCommandSpec cmd)
        {
            var build = new CommandBuilder(cmd.Name);
            build.AddByte(cmd.Body);
            return build.ToByteArray();
        }
        
        public UpstreamConnection(string address)
        {
            _atem = new AtemClient(address);

            _atem.OnDisconnect += (sender) =>
            {
                // Discard any pending packets
                _pktQueue.Clear();
            };

            _atem.OnReceivePacket += (sender, pkt) =>
            {
                _pktQueue.Enqueue(pkt);
            };

            _thread = new Thread(() =>
            {
                while (true)
                {
                    if (!_pktQueue.TryDequeue(out ReceivedPacket pkt))
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    var acceptedCommands = new List<Tuple<ICommand, byte[]>>();
                    var audioLevels = new List<ICommand>();
                    foreach (ParsedCommandSpec rawCmd in pkt.Commands)
                    {
                        var cmd = CommandParser.Parse(_version, rawCmd);
                        if (cmd != null)
                        {
                            // Ensure we know what version to parse with
                            if (cmd is VersionCommand vcmd)
                                _version = vcmd.ProtocolVersion;

                            if (AudioLevelCommands.Contains(cmd.GetType()))
                            {
                                audioLevels.Add(cmd);
                            }
                            else if (LockCommands.Contains(cmd.GetType()))
                            {
                            }
                            else if (TransferCommands.Contains(cmd.GetType()))
                            {

                            }
                            else
                            {
                                acceptedCommands.Add(Tuple.Create(cmd, ParsedCommandToBytes(rawCmd)));
                            }
                        }
                        else
                        {
                            // Unknown command, so forward it and hope!
                            // It is unlikely to break anything, but command-id logic wont handle it well
                            Log.WarnFormat("Atem gave unknown command {0} {1}", rawCmd.Name, BitConverter.ToString(rawCmd.Body));
                            
                            // TODO - this is not very optimal...
                            //newPayload = newPayload.Concat(rawCmd.Body).ToArray();
                            acceptedCommands.Add(Tuple.Create<ICommand, byte[]>(null, ParsedCommandToBytes(rawCmd)));
                        }
                    }

                    if (pkt.Commands.Count > 0)
                    {
                        Log.InfoFormat("Atem gave {0} forwardable commands of {1}", acceptedCommands.Count,
                            pkt.Commands.Count);
                    }

                    if (acceptedCommands.Count > 0)
                    {
                        // Forward if anything was left
                        OnReceive?.Invoke(this, acceptedCommands);
                    }

                    if (audioLevels.Count > 0)
                    {
                        // Forward audio levels commands to subscribed clients
                        OnAudioLevels?.Invoke(this, audioLevels);
                    }
                }
            });
            _thread.Name = "UpstreamConnection";
        }

        public void Start()
        {
            _atem.Connect();
            _thread.Start();
        }

        public void ForwardPacket(ReceivedPacket pkt)
        {
            // TODO
        }
    }
}