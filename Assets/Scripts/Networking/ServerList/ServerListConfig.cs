namespace Networking.ServerList
{
    public class ServerListConfig
    {
        // The Url of the server list service endpoint
        public string url { get; private set; }

        // The time in seconds between server list GET calls
        public int period { get; private set; }

        public static ServerListConfig BasicConfig(string projectId)
        {
            return new ServerListConfig
            {
                url = $"http://104.154.156.161:8080/api/projects/{projectId}/servers?multiplay=true",
                period = 5
            };
        }
    }
}