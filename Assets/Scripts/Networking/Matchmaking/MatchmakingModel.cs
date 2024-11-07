using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking.Matchmaking
{
    [Serializable]
    public class MatchmakingPlayer
    {
#pragma warning disable 649
        [SerializeField] private string id;

        [SerializeField] private string properties;
#pragma warning restore 649

        public string Id => id;

        public string Properties
        {
            get { return properties; }
            set { properties = value; }
        }

        internal MatchmakingPlayer(string id)
        {
            this.id = id;
        }
    }

    [Serializable]
    public class MatchmakingRequest
    {
#pragma warning disable 649
        [SerializeField] private List<MatchmakingPlayer> players;

        [SerializeField] private string properties;
#pragma warning restore 649

        public List<MatchmakingPlayer> Players
        {
            get { return players; }
            set { players = value; }
        }

        public string Properties
        {
            get { return properties; }
            set { properties = value; }
        }

        public MatchmakingRequest()
        {
            players = new List<MatchmakingPlayer>();
        }
    }

    [Serializable]
    public class MatchMakingResult
    {
#pragma warning disable 649
        [SerializeField] internal bool success;

        [SerializeField] internal string error;
#pragma warning restore 649
    }

    [Serializable]
    public class AssignmentRequest
    {
#pragma warning disable 649
        [SerializeField] private string id;
#pragma warning restore 649

        public string Id => id;

        internal AssignmentRequest(string id)
        {
            this.id = id;
        }
    }

    [Serializable]
    public class Assignment
    {
#pragma warning disable 649
        [SerializeField] private string connection_string;

        [SerializeField] private string assignment_error;

        [SerializeField] private List<string> roster;
#pragma warning restore 649

        public string ConnectionString => connection_string;
        public string AssignmentError => assignment_error;

        public List<string> Roster
        {
            get { return roster; }
            set { roster = value; }
        }
    }
}