using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Launcher.Core.Board;

// The generated C# view of the status board. With no argument: its own checks. --read FILE: the known board the
// C++ test wrote (test_board --write). --write FILE: the same known board, for the C++ test to read. --live PROGRAM:
// a file mapping made here, PROGRAM (test_board) as the game writing it from another process, read here meanwhile.
//   BoardCheck [--read FILE | --write FILE | --live PROGRAM]
var failures = 0;
void Check(bool ok, string what)
{
    if (!ok)
    {
        Console.Error.WriteLine($"board C#: CHECK failed: {what}");
        failures++;
    }
}

unsafe
{
    if (args.Length == 2 && args[0] is "--read" or "--write")
    {
        var bytes = (byte*)NativeMemory.AlignedAlloc(BoardLayout.Size, 64);
        NativeMemory.Clear(bytes, BoardLayout.Size);
        var view = new BoardView(bytes);
        if (args[0] == "--write")
        {
            Known.Write(view);
            File.WriteAllBytes(args[1], new ReadOnlySpan<byte>(bytes, BoardLayout.Size).ToArray());
        }
        else
        {
            File.ReadAllBytes(args[1]).AsSpan().CopyTo(new Span<byte>(bytes, BoardLayout.Size));
            Known.Check(view, Check);
        }
        NativeMemory.AlignedFree(bytes);
        Console.WriteLine($"board C# {args[0][2..]} {args[1]}: {(failures == 0 ? "ok" : "FAIL")}");
        return failures == 0 ? 0 : 1;
    }
    if (args.Length == 2 && args[0] == "--live")
    {
        var path = Path.Combine(Path.GetTempPath(), $"fzgx-board-{Guid.NewGuid():N}.board");
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                file.SetLength(BoardLayout.Size);
            }
            using var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, BoardLayout.Size, MemoryMappedFileAccess.ReadWrite);
            using var accessor = map.CreateViewAccessor(0, BoardLayout.Size);
            byte* at = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
            try
            {
                var view = new BoardView(at);
                view.Make((uint)Environment.ProcessId);
                using var game = Process.Start(args[1], ["--serve", path]);
                var t = Stress.Read(view);
                game.WaitForExit();
                Check(game.ExitCode == 0, $"the C++ writer ended with {game.ExitCode}");
                Check(view.Taken == true && view.HeaderRunnerPid == (uint)game.Id, "the C++ writer took the board");
                Stress.Report("C++ writer, C# reader, two processes", t, Check);
            }
            finally
            {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        finally
        {
            File.Delete(path);
        }
        return failures == 0 ? 0 : 1;
    }
    if (args.Length != 0)
    {
        Console.Error.WriteLine("usage: BoardCheck [--read FILE | --write FILE | --live PROGRAM]");
        return 2;
    }
    var memory = (byte*)NativeMemory.AlignedAlloc(BoardLayout.Size, 64);
    try
    {
        Checks.Header(memory, Check);
        Checks.RoundTrips(memory, Check);
        Checks.Ring(memory, Check);
        NativeMemory.Clear(memory, BoardLayout.Size);
        Known.Write(new BoardView(memory));
        Known.Check(new BoardView(memory), Check);
        NativeMemory.Clear(memory, BoardLayout.Size);
        // a new ModeHead is whole (its lines' and words' text arrays are there), and goes through the board and back
        var head = new BoardModeHead { Serial = 3, NameLength = 1, Count = 1 };
        Check(head.Lines.All(l => l.Label is not null) && head.Words.All(w => w.Text is not null), "a new ModeHead's text arrays are there");
        head.Name[0] = (byte)'K';
        head.Lines[0].Label[0] = (byte)'N';
        head.Words[383].Text[0] = (byte)'w';
        new BoardView(memory).WriteModeHead(head);
        var back = new BoardModeHead();
        Check(new BoardView(memory).TryReadModeHead(back) && back.Serial == 3 && back.Count == 1 && back.Name[0] == 'K' && back.Lines[0].Label[0] == 'N' && back.Words[383].Text[0] == 'w',
              "a ModeHead read back");
        NativeMemory.Clear(memory, BoardLayout.Size);
        var view = new BoardView(memory);
        var writer = new Thread(() => Stress.Write(view, TimeSpan.FromMilliseconds(700)));
        writer.Start();
        var tally = Stress.Read(view);
        writer.Join();
        Stress.Report("two threads", tally, Check);
    }
    finally
    {
        NativeMemory.AlignedFree(memory);
    }
}
Console.WriteLine($"board C#: {(failures == 0 ? "ok" : "FAIL")}");
return failures == 0 ? 0 : 1;
