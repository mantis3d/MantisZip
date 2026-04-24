using ICSharpCode.SharpZipLib.Zip;
using System.Text;

// Register GBK encoding (needed for .NET Core/5+)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

Console.WriteLine("=== Testing zp.zip encoding ===");
Console.WriteLine();

Console.WriteLine("1. Default:");
using (var zip = new ZipFile(@"E:\github\MantisZip\zp.zip"))
{
    foreach (ZipEntry e in zip)
    {
        Console.WriteLine($"   {e.Name}");
    }
}
Console.WriteLine();

Console.WriteLine("2. Force GBK (936):");
ZipStrings.CodePage = 936;
using (var zip = new ZipFile(@"E:\github\MantisZip\zp.zip"))
{
    foreach (ZipEntry e in zip)
    {
        Console.WriteLine($"   {e.Name}");
    }
}
Console.WriteLine();

Console.WriteLine("3. Force UTF-8 (65001):");
ZipStrings.CodePage = 65001;
using (var zip = new ZipFile(@"E:\github\MantisZip\zp.zip"))
{
    foreach (ZipEntry e in zip)
    {
        Console.WriteLine($"   {e.Name}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Done ===");