using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ReplayGlitchGTA;

/// <summary>
/// Reads the TCP connections owned by a process, so the effect of the managed firewall rule
/// can be observed instead of assumed. One snapshot cannot distinguish a connection that
/// predates activation from a new connection through the active rule.
/// </summary>
/// <remarks>Read-only: this never opens, closes, or alters a connection.</remarks>
internal static class GameConnectionInspector
{
    private const int AddressFamilyInternetwork = 2;
    private const int AddressFamilyInternetworkV6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int InsufficientBuffer = 122;
    private const int NoError = 0;
    private const int InternetworkRowSize = 24;
    private const int InternetworkV6RowSize = 56;
    private const int MaximumResizeAttempts = 6;

    internal static IReadOnlyList<GameConnection> GetConnections(int processId) =>
        ReadConnections(processId).Connections;

    internal static ReadResult ReadConnections(int processId) =>
        ReadConnections(processId, GetExtendedTcpTable);

    // An explicit reader keeps error and retry tests independent of the live TCP table.
    internal delegate int TcpTableReader(IntPtr buffer, ref int size, bool sortOrder,
        int addressFamily, int tableClass, int reserved);

    internal static ReadResult ReadConnections(int processId, TcpTableReader reader)
    {
        var connections = new List<GameConnection>();
        var ipv4Error = ReadTable(AddressFamilyInternetwork, processId, connections, reader);
        var ipv6Error = ReadTable(AddressFamilyInternetworkV6, processId, connections, reader);
        return new ReadResult(connections.AsReadOnly(), ipv4Error, ipv6Error);
    }

    private static string? ReadTable(
        int addressFamily, int processId, ICollection<GameConnection> connections,
        TcpTableReader reader)
    {
        var bufferSize = 0;
        var buffer = IntPtr.Zero;
        try
        {
            for (var attempt = 0; attempt < MaximumResizeAttempts; attempt++)
            {
                var allocatedSize = bufferSize;
                var result = reader(buffer, ref bufferSize, false,
                    addressFamily, TcpTableOwnerPidAll, 0);
                if (result == NoError && buffer != IntPtr.Zero)
                {
                    return ReadRows(buffer, Math.Min(bufferSize, allocatedSize),
                        addressFamily, processId, connections)
                        ? null : "The TCP table was truncated or malformed.";
                }
                if (result != InsufficientBuffer || bufferSize <= 0)
                {
                    return result == NoError
                        ? "Windows returned an invalid TCP table buffer."
                        : $"Windows error {result}: {new Win32Exception(result).Message}";
                }

                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                }
                buffer = Marshal.AllocHGlobal(bufferSize);
            }
            return "The TCP table kept changing; the read retry limit was reached.";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static bool ReadRows(
        IntPtr buffer, int bufferSize, int addressFamily, int processId,
        ICollection<GameConnection> connections)
    {
        if (bufferSize < sizeof(int))
        {
            return false;
        }
        var entryCount = Marshal.ReadInt32(buffer);
        var isVersion6 = addressFamily == AddressFamilyInternetworkV6;
        var rowSize = isVersion6 ? InternetworkV6RowSize : InternetworkRowSize;

        // The row count comes from the kernel and should always fit, but this walks unmanaged
        // memory by hand inside an elevated process: clamp it to what was actually allocated
        // rather than trusting the header.
        var capacity = (bufferSize - sizeof(int)) / rowSize;
        var complete = entryCount >= 0 && entryCount <= capacity;
        if (entryCount > capacity)
        {
            entryCount = capacity < 0 ? 0 : capacity;
        }

        for (var index = 0; index < entryCount; index++)
        {
            var row = IntPtr.Add(buffer, 4 + (index * rowSize));
            var owningProcessId = Marshal.ReadInt32(row, isVersion6 ? 52 : 20);
            if (owningProcessId != processId)
            {
                continue;
            }

            var addressBytes = new byte[isVersion6 ? 16 : 4];
            Marshal.Copy(IntPtr.Add(row, isVersion6 ? 24 : 12), addressBytes, 0,
                addressBytes.Length);
            var remotePort = ReadNetworkPort(row, isVersion6 ? 44 : 16);
            var localPort = ReadNetworkPort(row, isVersion6 ? 20 : 8);
            var state = (TcpConnectionState)Marshal.ReadInt32(row, isVersion6 ? 48 : 0);

            // A closed or listening row carries no meaningful remote endpoint.
            if (remotePort == 0 && state != TcpConnectionState.Established)
            {
                continue;
            }
            connections.Add(new GameConnection(
                new IPAddress(addressBytes), remotePort, localPort, state));
        }
        return complete;
    }

    internal sealed class ReadResult
    {
        internal ReadResult(IReadOnlyList<GameConnection> connections,
            string? ipv4Error, string? ipv6Error)
        {
            Connections = connections;
            Ipv4Error = ipv4Error;
            Ipv6Error = ipv6Error;
        }

        internal IReadOnlyList<GameConnection> Connections { get; }
        internal string? Ipv4Error { get; }
        internal string? Ipv6Error { get; }
        internal bool Complete => Ipv4Error is null && Ipv6Error is null;
    }

    /// <summary>Ports sit in the low half of a DWORD, in network byte order.</summary>
    private static int ReadNetworkPort(IntPtr row, int offset)
    {
        var raw = Marshal.ReadInt32(row, offset);
        return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr tcpTable, ref int tableSize, bool sortOrder, int addressFamily,
        int tableClass, int reserved);
}

internal enum TcpConnectionState
{
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAcknowledgement = 10,
    TimeWait = 11,
    DeleteTcb = 12
}

internal sealed class GameConnection
{
    internal GameConnection(
        IPAddress remoteAddress, int remotePort, int localPort, TcpConnectionState state)
    {
        RemoteAddress = remoteAddress;
        RemotePort = remotePort;
        LocalPort = localPort;
        State = state;
    }

    internal IPAddress RemoteAddress { get; }

    internal int RemotePort { get; }

    /// <summary>
    /// Identifies the connection. Adding a block rule does not tear down an already
    /// established flow, so "a connection to a blocked address exists" is not by itself
    /// evidence that the block failed. A connection whose local port appeared *after* the
    /// rule became active is, because the handshake had to complete through the rule.
    /// </summary>
    internal int LocalPort { get; }

    internal TcpConnectionState State { get; }

    internal bool IsVersion6 => RemoteAddress.AddressFamily == AddressFamily.InterNetworkV6;

    internal string Endpoint =>
        IsVersion6 ? $"[{RemoteAddress}]:{RemotePort}" : $"{RemoteAddress}:{RemotePort}";
}
