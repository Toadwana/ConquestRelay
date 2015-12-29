#region License
/*
 * RelayManager.cs
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

namespace ConquestRelay
{
  class RelayManager
  {
    public static RelayManager instance
    {
      get
      {
        if (_instance == null)
          _instance = new RelayManager();
        return _instance;
      }
    }
    private static RelayManager _instance = null;

    // managed session data
    private List<Relay> relays = new List<Relay>();
    private Dictionary<Guid, GameSession> sessions = new Dictionary<Guid, GameSession>();  // key = server id


    public void AddRelay(Relay relay)
    {
      relays.Add(relay);
    }

    public void CreateGameSession(Relay server)
    {
      GameSession newSession = new GameSession();
      newSession.server = server;
      sessions.Add(server.relayID, newSession);
    }

    public Relay GetRelay(Guid guid)
    {
      foreach (Relay relay in relays)
      {
        if (relay.relayID == guid)
        {
          return relay;
        }
      }

      return null;
    }

    public Guid GetSessionID(Guid serverGuid)
    {
      return sessions[serverGuid].sessionID;
    }

    /// <summary>
    /// Get list of known sessions
    /// </summary>
    /// <param name="sessionIdentifiers">Key: ID of server; value: ID of session</param>
    public void GetSessionList(out Dictionary<Guid, Guid> sessionIdentifiers)
    {
      sessionIdentifiers = new Dictionary<Guid, Guid>();

      foreach (KeyValuePair<Guid, GameSession> session in sessions)
      {
        sessionIdentifiers.Add(session.Key, session.Value.sessionID);
      }
    }

    public void ConnectToServer(Relay client, string serverName)
    {
      Console.WriteLine("Connecting to server by name is not implemented anymore");
    }

    public void ConnectToServer(Relay client, Guid serverID)
    {
      Relay server = GetRelay(serverID);
      if ((server != null) && (server.GetGameType() == Relay.GameType.Server) && sessions.ContainsKey(serverID))
      {
        sessions[serverID].AddClient(client);
        client.myServer = server;

        client.SendMessage("ServerConnect " + serverID.ToString());
        server.SendMessage("CreatePlayer " + client.relayID.ToString());

        Console.WriteLine("Client {0} connected to server {1}", client.GetName(), serverID.ToString());

        return;
      }
      else
      {
        Console.WriteLine("Client {0} failed to connect to server {1}", client.GetName(), serverID.ToString());
      }
    }

    public void ExchangeRelays(Relay oldRelay, Relay newRelay)
    {
      if (oldRelay.myServer != null && (oldRelay.myServer.GetGameType() == Relay.GameType.Server) && sessions.ContainsKey(oldRelay.myServer.relayID))
      {
        sessions[oldRelay.myServer.relayID].RemoveClient(oldRelay);
        sessions[oldRelay.myServer.relayID].AddClient(newRelay);
      }
      // remove relay, but do not disconnect the client from the server; the server shall continue to think that the client is still there
      relays.Remove(oldRelay); 
      //AddRelay(newRelay);
    }

    public void RemoveRelay(Relay relay, bool disconnect = true)
    {
      if ((relay.GetGameType() == Relay.GameType.Client) && (relay.myServer != null))
      {
        Guid serverID = relay.myServer.relayID;
        if (sessions.ContainsKey(serverID))
        {
          sessions[serverID].RemoveClient(relay);
          // only disconnect from server if necessary
          if (disconnect)
          {
            relay.myServer.DisconnectClient(relay.relayID);
          }
        }
      }
      else if (relay.GetGameType() == Relay.GameType.Server)
      {
        Guid serverID = relay.relayID;
        if (sessions.ContainsKey(serverID))
        {
          List<Relay> sessionClients = sessions[serverID].GetClients();
          foreach (Relay client in sessionClients)
          {
            client.DisconnectServer();
          }
          sessions.Remove(serverID);
        }
      }
 
      if (relays.Contains(relay))
      {
        relays.Remove(relay);
      }
    }
  }
}
