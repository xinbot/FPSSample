using UnityEngine;
using UnityEngine.Networking;

namespace Networking.Matchmaking
{
    internal class MatchmakingController
    {
        public delegate void RequestMatchSuccess();

        public delegate void RequestMatchError(string error);

        public delegate void GetAssignmentSuccess(Assignment assignment);

        public delegate void GetAssignmentError(string error);

        private RequestMatchSuccess _requestMatchSuccess;
        private RequestMatchError _requestMatchError;
        private GetAssignmentSuccess _getAssignmentSuccess;
        private GetAssignmentError _getAssignmentError;

        private MatchmakingClient _client;

        private UnityWebRequestAsyncOperation _requestMatchOperation;
        private UnityWebRequestAsyncOperation _getAssignmentOperation;

        internal MatchmakingController(string endpoint)
        {
            _client = new MatchmakingClient(endpoint);
        }

        /// <summary>
        /// Start a matchmaking request call on the controller
        /// </summary>
        internal void StartRequestMatch(MatchmakingRequest request, RequestMatchSuccess successCallback,
            RequestMatchError errorCallback)
        {
            _requestMatchOperation = _client.RequestMatchAsync(request);
            _requestMatchSuccess = successCallback;
            _requestMatchError = errorCallback;
        }

        /// <summary>
        /// Update the state of the request. If it is complete, this will invoke the correct registered callback
        /// </summary>
        internal void UpdateRequestMatch()
        {
            if (_requestMatchOperation == null)
            {
                Debug.Log("You must call StartRequestMatch first");
                return;
            }
            
            if (!_requestMatchOperation.isDone)
            {
                return;
            }

            if (_requestMatchOperation.webRequest.isNetworkError || _requestMatchOperation.webRequest.isHttpError)
            {
                Debug.LogError("There was an error calling matchmaking RequestMatch. Error: " +
                               _requestMatchOperation.webRequest.error);
                _requestMatchError.Invoke(_requestMatchOperation.webRequest.error);
                return;
            }

            MatchMakingResult result =
                JsonUtility.FromJson<MatchMakingResult>(_requestMatchOperation.webRequest.downloadHandler.text);
            if (!result.success)
            {
                _requestMatchError.Invoke(result.error);
                return;
            }

            _requestMatchSuccess.Invoke();
        }

        /// <summary>
        /// Start a matchmaking request to get the provided player's assigned connection information
        /// </summary>
        internal void StartGetAssignment(string id, GetAssignmentSuccess successCallback,
            GetAssignmentError errorCallback)
        {
            _getAssignmentOperation = _client.GetAssignmentAsync(id);
            _getAssignmentSuccess = successCallback;
            _getAssignmentError = errorCallback;
        }

        /// <summary>
        /// Update the state of the request. If it is complete, this will invoke the correct registered callback
        /// </summary>
        internal void UpdateGetAssignment()
        {
            if (_getAssignmentOperation == null)
            {
                Debug.Log("You must call StartGetAssignment first");
                return;
            }

            if (!_getAssignmentOperation.isDone)
            {
                return;
            }

            if (_getAssignmentOperation.webRequest.isNetworkError || _getAssignmentOperation.webRequest.isHttpError)
            {
                Debug.LogError("There was an error calling matchmaking getAssignment. Error: " +
                               _getAssignmentOperation.webRequest.error);
                _getAssignmentError.Invoke(_getAssignmentOperation.webRequest.error);
                return;
            }

            Assignment result =
                JsonUtility.FromJson<Assignment>(_getAssignmentOperation.webRequest.downloadHandler.text);

            if (!string.IsNullOrEmpty(result.AssignmentError))
            {
                _getAssignmentError.Invoke(result.AssignmentError);
                return;
            }

            _getAssignmentSuccess.Invoke(result);
        }
    }
}