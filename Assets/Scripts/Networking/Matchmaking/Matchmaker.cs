using System;
using UnityEngine;

namespace Networking.Matchmaking
{
    public class Matchmaker
    {
        /// <summary>
        /// The hostname[:port]/{projectid} of your matchmaking server
        /// </summary>
        public string Endpoint;

        private MatchmakingController _matchmakingController;
        private MatchmakingRequest _request;

        public delegate void SuccessCallback(Assignment assignment);

        public delegate void ErrorCallback(string error);

        public SuccessCallback successCallback;
        public ErrorCallback errorCallback;

        public enum MatchmakingState
        {
            None,
            Requesting,
            Searching,
            Found,
            Error
        };

        /// <summary>
        /// The matchmaking state machine's current state
        /// </summary>
        public MatchmakingState State = MatchmakingState.None;

        /// <summary>
        /// Matchmaker
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="onSuccessCallback">If a match is found, this callback will provide the connection information</param>
        /// <param name="onErrorCallback">If matchmaking fails, this callback will provided some failure information</param>
        public Matchmaker(string endpoint, SuccessCallback onSuccessCallback = null,
            ErrorCallback onErrorCallback = null)
        {
            Endpoint = endpoint;
            successCallback = onSuccessCallback;
            errorCallback = onErrorCallback;
        }

        /// <summary>
        /// Start Matchmaking
        /// </summary>
        /// <param name="playerId">The id of the player</param>
        /// <param name="playerProps">Custom player properties relevant to the matchmaking function</param>
        /// <param name="groupProps">Custom group properties relevant to the matchmaking function</param>
        public void RequestMatch(string playerId, MatchmakingPlayerProperties playerProps,
            MatchmakingGroupProperties groupProps)
        {
            _request = CreateMatchmakingRequest(playerId, playerProps, groupProps);

            _matchmakingController = new MatchmakingController(Endpoint);

            _matchmakingController.StartRequestMatch(_request, GetAssignment, OnError);
            State = MatchmakingState.Requesting;
            Debug.Log(State);
        }

        /// <summary>
        /// Matchmaking state-machine driver
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Update()
        {
            switch (State)
            {
                case MatchmakingState.Requesting:
                    _matchmakingController.UpdateRequestMatch();
                    break;
                case MatchmakingState.Searching:
                    _matchmakingController.UpdateGetAssignment();
                    break;
                case MatchmakingState.Found:
                case MatchmakingState.Error:
                    break; // User hasn't stopped the state machine yet.
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Generates a matchmaking request from the custom player and group properties provided.
        /// </summary>
        /// <param name="playerId">The id of the player</param>
        /// <param name="playerProps">Custom player properties relevant to the matchmaking function</param>
        /// <param name="groupProps">Custom group properties relevant to the matchmaking function</param>
        /// <returns></returns>
        private static MatchmakingRequest CreateMatchmakingRequest(string playerId,
            MatchmakingPlayerProperties playerProps, MatchmakingGroupProperties groupProps)
        {
            // TODO: WORKAROUND: Currently matchmaker handles IDs as UUIDs, not player names, and will only ever generate 1 match assignment for each UUID
            // Therefore, we'll append the current time in Ticks as an attempt at creating a UUID
            playerId = playerId + DateTime.UtcNow.Ticks.ToString();

            MatchmakingPlayer thisPlayer = new MatchmakingPlayer(playerId)
            {
                Properties = JsonUtility.ToJson(playerProps)
            };

            MatchmakingRequest request = new MatchmakingRequest()
            {
                Properties = JsonUtility.ToJson(groupProps)
            };

            request.Players.Add(thisPlayer);

            return request;
        }

        private void GetAssignment()
        {
            _matchmakingController.StartGetAssignment(_request.Players[0].Id, OnSuccess, OnError);
            State = MatchmakingState.Searching;
            Debug.Log(State);
        }

        private void OnSuccess(Assignment assignment)
        {
            State = MatchmakingState.Found;
            Debug.Log(State);
            successCallback?.Invoke(assignment);
        }

        private void OnError(string error)
        {
            State = MatchmakingState.Error;
            Debug.Log(State);
            errorCallback?.Invoke(error ?? "Undefined Error");
        }
    }
}