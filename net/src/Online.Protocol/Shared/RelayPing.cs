using System.Diagnostics;
using System.Net.Sockets;

namespace Online.Protocol;

/// <summary>The round trip to a relay as a forming match measures it, the launcher's and the bots' one way: five
/// pings over the relay's UDP link, each waited for half a second, the median of those answered.</summary>
public static class RelayPing
{
    /// <summary>The median round trip in milliseconds over a socket connected to the relay; null when no ping was
    /// answered.</summary>
    public static async Task<double?> MedianMsAsync(UdpClient relay, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(relay);
        var samples = new List<double>();
        for (uint n = 0; n < 5; n++)
        {
            var start = Stopwatch.GetTimestamp();
            await relay.SendAsync(RelayProtocol.Encode(new Ping(n, start)), stop).ConfigureAwait(false);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wait.CancelAfter(500);
            try
            {
                while (true)
                {
                    var r = await relay.ReceiveAsync(wait.Token).ConfigureAwait(false);
                    if (RelayProtocol.Decode(r.Buffer) is Pong p && p.Nonce == n)
                    {
                        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                // no answer in time: the next ping
            }
        }
        return samples.Count == 0 ? null : samples.Order().ElementAt(samples.Count / 2);
    }
}
