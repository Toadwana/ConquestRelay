#region License
/*
 * Relay.cs
 *
 * The MIT License
 *
 * Copyright (c) 2015 Ingo Scholz
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;


namespace ConquestRelay
{
  class Relay : WebSocketBehavior
  {
    public enum GameType
    {
      Unknown,
      Server,
      Client
    }

    private string     name;
    private static int number = 0;
    private int myNumber;
    public Guid relayID { get; private set; }
    private GameType gameType = GameType.Unknown;
    public Relay myServer { get; set; }

    public Relay()
    {
      relayID = Guid.NewGuid();
      myNumber = GetNumber();
      Console.WriteLine("Relay {0} created", myNumber);
    }

    public string GetQueryName()
    {
      var name = Context.QueryString["name"];
      return !name.IsNullOrEmpty ()
             ? name
             : (GetNumber ().ToString());
    }

    public string GetName()
    {
      return name;
    }

    private static int GetNumber()
    {
      return Interlocked.Increment (ref number);
    }

    public GameType GetGameType()
    {
      return gameType;
    }

    public void ChangeClientName(string newName)
    {
      name = newName;
      Send("ChangeName " + newName);
    }

    public void DisconnectClient(Guid clientID)
    {
      if (gameType == GameType.Server)
      {
        Console.WriteLine("Disconnecting client {0} from server {1}", clientID, relayID);
        Send("Disconnect " + clientID);
      }
      else
      {
        Console.WriteLine("Error: DisconnectClient can only be called on server");
      }
    }

    public void DisconnectServer()
    {
      if (gameType == GameType.Client)
      {
        Console.WriteLine("Disconnecting client {0} from server {1} upon server shutdown", name, myServer.GetName());
        Send("ServerDisconnect");
      }
      else
      {
        Console.WriteLine("Error: DisconnectServer can only be called on client");
      }
    }

    public void SendMessage(string message)
    {
      Console.WriteLine("Sending message on Relay {0}", myNumber);
      Send(message);
    }

    private void SendSessionList()
    {
      Dictionary<Guid, Guid> sessionIdentifiers;   // Key: ID of server; value: ID of session
      RelayManager.instance.GetSessionList(out sessionIdentifiers);

      string sessionString = "SessionList";
      foreach (KeyValuePair<Guid, Guid> session in sessionIdentifiers)
      {
        // SessionList <ServerName> <ServerGuid> <SessionGuid>
        sessionString = sessionString + " " + RelayManager.instance.GetRelay(session.Key).GetName() + " " + session.Key.ToString() + " " + session.Value.ToString();
      }

      SendMessage(sessionString);
    }

    protected override void OnOpen()
    {
      name = GetQueryName();
      Console.WriteLine("Client {0} connected", name);
      RelayManager.instance.AddRelay(this);
    }

    /// <summary>
    /// Distinguishes between four types of messages: 
    /// * "Server"/"Client": set role of the connecting client
    /// * "RequestServerList": client requests a list of servers available
    /// * "ConnectTo servername": client request to connect to a certain server
    /// * "Cmd receiver parameters ...": relay message to receiver
    /// </summary>
    /// <param name="e">The message to be parsed</param>
    protected override void OnMessage (MessageEventArgs e)
    {
      if (e.Type == Opcode.Text)
      {
        // determine role of connecting client (server or client) or handle request for list of servers
        if (e.Data.StartsWith("Server", StringComparison.Ordinal))
        {
          gameType = GameType.Server;
          RelayManager.instance.CreateGameSession(this);
          Send("ServerID " + relayID.ToString());
          Send("SessionID " + RelayManager.instance.GetSessionID(relayID));
          return;
        }
        if (e.Data.StartsWith("Client", StringComparison.Ordinal))
        {
          gameType = GameType.Client;
          // new connection?
          if (e.Data == "Client")
          {
            Send("ClientID " + relayID.ToString());
          }
          // in case of a reconnect search for the existing relay and use its ID
          else
          {
            Guid oldClientID;
            try
            {
              oldClientID = new Guid(e.Data.Replace("Client ", ""));
              Console.WriteLine("Reconnecting: {0}", oldClientID.ToString());
            }
            catch (FormatException exc)
            {
              Console.WriteLine("{0}: {1}", exc.Message, e.Data);
              Send("Reconnect failed");
              return;
            }
            if (oldClientID != Guid.Empty)
            {
              Relay oldRelay = RelayManager.instance.GetRelay(oldClientID);
              if (oldRelay != null)
              {
                relayID = oldClientID;
                myServer = oldRelay.myServer;
                RelayManager.instance.ExchangeRelays(oldRelay, this);
                Send("Reconnect successful");
              }
            }
          }
          return;
        }
        if (e.Data.StartsWith("RequestServerList", StringComparison.Ordinal))
        {
          SendSessionList();
          return;
        }

        // connection request by a client
        if (e.Data.StartsWith("ConnectTo", StringComparison.Ordinal) && (gameType == GameType.Client))
        {
          string connectionType = e.Data.Replace("ConnectTo ", "");
          if (connectionType.StartsWith("Name", StringComparison.Ordinal))
          {
            string serverName = connectionType.Replace("Name ", "");

            Console.WriteLine("Trying to find server {0}", serverName);

            RelayManager.instance.ConnectToServer(this, serverName);
            return;
          }
          else if (connectionType.StartsWith("Guid", StringComparison.Ordinal))
          {
            string serverGuid = connectionType.Replace("Guid ", "");

            Console.WriteLine("Trying to find server {0}", serverGuid);

            RelayManager.instance.ConnectToServer(this, new Guid(serverGuid));
            return;
          }
        }

        // generic command which will be relayed to designated receiver
        if (e.Data.StartsWith("Cmd", StringComparison.Ordinal))
        {
          string[] messageParts = e.Data.Split(new Char [] {' '});
          string receiver = messageParts[1];
          // replace receiver with sender before relaying the message
          messageParts[1] = messageParts[1].Replace(receiver, relayID.ToString());
          // rebuild message string
          string relayedMessage = messageParts[0];
          for (int partCount = 1; partCount < messageParts.Length; ++partCount)
          {
            relayedMessage = relayedMessage + " " + messageParts[partCount];
          }

          Console.WriteLine("Message for {0}: {1}", receiver, relayedMessage);

          Relay receiverRelay = RelayManager.instance.GetRelay(new Guid(receiver));
          if (receiverRelay != null)
          {
            receiverRelay.SendMessage(relayedMessage);
          }
          return;
        }
      }

      Send("Message unknown: " + e.Data);
      //Sessions.Broadcast (String.Format ("{0}: {1}", name, e.Data));
    }

    protected override void OnClose(CloseEventArgs e)
    {
      Console.WriteLine("Client {0} has closed its connection", name);

      // do not remove relay of a disconnected player (which sends a disconnect client message to the server)
      // when a client disconnects, as it might try to reconnect later
      //RelayManager.instance.RemoveRelay(this);
      //Sessions.Broadcast (String.Format ("{0} got logged off...", name));
    }
  }
}
