using System;
using System.Net;
using LibAtem.Commands;
using LibAtem.Net;

namespace AtemProxy
{
    public class AtemServerConnection : AtemConnection
    {
        public AtemServerConnection(EndPoint endpoint, int sessionId) : base(endpoint, sessionId)
        {
        }

        private bool _sentDataDump;

        public bool ReadyForData
        {
            get
            {
                if (_sentDataDump)
                    return false;

                if (!IsOpened)
                    return false;

                return _sentDataDump = true;
            }
        }

        protected override OutboundMessage CompileNextMessage()
        {
            // TODO
            return null;
        }

        public override void QueueCommand(ICommand command)
        {
            throw new NotImplementedException();
        }
    }
}