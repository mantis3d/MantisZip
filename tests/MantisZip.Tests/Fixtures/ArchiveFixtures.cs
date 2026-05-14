using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

namespace MantisZip.Tests.Fixtures;

/// <summary>
/// Creates small archive files for use in engine tests.
/// All files are created in the system temp directory and cleaned up by the caller.
/// </summary>
public static class ArchiveFixtures
{
    /// <summary>Shared known content used across test archives.</summary>
    public static readonly string HelloText = "Hello, World!";
    public static readonly string NestedDirFileContent = "Nested content";
    public static readonly string BinaryContent = "\x00\x01\x02\xFF\xFE";

    /// <summary>
    /// Create a temp directory containing source files for compression tests.
    /// Returns the directory path. Caller must delete it.
    /// </summary>
    public static string CreateSourceDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "hello.txt"), HelloText);
        File.WriteAllText(Path.Combine(dir, "binary.dat"), BinaryContent);

        var subDir = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), NestedDirFileContent);

        return dir;
    }

    /// <summary>
    /// Create a small ZIP archive for testing. Returns the file path.
    /// </summary>
    public static string CreateZipArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);

        var entry = new ZipEntry("hello.txt");
        zipStream.PutNextEntry(entry);
        var helloBytes = Encoding.UTF8.GetBytes(HelloText);
        zipStream.Write(helloBytes, 0, helloBytes.Length);
        zipStream.CloseEntry();

        var nestedEntry = new ZipEntry("subdir/nested.txt");
        zipStream.PutNextEntry(nestedEntry);
        var nestedBytes = Encoding.UTF8.GetBytes(NestedDirFileContent);
        zipStream.Write(nestedBytes, 0, nestedBytes.Length);
        zipStream.CloseEntry();

        return path;
    }

    /// <summary>
    /// Create a small encrypted ZIP archive for testing. Returns the file path.
    /// Password: "test123"
    /// </summary>
    public static string CreateEncryptedZipArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_encrypted.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);
        zipStream.Password = "test123";

        var entry = new ZipEntry("secret.txt");
        entry.IsCrypted = true;
        zipStream.PutNextEntry(entry);
        var content = Encoding.UTF8.GetBytes("secret data");
        zipStream.Write(content, 0, content.Length);
        zipStream.CloseEntry();

        return path;
    }

    /// <summary>
    /// Create a small TAR.GZ archive for testing. Returns the file path.
    /// </summary>
    public static string CreateTarGzArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.tar.gz");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var dirName = Path.GetDirectoryName(path)!;

        // Write a temp tar first
        var tarPath = Path.Combine(dirName, "temp.tar");
        try
        {
            using (var tarFs = File.Create(tarPath))
            using (var tarOut = new TarOutputStream(tarFs, Encoding.UTF8))
            {
                var entry = TarEntry.CreateTarEntry("hello.txt");
                var helloBytes = Encoding.UTF8.GetBytes(HelloText);
                entry.Size = helloBytes.Length;
                tarOut.PutNextEntry(entry);
                tarOut.Write(helloBytes, 0, helloBytes.Length);
                tarOut.CloseEntry();

                var nestedEntry = TarEntry.CreateTarEntry("subdir/nested.txt");
                var nestedBytes = Encoding.UTF8.GetBytes(NestedDirFileContent);
                nestedEntry.Size = nestedBytes.Length;
                tarOut.PutNextEntry(nestedEntry);
                tarOut.Write(nestedBytes, 0, nestedBytes.Length);
                tarOut.CloseEntry();
            }

            // GZip compress
            using (var inputFs = File.OpenRead(tarPath))
            using (var outputFs = File.Create(path))
            using (var gzipStream = new GZipOutputStream(outputFs))
            {
                inputFs.CopyTo(gzipStream);
            }
        }
        finally
        {
            if (File.Exists(tarPath)) File.Delete(tarPath);
        }

        return path;
    }

    /// <summary>
    /// Create a small 7z archive using 7z.exe. Returns the file path, or null if 7z.exe not found.
    /// </summary>
    public static string? CreateSevenZipArchive()
    {
        var sevenZipExe = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(sevenZipExe))
            return null;

        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.7z");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var srcFile = Path.Combine(dir, "hello.txt");
        File.WriteAllText(srcFile, HelloText);

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"a -t7z \"{path}\" \"{srcFile}\" -mx=1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        process.WaitForExit(15000);

        return File.Exists(path) ? path : null;
    }
}
