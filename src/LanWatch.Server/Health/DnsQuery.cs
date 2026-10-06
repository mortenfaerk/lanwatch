using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LanWatch.Server.Health;

/// <summary>
/// A minimal DNS A-record lookup against a specific server. .NET's resolver always uses the OS configuration,
/// and the probe has to ask lancache-dns itself.
/// </summary>
public static class DnsQuery
{
    public static async Task<IReadOnlyList<IPAddress>> QueryAAsync(IPEndPoint server, string name, TimeSpan timeout, CancellationToken ct)
    {
        var id = (ushort)Random.Shared.Next(ushort.MaxValue);
        var query = BuildQuery(id, name);

        using var udp = new UdpClient(server.AddressFamily);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await udp.SendAsync(query, server, cts.Token);
        var response = await udp.ReceiveAsync(cts.Token);
        return ParseAnswers(response.Buffer, id);
    }

    internal static byte[] BuildQuery(ushort id, string name)
    {
        var buf = new List<byte>(32 + name.Length);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // one question
        buf.AddRange(header.ToArray());
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.AddRange([0, 0, 1, 0, 1]); // root, QTYPE A, QCLASS IN
        return [.. buf];
    }

    internal static IReadOnlyList<IPAddress> ParseAnswers(byte[] msg, ushort expectedId)
    {
        if (msg.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(msg) != expectedId) return [];
        var rcode = msg[3] & 0x0F;
        if (rcode != 0) return [];
        int qd = BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(4)), an = BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(6));
        var pos = 12;
        for (var i = 0; i < qd; i++) pos = SkipName(msg, pos) + 4;

        var result = new List<IPAddress>();
        for (var i = 0; i < an && pos < msg.Length; i++)
        {
            pos = SkipName(msg, pos);
            if (pos + 10 > msg.Length) break;
            var type = BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(pos));
            var len = BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(pos + 8));
            pos += 10;
            if (type == 1 && len == 4 && pos + 4 <= msg.Length) result.Add(new IPAddress(msg.AsSpan(pos, 4)));
            pos += len;
        }
        return result;
    }

    private static int SkipName(byte[] msg, int pos)
    {
        while (pos < msg.Length)
        {
            var len = msg[pos];
            if (len == 0) return pos + 1;
            if ((len & 0xC0) == 0xC0) return pos + 2; // compression pointer
            pos += len + 1;
        }
        return pos;
    }
}
