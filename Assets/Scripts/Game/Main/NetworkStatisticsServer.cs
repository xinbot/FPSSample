using Networking;
using UnityEngine;

internal class NetworkStatisticsServer
{
    private const int WindowSize = 120;

    private readonly NetworkServer _networkServer;
    private double _lastStatsTime;
    private int _totalSnapshotsLastFrame;

    private readonly float[] _snapsPerFrame = new float[64];

    private readonly FloatRollingAverage _statsDeltaTime = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _serverSimTime = new FloatRollingAverage(WindowSize);

    public NetworkStatisticsServer(NetworkServer networkServer)
    {
        _networkServer = networkServer;
    }

    public void Update()
    {
        _statsDeltaTime.Update(Time.deltaTime);

        _serverSimTime.Update(_networkServer.serverSimTime);

        switch (NetworkConfig.NetStats.IntValue)
        {
            case 1:
                DrawStats();
                break;
            case 2:
                DrawCounters();
                break;
        }

        if (NetworkConfig.NetPrintStats.IntValue > 0)
        {
            UpdateStats();
            if (Time.frameCount % NetworkConfig.NetPrintStats.IntValue == 0)
            {
                PrintStats();
            }
        }
    }

    private void UpdateStats()
    {
        foreach (var client in _networkServer.GetConnections())
        {
            client.Value.counters.UpdateAverages();
        }
    }

    private void PrintStats()
    {
        double timePassed = Game.FrameTime - _lastStatsTime;
        _lastStatsTime = Game.FrameTime;

        GameDebug.Log("Network stats");
        GameDebug.Log("=============");
        GameDebug.Log("Tick rate  : " + Game.ServerTickRate.IntValue);
        GameDebug.Log("Num netents: " + _networkServer.numEntities);
        Console.Write("--------------");
        Console.Write("Connections:");
        Console.Write("------------");
        Console.Write(string.Format("   {0,2} {1,-5}, {2,-5} {3,-5} {4,-5} {5,-5} {6,-5} {7,-5} {8,-5} {9,-5} {10,-5}",
            "ID", "RTT", "ISEQ", "ITIM", "OSEQ", "OACK", "ppsI", "bpsI", "ppsO", "bpsO", "frag"));
        Console.Write("-------------------");

        int byteOutSum = 0;
        int byteOutCount = 0;
        foreach (var connection in _networkServer.GetConnections())
        {
            var client = connection.Value;
            Console.Write(string.Format(
                "   {0:00} {1,5} {2,5} {3,5} {4,5} {5,5} {6:00.00} {7,5} {8:00.00} {9,5} {10,5}",
                client.ConnectionId, client.RTT, client.InSequence, client.InSequenceTime, client.OutSequence,
                client.OutSequenceAck,
                (client.counters.AvgPackagesIn.Graph.average * Game.ServerTickRate.FloatValue),
                (int) (client.counters.AvgBytesIn.Graph.average * Game.ServerTickRate.FloatValue),
                (client.counters.AvgPackagesOut.Graph.average * Game.ServerTickRate.FloatValue),
                (int) (client.counters.AvgBytesOut.Graph.average * Game.ServerTickRate.FloatValue),
                client.counters.FragmentedPackagesOut
            ));
            byteOutSum += (int) (client.counters.AvgBytesOut.Graph.average * Game.ServerTickRate.FloatValue);
            byteOutCount++;
        }

        if (byteOutCount > 0)
        {
            Console.Write("Avg bytes out: " + (byteOutSum / byteOutCount));
        }

        Console.Write("-------------------");
        var freq = NetworkConfig.NetPrintStats.IntValue;
        GameDebug.Log("Entity snapshots generated /frame : " + _networkServer.StatsGeneratedEntitySnapshots / freq);
        GameDebug.Log("Generated worldsnapsize    /frame : " + _networkServer.StatsGeneratedSnapshotSize / freq);
        GameDebug.Log("Entity snapshots total size/frame : " + _networkServer.StatsSnapshotData / freq);
        GameDebug.Log("Updates sent               /frame : " + _networkServer.StatsSentUpdates / freq);
        GameDebug.Log("Processed data outgoing      /sec : " + _networkServer.StatsProcessedOutgoing / timePassed);
        GameDebug.Log("Sent data outgoing         /frame : " + _networkServer.StatsSentOutgoing / freq);
        _networkServer.StatsGeneratedEntitySnapshots = 0;
        _networkServer.StatsGeneratedSnapshotSize = 0;
        _networkServer.StatsSnapshotData = 0;
        _networkServer.StatsSentUpdates = 0;
        _networkServer.StatsProcessedOutgoing = 0;
        _networkServer.StatsSentOutgoing = 0;
        Console.Write("-------------------");
    }

    private void DrawStats()
    {
        int y = 2;
        DebugOverlay.Write(2, y++, "  tick rate: {0}", Game.ServerTickRate.IntValue);
        DebugOverlay.Write(2, y++, "  entities:  {0}", _networkServer.numEntities);

        DebugOverlay.Write(2, y, "  sim  : {0:0.0} / {1:0.0} / {2:0.0} ({3:0.0})",
            _serverSimTime.min,
            _serverSimTime.min,
            _serverSimTime.max,
            _serverSimTime.stdDeviation);

        int totalSnapshots = 0;
        foreach (var c in _networkServer.GetConnections())
        {
            totalSnapshots += c.Value.counters.SnapshotsOut;
        }

        _snapsPerFrame[Time.frameCount % _snapsPerFrame.Length] = (totalSnapshots - _totalSnapshotsLastFrame);
        _totalSnapshotsLastFrame = totalSnapshots;
        DebugOverlay.DrawHist(2, 10, 60, 5, _snapsPerFrame, Time.frameCount % _snapsPerFrame.Length,
            new Color(0.5f, 0.5f, 1.0f));
    }

    private void DrawCounters()
    {
    }
}