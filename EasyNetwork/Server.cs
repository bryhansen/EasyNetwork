//-----------------------------------------------------------------------------
// Server.cs Copyright(c) 2015 Jacob Christensen and Bryan Hansen
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
//-----------------------------------------------------------------------------
namespace EasyNetwork
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;

    using NetMQ;

    /// <summary>
    /// Server class which handles connections to any number of Clients
    /// </summary>
    public class Server
    {
        /// <summary> NetMQ socket used for communication with clients </summary>
        private NetMQSocket netMqSocket;

        /// <summary> String defining connection type, address, and port </summary>
        private string serverConnectionString;

        /// <summary> String defining connection type, address, and port </summary>
        private Thread listenerThread = null;

        /// <summary> Flag used to know when to shut down listener thread </summary>
        private bool isDone = false;

        /// <summary> String defining connection type, address, and port </summary>
        private ConcurrentQueue<NetMQMessage> receiveQueue = new ConcurrentQueue<NetMQMessage>();

        /// <summary> List of all clients who have communicated with this server </summary>
        private List<Guid> clientList = new List<Guid>();

        /// <summary>
        /// Initializes a new instance of the Server class
        /// </summary>
        /// <param name="connectionString">How the server should be set up, to listen on TCP port 7954 to all incoming connections: "tcp://*:7954"</param>
        public Server(string connectionString)
        {
            serverConnectionString = connectionString;
            NetMQContext context = NetMQContext.Create();
            netMqSocket = context.CreateRouterSocket();
            netMqSocket.Bind(serverConnectionString);
        }

        /// <summary>
        /// Gets the list of clients
        /// </summary>
        public List<Guid> ClientList
        {
            get { return clientList; }
        }

        /// <summary>
        /// Starts the server
        /// </summary>
        public void Start()
        {
            isDone = false;
            listenerThread = new Thread(new ThreadStart(Listener));
            listenerThread.Start();
        }

        /// <summary>
        /// Stops the server
        /// </summary>
        public void Stop()
        {
            isDone = true;
            listenerThread.Join();

            netMqSocket.Unbind(serverConnectionString);
            netMqSocket.Close();
        }

        /// <summary>
        /// Attempts to receive an object from a client
        /// </summary>
        /// <param name="clientId">Returns the ID of the client from which the object was received</param>
        /// <returns>The object received, or NULL if there was nothing to receive</returns>
        public Object Receive(out Guid clientId)
        {
            NetMQMessage netMqMsg = null;            

            if (receiveQueue.TryDequeue(out netMqMsg))
            {
                clientId = new Guid(netMqMsg[0].ToByteArray());
                string typeStr = netMqMsg[2].ConvertToString();
                Type t = Type.GetType(typeStr);

                System.Reflection.MethodInfo method = typeof(BsonMagic).GetMethod("DeserializeObject");
                System.Reflection.MethodInfo generic = method.MakeGenericMethod(t);

                Object[] methodParams = new Object[1] { netMqMsg[3].ToByteArray() };
                return generic.Invoke(null, methodParams);
            }
            else
            {
                clientId = new Guid("00000000000000000000000000000000");
                return null;
            }
        }

        /// <summary>
        /// Send an object to a specified client
        /// </summary>
        /// <typeparam name="T">The type of the object being sent</typeparam>
        /// <param name="objectToSend">The object to send to the client</param>
        /// <param name="clientId">ID of the client to whom the object will be sent</param>
        public void Send<T>(T objectToSend, Guid clientId)
        {
            byte[] data = BsonMagic.SerializeObject<T>(objectToSend);

            NetMQMessage msgToSend = new NetMQMessage();
            msgToSend.Append(clientId.ToByteArray());
            msgToSend.AppendEmptyFrame();
            msgToSend.Append(typeof(T).AssemblyQualifiedName);
            msgToSend.Append(data);

            netMqSocket.SendMultipartMessage(msgToSend);
        }

        /// <summary>
        /// Thread which listens for incoming NetMQMessages from clients
        /// </summary>
        private void Listener()
        {            
            while (!isDone)
            {
                NetMQMessage receivedMsg = null;
                bool result = netMqSocket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1.0), ref receivedMsg);

                if (result)
                {
                    // Add to client list if this is first time this ID has been seen
                    Guid receivedId = new Guid(receivedMsg[0].ToByteArray());

                    if (!clientList.Contains(receivedId))
                    {
                        clientList.Add(receivedId);
                    }

                    // Enqueue for processing
                    receiveQueue.Enqueue(receivedMsg);
                }
            }
        }
    }
}
