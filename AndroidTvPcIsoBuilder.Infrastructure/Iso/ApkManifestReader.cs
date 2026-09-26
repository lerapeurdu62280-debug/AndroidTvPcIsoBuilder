using System.IO.Compression;
using System.Text;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Lit dans un APK le nom du paquet et les permissions demandées (uses-permission), depuis
/// AndroidManifest.xml compilé (format XML binaire d'Android, « AXML »).
/// </summary>
public static class ApkManifestReader
{
    private const ushort StringPoolType = 0x0001;
    private const ushort XmlType = 0x0003;
    private const ushort ResourceMapType = 0x0180;
    private const ushort StartElementType = 0x0102;
    private const uint AndroidNameResourceId = 0x01010003;
    private const byte TypeString = 0x03;
    private const uint Utf8Flag = 0x100;

    public sealed record ApkManifest(string PackageName, IReadOnlyList<string> Permissions);

    public static ApkManifest Read(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        var entry = zip.GetEntry("AndroidManifest.xml")
            ?? throw new InvalidDataException($"AndroidManifest.xml absent de {apkPath}");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Parse(memory.ToArray());
    }

    public static ApkManifest Parse(byte[] data)
    {
        if (data.Length < 8 || BitConverter.ToUInt16(data, 0) != XmlType)
            throw new InvalidDataException("AndroidManifest.xml n'est pas au format XML binaire d'Android.");

        string[] strings = [];
        uint[] resourceIds = [];
        string? packageName = null;
        var permissions = new List<string>();

        int offset = BitConverter.ToUInt16(data, 2);
        while (offset + 8 <= data.Length)
        {
            var type = BitConverter.ToUInt16(data, offset);
            var headerSize = BitConverter.ToUInt16(data, offset + 2);
            var size = (int)BitConverter.ToUInt32(data, offset + 4);
            if (size < 8 || offset + size > data.Length)
                break;

            switch (type)
            {
                case StringPoolType:
                    strings = ReadStringPool(data, offset);
                    break;
                case ResourceMapType:
                    resourceIds = new uint[(size - headerSize) / 4];
                    for (var i = 0; i < resourceIds.Length; i++)
                        resourceIds[i] = BitConverter.ToUInt32(data, offset + headerSize + i * 4);
                    break;
                case StartElementType:
                    var element = ReadStartElement(data, offset, headerSize, strings, resourceIds);
                    if (element.Name == "manifest")
                        packageName ??= element.Get("package");
                    else if (element.Name is "uses-permission" or "uses-permission-sdk-23" or "uses-permission-sdk-m"
                             && element.AndroidName is { Length: > 0 } permission && !permissions.Contains(permission))
                        permissions.Add(permission);
                    break;
            }

            offset += size;
        }

        return new ApkManifest(
            packageName ?? throw new InvalidDataException("Nom de paquet introuvable dans AndroidManifest.xml."),
            permissions);
    }

    private sealed record Element(string Name, Dictionary<string, string> Attributes, string? AndroidName)
    {
        public string? Get(string name) => Attributes.GetValueOrDefault(name);
    }

    private static Element ReadStartElement(byte[] data, int offset, int headerSize, string[] strings, uint[] resourceIds)
    {
        var ext = offset + headerSize;
        var name = StringAt(strings, BitConverter.ToUInt32(data, ext + 4));
        var attributeStart = BitConverter.ToUInt16(data, ext + 8);
        var attributeSize = BitConverter.ToUInt16(data, ext + 10);
        var attributeCount = BitConverter.ToUInt16(data, ext + 12);

        var attributes = new Dictionary<string, string>();
        string? androidName = null;
        for (var i = 0; i < attributeCount; i++)
        {
            var a = ext + attributeStart + i * attributeSize;
            var nameIndex = BitConverter.ToUInt32(data, a + 4);
            var rawValue = BitConverter.ToUInt32(data, a + 8);
            var dataType = data[a + 15];
            var typedData = BitConverter.ToUInt32(data, a + 16);

            var value = rawValue != uint.MaxValue ? StringAt(strings, rawValue)
                : dataType == TypeString ? StringAt(strings, typedData)
                : null;
            if (value is null)
                continue;

            // Les noms d'attributs peuvent être effacés (APK optimisés) : android:name se
            // reconnaît alors à son identifiant de ressource.
            var attributeName = StringAt(strings, nameIndex);
            if (nameIndex < resourceIds.Length && resourceIds[nameIndex] == AndroidNameResourceId)
                androidName = value;
            else if (attributeName.Length > 0)
                attributes[attributeName] = value;
        }

        return new Element(name, attributes, androidName);
    }

    private static string[] ReadStringPool(byte[] data, int offset)
    {
        var count = (int)BitConverter.ToUInt32(data, offset + 8);
        var flags = BitConverter.ToUInt32(data, offset + 16);
        var stringsStart = (int)BitConverter.ToUInt32(data, offset + 20);
        var headerSize = BitConverter.ToUInt16(data, offset + 2);
        var utf8 = (flags & Utf8Flag) != 0;

        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var p = offset + stringsStart + (int)BitConverter.ToUInt32(data, offset + headerSize + i * 4);
            if (utf8)
            {
                p += (data[p] & 0x80) != 0 ? 2 : 1; // longueur en caractères
                var byteLength = data[p] & 0x7F;
                if ((data[p] & 0x80) != 0)
                    byteLength = (byteLength << 8) | data[++p];
                p++;
                result[i] = Encoding.UTF8.GetString(data, p, byteLength);
            }
            else
            {
                int length = BitConverter.ToUInt16(data, p);
                p += 2;
                if ((length & 0x8000) != 0)
                {
                    length = ((length & 0x7FFF) << 16) | BitConverter.ToUInt16(data, p);
                    p += 2;
                }
                result[i] = Encoding.Unicode.GetString(data, p, length * 2);
            }
        }

        return result;
    }

    private static string StringAt(string[] strings, uint index)
        => index < strings.Length ? strings[index] : string.Empty;
}
