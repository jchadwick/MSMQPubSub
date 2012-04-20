using System;
using System.Diagnostics;
using System.IO;
using System.Messaging;
using System.Text;
using Newtonsoft.Json;

namespace MSMQPubSub
{
    public class JsonMessageFormatter : IMessageFormatter
    {
        private static readonly JsonSerializerSettings DefaultSerializerSettings = 
            new JsonSerializerSettings {
                                           TypeNameHandling = TypeNameHandling.All
                                       };

        private readonly JsonSerializerSettings _serializerSettings;


        public Encoding Encoding { get; set; }


        public JsonMessageFormatter(Encoding encoding = null)
            : this(encoding, null)
        {
        }

        internal JsonMessageFormatter(Encoding encoding, JsonSerializerSettings serializerSettings = null)
        {
            Encoding = encoding ?? Encoding.UTF8;
            _serializerSettings = serializerSettings ?? DefaultSerializerSettings;
        }


        public bool CanRead(Message message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            var stream = message.BodyStream;

            return stream != null
                   && stream.CanRead
                   && stream.Length > 0;
        }

        public object Clone()
        {
            return new JsonMessageFormatter(Encoding, _serializerSettings);
        }

        public object Read(Message message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            if(CanRead(message) == false)
                return null;

            using (var reader = new StreamReader(message.BodyStream, Encoding))
            {
                var json = reader.ReadToEnd();
                Trace.WriteLine(string.Format("Message {0} serialized body:\r\n{1}", message.Id, json));
                return JsonConvert.DeserializeObject(json, _serializerSettings);
            }
        }

        public void Write(Message message, object obj)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            if (obj == null)
                throw new ArgumentNullException("obj");

            string json = JsonConvert.SerializeObject(obj, Formatting.None, _serializerSettings);

            Trace.WriteLine(string.Format("Message {0} serialized body:\r\n{1}", message.Id, json));

            message.BodyStream = new MemoryStream(Encoding.GetBytes(json));

            //Need to reset the body type, in case the same message
            //is reused by some other formatter.
            message.BodyType = 0;
        }
    }
}
