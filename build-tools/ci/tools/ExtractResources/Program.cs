// Build helper, not part of the mod.
//
//   ExtractResources <assembly.dll> <output folder>
//
// Writes every file embedded in a .NET assembly to the output folder and prints what it found.
// Plain manifest resources are written under their resource name. A ".resources" container (what
// a .resx file compiles to) is opened and each byte-array or stream entry in it is written under
// its entry name. fetch-refs.sh uses this to get the bHaptics and Shockwave SDK libraries out of
// DrBibop's original VRPatcher.dll, which carries them as embedded files.
using System;
using System.Collections;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: ExtractResources <assembly.dll> <output folder>");
            return 2;
        }

        string outDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outDir);

        using FileStream file = File.OpenRead(args[0]);
        using PEReader pe = new PEReader(file);
        MetadataReader md = pe.GetMetadataReader();

        var corHeader = pe.PEHeaders.CorHeader;
        if (corHeader == null || corHeader.ResourcesDirectory.Size == 0)
        {
            Console.WriteLine("  (no embedded resources)");
            return 0;
        }

        PEMemoryBlock resources = pe.GetSectionData(corHeader.ResourcesDirectory.RelativeVirtualAddress);

        foreach (ManifestResourceHandle handle in md.ManifestResources)
        {
            ManifestResource resource = md.GetManifestResource(handle);
            string name = md.GetString(resource.Name);

            if (!resource.Implementation.IsNil)
            {
                Console.WriteLine($"  {name}: stored in another file, skipped");
                continue;
            }

            BlobReader reader = resources.GetReader((int)resource.Offset, resources.Length - (int)resource.Offset);
            int length = reader.ReadInt32();
            byte[] data = reader.ReadBytes(length);

            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {name}: resource container, {length} bytes");
                ExtractContainer(data, outDir);
            }
            else
            {
                Write(outDir, name, data, 0, data.Length);
            }
        }

        return 0;
    }

    private static void ExtractContainer(byte[] container, string outDir)
    {
        using ResourceReader reader = new ResourceReader(new MemoryStream(container));

        foreach (DictionaryEntry entry in reader)
        {
            string key = (string)entry.Key;
            reader.GetResourceData(key, out string type, out byte[] data);

            // Byte arrays and streams are stored as a 4-byte length followed by the bytes.
            bool binary = type.Contains("ByteArray") || type.Contains("Stream") || type.StartsWith("System.Byte[]");
            if (binary && data.Length >= 4 && BitConverter.ToInt32(data, 0) == data.Length - 4)
                Write(outDir, key, data, 4, data.Length - 4);
            else
                Console.WriteLine($"    {key}: {type}, {data.Length} bytes, not a file - skipped");
        }
    }

    private static void Write(string outDir, string name, byte[] data, int offset, int count)
    {
        string safe = name;
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');

        using (FileStream output = File.Create(Path.Combine(outDir, safe)))
            output.Write(data, offset, count);

        Console.WriteLine($"    {safe}: {count} bytes");
    }
}
