using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Profiling;

namespace Networking
{
    public class NetworkConnectionCounters
    {
        // The number of user bytes received on this connection
        public int BytesIn;

        // The number of user bytes sent on this connection
        public int BytesOut;

        // The number of header bytes received on this connection
        public int HeaderBitsIn;

        // The number of packages received on this connection (including package fragments)
        public int PackagesIn;

        // The number of packages sent on this connection (including package fragments)
        public int PackagesOut;

        // The number of state packages we received
        public int PackagesStaleIn;

        // The number of duplicate packages we received
        public int PackagesDuplicateIn;

        // The number of packages we received out of order
        public int PackagesOutOfOrderIn;

        // The number of incoming packages that was lost (i.e. holes in the package sequence)
        public int PackagesLostIn;

        // The number of outgoing packages that wasn't acked (either due to choke or network)
        public int PackagesLostOut;

        // The number of incoming packages that was fragmented
        public int FragmentedPackagesIn;

        // The number of outgoing packages that was fragmented
        public int FragmentedPackagesOut;

        // The number of incoming fragmented packages we couldn't reassemble
        public int FragmentedPackagesLostIn;

        // The number of outgoing fragmented packages that wasn't acked
        public int FragmentedPackagesLostOut;

        // The number of packages we dropped due to choke
        public int ChokedPackagesOut;

        // The total number of events received
        public int EventsIn;

        // The total number of events sent
        public int EventsOut;

        // The number of events that was lost
        public int EventsLostOut;

        // The number of reliable events sent
        public int ReliableEventsOut;

        // The number of reliable events we had to resend
        public int ReliableEventResendOut;

        public readonly Aggregator AvgBytesIn = new Aggregator();
        public readonly Aggregator AvgBytesOut = new Aggregator();
        public readonly Aggregator AvgPackagesIn = new Aggregator();
        public readonly Aggregator AvgPackagesOut = new Aggregator();
        public readonly Aggregator AvgPackageSize = new Aggregator();

        public void UpdateAverages()
        {
            AvgBytesIn.Update(BytesIn);
            AvgBytesOut.Update(BytesOut);
            AvgPackagesIn.Update(PackagesIn);
            AvgPackagesOut.Update(PackagesOut);
        }
    }

    public class PackageInfo
    {
        public long SentTime;
        public bool Fragmented;
        public NetworkMessage Content;

        public readonly List<NetworkEvent> Events = new List<NetworkEvent>(10);

        public virtual void Reset()
        {
            SentTime = 0;
            Fragmented = false;
            Content = 0;

            foreach (var eventInfo in Events)
            {
                eventInfo.Release();
            }

            Events.Clear();
        }
    }

    public class ClientPackageInfo : PackageInfo
    {
        public int CommandTime;
        public int CommandSequence;

        public override void Reset()
        {
            base.Reset();
            CommandTime = 0;
            CommandSequence = 0;
        }
    }

    public class NetworkConnection<TCounters, TPackageInfo> where TCounters : NetworkConnectionCounters, new()
        where TPackageInfo : PackageInfo, new()
    {
        private class FragmentReassemblyInfo
        {
            public int NumFragments;
            public uint ReceivedMask;
            public uint ReceivedCount;
            public readonly byte[] Data = new byte[1024 * 64];
        }

        private double _chokedTimeToNextPackage;

        private readonly byte[] _fragmentBuffer = new byte[2048];

        private readonly Dictionary<ushort, NetworkEventType>
            _eventTypesIn = new Dictionary<ushort, NetworkEventType>();

        private readonly List<NetworkEventType> _ackedEventTypes = new List<NetworkEventType>();

        private readonly SequenceBuffer<FragmentReassemblyInfo> _fragmentReassembly =
            new SequenceBuffer<FragmentReassemblyInfo>(8, () => new FragmentReassemblyInfo());

        // TODO : Should be private (content calc issue)
        public readonly List<NetworkEvent> EventsOut = new List<NetworkEvent>();

        //TODO: fix this
        public readonly byte[] PackageBuffer = new byte[1024 * 64];

        public readonly SequenceBuffer<TPackageInfo> OutstandingPackages =
            new SequenceBuffer<TPackageInfo>(64, () => new TPackageInfo());

        public int ConnectionId;
        public INetworkTransport Transport;
        public BinaryWriter DebugSendStreamWriter;

        public TCounters counters = new TCounters();

        // Round trip time (ping + time lost due to read / send frequencies)
        public int RTT;

        // The highest sequence of packages we have received
        public int InSequence;

        // The time the last package was received
        public long InSequenceTime;

        // The mask describing which of the last packages we have received relative to inSequence
        public ushort InSequenceAckMask;

        // The sequence of the next outgoing package
        public int OutSequence = 1;

        // The highest sequence of packages that have been acked
        public int OutSequenceAck;

        // The mask describing which of the last packaged have been acked related to outSequence
        public ushort OutSequenceAckMask;

        public NetworkConnection(int connectionId, INetworkTransport transport)
        {
            ConnectionId = connectionId;
            Transport = transport;

            _chokedTimeToNextPackage = 0;
        }

        /// <summary>
        /// Called when the connection released (e.g. when the connection was disconnected)
        /// unlike Reset, which can be called multiple times on the connection in order to reset any
        /// state cached on the connection
        /// </summary>
        public virtual void Shutdown()
        {
            if (DebugSendStreamWriter != null)
            {
                DebugSendStreamWriter.Close();
                DebugSendStreamWriter.Dispose();
                DebugSendStreamWriter = null;
            }
        }

        /// <summary>
        /// Resets all cached connection state including reliable data pending acknowledgments
        /// </summary>
        public virtual void Reset()
        {
        }

        protected bool CanSendPackage(ref BitOutputStream output)
        {
            // running out here means we hit 64 packs without any acks from client...
            if (!OutstandingPackages.Available(OutSequence))
            {
                // We have too many outstanding packages. We need the other end to send something to us, so we know he 
                // is alive. This happens for example when we break the client in the debugger while the server is still 
                // sending messages but potentially it could also happen in extreme cases of congestion or package loss. 
                // We will try to send empty packages with low frequency to see if we can get the connection up and running again

                if (Game.frameTime >= _chokedTimeToNextPackage)
                {
                    _chokedTimeToNextPackage = Game.frameTime + NetworkConfig.NetChokeSendInterval.FloatValue;

                    // Treat the last package as lost
                    var info = OutstandingPackages.TryGetByIndex(OutSequence % OutstandingPackages.Capacity,
                        out var chokedSequence);
                    GameDebug.Assert(info != null);

                    NotifyDelivered(chokedSequence, info, false);

                    counters.ChokedPackagesOut++;

                    info?.Reset();

                    OutstandingPackages.Remove(chokedSequence);

                    // Send empty package
                    TPackageInfo emptyPackage;
                    BeginSendPackage(ref output, out emptyPackage);
                    CompleteSendPackage(emptyPackage, ref output);
                }

                return false;
            }

            return true;
        }

        // Returns the 'wide' packageSequenceNumber (i.e. 32 bit reconstructed from the 16bits sent over wire)
        protected int ProcessPackageHeader(byte[] packageData, int packageSize, out NetworkMessage content,
            out byte[] assembledData, out int assembledSize, out int headerSize)
        {
            counters.PackagesIn++;
            assembledData = packageData;
            assembledSize = packageSize;
            headerSize = 0;

            var input = new BitInputStream(packageData);
            int headerStartInBits = input.GetBitPosition();

            content = (NetworkMessage) input.ReadBits(8);

            // TODO: Possible improvement is to ack on individual fragments not just entire message
            if ((content & NetworkMessage.Fragment) != 0)
            {
                // Package fragment
                var fragmentPackageSequence = Sequence.FromUInt16((ushort) input.ReadBits(16), InSequence);
                var numFragments = (int) input.ReadBits(8);
                var fragmentIndex = (int) input.ReadBits(8);
                var fragmentSize = (int) input.ReadBits(16);

                FragmentReassemblyInfo assembly;
                if (!_fragmentReassembly.TryGetValue(fragmentPackageSequence, out assembly))
                {
                    // If we run out of room in the reassembly buffer we will not be able to reassemble this package
                    if (!_fragmentReassembly.Available(fragmentPackageSequence))
                    {
                        counters.FragmentedPackagesLostIn++;
                    }

                    GameDebug.Assert(numFragments <= NetworkConfig.MAXFragments);

                    assembly = _fragmentReassembly.Acquire(fragmentPackageSequence);
                    assembly.NumFragments = numFragments;
                    assembly.ReceivedMask = 0;
                    assembly.ReceivedCount = 0;
                }

                GameDebug.Assert(assembly.NumFragments == numFragments);
                GameDebug.Assert(fragmentIndex < assembly.NumFragments);
                counters.HeaderBitsIn += input.GetBitPosition() - headerStartInBits;

                if ((assembly.ReceivedMask & (1U << fragmentIndex)) != 0)
                {
                    // Duplicate package fragment
                    counters.PackagesDuplicateIn++;
                    return 0;
                }

                assembly.ReceivedMask |= 1U << fragmentIndex;
                assembly.ReceivedCount++;

                input.ReadBytes(assembly.Data, fragmentIndex * NetworkConfig.PackageFragmentSize, fragmentSize);

                if (assembly.ReceivedCount < assembly.NumFragments)
                {
                    return 0; // Not fully assembled
                }

                // Continue processing package as we have now reassembled the package
                assembledData = assembly.Data;
                assembledSize = fragmentIndex * NetworkConfig.PackageFragmentSize + fragmentSize;
                input.Initialize(assembledData);
                headerStartInBits = 0;
                content = (NetworkMessage) input.ReadBits(8);
            }

            var inSequenceNew = Sequence.FromUInt16((ushort) input.ReadBits(16), InSequence);
            var outSequenceAckNew = Sequence.FromUInt16((ushort) input.ReadBits(16), OutSequenceAck);
            var outSequenceAckMaskNew = (ushort) input.ReadBits(16);

            if (inSequenceNew > InSequence)
            {
                // If we have a hole in the package sequence that will fall off the ack mask that 
                // means the package (inSequenceNew - 15 and before) will be considered lost (either it will never come or we will 
                // reject it as being stale if we get it at a later point in time)
                var distance = inSequenceNew - InSequence;
                // TODO : Fix this constant 15
                for (var i = 0; i < Math.Min(distance, 15); ++i)
                {
                    if ((InSequenceAckMask & 1 << (15 - i)) == 0)
                    {
                        counters.PackagesLostIn++;
                    }
                }

                // If there is a really big hole then those packages are considered lost as well

                // Update the incoming ack mask.
                if (distance > 15)
                {
                    counters.PackagesLostIn += distance - 15;
                    InSequenceAckMask = 1; // all is lost except current package
                }
                else
                {
                    InSequenceAckMask <<= distance;
                    InSequenceAckMask |= 1;
                }

                InSequence = inSequenceNew;
                InSequenceTime = NetworkUtils.stopwatch.ElapsedMilliseconds;
            }
            else if (inSequenceNew < InSequence)
            {
                // Package is out of order 

                // Check if the package is stale
                // NOTE : We rely on the fact that we will reject packages that we cannot ack due to the size
                // of the ack mask, so we don't have to worry about resending messages as long as we do that
                // after the original package has fallen off the ack mask.
                var distance = InSequence - inSequenceNew;
                // TODO : Fix this constant 15
                if (distance > 15)
                {
                    counters.PackagesStaleIn++;
                    return 0;
                }

                // Check if the package is a duplicate
                var ackBit = 1 << distance;
                if ((ackBit & InSequenceAckMask) != 0)
                {
                    // Duplicate package
                    counters.PackagesDuplicateIn++;
                    return 0;
                }

                // Accept the package out of order
                counters.PackagesOutOfOrderIn++;
                InSequenceAckMask |= (ushort) ackBit;
            }
            else
            {
                // Duplicate package
                counters.PackagesDuplicateIn++;
                return 0;
            }

            if (inSequenceNew % 3 == 0)
            {
                var timeOnServer = (ushort) input.ReadBits(8);
                if (OutstandingPackages.TryGetValue(outSequenceAckNew, out var info))
                {
                    var now = NetworkUtils.stopwatch.ElapsedMilliseconds;
                    RTT = (int) (now - info.SentTime - timeOnServer);
                }
            }

            // If the ack sequence is not higher we have nothing new to do
            if (outSequenceAckNew <= OutSequenceAck)
            {
                headerSize = input.Align();
                return inSequenceNew;
            }

            // Find the sequence numbers that we have to consider lost
            var seqBeforeThisAlreadyNotifiedAsLost = OutSequenceAck - 15;
            var seqBeforeThisAreLost = outSequenceAckNew - 15;
            for (var sequence = seqBeforeThisAlreadyNotifiedAsLost; sequence <= seqBeforeThisAreLost; ++sequence)
            {
                // Handle conditions before first 15 packets
                if (sequence < 0)
                {
                    continue;
                }

                // If sequence covered by old ack mask, we may already have received it (and notified)
                int bitNum = OutSequenceAck - sequence;
                var ackBit = bitNum >= 0 ? 1 << bitNum : 0;
                var notNotified = (ackBit & OutSequenceAckMask) == 0;

                if (OutstandingPackages.Exists(sequence) && notNotified)
                {
                    var info = OutstandingPackages[sequence];
                    NotifyDelivered(sequence, info, false);

                    counters.PackagesLostOut++;
                    if (info.Fragmented)
                    {
                        counters.FragmentedPackagesLostOut++;
                    }

                    info.Reset();
                    OutstandingPackages.Remove(sequence);
                }
            }

            OutSequenceAck = outSequenceAckNew;
            OutSequenceAckMask = outSequenceAckMaskNew;

            // Ack packages if they haven't been acked already
            for (var sequence = Math.Max(OutSequenceAck - 15, 0); sequence <= OutSequenceAck; ++sequence)
            {
                var ackBit = 1 << OutSequenceAck - sequence;
                if (OutstandingPackages.Exists(sequence) && (ackBit & OutSequenceAckMask) != 0)
                {
                    var info = OutstandingPackages[sequence];
                    NotifyDelivered(sequence, info, true);

                    info.Reset();
                    OutstandingPackages.Remove(sequence);
                }
            }

            counters.HeaderBitsIn += input.GetBitPosition() - headerStartInBits;

            headerSize = input.Align();
            return inSequenceNew;
        }

        protected void BeginSendPackage(ref BitOutputStream output, out TPackageInfo info)
        {
            GameDebug.Assert(OutstandingPackages.Available(OutSequence),
                "NetworkConnection.BeginSendPackage : package info not available for sequence : {0}", OutSequence);

            output.WriteBits(0, 8); // Package content flags (will set later as we add messages)
            output.WriteBits(Sequence.ToUInt16(OutSequence), 16);
            output.WriteBits(Sequence.ToUInt16(InSequence), 16);
            output.WriteBits(InSequenceAckMask, 16);

            // Send rtt info every 3th package. We calculate the RTT as the time from sending the package
            // and receiving the ack for the package minus the time the package spent on the server

            // TODO should this be sent from client to server?
            if (OutSequence % 3 == 0)
            {
                var now = NetworkUtils.stopwatch.ElapsedMilliseconds;
                // TODO Is 255 enough? 
                var timeOnServer = (byte) Math.Min(now - InSequenceTime, 255);
                output.WriteBits(timeOnServer, 8);
            }

            info = OutstandingPackages.Acquire(OutSequence);
        }

        protected void AddMessageContentFlag(NetworkMessage message)
        {
            PackageBuffer[0] |= (byte) message;
        }

        protected int CompleteSendPackage(TPackageInfo info, ref BitOutputStream output)
        {
            Profiler.BeginSample("NetworkConnection.CompleteSendPackage()");

            info.SentTime = NetworkUtils.stopwatch.ElapsedMilliseconds;
            info.Content = (NetworkMessage) PackageBuffer[0];
            int packageSize = output.Flush();

            GameDebug.Assert(packageSize < NetworkConfig.MAXPackageSize, "packageSize < NetworkConfig.maxPackageSize");

            if (DebugSendStreamWriter != null)
            {
                DebugSendStreamWriter.Write(PackageBuffer, 0, packageSize);
                DebugSendStreamWriter.Write(0xedededed);
            }

            if (packageSize > NetworkConfig.PackageFragmentSize)
            {
                // Package is too big and needs to be sent as fragments
                var numFragments = packageSize / NetworkConfig.PackageFragmentSize;
                var lastFragmentSize = packageSize % NetworkConfig.PackageFragmentSize;
                if (lastFragmentSize != 0)
                {
                    ++numFragments;
                }
                else
                {
                    lastFragmentSize = NetworkConfig.PackageFragmentSize;
                }

                for (var i = 0; i < numFragments; ++i)
                {
                    var fragmentSize = i < numFragments - 1 ? NetworkConfig.PackageFragmentSize : lastFragmentSize;

                    var fragmentOutput = new BitOutputStream(_fragmentBuffer);
                    fragmentOutput.WriteBits((uint) NetworkMessage.Fragment, 8); // Package fragment identifier
                    fragmentOutput.WriteBits(Sequence.ToUInt16(OutSequence), 16);
                    fragmentOutput.WriteBits((uint) numFragments, 8);
                    fragmentOutput.WriteBits((uint) i, 8);
                    fragmentOutput.WriteBits((uint) fragmentSize, 16);
                    fragmentOutput.WriteBytes(PackageBuffer, i * NetworkConfig.PackageFragmentSize, fragmentSize);

                    int fragmentPackageSize = fragmentOutput.Flush();
                    Transport.SendData(ConnectionId, _fragmentBuffer, fragmentPackageSize);
                    counters.PackagesOut++;
                    counters.BytesOut += fragmentPackageSize;
                }

                counters.FragmentedPackagesOut++;
            }
            else
            {
                Transport.SendData(ConnectionId, PackageBuffer, packageSize);
                counters.PackagesOut++;
                counters.BytesOut += packageSize;
            }

            ++OutSequence;

            Profiler.EndSample();

            return packageSize;
        }

        protected virtual void NotifyDelivered(int sequence, TPackageInfo info, bool madeIt)
        {
            if (madeIt)
            {
                // Release received reliable events
                foreach (var eventInfo in info.Events)
                {
                    if (!_ackedEventTypes.Contains(eventInfo.Type))
                    {
                        _ackedEventTypes.Add(eventInfo.Type);
                    }

                    eventInfo.Release();
                }
            }
            else
            {
                foreach (var eventInfo in info.Events)
                {
                    counters.EventsLostOut++;
                    if (eventInfo.Reliable)
                    {
                        // Re-add dropped reliable events to outgoing events
                        counters.ReliableEventResendOut++;
                        GameDebug.Log("Resending lost reliable event: " +
                                      ((GameNetworkEvents.EventType) eventInfo.Type.TypeId) + ":" + eventInfo.Sequence);
                        EventsOut.Add(eventInfo);
                    }
                    else
                    {
                        eventInfo.Release();
                    }
                }
            }

            info.Events.Clear();
        }

        public void QueueEvent(NetworkEvent info)
        {
            EventsOut.Add(info);
            info.AddRef();
        }

        public void ReadEvents<TInputStream>(ref TInputStream input, INetworkCallbacks networkConsumer)
            where TInputStream : NetworkCompression.IInputStream
        {
            var numEvents = NetworkEvent.ReadEvents(_eventTypesIn, ConnectionId, ref input, networkConsumer);
            counters.EventsIn += numEvents;
        }

        public void WriteEvents<TOutputStream>(TPackageInfo info, ref TOutputStream output)
            where TOutputStream : NetworkCompression.IOutputStream
        {
            if (EventsOut.Count == 0)
            {
                return;
            }

            foreach (var eventInfo in EventsOut)
            {
                counters.EventsOut++;
                if (eventInfo.Reliable)
                {
                    counters.ReliableEventsOut++;
                }
            }

            AddMessageContentFlag(NetworkMessage.Events);

            GameDebug.Assert(info.Events.Count == 0);
            NetworkEvent.WriteEvents(EventsOut, _ackedEventTypes, ref output);
            info.Events.AddRange(EventsOut);
            EventsOut.Clear();
        }
    }
}