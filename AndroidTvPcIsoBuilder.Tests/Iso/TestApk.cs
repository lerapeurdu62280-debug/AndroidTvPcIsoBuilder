using System.IO.Compression;
using System.Text;

namespace AndroidTvPcIsoBuilder.Tests.Iso;

/// <summary>
/// APK minimal pour les tests : un zip contenant un AndroidManifest.xml au format XML binaire
/// d'Android (pool de chaînes UTF-16, table des ressources, un élément par permission).
/// </summary>
internal static class TestApk
{
    public static byte[] Create(string packageName, params string[] permissions)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("AndroidManifest.xml").Open();
            var manifest = CreateManifest(packageName, permissions);
            entry.Write(manifest, 0, manifest.Length);
        }
        return memory.ToArray();
    }

    /// <summary>
    /// Chaîne 0 = "name" (liée à android:name par la table des ressources), 1 = "package",
    /// 2 = "manifest", 3 = "uses-permission", puis le paquet et les permissions.
    /// </summary>
    public static byte[] CreateManifest(string packageName, string[] permissions)
    {
        var strings = new List<string> { "name", "package", "manifest", "uses-permission", packageName };
        strings.AddRange(permissions);

        var body = new MemoryStream();
        var writer = new BinaryWriter(body);

        // Pool de chaînes (UTF-16)
        var stringData = new MemoryStream();
        var offsets = new List<int>();
        foreach (var s in strings)
        {
            offsets.Add((int)stringData.Length);
            stringData.Write(BitConverter.GetBytes((ushort)s.Length));
            stringData.Write(Encoding.Unicode.GetBytes(s));
            stringData.Write(new byte[2]);
        }
        while (stringData.Length % 4 != 0)
            stringData.WriteByte(0);
        var poolHeader = 28;
        var stringsStart = poolHeader + strings.Count * 4;
        writer.Write((ushort)0x0001);
        writer.Write((ushort)poolHeader);
        writer.Write(stringsStart + (int)stringData.Length);
        writer.Write(strings.Count);
        writer.Write(0); // styles
        writer.Write(0); // flags : UTF-16
        writer.Write(stringsStart);
        writer.Write(0);
        foreach (var o in offsets)
            writer.Write(o);
        writer.Write(stringData.ToArray());

        // Table des ressources : chaîne 0 ("name") = android:name
        writer.Write((ushort)0x0180);
        writer.Write((ushort)8);
        writer.Write(12);
        writer.Write(0x01010003u);

        WriteElement(writer, 2, (1u, 4u)); // <manifest package="...">
        for (var i = 0; i < permissions.Length; i++)
            WriteElement(writer, 3, (0u, (uint)(5 + i))); // <uses-permission android:name="...">

        var content = body.ToArray();
        var result = new MemoryStream();
        var header = new BinaryWriter(result);
        header.Write((ushort)0x0003);
        header.Write((ushort)8);
        header.Write(8 + content.Length);
        header.Write(content);
        return result.ToArray();
    }

    private static void WriteElement(BinaryWriter writer, uint name, (uint Name, uint Value) attribute)
    {
        writer.Write((ushort)0x0102);
        writer.Write((ushort)16);
        writer.Write(16 + 20 + 20);
        writer.Write(1);             // ligne
        writer.Write(uint.MaxValue); // commentaire
        writer.Write(uint.MaxValue); // espace de noms
        writer.Write(name);
        writer.Write((ushort)20);    // début des attributs
        writer.Write((ushort)20);    // taille d'un attribut
        writer.Write((ushort)1);     // nombre
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);

        writer.Write(uint.MaxValue);
        writer.Write(attribute.Name);
        writer.Write(attribute.Value);
        writer.Write((ushort)8);
        writer.Write((byte)0);
        writer.Write((byte)0x03);
        writer.Write(attribute.Value);
    }
}
