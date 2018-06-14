﻿using Citadel.IPC.Messages;
using System;
using System.Collections.Generic;

namespace Citadel.Platform.Common.IPC
{
    /// <summary>
    /// Used by IPCClient to track messages which are expecting replies.
    /// </summary>
    public class IPCMessageTracker
    {
        /// <summary>
        /// The internal wrapper for a message.
        /// 
        /// BaseMessage contains all the information needed to track the replies from the server (assuming the server has the correct info filled out)
        /// </summary>
        private class IPCMessageData
        {
            public BaseMessage Message { get; set; }
            public GenericReplyHandler Handler { get; set; }
        }

        private List<IPCMessageData> m_messageList;
        private object m_lock;

        public IPCMessageTracker()
        {
            m_messageList = new List<IPCMessageData>();
            m_lock = new object();
        }

        /// <summary>
        /// Adds a message and a handler to the tracker to wait until HandleMessage is called with a reply.
        /// </summary>
        /// <param name="message">The message which was sent to the server</param>
        /// <param name="handler">The handler for the reply message.</param>
        public void AddMessage(BaseMessage message, GenericReplyHandler handler)
        {
            lock (m_lock)
            {
                m_messageList.Add(new IPCMessageData() { Message = message, Handler = handler });
            }
        }

        /// <summary>
        /// Called by IPCClient.OnServerMessage() before any of its own handlers handle the message.
        /// 
        /// If this function returns true, OnServerMessage() does not handle the message.
        /// </summary>
        /// <param name="message">The reply message from the IPC server</param>
        /// <returns>true if handled, false if not</returns>
        public bool HandleMessage(BaseMessage message)
        {
            try
            {
                lock (m_lock)
                {
                    for (int i = 0; i < m_messageList.Count; i++)
                    {
                        IPCMessageData data = null;
                        data = m_messageList[i];

                        if (data.Message.Id == message.ReplyToId)
                        {
                            m_messageList.Remove(data);

                            if (data.Handler != null)
                            {
                                bool ret = data.Handler(message);
                                return ret;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }

                    return false;
                }
            }
            catch (Exception e)
            {
                return false;
            }
        }
    }
}