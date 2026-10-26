using Online.Protocol;

namespace Protocol.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void AFileIsWrittenWholeAndNothingIsLeftBesideIt()
    {
        var folder = Directory.CreateTempSubdirectory("atomic-file-").FullName;
        try
        {
            var file = Path.Combine(folder, "a.json");
            File.WriteAllText(file, "old");
            AtomicFile.WriteAllText(file, "new");
            Assert.Equal("new", File.ReadAllText(file));
            AtomicFile.WriteAllBytes(file, [1, 2, 3]);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
            Assert.Equal([file], Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A write that fails halfway: the file is as it was, the half-written one beside it is gone, and the
    /// caller gets what the write raised.</summary>
    [Fact]
    public void AWriteThatFailsLeavesTheFileAsItWasAndNothingBeside()
    {
        var folder = Directory.CreateTempSubdirectory("atomic-file-").FullName;
        try
        {
            var file = Path.Combine(folder, "a.json");
            File.WriteAllText(file, "old");
            var raised = Assert.Throws<IOException>(() => AtomicFile.Write(file, beside =>
            {
                File.WriteAllText(beside, "half");
                throw new IOException("the disk is full");
            }));
            Assert.Equal("the disk is full", raised.Message);
            Assert.Equal("old", File.ReadAllText(file));
            Assert.Equal([file], Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
