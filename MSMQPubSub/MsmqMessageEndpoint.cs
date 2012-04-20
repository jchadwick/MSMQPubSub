using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Messaging;
using System.Transactions;

namespace MSMQPubSub
{
    /// <summary>
    /// A Message Endpoint that uses the MSMQ transport layer
    /// </summary>
    /// <example>
    /// new MsmqMessageEndpoint().Send("Hello, world!");
    /// </example>
    public class MsmqMessageEndpoint : IMessageEndpoint
    {
        private const int MessageCommand = 0;

        private readonly ICollection<KeyValuePair<int, Action<object>>> _messageHandlers;
        private readonly string _messageQueueName;
        private readonly string _errorQueueName;

        public string Uri { get; private set; }

        public IMessageFormatter MessageFormatter { get; set; }

        protected internal MessageQueue MessageQueue
        {
            get { return _messageQueue.Value; }
        }
        private readonly Lazy<MessageQueue> _messageQueue;


        public MsmqMessageEndpoint(string uri)
        {
            _messageHandlers = new ConcurrentList<KeyValuePair<int, Action<object>>>();

            MessageFormatter = new JsonMessageFormatter();
            Uri = uri;

            _messageQueueName = GetEndpointName(uri);
            _errorQueueName = _messageQueueName + "_errors";

            _messageQueue = new Lazy<MessageQueue>(() =>
                                                   new MessageQueue(_messageQueueName)
                                                       {
                                                           MessageReadPropertyFilter = { AppSpecific = true }
                                                       });
        }

        public void Subscribe(int command, Action<object> action)
        {
            _messageHandlers.Add(new KeyValuePair<int, Action<object>>(command, action));
        }


        public void Start()
        {
            Trace.TraceInformation("Starting listener on MSMQ endpoint {0}...", _messageQueueName);

            CreateTransactionalQueueIfNotExists(_messageQueueName);
            CreateTransactionalQueueIfNotExists(_errorQueueName);

            MessageQueue.PeekCompleted += QueuePeekCompleted;
            MessageQueue.BeginPeek();

            Trace.TraceInformation("Listening on MSMQ endpoint {0}.", _messageQueueName);
        }

        public void Stop()
        {
            if (_messageQueue.IsValueCreated)
            {
                MessageQueue.PeekCompleted -= QueuePeekCompleted;
            }

            Trace.TraceInformation("Stopped listening on MSMQ endpoint {0}.", _messageQueueName);
        }

        public void Send<TMessage>(TMessage message, IMessageEndpoint responseEndpoint = null, TimeSpan? timeToLive = null)
        {
            var msmqMessage = BuildMsmqMessage(
                MessageCommand, 
                typeof(TMessage).Name, 
                message, 
                responseEndpoint, 
                timeToLive
                );

            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                MessageQueue.Send(msmqMessage, transaction);
                transaction.Commit();
            }

            Trace.WriteLine(string.Format("Sent {0} message {1}", typeof(TMessage).Name, msmqMessage.Id));
        }

        public void SendControlCommand(Int32 command, string descriptor, object message, IMessageEndpoint responseEndpoint = null, TimeSpan? timeToLive = null)
        {
            var msmqMessage = BuildMsmqMessage(command, descriptor, message, responseEndpoint, timeToLive);

            using (var transaction = new TransactionScope())
            {
                MessageQueue.Send(msmqMessage, MessageQueueTransactionType.Automatic);
                transaction.Complete();
            }

            Trace.WriteLine(string.Format("Sent command: {0}", command.ToString()));
        }

        private Message BuildMsmqMessage(Int32 command, string descriptor, object message,
                                         IMessageEndpoint responseEndpoint, TimeSpan? timeToLive)
        {
            if (responseEndpoint != null && !(responseEndpoint is MsmqMessageEndpoint))
                throw new NotSupportedException("MSMQ endpoints can only talk to each other!");

            MessageQueue responseQueue = null;

            var msmqResponseEndpoint = responseEndpoint as MsmqMessageEndpoint;
            if (msmqResponseEndpoint != null)
                responseQueue = msmqResponseEndpoint.MessageQueue;

            var msmqMessage = new Message
                                  {
                                      AppSpecific = (int) command,
                                      Body = message,
                                      Formatter = (IMessageFormatter)MessageFormatter.Clone(),
                                      Label = descriptor,
                                      Recoverable = true,
                                      ResponseQueue = responseQueue,
                                  };

            if (timeToLive.HasValue)
                msmqMessage.TimeToBeReceived = timeToLive.Value;
            return msmqMessage;
        }

        private static void CreateTransactionalQueueIfNotExists(string queueName)
        {
            if (!MessageQueue.Exists(queueName))
                MessageQueue.Create(queueName, true);
        }

        private void QueuePeekCompleted(object sender, PeekCompletedEventArgs e)
        {
            var messageQueue = (MessageQueue)sender;
            messageQueue.EndPeek(e.AsyncResult);

            Message message = null;
            try
            {
                message = messageQueue.Receive();

                if (message == null)
                    throw new InvalidOperationException("Null message");

                message.Formatter = (IMessageFormatter) MessageFormatter.Clone();

                Trace.WriteLine(string.Format("Received message {0}", message.Id));

                var command = (Int32) message.AppSpecific;

                var handlers = _messageHandlers
                    .Where(x => x.Key == command)
                    .Select(x => x.Value)
                    .Where(x => x != null)
                    .ToArray();

                var handlerCount = handlers.Count();

                Trace.WriteLine(string.Format("Executing {0} handlers", handlerCount));

                if (!handlers.Any())
                    handlers = new Action<object>[] { msg => Trace.TraceWarning("Message {0} has no registered handlers", message.Id) };

                foreach (var handler in handlers)
                {
                    if (command == MessageCommand)
                    {
                        handler(message.Body);
                    }
                    else
                    {
                        handler(message);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(message, ex);
            }

            messageQueue.Refresh();
            messageQueue.BeginPeek();
        }

        private static string GetEndpointName(string value)
        {
            string uri = value;

            if (uri.StartsWith("msmq://"))
                uri = uri.Remove(0, "msmq://".Length);

            var queue = value;
            var machine = ".";
            if (uri.Contains("@"))
            {
                queue = uri.Split('@')[0];
                machine = uri.Split('@')[1];
            }

            if (machine == "localhost")
                machine = ".";

            return machine + "\\private$\\" + queue;
        }

        private void LogError(Message message, Exception exception = null)
        {
            if (exception != null)
                Trace.TraceError("{0}\r\n{1}", exception.Message, exception.StackTrace);

            if (message == null)
                return;

            using (var scope = new TransactionScope())
            {
                using (var errorQueue = new MessageQueue(_errorQueueName))
                {
                    errorQueue.Send(message, MessageQueueTransactionType.Automatic);
                }
                scope.Complete();
            }
        }

        public void Dispose()
        {
            Stop();

            if(_messageQueue != null && _messageQueue.IsValueCreated)
                _messageQueue.Value.Dispose();
        }
    }
}