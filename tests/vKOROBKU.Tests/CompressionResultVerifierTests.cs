extern alias worker;

using worker::vKOROBKU.Worker;
using WorkerJob = worker::vKOROBKU.Protocol.WorkerJob;

namespace vKOROBKU.Tests;

public sealed class CompressionResultVerifierTests
{
    [Theory]
    [InlineData("XPRESS4K", 4 * 1024)]
    [InlineData("XPRESS8K", 8 * 1024)]
    [InlineData("XPRESS16K", 16 * 1024)]
    [InlineData("LZX", 32 * 1024)]
    public void CountErrors_ReadableFileNoLargerThanCompressionChunk_IsNotAnError(
        string algorithm,
        int length)
    {
        var path = CreateFile(length);
        try
        {
            var errors = CountErrors(path, length, algorithm);

            Assert.Equal(0, errors);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CountErrors_CompressibleFileLargerThanCompressionChunk_IsAnError()
    {
        const int length = 20 * 1024;
        var path = CreateFile(length);
        try
        {
            var errors = CountErrors(path, length, "XPRESS16K");

            Assert.Equal(1, errors);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CountErrors_LockedSmallFile_RemainsAnError()
    {
        const int length = 12 * 1024;
        var path = CreateFile(length);
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var errors = CountErrors(path, length, "XPRESS16K");

                Assert.Equal(1, errors);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int CountErrors(string path, long length, string algorithm) =>
        CompressionResultVerifier.CountErrors(
            [new WorkerFile(path, length)],
            new WorkerJob(Path.GetDirectoryName(path)!, "compress", algorithm),
            CancellationToken.None).Errors;

    private static string CreateFile(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vkorobku-verifier-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }
}
