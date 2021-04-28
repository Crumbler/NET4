using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;

namespace ProxyService
{
    public class RequestReflector
    {
        private Socket clientSocket, serverSocket;
        private byte[] clientBuf, serverBuf;
        private string hostName;

        private bool isClosed, isSecure;

        private object closingLock;

        public RequestReflector(Socket ClientSocket)
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            clientSocket = ClientSocket;

            closingLock = new object();

            isClosed = false;
            isSecure = false;

            const int bufSize = 4 * 1024;

            clientBuf = new byte[bufSize];
            serverBuf = new byte[bufSize];

            hostName = "unknown host";
        }

        private void ProcessRequest(int receivedBytes)
        {
            // If using HTTPS, just forward packets
            if (isSecure)
            {
               serverSocket.Send(clientBuf, receivedBytes, SocketFlags.None);

                return;
            }

            var memoryStream = new MemoryStream(clientBuf, 0, receivedBytes);

            var reader = new StreamReader(memoryStream, Encoding.ASCII);

            // Read all lines
            string message = reader.ReadToEnd();

            string[] lines = message.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            lines[0] = Utils.RemoveAbsoluteName(lines[0]);

            if (lines[0].StartsWith("CONNECT"))
                isSecure = true;

            string hostLine = null;
            for (int i = 1; i < lines.Length; ++i)
                if (lines[i].StartsWith("Host"))
                {
                    hostLine = lines[i];
                    break;
                }
            
            // Get host name
            hostName = hostLine.Split(' ')[1];

            string[] hostNameSplits = hostName.Split(':');

            lock (ProxyService.logLock)
                ProxyService.Log(hostName + " " + lines[0]);

            int serverPort = isSecure ? 443 : 80;

            // Parse port if specified
            if (hostNameSplits.Length > 1)
                serverPort = Convert.ToInt32(hostNameSplits[1]);

            try
            {
                if (!serverSocket.Connected)
                {
                    serverSocket.Connect(hostNameSplits[0], serverPort);

                    if (isSecure)
                    {
                        byte[] okMessage = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");

                        clientSocket.Send(okMessage);

                        return;
                    }
                }
            }
            catch (Exception)
            {
                throw new SocketException();
            }

            string newMessage = string.Join("\r\n", lines);

            byte[] sendBuf = Encoding.ASCII.GetBytes(newMessage);

            serverSocket.Send(sendBuf);

            memoryStream.Close();
            reader.Close();
        }

        private void ProcessResponse(int receivedBytes)
        {
            clientSocket.Send(serverBuf, receivedBytes, SocketFlags.None);
        }

        private void TryClose()
        {
            lock (closingLock)
            {
                if (isClosed)
                    return;

                isClosed = true;
            }

            if (clientSocket.Connected)
                clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();

            if (serverSocket.Connected)
                serverSocket.Shutdown(SocketShutdown.Both);
            serverSocket.Close();

            lock (ProxyService.logLock)
                ProxyService.Log($"Received 0 bytes from client. Closing connection with {hostName}");
        }

        private async void ReceiveClient()
        {
            var segment = new ArraySegment<byte>(clientBuf);

            try
            {
                while (true)
                {
                    int receivedBytes = await clientSocket.ReceiveAsync(segment, SocketFlags.None);

                    if (receivedBytes == 0)
                    {
                        TryClose();
                        break;
                    }

                    ProcessRequest(receivedBytes);
                }
            }
            catch (SocketException)
            {
                // If connection closed, stop
                TryClose();
            }
        }

        private async void ReceiveServer()
        {
            while (!serverSocket.Connected && clientSocket.Connected && !isClosed)
                await Task.Delay(100);

            if (!clientSocket.Connected || isClosed)
                return;

            var segment = new ArraySegment<byte>(serverBuf);

            try
            {
                while (true)
                {
                    int receivedBytes = await serverSocket.ReceiveAsync(segment, SocketFlags.None);

                    if (receivedBytes == 0)
                    {
                        TryClose();
                        break;
                    }

                    ProcessResponse(receivedBytes);
                }
            }
            catch (SocketException)
            {
                // If connection closed, stop

                TryClose();
            }
        }

        public void Run()
        {
            ReceiveClient();

            ReceiveServer();
        }
    }
}
