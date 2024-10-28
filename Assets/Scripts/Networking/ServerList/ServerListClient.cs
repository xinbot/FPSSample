using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Networking.ServerList
{
    public class ServerListClient
    {
        private float _nextUpdate;

        private UnityWebRequest _request;
        private UnityWebRequestAsyncOperation _webRequestAsyncOp;

        private readonly ServerListConfig _config;
        private readonly List<global::ServerInfo> _knownServers = new List<global::ServerInfo>();

        public ServerListClient(ServerListConfig config)
        {
            _config = config;
            _nextUpdate = Time.time;
        }

        public void UpdateKnownServers()
        {
            // This is called every cycle. Use the configured period to throttle the number of REST calls being made.
            float now = Time.time;
            if (_webRequestAsyncOp == null)
            {
                if (now > _nextUpdate)
                {
                    _nextUpdate = now + _config.period;
                    StartGetRequest(_config.url);
                }
            }

            if (_webRequestAsyncOp == null || !_webRequestAsyncOp.isDone)
            {
                return;
            }

            var response = ProcessRequestResponse();
            // Add or update servers 
            foreach (ServerListInfo serverInfo in response.servers)
            {
                // Check if server info already known
                var index = -1;
                for (var i = 0; i < _knownServers.Count; i++)
                {
                    if (_knownServers[i].Address != serverInfo.ip || _knownServers[i].Port != serverInfo.port)
                    {
                        continue;
                    }

                    index = i;
                    break;
                }

                if (index == -1)
                {
                    _knownServers.Add(new global::ServerInfo());
                    index = _knownServers.Count - 1;
                }

                var server = _knownServers[index];
                server.Address = serverInfo.ip;
                server.Port = serverInfo.port;
                server.Name = serverInfo.name;
                server.LevelName = serverInfo.map;
                server.GameMode = serverInfo.description;
                server.Players = serverInfo.playerCount;
                server.MaxPlayers = serverInfo.maxPlayerCount;
                server.LastSeenTime = now;
            }

            // Remove servers that wasn't in the response
            for (var i = _knownServers.Count - 1; i > 0; --i)
            {
                if (_knownServers[i].LastSeenTime < now)
                {
                    _knownServers.RemoveAt(i);
                }
            }
        }

        private void StartGetRequest(string url)
        {
            _request = UnityWebRequest.Get(url);
            _request.downloadHandler = new DownloadHandlerBuffer();
            _webRequestAsyncOp = _request.SendWebRequest();
        }

        private ServerListResponse ProcessRequestResponse()
        {
            if (_request.isNetworkError || _request.isHttpError || _request.isNetworkError)
            {
                var message = $"There was an error calling server list. Error: {_webRequestAsyncOp.webRequest.error}";
                GameDebug.LogError(message);

                // TODO: What is the current methodology for handling exceptions and errors?
                return new ServerListResponse();
            }

            _webRequestAsyncOp = null;

            return JsonUtility.FromJson<ServerListResponse>(_request.downloadHandler.text);
        }

        // unassigned variables
#pragma warning disable 0649
        [Serializable]
        private class ServerListResponse
        {
            public int skip;
            public int take;
            public int total;
            public List<ServerListInfo> servers;
        }
#pragma warning restore

        // unassigned variables
#pragma warning disable 0649
        [Serializable]
        private class ServerListInfo
        {
            public string id;
            public string ip;
            public int port;
            public string name;
            public string description;
            public string map;
            public int playerCount;
            public int maxPlayerCount;
            public string custom;
        }
#pragma warning restore
    }
}