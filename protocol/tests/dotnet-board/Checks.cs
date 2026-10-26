using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Launcher.Core.Board;

/// <summary>The same values as the C++ test's (tests/cxx/test_board.cpp): a snapshot and an event from one number,
/// so that a read that mixes two writes shows.</summary>
internal static class Values
{
    public static BoardSnapshot SnapshotOf(uint k)
    {
        var s = new BoardSnapshot { Serial = k, Frame = k * 3, LocalSeat = k % 30, Count = 30 };
        for (var i = 0u; i < 30; i++)
        {
            s.Racers[i] = new BoardRacer
            {
                Seat = (byte)i, State = (BoardRacerState)(k % 3), Rank = (byte)(k + i), Flags = BoardRacerFlags.HasBestLap,
                Lap = (ushort)k, Kos = (ushort)(k + 1), MaxEnergy = (ushort)(k + 2), LapMs = k + i, TotalMs = k * 2 + i,
                BestLapMs = k * 5, Speed = k + 0.5f, Energy = k * 2f, TopSpeed = k * 3f, X = k - 1f, Y = i, Z = -(float)k,
                Distance = k * 10f,
            };
        }
        return s;
    }

    public static bool Same(BoardSnapshot a, BoardSnapshot b)
    {
        if (a.Serial != b.Serial || a.Frame != b.Frame || a.LocalSeat != b.LocalSeat || a.Count != b.Count)
        {
            return false;
        }
        for (var i = 0; i < 30; i++)
        {
            ref var x = ref a.Racers[i];
            ref var y = ref b.Racers[i];
            if (x.Seat != y.Seat || x.State != y.State || x.Rank != y.Rank || x.Flags != y.Flags || x.Lap != y.Lap ||
                x.Kos != y.Kos || x.MaxEnergy != y.MaxEnergy || x.LapMs != y.LapMs || x.TotalMs != y.TotalMs ||
                x.BestLapMs != y.BestLapMs || x.Speed != y.Speed || x.Energy != y.Energy || x.TopSpeed != y.TopSpeed ||
                x.X != y.X || x.Y != y.Y || x.Z != y.Z || x.Distance != y.Distance)
            {
                return false;
            }
        }
        return true;
    }

    public static BoardEvent EventOf(ulong n) => new()
    {
        Kind = (BoardEventKind)(1 + n % 6), Beat = (uint)(n * 7), Step = (uint)(n * 11), A = (uint)n, B = (uint)~n, Ms = (uint)(n * 13),
    };

    public static bool SameEvent(BoardEvent e, ulong n)
    {
        var x = EventOf(n);
        return e.Seq == (uint)(n * 2 + 2) && e.Kind == x.Kind && e.Beat == x.Beat && e.Step == x.Step && e.A == x.A &&
               e.B == x.B && e.Ms == x.Ms;
    }
}

internal static unsafe class Checks
{
    public static void Header(byte* memory, Action<bool, string> check)
    {
        NativeMemory.Clear(memory, BoardLayout.Size);
        var v = new BoardView(memory);
        check(v.Taken is null, "a board of zeros is not taken");
        v.Make(42);
        check(Encoding.ASCII.GetString(memory, 4) == "FZSB" && v.HeaderVersion == BoardLayout.Version &&
              v.HeaderSize == BoardLayout.Size && v.HeaderLauncherPid == 42, "made");
        check(v.Taken is null, "made, not taken yet");
        // the game's answer, as the C++ Take writes it: its version, its size, its process
        v.HeaderRunnerSize = BoardLayout.Size;
        v.HeaderRunnerPid = 7;
        v.HeaderRunnerVersion = BoardLayout.Version;
        check(v.Taken == true, "taken");
        v.HeaderRunnerVersion = BoardLayout.Version + 1;
        check(v.Taken == false, "a game of another version refuses");
        v.HeaderRunnerVersion = BoardLayout.Version;
        v.HeaderRunnerSize = BoardLayout.Size + 64;
        check(v.Taken == false, "a game of another size refuses");
        check(BoardLayout.HeaderMagic == 0 && BoardLayout.HeaderVersion == 4 && BoardLayout.HeaderSize == 8 &&
              BoardLayout.HeaderLauncherPid == 12 && BoardLayout.HeaderRunnerVersion == 16 && BoardLayout.HeaderRunnerSize == 20 &&
              BoardLayout.HeaderRunnerPid == 24, "the header is where version 1 put it");
    }

    public static void RoundTrips(byte* memory, Action<bool, string> check)
    {
        NativeMemory.Clear(memory, BoardLayout.Size);
        var v = new BoardView(memory);
        var st = new BoardStatus();
        check(v.TryReadStatus(st) && st.Phase == BoardPhase.None, "zeros read as nothing yet");
        v.WriteStatus(new BoardStatus { Phase = BoardPhase.Gate, Flags = BoardStatusFlags.Watching, Step = 9, StopSeen = 1, NoticeSeen = 2 });
        check(v.TryReadStatus(st) && st is { Phase: BoardPhase.Gate, Flags: 4, Step: 9, StopSeen: 1, NoticeSeen: 2 }, "status");
        v.WriteSnapshot(Values.SnapshotOf(11));
        var snap = new BoardSnapshot();
        check(v.TryReadSnapshot(snap) && Values.Same(snap, Values.SnapshotOf(11)), "snapshot");
        var mail = new BoardMailbox { StopSerial = 2, NoticeSerial = 3, NoticeLength = 4 };
        "abcd"u8.CopyTo(mail.NoticeText);
        v.WriteMailbox(mail);
        var got = new BoardMailbox();
        check(v.TryReadMailbox(got) && got.StopSerial == 2 && got.NoticeSerial == 3 && got.NoticeLength == 4 &&
              got.NoticeText.AsSpan(0, 5).SequenceEqual("abcd\0"u8), "mailbox");
        v.PulseHeartbeat = 1ul << 40;
        v.PulseStopReason = BoardStopReason.Launcher;
        check(v.PulseHeartbeat == 1ul << 40 && v.PulseStopReason == BoardStopReason.Launcher, "pulse");
        // a writer caught in the middle: every try fails, and the reader says so
        Interlocked.Increment(ref *(uint*)(memory + BoardLayout.Status));
        check(!v.TryReadStatus(st, 8), "a status in the middle of its write is not read");
    }

    public static void Ring(byte* memory, Action<bool, string> check)
    {
        NativeMemory.Clear(memory, BoardLayout.Size);
        var v = new BoardView(memory);
        check(v.ReadEvents(0, out _) == BoardRead.NotYet, "no event yet");
        for (var n = 0ul; n < 200; n++)
        {
            v.WriteEvents(n, Values.EventOf(n));
        }
        int lost = 0, ok = 0;
        for (var n = 0ul; n < 200; n++)
        {
            var r = v.ReadEvents(n, out var e);
            lost += r == BoardRead.Lost ? 1 : 0;
            ok += r == BoardRead.Ok && Values.SameEvent(e, n) ? 1 : 0;
        }
        check(v.EventsHead == 200 && lost == 200 - BoardLayout.EventsSlots && ok == BoardLayout.EventsSlots, "overwrite-oldest");
        check(v.ReadEvents(200, out _) == BoardRead.NotYet, "the next is not yet");
    }
}

/// <summary>The C++ test's known board (test_board.cpp Known).</summary>
internal static class Known
{
    private static readonly byte[] Text = Encoding.UTF8.GetBytes("héllo");

    public static void Write(BoardView v)
    {
        v.Make(4242);
        v.HeaderRunnerSize = BoardLayout.Size;
        v.HeaderRunnerPid = 4343;
        v.HeaderRunnerVersion = BoardLayout.Version;
        v.PulseHeartbeat = 0x0102030405060708ul;
        v.PulseWatchBeat = 99;
        v.PulseStopReason = BoardStopReason.Escape;
        v.WriteStatus(new BoardStatus { Phase = BoardPhase.Finished, Flags = BoardStatusFlags.Practice, Step = 5000, StopSeen = 1, NoticeSeen = 2 });
        v.WriteSnapshot(Values.SnapshotOf(321));
        for (var n = 0ul; n < 70; n++)
        {
            v.WriteEvents(n, Values.EventOf(n));
        }
        var mail = new BoardMailbox { StopSerial = 3, NoticeSerial = 5, NoticeLength = (uint)Text.Length };
        Text.CopyTo(mail.NoticeText, 0);
        v.WriteMailbox(mail);
    }

    public static void Check(BoardView v, Action<bool, string> check)
    {
        check(v.HeaderMagic == BoardLayout.Magic && v.HeaderLauncherPid == 4242 && v.HeaderRunnerPid == 4343 && v.Taken == true, "header");
        check(v.PulseHeartbeat == 0x0102030405060708ul && v.PulseWatchBeat == 99 && v.PulseStopReason == BoardStopReason.Escape, "pulse");
        var st = new BoardStatus();
        check(v.TryReadStatus(st) && st is { Phase: BoardPhase.Finished, Flags: 2, Step: 5000, StopSeen: 1, NoticeSeen: 2 }, "status");
        var s = new BoardSnapshot();
        check(v.TryReadSnapshot(s) && Values.Same(s, Values.SnapshotOf(321)), "snapshot");
        check(v.EventsHead == 70 && v.ReadEvents(5, out _) == BoardRead.Lost, "events: head, the oldest lost");
        check(v.ReadEvents(6, out var e6) == BoardRead.Ok && Values.SameEvent(e6, 6), "event 6");
        check(v.ReadEvents(69, out var e69) == BoardRead.Ok && Values.SameEvent(e69, 69), "event 69");
        var mail = new BoardMailbox();
        check(v.TryReadMailbox(mail) && mail.StopSerial == 3 && mail.NoticeSerial == 5 && mail.NoticeLength == Text.Length &&
              mail.NoticeText.AsSpan(0, Text.Length).SequenceEqual(Text), "mailbox");
    }
}

/// <summary>One writer, one reader: no read may mix two writes (test_board.cpp Writer and Reader).</summary>
internal static class Stress
{
    public sealed record Tally(long Reads, long Torn, long Events, long BadEvents, long Lost);

    public static void Write(BoardView v, TimeSpan howLong)
    {
        var clock = Stopwatch.StartNew();
        var k = 1u;
        var n = 0ul;
        var st = new BoardStatus();
        while (clock.Elapsed < howLong)
        {
            v.WriteSnapshot(Values.SnapshotOf(k));
            st.Phase = (BoardPhase)(k % 12);
            st.Flags = st.Step = st.StopSeen = st.NoticeSeen = k;
            v.WriteStatus(st);
            v.WriteEvents(n, Values.EventOf(n));
            n++;
            v.PulseHeartbeat = k;
            k++;
        }
        v.PulseStopReason = BoardStopReason.Game;
    }

    public static Tally Read(BoardView v)
    {
        long reads = 0, torn = 0, events = 0, bad = 0, lost = 0;
        var next = 0ul;
        var s = new BoardSnapshot();
        var st = new BoardStatus();
        var limit = Stopwatch.StartNew();
        while (v.PulseStopReason != BoardStopReason.Game && limit.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (v.TryReadSnapshot(s, 1000))
            {
                reads++;
                torn += s.Serial != 0 && !Values.Same(s, Values.SnapshotOf(s.Serial)) ? 1 : 0;
            }
            if (v.TryReadStatus(st, 1000))
            {
                reads++;
                torn += st.Flags != st.Step || st.Step != st.StopSeen || st.Step != st.NoticeSeen ||
                        (st.Step != 0 && st.Phase != (BoardPhase)(st.Step % 12)) ? 1 : 0;
            }
            for (var head = v.EventsHead; next < head; next++)
            {
                var r = v.ReadEvents(next, out var e);
                if (r == BoardRead.NotYet)
                {
                    break;
                }
                events += r == BoardRead.Ok ? 1 : 0;
                bad += r == BoardRead.Ok && !Values.SameEvent(e, next) ? 1 : 0;
                lost += r == BoardRead.Lost ? 1 : 0;
            }
        }
        return new Tally(reads, torn, events, bad, lost);
    }

    public static void Report(string what, Tally t, Action<bool, string> check)
    {
        Console.WriteLine($"board C# {what}: {t.Reads} reads, {t.Torn} torn; {t.Events} events read, {t.BadEvents} wrong, {t.Lost} lost to overwriting");
        check(t.Reads > 1000 && t.Torn == 0 && t.Events > 100 && t.BadEvents == 0, what);
    }
}
