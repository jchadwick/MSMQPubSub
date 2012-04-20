using System;
using System.Diagnostics;
using System.Messaging;
using MSMQPubSub;

public enum ControlCommand : byte
{
    Message = 0,
    Subscribe = 1,
    Unsubscribe = 2,
}

public class Program
{
    static void Main(string[] args)
    {
        Trace.Listeners.Add(new ConsoleTraceListener());

        var executable = Environment.CommandLine.Replace("\"", string.Empty).Replace(".vshost", string.Empty);

        Process remoteProcess = null;
        string localUri, remoteUri;

        if (args.Length == 0)
        {
            const string endpoint1 = "msmq://endpoint1@localhost";
            const string endpoint2 = "msmq://endpoint2@localhost";

            localUri = endpoint1;
            remoteUri = endpoint2;

            var startInfo = new ProcessStartInfo(executable, string.Format("\"{0}\" \"{1}\"", endpoint2, endpoint1));
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false;

            remoteProcess = Process.Start(startInfo);
        }
        else if (args.Length != 2)
        {
            Console.WriteLine("Usage: {0} [local endpoint] [remote endpoint]", executable);
            return;
        }
        else
        {
            localUri = args[0];
            remoteUri = args[1];
        }

        var instance = new Program(localUri, remoteUri);
        try
        {
            instance.Start();
        }
        finally
        {
            if (remoteProcess != null && !remoteProcess.HasExited) 
                remoteProcess.Kill();
        }
    }


    private readonly MsmqMessageEndpoint _localEndpoint;
    private readonly IMessageEndpoint _remoteEndpoint;

    public Program(string localUri, string remoteUri)
    {
        _localEndpoint = new MsmqMessageEndpoint(localUri);
        _remoteEndpoint = new MsmqMessageEndpoint(remoteUri);
    }

    public void Start()
    {
        using(_localEndpoint)
        {
            _localEndpoint.Subscribe((int)ControlCommand.Message,
                message => Trace.WriteLine(string.Format("Received Message {0}:\r\n{1}", message.GetType(), message.ToString())));

            _localEndpoint.Subscribe((int)ControlCommand.Subscribe,
                message => Trace.WriteLine(string.Format("Received Subscribe request for {0}", ((Message)message).Label)));

            _localEndpoint.Subscribe((int)ControlCommand.Unsubscribe,
                message => Trace.WriteLine(string.Format("Received Unsubscribe request for {0}", ((Message)message).Label)));

            _localEndpoint.Start();

            ShowMenu();
        }
    }

    private void ShowMenu()
    {
        Console.WriteLine("*******  Message Endpoint Demo  **********");
        Console.WriteLine("[S]ubscribe, [U]nsubscribe, Send a [M]essage, or e[X]it");

        ConsoleKey consoleKey;
        while ((consoleKey = Console.ReadKey(true).Key) != ConsoleKey.X)
        {
            if(consoleKey == ConsoleKey.S)
                SendControlMessage(ControlCommand.Subscribe);
            if(consoleKey == ConsoleKey.U)
                SendControlMessage(ControlCommand.Unsubscribe);
            if(consoleKey == ConsoleKey.M)
                SendMessage();
        }
    }

    private void SendControlMessage(ControlCommand command)
    {
        _remoteEndpoint.SendControlCommand((int)command, command.ToString(), null, _remoteEndpoint);
    }

    private void SendMessage()
    {
        var message = new TestMessage { Timestamp = DateTime.Now, Message = "Hello world!" };
        _remoteEndpoint.Send(new [] { message, message }, _localEndpoint);
    }

    public class TestMessage
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Timestamp, Message);
        }
    }
}
