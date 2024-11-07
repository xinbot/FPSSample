using Networking;
using UnityEngine;
using UnityEngine.Profiling;

public class NetworkStatisticsClient
{
    private const int WindowSize = 120;
    private const int NumPackageContentStats = 16;

    private class Aggregator
    {
        public float PreviousValue;
        public FloatRollingAverage graph = new FloatRollingAverage(WindowSize);

        public void Update(float value)
        {
            graph.Update(value - PreviousValue);
            PreviousValue = value;
        }
    }

    // Set to true to record hard catchup for this update
    public bool NotifyHardCatchup;

    public FloatRollingAverage rtt => _rtt;

    private float _bitHeight = 0.01f;

    private int _packageCountPrevIn;
    private int _packageLossPrevIn;
    private int _packageCountPrevOut;
    private int _packageLossPrevOut;

    private float _frameTimeScale;
    private float _nextLossCalc;
    private float _packagesLostPctIn;
    private float _packagesLostPctOut;

    private readonly NetworkClient _networkClient;

    private readonly FloatRollingAverage _statsDeltaTime = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _serverSimTime = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _latency = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _rtt = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _cmdq = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _interp = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _packageLossPctIn = new FloatRollingAverage(WindowSize);
    private readonly FloatRollingAverage _packageLossPctOut = new FloatRollingAverage(WindowSize);

    private readonly Aggregator _bytesIn = new Aggregator();
    private readonly Aggregator _packagesIn = new Aggregator();

    private readonly Aggregator _headerBitsIn = new Aggregator();

    private readonly Aggregator _snapshotsIn = new Aggregator();
    private readonly Aggregator _eventsIn = new Aggregator();

    private readonly Aggregator _bytesOut = new Aggregator();
    private readonly Aggregator _packagesOut = new Aggregator();

    private readonly Aggregator _commandsOut = new Aggregator();
    private readonly Aggregator _eventsOut = new Aggregator();

    private readonly float[][] _2GraphData = new float[2][];

    private readonly Color[] _bytesGraphColors = {Color.blue, Color.red};

    private readonly CircularList<float> _hardCatchup = new CircularList<float>(WindowSize);

    public NetworkStatisticsClient(NetworkClient networkClient)
    {
        _networkClient = networkClient;
    }

    public void Update(float frameTimeScale, float durationTime)
    {
        Profiler.BeginSample("NetworkStatisticsClient.Update");

        var clientCounters = _networkClient.counters;

        _frameTimeScale = frameTimeScale;
        _statsDeltaTime.Update(Time.deltaTime);

        _hardCatchup.Add(NotifyHardCatchup ? 100 : 0);
        NotifyHardCatchup = false;

        _serverSimTime.Update(_networkClient.serverSimTime);

        _bytesIn.Update(clientCounters != null ? clientCounters.BytesIn : 0);
        _packagesIn.Update(clientCounters != null ? clientCounters.PackagesIn : 0);

        _headerBitsIn.Update(clientCounters != null ? clientCounters.HeaderBitsIn : 0);

        _bytesOut.Update(clientCounters != null ? clientCounters.BytesOut : 0);
        _packagesOut.Update(clientCounters != null ? clientCounters.PackagesOut : 0);

        _latency.Update(_networkClient.timeSinceSnapshot);
        _rtt.Update(_networkClient.rtt);
        _cmdq.Update(_networkClient.lastAcknowledgedCommandTime - _networkClient.serverTime);
        _interp.Update(durationTime * 1000);

        _snapshotsIn.Update(clientCounters != null ? clientCounters.SnapshotsIn : 0);
        _commandsOut.Update(clientCounters != null ? clientCounters.CommandsOut : 0);
        _eventsIn.Update(clientCounters != null ? clientCounters.EventsIn : 0);
        _eventsOut.Update(clientCounters != null ? clientCounters.EventsOut : 0);

        // Calculate package loss pct
        if (clientCounters != null && Time.time > _nextLossCalc)
        {
            _nextLossCalc = Time.time + 0.2f;

            var packagesIn = clientCounters.PackagesIn - _packageCountPrevIn;
            _packageCountPrevIn = clientCounters.PackagesIn;

            var loss = clientCounters.PackagesLostIn - _packageLossPrevIn;
            _packageLossPrevIn = clientCounters.PackagesLostIn;

            var totalIn = packagesIn + loss;
            _packagesLostPctIn = totalIn != 0 ? loss * 100 / totalIn : 0;

            var packagesOut = clientCounters.PackagesOut - _packageCountPrevOut;
            _packageCountPrevOut = clientCounters.PackagesOut;

            loss = clientCounters.PackagesLostOut - _packageLossPrevOut;
            _packageLossPrevOut = clientCounters.PackagesLostOut;

            var totalOut = packagesOut + loss;
            _packagesLostPctOut = totalOut != 0 ? loss * 100 / totalOut : 0;
        }

        _packageLossPctIn.Update(_packagesLostPctIn);
        _packageLossPctOut.Update(_packagesLostPctOut);

        switch (NetworkConfig.NetStats.IntValue)
        {
            case 1:
                DrawCompactStats();
                break;
            case 2:
                DrawStats();
                break;
            case 3:
                DrawCounters();
                break;
            case 4:
                DrawPackageStatistics();
                break;
        }

        if (NetworkConfig.NetPrintStats.IntValue > 0)
        {
            if (Time.frameCount % NetworkConfig.NetPrintStats.IntValue == 0)
            {
                PrintStats();
            }
        }

        // Pass on a few key stats to game statistics
        if (Game.game.gameStatistics != null)
        {
            Game.game.gameStatistics.rtt = Mathf.RoundToInt(_rtt.average);
        }

        Profiler.EndSample();
    }

    private void PrintStats()
    {
        GameDebug.Log("Network stats");
        GameDebug.Log("=============");
        GameDebug.Log("Tick rate : " + Game.ServerTickRate.IntValue);
        GameDebug.Log("clientID  : " + _networkClient.clientId);
        GameDebug.Log("rtt       : " + _networkClient.rtt);
        GameDebug.Log("LastPkgSeq: " + _networkClient.counters.PackageContentStatsPackageSequence);
        GameDebug.Log("ServerTime: " + _networkClient.serverTime);
        Console.Write("-------------------");
    }

    private void DrawPackageStatistics()
    {
        float x = DebugOverlay.Width - 20;
        float y = DebugOverlay.Height - 8;
        float dx = 1.0f; // bar spacing
        float w = 1.0f; // width of bars
        int maxbits = 0;
        var stats = _networkClient.counters.PackageContentStats;
        var last = _networkClient.counters.PackagesIn;
        for (var i = last; i > 0 && i > last - stats.Length; --i)
        {
            var s = stats[i % stats.Length];
            if (s == null)
            {
                continue;
            }

            var barx = x + (i - last) * dx;

            for (int j = 0, c = s.Count; j < c; ++j)
            {
                var stat = s[j];
                DebugOverlay.DrawRect(barx, y - (stat.SectionStart + stat.SectionLength) * _bitHeight, w,
                    stat.SectionLength * _bitHeight, stat.Color);
            }

            var lastStat = s[s.Count - 1];
            if (lastStat.SectionStart + lastStat.SectionLength > maxbits)
            {
                maxbits = lastStat.SectionStart + lastStat.SectionLength;
            }
        }

        int maxbytes = (maxbits + 7) / 8;
        int step = Mathf.Max(1, maxbytes >> 4) * 16;
        for (var i = 0; i <= maxbytes; i += step)
        {
            DebugOverlay.Write(x - 4, y - i * 8 * _bitHeight - 0.5f, "{0:###}b", i);
        }

        _bitHeight = Mathf.Min(0.01f, 10.0f / maxbits);
    }

    private void DrawCompactStats()
    {
        var samplesPerSecond = 1.0f / _statsDeltaTime.average;
        DebugOverlay.Write(-50, -4, "pps (in/out): {0} / {1}", _packagesIn.graph.average * samplesPerSecond,
            _packagesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(-50, -3, "bps (in/out): {0:00.0} / {1:00.0}", _bytesIn.graph.average * samplesPerSecond,
            _bytesOut.graph.average * samplesPerSecond);

        var startIndex = _bytesIn.graph.GetData().HeadIndex;
        DebugOverlay.DrawHist(-50, -2, 20, 2, _bytesIn.graph.GetData().GetArray(), startIndex, Color.blue, 5.0f);
    }

    private void DrawStats()
    {
        var samplesPerSecond = 1.0f / _statsDeltaTime.average;
        int y = 2;
        DebugOverlay.Write(2, y++, "  tick rate: {0}", _networkClient.serverTickRate);
        DebugOverlay.Write(2, y++, "  frame timescale: {0}", _frameTimeScale);

        DebugOverlay.Write(2, y++, "  sim  : {0:0.0} / {1:0.0} / {2:0.0} ({3:0.0})",
            _serverSimTime.min,
            _serverSimTime.min,
            _serverSimTime.max,
            _serverSimTime.stdDeviation);

        DebugOverlay.Write(2, y++, "^FF0  lat  : {0:0.0} / {1:0.0} / {2:0.0}", _latency.min, _latency.average,
            _latency.max);
        DebugOverlay.Write(2, y++, "^0FF  rtt  : {0:0.0} / {1:0.0} / {2:0.0}", _rtt.min, _rtt.average, _rtt.max);
        DebugOverlay.Write(2, y++, "^0F0  cmdq : {0:0.0} / {1:0.0} / {2:0.0}", _cmdq.min, _cmdq.average, _cmdq.max);
        DebugOverlay.Write(2, y++, "^F0F  intp : {0:0.0} / {1:0.0} / {2:0.0}", _interp.min, _interp.average,
            _interp.max);

        y++;
        DebugOverlay.Write(2, y++, "^22F  header/payload/total bps (in):");
        DebugOverlay.Write(2, y++, "^22F   {0:00.0} / {1:00.0} / {2:00.0} ({3})",
            _headerBitsIn.graph.average / 8.0f * samplesPerSecond,
            (_bytesIn.graph.average - _headerBitsIn.graph.average / 8.0f) * samplesPerSecond,
            _bytesIn.graph.average * samplesPerSecond,
            _networkClient.ClientConfig.ServerUpdateRate);
        DebugOverlay.Write(2, y++, "^F00  bps (out): {0:00.0}", _bytesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pps (in):  {0:00.0}", _packagesIn.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pps (out): {0:00.0}", _packagesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pl% (in):  {0:00.0}", _packageLossPctIn.average);
        DebugOverlay.Write(2, y++, "  pl% (out): {0:00.0}", _packageLossPctOut.average);

        y++;
        DebugOverlay.Write(2, y++, "  upd_srate: {0:00.0} ({1})", _snapshotsIn.graph.average * samplesPerSecond,
            _networkClient.ClientConfig.ServerUpdateInterval);
        DebugOverlay.Write(2, y++, "  cmd_srate: {0:00.0} ({1})", _commandsOut.graph.average * samplesPerSecond,
            _networkClient.serverTickRate);
        DebugOverlay.Write(2, y++, "  ev (in):   {0:00.0}", _eventsIn.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y, "  ev (out):  {0:00.0}", _eventsOut.graph.average * samplesPerSecond);

        var startIndex = _bytesIn.graph.GetData().HeadIndex;
        var graphY = 5;

        DebugOverlay.DrawGraph(38, graphY, 60, 5, _latency.GetData().GetArray(), startIndex, Color.yellow, 100);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, _rtt.GetData().GetArray(), startIndex, Color.cyan, 100);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, _cmdq.GetData().GetArray(), startIndex, Color.green, 10);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, _interp.GetData().GetArray(), startIndex, Color.magenta, 100);
        DebugOverlay.DrawHist(38, graphY, 60, 5, _hardCatchup.GetArray(), startIndex, Color.red, 100);

        _2GraphData[0] = _bytesIn.graph.GetData().GetArray();
        _2GraphData[1] = _bytesOut.graph.GetData().GetArray();

        graphY += 7;
        DebugOverlay.DrawGraph(38, graphY, 60, 5, _2GraphData, startIndex, _bytesGraphColors);

        graphY += 6;
        DebugOverlay.DrawHist(38, graphY++, 60, 1, _snapshotsIn.graph.GetData().GetArray(), startIndex, Color.blue,
            5.0f);
        DebugOverlay.DrawHist(38, graphY++, 60, 1, _commandsOut.graph.GetData().GetArray(), startIndex, Color.red,
            5.0f);
        DebugOverlay.DrawHist(38, graphY++, 60, 1, _eventsIn.graph.GetData().GetArray(), startIndex, Color.yellow,
            5.0f);
        DebugOverlay.DrawHist(38, graphY, 60, 1, _eventsOut.graph.GetData().GetArray(), startIndex, Color.green,
            5.0f);
    }

    private void DrawCounters()
    {
        var counters = _networkClient.counters;
        if (counters == null)
        {
            return;
        }

        int y = 2;
        DebugOverlay.Write(2, y++, "  Bytes in     : {0}", counters.BytesIn);
        DebugOverlay.Write(2, y++, "  Bytes out    : {0}", counters.BytesOut);
        DebugOverlay.Write(2, y++, "  Packages in  : {0}", counters.PackagesIn);
        DebugOverlay.Write(2, y++, "  Packages out : {0}", counters.PackagesOut);

        y++;
        DebugOverlay.Write(2, y++, "  Stale packages        : {0}", counters.PackagesStaleIn);
        DebugOverlay.Write(2, y++, "  Duplicate packages    : {0}", counters.PackagesDuplicateIn);
        DebugOverlay.Write(2, y++, "  Out of order packages : {0}", counters.PackagesOutOfOrderIn);

        y++;
        DebugOverlay.Write(2, y++, "  Lost packages in      : {0}", counters.PackagesLostIn);
        DebugOverlay.Write(2, y++, "  Lost packages out     : {0}", counters.PackagesLostOut);

        y++;
        DebugOverlay.Write(2, y++, "  Fragmented packages in       : {0}", counters.FragmentedPackagesIn);
        DebugOverlay.Write(2, y++, "  Fragmented packages out      : {0}", counters.FragmentedPackagesOut);

        DebugOverlay.Write(2, y++, "  Fragmented packages lost in  : {0}", counters.FragmentedPackagesLostIn);
        DebugOverlay.Write(2, y++, "  Fragmented packages lost out : {0}", counters.FragmentedPackagesLostOut);

        y++;
        DebugOverlay.Write(2, y, "  Choked packages lost : {0}", counters.ChokedPackagesOut);
    }
}