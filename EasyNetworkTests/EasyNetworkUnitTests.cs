//-----------------------------------------------------------------------------
// EasyNetworkUnitTests.cs Copyright(c) 2015 Jacob Christensen and Bryan Hansen
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

namespace EasyNetworkTests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for the EasyNetwork classes
    /// </summary>
    [TestClass]
    public class EasyNetworkUnitTests
    {
        /// <summary>
        /// Tests that multiple clients can send data to the server
        /// </summary>
        [TestMethod]
        public void ClientToServer()
        {
            EasyNetwork.Server server = new EasyNetwork.Server("tcp://*:1982");
            EasyNetwork.Client clientOne = new EasyNetwork.Client("tcp://localhost:1982");
            EasyNetwork.Client clientTwo = new EasyNetwork.Client("tcp://localhost:1982");
            MyTestObject obj = new MyTestObject();
            obj.Value = 3.14159f;
            obj.Text = "Hello World!";

            OtherTestObject obj2 = new OtherTestObject();
            obj2.Value = 1234;
            obj2.Text = "Hello World Again!";

            server.Start();
            clientOne.Start();
            clientTwo.Start();

            clientOne.Send<MyTestObject>(obj);
            clientTwo.Send<OtherTestObject>(obj2);

            // Generically receive object at server from first client
            Object receivedObject = null;
            while (receivedObject == null)
            {
                Guid firstId;
                receivedObject = server.Receive(out firstId);

                if (receivedObject is MyTestObject)
                {
                    MyTestObject actualObjectOne = receivedObject as MyTestObject;
                    Assert.AreEqual(clientOne.Id, firstId);
                    Assert.AreEqual(obj.Value, actualObjectOne.Value);
                    Assert.AreEqual(obj.Text, actualObjectOne.Text);
                }

                Thread.Sleep(100);
            }

            // Generically receive object at server from second client
            receivedObject = null;            
            while (receivedObject == null)
            {
                Guid secondId;
                receivedObject = server.Receive(out secondId);

                if (receivedObject is OtherTestObject)
                {
                    OtherTestObject actualObjectTwo = receivedObject as OtherTestObject;
                    Assert.AreEqual(clientTwo.Id, secondId);
                    Assert.AreEqual(obj2.Value, actualObjectTwo.Value);
                    Assert.AreEqual(obj2.Text, actualObjectTwo.Text);
                }

                Thread.Sleep(100);
            }

            server.Stop();
            clientOne.Stop();
            clientTwo.Stop();       
        }
        
        /// <summary>
        /// Verifies that the server can respond to requests from multiple clients
        /// </summary>
        [TestMethod]
        public void ServerResponses()
        {
            EasyNetwork.Server server = new EasyNetwork.Server("tcp://*:1982");
            EasyNetwork.Client clientOne = new EasyNetwork.Client("tcp://localhost:1982");
            EasyNetwork.Client clientTwo = new EasyNetwork.Client("tcp://localhost:1982");

            server.Start();
            clientOne.Start();
            clientTwo.Start();

            // Build and send a message to the server
            MyTestObject clientOneMessage = new MyTestObject();
            clientOneMessage.Value = 3.14159f;
            clientOneMessage.Text = "Hello Server!";
            clientOne.Send<MyTestObject>(clientOneMessage);

            // Server receives, handles, and responds to message
            Object receivedObj = null;
            while (receivedObj == null)
            {
                Guid receivedId;
                receivedObj = server.Receive(out receivedId);

                if (receivedObj is MyTestObject)
                {
                    MyTestObject receivedMessage = receivedObj as MyTestObject;
                    MyTestObject responseMessage = new MyTestObject();
                    responseMessage.Value = receivedMessage.Value * 2;
                    responseMessage.Text = "Howdy client!";

                    server.Send<MyTestObject>(receivedMessage, receivedId);                  
                }

                Thread.Sleep(100);
            }

            Thread.Sleep(1000);

            // Make sure the second client didn't receive anything
            Object clientTwoReceived = clientTwo.Receive();
            Assert.IsNull(clientTwoReceived);

            // The clientOne should have received the message destined for it
            Object clientReceived = clientOne.Receive();
            Assert.IsNotNull(clientReceived);
            Assert.IsTrue(clientReceived is MyTestObject);

            server.Stop();
            clientOne.Stop();
            clientTwo.Stop();
        }

        /// <summary>
        /// Tests that the server maintains a list of clients and that it can actively push to all of them
        /// </summary>
        [TestMethod]
        public void PushToAllClients()
        {            
            EasyNetwork.Server server = new EasyNetwork.Server("tcp://*:1982");
            List<EasyNetwork.Client> clients = new List<EasyNetwork.Client>();
            const int NumClients = 10;

            for (int i = 0; i < NumClients; i++)
            {
                clients.Add(new EasyNetwork.Client("tcp://localhost:1982"));
            }

            server.Start();
            
            foreach (EasyNetwork.Client client in clients)
            {
                client.Start();
            }

            // Clients are registered with server after any messaging, so send something from each
            MyTestObject helloFromClient = new MyTestObject();
            helloFromClient.Value = 42.42f;
            helloFromClient.Text = "Hello Server!";

            foreach (EasyNetwork.Client client in clients)
            {
                client.Send<MyTestObject>(helloFromClient);
            }

            // Server should keep track of each client after hearing from them
            while (server.ClientList.Count < NumClients)
            {
                Thread.Sleep(100);
            }

            Assert.AreEqual(NumClients, server.ClientList.Count);

            // Send something to each client
            foreach (Guid clientId in server.ClientList)
            {
                MyTestObject helloFromServer = new MyTestObject();
                helloFromServer.Value = 4.2f;
                helloFromServer.Text = clientId.ToString();
                server.Send<MyTestObject>(helloFromServer, clientId);
            }

            // Give a little time to make sure all the messages arrived...
            Thread.Sleep(1000);

            // Make sure each client has received their personalized messages from the server
            foreach (EasyNetwork.Client client in clients)
            {
                Object genericObject = client.Receive();
                Assert.IsNotNull(genericObject);

                if (genericObject is MyTestObject)
                {
                    MyTestObject receivedObject = genericObject as MyTestObject;
                    Assert.AreEqual(client.Id.ToString(), receivedObject.Text);
                }
            }

            server.Stop();

            foreach (EasyNetwork.Client client in clients)
            {
                client.Stop();
            }
        }

        /// <summary>
        /// Object used to test passing objects between client and server
        /// </summary>
        private class MyTestObject
        {
            /// <summary> Gets or sets the value of Value </summary>
            public float Value { get; set; }

            /// <summary> Gets or sets the value of Text </summary>
            public string Text { get; set; }
        }

        /// <summary>
        /// Object used to test passing objects between client and server
        /// </summary>
        private class OtherTestObject
        {
            /// <summary> Gets or sets the value of Value </summary>
            public int Value { get; set; }

            /// <summary> Gets or sets the value of Text </summary>
            public string Text { get; set; }
        }
    }
}
