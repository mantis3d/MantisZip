using System;
using System.IO;
using System.Text;
using System.Windows.Media;

var woffPath = Path.GetFullPath(args.Length > 0 ? args[0] : "TestPreview/font/FiraCode-Medium.woff");
Console.WriteLine($"WOFF file: {woffPath}");
Console.WriteLine();

// Manually parse name table from decompressed TTF
var info = MantisZip.Core.Utils.FontParser.Parse(woffPath);
if (info == null) { Console.WriteLine("Parse returned NULL"); return; }

Console.WriteLine($"FontParser result:");
Console.WriteLine($"  FontName:      {info.FontName}");
Console.WriteLine($"  FontStyle:     {info.FontStyle}");
Console.WriteLine($"  GlyphCount:    {info.GlyphCount}");
Console.WriteLine($"  Decompressed:  {info.FontDecompressedPath}");
Console.WriteLine();

// Read raw name table from decompressed TTF
var ttfPath = info.FontDecompressedPath ?? woffPath;
using var fs = new FileStream(ttfPath, FileMode.Open, FileAccess.Read);
using var reader = new BinaryReader(fs);

// Skip to table directory
reader.ReadBytes(4); // sfVersion
ushort numTables = ReadBEU16(reader);
reader.ReadBytes(6); // searchRange, entrySelector, rangeShift

uint nameOff = 0, nameLen = 0;
for (int i = 0; i < numTables; i++)
{
    string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
    reader.ReadBytes(4); // checksum
    uint off = ReadBEU32(reader);
    uint len = ReadBEU32(reader);
    if (tag == "name") { nameOff = off; nameLen = len; break; }
}

if (nameOff == 0) { Console.WriteLine("No name table found"); return; }

Console.WriteLine("Name table entries:");
Console.WriteLine($"  Offset: 0x{nameOff:X}, Length: {nameLen}");
Console.WriteLine();

fs.Seek(nameOff, SeekOrigin.Begin);
ReadBEU16(reader); // format
ushort nameCount = ReadBEU16(reader);
ushort strOff = ReadBEU16(reader);

for (int i = 0; i < nameCount; i++)
{
    ushort pid = ReadBEU16(reader);
    ushort eid = ReadBEU16(reader);
    ushort lid = ReadBEU16(reader);
    ushort nid = ReadBEU16(reader);
    ushort len = ReadBEU16(reader);
    ushort off = ReadBEU16(reader);
    if (len == 0) continue;

    long saved = fs.Position;
    try
    {
        long pos = nameOff + strOff + off;
        if (pos < 0 || pos + len > fs.Length) continue;

        fs.Seek(pos, SeekOrigin.Begin);
        byte[] raw = reader.ReadBytes(len);

        string val = pid == 3 ? Encoding.BigEndianUnicode.GetString(raw)
            : pid == 1 ? Encoding.ASCII.GetString(raw)
            : Encoding.UTF8.GetString(raw);
        int nul = val.IndexOf('\0');
        if (nul >= 0) val = val[..nul];

        string nidName = nid switch
        {
            0 => "Copyright",
            1 => "Font Family",
            2 => "Font Subfamily",
            3 => "Unique ID",
            4 => "Full Name",
            5 => "Version",
            6 => "PostScript Name",
            7 => "Trademark",
            9 => "Designer",
            10 => "Description",
            11 => "Vendor URL",
            12 => "Designer URL",
            13 => "License",
            14 => "License URL",
            _ => $"Name ID {nid}"
        };

        string platform = pid switch { 1 => "Mac", 3 => "Win", 0 => "Unicode", _ => $"pid={pid}" };
        string encoding = pid == 3 ? (eid == 1 ? "UCS-2" : eid == 10 ? "UCS-4" : $"eid={eid}") : eid.ToString();

        Console.WriteLine($"  [{nidName,-20}] platform={platform,-8} encoding={encoding,-8} lang={lid,-5} value=\"{val}\"");
    }
    finally { fs.Seek(saved, SeekOrigin.Begin); }
}

Console.WriteLine();
Console.WriteLine("FontFamily test:");
try
{
    var familyName = info.FontName ?? Path.GetFileNameWithoutExtension(woffPath);
    Console.WriteLine($"  Trying: \"{ttfPath}#{familyName}\"");
    var ff = new FontFamily(ttfPath + "#" + familyName);
    Console.WriteLine($"  Success: Source={ff.Source}");
    foreach (var kv in ff.FamilyNames)
        Console.WriteLine($"    FamilyName[{kv.Key}]: {kv.Value}");
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
}

static ushort ReadBEU16(BinaryReader r)
{
    var b = r.ReadBytes(2);
    if (b.Length < 2) throw new EndOfStreamException();
    return (ushort)((b[0] << 8) | b[1]);
}

static uint ReadBEU32(BinaryReader r)
{
    var b = r.ReadBytes(4);
    if (b.Length < 4) throw new EndOfStreamException();
    return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
}
