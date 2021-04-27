using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ProxyService
{
    public partial class ProxyService : ServiceBase
    {
        public static object logLock = new object();

        public static Action<string> Log;

        public ProxyService()
        {
            InitializeComponent();

            if (!EventLog.SourceExists("Proxy source"))
                EventLog.CreateEventSource("Proxy source", "Proxy log");

            evlogMain.Source = "Proxy source";
            evlogMain.Log = "Proxy log";
        }

        private static async void BeginListening()
        {
            var tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListener.Bind(new IPEndPoint(IPAddress.Any, 30000));

            tcpListener.Listen(5);

            while (true)
            {
                Socket connectionSocket = await tcpListener.AcceptAsync();

                new RequestReflector(connectionSocket).Run();
            }
        }

        protected override void OnStart(string[] args)
        {
            Log = evlogMain.WriteEntry;

            BeginListening();
        }

        protected override void OnStop()
        {
        }
    }
}
