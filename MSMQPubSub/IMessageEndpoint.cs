using System;

namespace MSMQPubSub
{
    public interface IMessageEndpoint : IDisposable
    {
        string Uri { get; }

        void Send<TMessage>(TMessage message, IMessageEndpoint responseEndpoint = null, TimeSpan? timeToLive = null);

        void SendControlCommand(Int32 command, string descriptor, object message, IMessageEndpoint responseEndpoint = null, TimeSpan? timeToLive = null);
        void Subscribe(int command, Action<object> action);
    }
}