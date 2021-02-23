using System;
using System.Collections.Generic;
using LibAtem;
using LibAtem.Commands.Audio;
using LibAtem.Commands.Audio.Fairlight;
using LibAtem.Commands.DataTransfer;
using LibAtem.Net;

namespace AtemProxy
{
    public static class AtemProxyUtil
    {

        public static readonly Type[] AudioLevelCommands = new[]
        {
            typeof(FairlightMixerMasterLevelsCommand), typeof(FairlightMixerSourceLevelsCommand),
            typeof(FairlightMixerSendLevelsCommand), typeof(AudioMixerLevelsCommand),
            typeof(AudioMixerSendLevelsCommand)
        };

        public static readonly Type[] TransferCommands = new[]
        {
            typeof(DataTransferAbortCommand), typeof(DataTransferAckCommand),
            typeof(DataTransferCompleteCommand), typeof(DataTransferDataCommand), typeof(DataTransferErrorCommand),
            typeof(DataTransferDownloadRequestCommand), typeof(DataTransferUploadContinueCommand),
            typeof(DataTransferUploadRequestCommand)
        };

        public static readonly Type[] LockCommands = new[] {typeof(LockObtainedCommand), typeof(LockStateSetCommand)};

        public static byte[] ParsedCommandToBytes(ParsedCommandSpec cmd)
        {
            var build = new CommandBuilder(cmd.Name);
            build.AddByte(cmd.Body);
            return build.ToByteArray();
        }

        public static  List<OutboundMessage> CommandsToMessages(List<byte[]> commands)
        {
            var messages = new List<OutboundMessage>();
            while (commands.Count > 0)
            {
                var builder = new OutboundMessageBuilder();

                int removeCount = 0;
                foreach (byte[] data in commands)
                {
                    if (!builder.TryAddData(data))
                        break;

                    removeCount++;
                }

                if (removeCount == 0)
                {
                    throw new Exception("Failed to build message!");
                }

                commands.RemoveRange(0, removeCount);
                messages.Add(builder.Create());
                // Log.InfoFormat("Length {0} {1}", builder.currentLength , removeCount);
            }

            return messages;
        }
    }
}