using System.Text;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Corrige les noms Joliet des fichiers sans extension écrits par CDBuilder : il les enregistre
/// comme "kernel.;1", avec le point qui sépare le nom de l'extension ISO9660. Linux retire ce
/// point, mais pas GRUB : en Joliet, il compare les noms tels quels (sans ignorer la casse),
/// si bien que "linux /kernel" et "search -f /kernel" ne trouvaient plus "kernel." et l'ISO ne
/// démarrait pas. Le nom est raccourci sur place ("kernel;1") ; la longueur de l'enregistrement
/// ne change pas, les 2 octets libérés restent à zéro en zone "System Use".
/// </summary>
public static class JolietFileNameFixer
{
    private const int SectorSize = 2048;
    private const int FirstDescriptorSector = 16;
    private const byte SupplementaryDescriptorType = 2;
    private const byte TerminatorType = 255;
    private const int RootDirectoryRecordOffset = 156;
    private const byte DirectoryFlag = 0x02;

    private static readonly byte[] TrailingDotWithVersion = Encoding.BigEndianUnicode.GetBytes(".;1");
    private static readonly byte[] Version = Encoding.BigEndianUnicode.GetBytes(";1");

    /// <returns>Nombre de noms corrigés.</returns>
    public static int Fix(Stream isoStream)
    {
        var root = FindJolietRoot(isoStream);
        if (root is null)
            return 0;

        var fixedCount = 0;
        var visited = new HashSet<int>();
        var pending = new Stack<(int Lba, int Length)>();
        pending.Push(root.Value);

        while (pending.Count > 0)
        {
            var (lba, length) = pending.Pop();
            if (!visited.Add(lba))
                continue;

            var sectors = (length + SectorSize - 1) / SectorSize;
            var data = new byte[sectors * SectorSize];
            isoStream.Seek((long)lba * SectorSize, SeekOrigin.Begin);
            isoStream.ReadExactly(data);

            var modified = false;
            for (var sector = 0; sector < sectors; sector++)
            {
                var position = sector * SectorSize;
                var sectorEnd = position + SectorSize;
                // Un enregistrement ne chevauche jamais deux secteurs : un octet de longueur nul
                // signale la fin des enregistrements de ce secteur.
                while (position < sectorEnd && data[position] > 0)
                {
                    var recordLength = data[position];
                    var nameLength = data[position + 32];
                    var isDirectory = (data[position + 25] & DirectoryFlag) != 0;
                    var isSelfOrParent = nameLength == 1 && data[position + 33] is 0 or 1;

                    if (isDirectory && !isSelfOrParent)
                    {
                        pending.Push((BitConverter.ToInt32(data, position + 2), BitConverter.ToInt32(data, position + 10)));
                    }
                    else if (!isDirectory && nameLength > TrailingDotWithVersion.Length
                        && data.AsSpan(position + 33 + nameLength - TrailingDotWithVersion.Length, TrailingDotWithVersion.Length).SequenceEqual(TrailingDotWithVersion))
                    {
                        var newNameLength = nameLength - 2;
                        Version.CopyTo(data, position + 33 + newNameLength - Version.Length);
                        data[position + 33 + newNameLength] = 0;
                        data[position + 33 + newNameLength + 1] = 0;
                        data[position + 32] = (byte)newNameLength;
                        modified = true;
                        fixedCount++;
                    }

                    position += recordLength;
                }
            }

            if (modified)
            {
                isoStream.Seek((long)lba * SectorSize, SeekOrigin.Begin);
                isoStream.Write(data);
            }
        }

        return fixedCount;
    }

    private static (int Lba, int Length)? FindJolietRoot(Stream isoStream)
    {
        var descriptor = new byte[SectorSize];
        for (var sector = FirstDescriptorSector; ; sector++)
        {
            isoStream.Seek((long)sector * SectorSize, SeekOrigin.Begin);
            if (isoStream.Read(descriptor, 0, SectorSize) < SectorSize)
                return null;
            if (Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001" || descriptor[0] == TerminatorType)
                return null;

            // Séquences d'échappement Joliet : "%/@", "%/C" ou "%/E" (niveaux UCS-2 1 à 3).
            if (descriptor[0] == SupplementaryDescriptorType && descriptor[88] == '%' && descriptor[89] == '/')
                return (BitConverter.ToInt32(descriptor, RootDirectoryRecordOffset + 2),
                        BitConverter.ToInt32(descriptor, RootDirectoryRecordOffset + 10));
        }
    }
}
