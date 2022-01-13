using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Commands.Audio;
using LibAtem.Commands.Audio.Fairlight;
using log4net;
using log4net.Config;

namespace AtemProxy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please include IP address for ATEM.");
                return;
            }

            string ipAddress = args[0];
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            var log = LogManager.GetLogger(typeof(Program));
            log.Info("Starting");

            var currentStateCommands = new CommandQueue();
            var unknownCommandId = 1;

            var upstream = new UpstreamConnection(ipAddress);
            
            var server = new AtemServer(currentStateCommands);
            server.Connections.OnReceive += (sender, pkt) =>
            {
                log.InfoFormat("Got packet from {0}", sender);
                
                var acceptedCommands = new List<Tuple<ICommand, byte[]>>();
                foreach (ParsedCommandSpec rawCmd in pkt.Commands)
                {
                    var cmd = CommandParser.Parse(upstream.Version, rawCmd);
                    if (cmd != null)
                    {
                        if (AtemProxyUtil.AudioLevelCommands.Contains(cmd.GetType()))
                        {
                            if (cmd is AudioMixerSendLevelsCommand sendLevels && server.Connections.SubscribeAudio(sender, sendLevels.SendLevels))
                            {
                                log.InfoFormat("Changing legacy levels subscription: {0}", sendLevels.SendLevels);
                                acceptedCommands.Add(Tuple.Create(cmd, AtemProxyUtil.ParsedCommandToBytes(rawCmd)));
                            } else if (cmd is FairlightMixerSendLevelsCommand sendLevels2 && server.Connections.SubscribeAudio(sender, sendLevels2.SendLevels))
                            {
                                log.InfoFormat("Changing fairlight levels subscription: {0}", sendLevels2.SendLevels);
                                acceptedCommands.Add(Tuple.Create(cmd, AtemProxyUtil.ParsedCommandToBytes(rawCmd)));
                            }
                        }
                        else if (AtemProxyUtil.LockCommands.Contains(cmd.GetType()))
                        {
                        }
                        else if (AtemProxyUtil.TransferCommands.Contains(cmd.GetType()))
                        {

                        }
                        else
                        {
                            acceptedCommands.Add(Tuple.Create(cmd, AtemProxyUtil.ParsedCommandToBytes(rawCmd)));
                        }
                    }
                    else
                    {
                        // Unknown command, so forward it and hope!
                        // It is unlikely to break anything, but command-id logic wont handle it well
                        log.WarnFormat("Atem gave unknown command {0} {1}", rawCmd.Name,
                            BitConverter.ToString(rawCmd.Body));

                        acceptedCommands.Add(Tuple.Create<ICommand, byte[]>(null, AtemProxyUtil.ParsedCommandToBytes(rawCmd)));
                    }
                }

                if (acceptedCommands.Count > 0)
                {
                    upstream.ForwardCommands(acceptedCommands);
                }
            };

            string deviceName = "Test Proxy";
            server.StartAnnounce(deviceName, deviceName.GetHashCode().ToString());

            upstream.OnConnection += (sender) =>
            {
                log.DebugFormat("Connected to atem");
                // TODO - state
                server.AcceptConnections();
            };
            upstream.OnDisconnection += (sender) =>
            {
                log.DebugFormat("Lost connection to atem");
                server.RejectConnections();
                currentStateCommands.Clear();
            };
            upstream.OnReceive += (sender, commands) =>
            {
                foreach (var cmd in commands)
                {
                    if (cmd.Item1 != null)
                    {
                        currentStateCommands.Set(new CommandQueueKey(cmd.Item1), cmd.Item2);
                    }
                    else
                    {
                        currentStateCommands.Set(unknownCommandId++, cmd.Item2);
                    }
                }

                var messages = AtemProxyUtil.CommandsToMessages(commands.Select(c => c.Item2).ToList());
                server.Connections.Broadcast(messages);
            };
            upstream.OnAudioLevels += (sender, commands) =>
            {
                var messages = AtemProxyUtil.CommandsToMessages(commands);
                server.Connections.BroadcastAudioLevels(messages);
            };
            
            upstream.Start();
            server.StartReceive();
            server.StartPingTimer();
            
            Console.WriteLine("Press Ctrl+C to terminate...");
            
            AutoResetEvent waitHandle = new AutoResetEvent(false);
            // Handle Control+C or Control+Break
            Console.CancelKeyPress += (o, e) =>
            {
                Console.WriteLine("Exit");

                // Allow the manin thread to continue and exit...
                waitHandle.Set();
            };

            // Wait
            waitHandle.WaitOne();
            
            // Force the exit
            System.Environment.Exit(0);
        }
    }
}