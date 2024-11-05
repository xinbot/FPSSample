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

    public FloatRollingAverage rtt => m_RTT;

    private float _bitHeight = 0.01f;

    public NetworkStatisticsClient(NetworkClient networkClient)
    {
        m_NetworkClient = networkClient;
    }

    public void Update(float frameTimeScale, float interpTime)
    {
        Profiler.BeginSample("NetworkStatisticsClient.Update");

        var clientCounters = m_NetworkClient.counters;

        m_FrameTimeScale = frameTimeScale;
        m_StatsDeltaTime.Update(Time.deltaTime);

        m_HardCatchup.Add(NotifyHardCatchup ? 100 : 0);
        NotifyHardCatchup = false;

        m_ServerSimTime.Update(m_NetworkClient.serverSimTime);

        m_BytesIn.Update(clientCounters != null ? clientCounters.BytesIn : 0);
        m_PackagesIn.Update(clientCounters != null ? clientCounters.PackagesIn : 0);

        m_HeaderBitsIn.Update(clientCounters != null ? clientCounters.HeaderBitsIn : 0);

        m_BytesOut.Update(clientCounters != null ? clientCounters.BytesOut : 0);
        m_PackagesOut.Update(clientCounters != null ? clientCounters.PackagesOut : 0);

        m_Latency.Update(m_NetworkClient.timeSinceSnapshot);
        m_RTT.Update(m_NetworkClient.rtt);
        m_CMDQ.Update(m_NetworkClient.lastAcknowledgedCommandTime - m_NetworkClient.serverTime);
        m_Interp.Update(interpTime * 1000);

        m_SnapshotsIn.Update(clientCounters != null ? clientCounters.SnapshotsIn : 0);
        m_CommandsOut.Update(clientCounters != null ? clientCounters.CommandsOut : 0);
        m_EventsIn.Update(clientCounters != null ? clientCounters.EventsIn : 0);
        m_EventsOut.Update(clientCounters != null ? clientCounters.EventsOut : 0);

        // Calculate package loss pct
        if (clientCounters != null && Time.time > m_NextLossCalc)
        {
            m_NextLossCalc = Time.time + 0.2f;

            var packagesIn = clientCounters.PackagesIn - m_PackageCountPrevIn;
            m_PackageCountPrevIn = clientCounters.PackagesIn;

            var loss = clientCounters.PackagesLostIn - m_PackageLossPrevIn;
            m_PackageLossPrevIn = clientCounters.PackagesLostIn;

            var totalIn = packagesIn + loss;
            m_PackagesLostPctIn = totalIn != 0 ? loss * 100 / totalIn : 0;

            var packagesOut = clientCounters.PackagesOut - m_PackageCountPrevOut;
            m_PackageCountPrevOut = clientCounters.PackagesOut;

            loss = clientCounters.PackagesLostOut - m_PackageLossPrevOut;
            m_PackageLossPrevOut = clientCounters.PackagesLostOut;

            var totalOut = packagesOut + loss;
            m_PackagesLostPctOut = totalOut != 0 ? loss * 100 / totalOut : 0;
        }

        m_PackageLossPctIn.Update(m_PackagesLostPctIn);
        m_PackageLossPctOut.Update(m_PackagesLostPctOut);

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
            Game.game.gameStatistics.rtt = Mathf.RoundToInt(m_RTT.average);
        }

        Profiler.EndSample();
    }

    private void PrintStats()
    {
        GameDebug.Log("Network stats");
        GameDebug.Log("=============");
        GameDebug.Log("Tick rate : " + Game.ServerTickRate.IntValue);
        GameDebug.Log("clientID  : " + m_NetworkClient.clientId);
        GameDebug.Log("rtt       : " + m_NetworkClient.rtt);
        GameDebug.Log("LastPkgSeq: " + m_NetworkClient.counters.PackageContentStatsPackageSequence);
        GameDebug.Log("ServerTime: " + m_NetworkClient.serverTime);
        Console.Write("-------------------");
    }

    private void DrawPackageStatistics()
    {
        float x = DebugOverlay.Width - 20;
        float y = DebugOverlay.Height - 8;
        float dx = 1.0f; // bar spacing
        float w = 1.0f; // width of bars
        int maxbits = 0;
        var stats = m_NetworkClient.counters.PackageContentStats;
        var last = m_NetworkClient.counters.PackagesIn;
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
        var samplesPerSecond = 1.0f / m_StatsDeltaTime.average;
        DebugOverlay.Write(-50, -4, "pps (in/out): {0} / {1}", m_PackagesIn.graph.average * samplesPerSecond,
            m_PackagesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(-50, -3, "bps (in/out): {0:00.0} / {1:00.0}", m_BytesIn.graph.average * samplesPerSecond,
            m_BytesOut.graph.average * samplesPerSecond);

        var startIndex = m_BytesIn.graph.GetData().HeadIndex;
        DebugOverlay.DrawHist(-50, -2, 20, 2, m_BytesIn.graph.GetData().GetArray(), startIndex, Color.blue, 5.0f);
    }

    private void DrawStats()
    {
        var samplesPerSecond = 1.0f / m_StatsDeltaTime.average;
        int y = 2;
        DebugOverlay.Write(2, y++, "  tick rate: {0}", m_NetworkClient.serverTickRate);
        DebugOverlay.Write(2, y++, "  frame timescale: {0}", m_FrameTimeScale);

        DebugOverlay.Write(2, y++, "  sim  : {0:0.0} / {1:0.0} / {2:0.0} ({3:0.0})",
            m_ServerSimTime.min,
            m_ServerSimTime.min,
            m_ServerSimTime.max,
            m_ServerSimTime.stdDeviation);

        DebugOverlay.Write(2, y++, "^FF0  lat  : {0:0.0} / {1:0.0} / {2:0.0}", m_Latency.min, m_Latency.average,
            m_Latency.max);
        DebugOverlay.Write(2, y++, "^0FF  rtt  : {0:0.0} / {1:0.0} / {2:0.0}", m_RTT.min, m_RTT.average, m_RTT.max);
        DebugOverlay.Write(2, y++, "^0F0  cmdq : {0:0.0} / {1:0.0} / {2:0.0}", m_CMDQ.min, m_CMDQ.average, m_CMDQ.max);
        DebugOverlay.Write(2, y++, "^F0F  intp : {0:0.0} / {1:0.0} / {2:0.0}", m_Interp.min, m_Interp.average,
            m_Interp.max);

        y++;
        DebugOverlay.Write(2, y++, "^22F  header/payload/total bps (in):");
        DebugOverlay.Write(2, y++, "^22F   {0:00.0} / {1:00.0} / {2:00.0} ({3})",
            m_HeaderBitsIn.graph.average / 8.0f * samplesPerSecond,
            (m_BytesIn.graph.average - m_HeaderBitsIn.graph.average / 8.0f) * samplesPerSecond,
            m_BytesIn.graph.average * samplesPerSecond,
            m_NetworkClient.ClientConfig.ServerUpdateRate);
        DebugOverlay.Write(2, y++, "^F00  bps (out): {0:00.0}", m_BytesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pps (in):  {0:00.0}", m_PackagesIn.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pps (out): {0:00.0}", m_PackagesOut.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  pl% (in):  {0:00.0}", m_PackageLossPctIn.average);
        DebugOverlay.Write(2, y++, "  pl% (out): {0:00.0}", m_PackageLossPctOut.average);

        y++;
        DebugOverlay.Write(2, y++, "  upd_srate: {0:00.0} ({1})", m_SnapshotsIn.graph.average * samplesPerSecond,
            m_NetworkClient.ClientConfig.ServerUpdateInterval);
        DebugOverlay.Write(2, y++, "  cmd_srate: {0:00.0} ({1})", m_CommandsOut.graph.average * samplesPerSecond,
            m_NetworkClient.serverTickRate);
        DebugOverlay.Write(2, y++, "  ev (in):   {0:00.0}", m_EventsIn.graph.average * samplesPerSecond);
        DebugOverlay.Write(2, y++, "  ev (out):  {0:00.0}", m_EventsOut.graph.average * samplesPerSecond);

        var startIndex = m_BytesIn.graph.GetData().HeadIndex;
        var graphY = 5;

        DebugOverlay.DrawGraph(38, graphY, 60, 5, m_Latency.GetData().GetArray(), startIndex, Color.yellow, 100);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, m_RTT.GetData().GetArray(), startIndex, Color.cyan, 100);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, m_CMDQ.GetData().GetArray(), startIndex, Color.green, 10);
        DebugOverlay.DrawGraph(38, graphY, 60, 5, m_Interp.GetData().GetArray(), startIndex, Color.magenta, 100);
        DebugOverlay.DrawHist(38, graphY, 60, 5, m_HardCatchup.GetArray(), startIndex, Color.red, 100);

        m_2GraphData[0] = m_BytesIn.graph.GetData().GetArray();
        m_2GraphData[1] = m_BytesOut.graph.GetData().GetArray();

        graphY += 7;
        DebugOverlay.DrawGraph(38, graphY, 60, 5, m_2GraphData, startIndex, m_BytesGraphColors);

        graphY += 6;
        DebugOverlay.DrawHist(38, graphY++, 60, 1, m_SnapshotsIn.graph.GetData().GetArray(), startIndex, Color.blue,
            5.0f);
        DebugOverlay.DrawHist(38, graphY++, 60, 1, m_CommandsOut.graph.GetData().GetArray(), startIndex, Color.red,
            5.0f);
        DebugOverlay.DrawHist(38, graphY++, 60, 1, m_EventsIn.graph.GetData().GetArray(), startIndex, Color.yellow,
            5.0f);
        DebugOverlay.DrawHist(38, graphY, 60, 1, m_EventsOut.graph.GetData().GetArray(), startIndex, Color.green,
            5.0f);
    }

    private void DrawCounters()
    {
        var counters = m_NetworkClient.counters;
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

    private int m_PackageCountPrevIn;
    private int m_PackageLossPrevIn;
    private int m_PackageCountPrevOut;
    private int m_PackageLossPrevOut;

    private float m_FrameTimeScale;
    private float m_NextLossCalc;
    private float m_PackagesLostPctIn;
    private float m_PackagesLostPctOut;

    private NetworkClient m_NetworkClient;

    private FloatRollingAverage m_StatsDeltaTime = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_ServerSimTime = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_Latency = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_RTT = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_CMDQ = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_Interp = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_PackageLossPctIn = new FloatRollingAverage(WindowSize);
    private FloatRollingAverage m_PackageLossPctOut = new FloatRollingAverage(WindowSize);

    private Aggregator m_BytesIn = new Aggregator();
    private Aggregator m_PackagesIn = new Aggregator();

    private Aggregator m_HeaderBitsIn = new Aggregator();

    private Aggregator m_SnapshotsIn = new Aggregator();
    private Aggregator m_EventsIn = new Aggregator();

    private Aggregator m_BytesOut = new Aggregator();
    private Aggregator m_PackagesOut = new Aggregator();

    private Aggregator m_CommandsOut = new Aggregator();
    private Aggregator m_EventsOut = new Aggregator();

    private float[][] m_2GraphData = new float[2][];

    private Color[] m_BytesGraphColors = {Color.blue, Color.red};

    private CircularList<float> m_HardCatchup = new CircularList<float>(WindowSize);
}