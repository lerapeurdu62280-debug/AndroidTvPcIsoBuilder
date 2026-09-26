using System.Linq;
using DiscUtils.Iso9660;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Lit et réapplique le catalogue de boot El Torito d'une image ISO9660 au niveau binaire,
/// sans dépendre du support (limité à une seule image) de DiscUtils.Iso9660.CDBuilder.
/// Référence : "El Torito" Bootable CD-ROM Format Specification v1.0 (1995).
/// </summary>
public class BootCatalogPreserver
{
    public const int SectorSize = 2048;

    private const int VolumeDescriptorStartSector = 16;
    private const byte BootRecordTypeCode = 0x00;
    private const byte BootCatalogEntrySize = 32;

    /// <summary>
    /// Lit le catalogue de boot. <paramref name="reader"/>, s'il est fourni, permet de retrouver
    /// la vraie taille de l'image de boot en mode "No Emulation" (ex: isolinux.bin) : dans ce mode,
    /// le champ "Sector Count" du catalogue ne représente pas la taille du fichier mais un nombre
    /// de secteurs de démarrage initial arbitraire (souvent 4, indépendamment de la taille réelle),
    /// donc s'y fier tronque le loader et casse le boot ("Image checksum error" côté ISOLINUX).
    /// Sans reader, on retombe sur le champ du catalogue (peut sous-évaluer la taille en No Emulation).
    /// </summary>
    public BootCatalogInfo? ReadBootCatalog(Stream isoStream, CDReader? reader = null)
    {
        var bootRecordSector = FindBootRecordVolumeDescriptor(isoStream);
        if (bootRecordSector is null)
            return null;

        var bootRecord = ReadSector(isoStream, bootRecordSector.Value);

        var catalogLba = BitConverter.ToInt32(bootRecord, 71);
        var catalogSector = ReadSector(isoStream, catalogLba);

        // Le Platform ID de la Validation Entry (offset 1, premier bloc de 32 octets) s'applique à
        // l'Initial/Default Entry qui suit, ainsi qu'à toute entrée tant qu'aucune Section Header
        // (0x90/0x91) ne vient annoncer un nouveau Platform ID pour les entrées suivantes.
        var currentPlatformId = (BootPlatformId)catalogSector[1];

        var bootImages = new List<BootImageInfo>();
        for (int entryOffset = BootCatalogEntrySize; entryOffset + BootCatalogEntrySize <= SectorSize; entryOffset += BootCatalogEntrySize)
        {
            var entry = catalogSector.AsSpan(entryOffset, BootCatalogEntrySize);
            var bootIndicatorOrHeaderId = entry[0];

            // Une Section Header (0x90 = dernière, 0x91 = suivie d'une autre) annonce un nouveau
            // Platform ID pour les entrées de boot qui la suivent dans ce bloc.
            if (bootIndicatorOrHeaderId is 0x90 or 0x91)
            {
                currentPlatformId = (BootPlatformId)entry[1];
                continue;
            }

            // Une entrée de boot valide (Initial/Default Entry ou Section Entry) commence par 0x88.
            if (bootIndicatorOrHeaderId != 0x88)
                continue;

            var bootMediaType = entry[1];
            var sectorCount512 = BitConverter.ToUInt16(entry.Slice(6, 2));
            var loadRba = BitConverter.ToInt32(entry.Slice(8, 4));

            if (loadRba == 0)
                continue;

            // Le champ "Sector Count" du catalogue El Torito ne reflète pas de façon fiable la
            // taille réelle de l'image : pour les émulations de disquette, la taille est fixée par
            // le type d'émulation lui-même ; pour "No Emulation" (0x00, ex: isolinux.bin chargé en
            // mode natif BIOS), ce champ est un nombre de secteurs de démarrage initial arbitraire
            // (souvent 4 = 2 Ko), sans rapport avec la taille réelle du fichier — s'y fier tronque
            // le loader. On retrouve alors la vraie taille via le fichier du système ISO9660 dont
            // le premier secteur correspond à loadRba, quand un reader est disponible.
            var sizeInBytes = bootMediaType switch
            {
                0x01 => 1200 * 1024, // Diskette 1.2M
                0x02 => 1440 * 1024, // Diskette 1.44M
                0x03 => 2880 * 1024, // Diskette 2.88M
                0x00 => ReadIsolinuxBootInfoLength(isoStream, loadRba)
                        ?? (reader is not null ? FindFileSizeAtLba(reader, loadRba) : null)
                        ?? sectorCount512 * 512,
                _ => sectorCount512 * 512
            };
            var imageData = ReadBytes(isoStream, loadRba, sizeInBytes);

            bootImages.Add(new BootImageInfo(
                CatalogEntryOffset: entryOffset,
                OriginalLba: loadRba,
                SizeInBytes: sizeInBytes,
                Data: imageData,
                PlatformId: currentPlatformId));
        }

        return new BootCatalogInfo(bootRecordSector.Value, bootRecord, catalogSector, bootImages);
    }

    /// <summary>
    /// Réécrit le catalogue de boot et les images de boot d'origine dans une nouvelle image ISO
    /// (déjà générée par CDBuilder), en les plaçant dans les secteurs libres situés après la fin
    /// des données existantes, et en corrigeant les LBA dans le Boot Record et le catalogue.
    /// Les entrées dont le Platform ID n'est pas dans <paramref name="platformsToKeep"/> sont
    /// neutralisées dans le catalogue réécrit (leur LBA est mis à 0, ce qui les rend inertes sans
    /// perturber la structure du catalogue) : elles ne seront ni recopiées ni utilisables au boot.
    /// </summary>
    public void ApplyBootCatalog(Stream targetIsoStream, BootCatalogInfo bootCatalog, IReadOnlySet<BootPlatformId>? platformsToKeep = null)
    {
        targetIsoStream.Seek(0, SeekOrigin.End);
        var appendSector = (int)((targetIsoStream.Length + SectorSize - 1) / SectorSize);

        var catalogSector = (byte[])bootCatalog.RawCatalogSector.Clone();
        var nextFreeSector = appendSector + 1;

        var relocatedImages = new List<(int NewLba, byte[] Data)>();
        foreach (var image in bootCatalog.BootImages)
        {
            if (platformsToKeep is not null && !platformsToKeep.Contains(image.PlatformId))
            {
                BitConverter.GetBytes(0).CopyTo(catalogSector, image.CatalogEntryOffset + 8);
                continue;
            }

            var newLba = nextFreeSector;
            var sectorsNeeded = (image.SizeInBytes + SectorSize - 1) / SectorSize;
            nextFreeSector += sectorsNeeded;

            BitConverter.GetBytes(newLba).CopyTo(catalogSector, image.CatalogEntryOffset + 8);

            // La copie de l'image de boot réellement chargée par le BIOS est CELLE-CI (relocalisée
            // en fin de disque à newLba), pas le fichier du même nom resté dans l'arborescence
            // ISO9660 : c'est l'entrée du catalogue El Torito (patchée juste au-dessus) qui fait foi
            // pour le BIOS, indépendamment de l'emplacement du fichier visible dans le système de
            // fichiers. Si cette image est un loader ISOLINUX (identifiable par sa signature et sa
            // Boot Info Table intégrée à l'offset 8), il faut donc patcher le checksum/LBA ICI, sur
            // ces bytes-là avec newLba, plutôt que sur le fichier de l'arborescence : patcher ce
            // dernier laisserait la copie réellement exécutée avec une Boot Info Table obsolète
            // pointant vers son ancien LBA d'origine, d'où "ISOLINUX: Image checksum error, sorry...".
            var patchedData = IsolinuxBootInfoTablePatcher.TryPatchInMemory(image.Data, newLba)
                ?? image.Data;
            relocatedImages.Add((newLba, patchedData));
        }

        var bootRecord = (byte[])bootCatalog.RawBootRecordSector.Clone();
        BitConverter.GetBytes(appendSector).CopyTo(bootRecord, 71);

        // Le Boot Record est réécrit à l'emplacement réservé dans la NOUVELLE image, pas à celui
        // qu'il occupait dans la source : dans une image CDBuilder sans image de boot, ce secteur
        // (17) est le descripteur Joliet, et l'écraser privait l'ISO de ses noms longs (GRUB ne
        // trouvait plus boot/grub/i386-pc, devenu I386_PC en ISO9660 strict).
        var targetBootRecordSector = FindBootRecordVolumeDescriptor(targetIsoStream) ?? bootCatalog.BootRecordSector;
        WriteSector(targetIsoStream, targetBootRecordSector, bootRecord);
        WriteSector(targetIsoStream, appendSector, catalogSector);

        foreach (var (newLba, data) in relocatedImages)
        {
            WriteBytes(targetIsoStream, newLba, data);
        }
    }

    /// <summary>
    /// Taille d'un loader ISOLINUX d'après sa propre Boot Info Table (offset 8 : LBA du PVD,
    /// LBA du fichier, longueur, checksum). Le BIOS ne charge que les premiers secteurs, puis
    /// ISOLINUX lit lui-même la suite grâce à cette table : c'est donc la source de vérité.
    /// Plus fiable que <see cref="FindFileSizeAtLba"/> : sur l'ISO Google TV 14, l'image chargée
    /// au boot (LBA 47) est un exemplaire distinct de l'isolinux.bin visible dans l'arborescence
    /// Joliet (autre LBA), si bien que la recherche par LBA échoue et tronquait le loader à 2 Ko
    /// (écran noir au démarrage). La table n'est retenue que si elle désigne bien
    /// <paramref name="loadRba"/> et le PVD standard (secteur 16).
    /// </summary>
    private static int? ReadIsolinuxBootInfoLength(Stream isoStream, int loadRba)
    {
        var firstSector = ReadSector(isoStream, loadRba);
        var pvdLba = BitConverter.ToInt32(firstSector, 8);
        var fileLba = BitConverter.ToInt32(firstSector, 12);
        var length = BitConverter.ToInt32(firstSector, 16);

        const int maxLoaderSize = 1024 * 1024;
        if (pvdLba == VolumeDescriptorStartSector && fileLba == loadRba && length is > 0 and <= maxLoaderSize)
            return length;

        return null;
    }

    /// <summary>
    /// Cherche, dans l'arborescence du système de fichiers ISO9660, le fichier dont le premier
    /// secteur correspond à <paramref name="loadRba"/>, et retourne sa taille réelle en octets.
    /// </summary>
    private static int? FindFileSizeAtLba(CDReader reader, int loadRba)
    {
        return SearchDirectory(reader.Root);

        int? SearchDirectory(DiscUtils.DiscDirectoryInfo dir)
        {
            foreach (var file in dir.GetFiles())
            {
                var clusters = reader.PathToClusters(file.FullName).FirstOrDefault();
                if (clusters.Offset == loadRba)
                    return (int)file.Length;
            }

            foreach (var subDir in dir.GetDirectories())
            {
                var found = SearchDirectory(subDir);
                if (found is not null)
                    return found;
            }

            return null;
        }
    }

    private static int? FindBootRecordVolumeDescriptor(Stream isoStream)
    {
        for (var sector = VolumeDescriptorStartSector; ; sector++)
        {
            var descriptor = ReadSector(isoStream, sector);
            var typeCode = descriptor[0];

            // 255 = Volume Descriptor Set Terminator : fin de la liste, aucun Boot Record trouvé.
            if (typeCode == 255)
                return null;

            var standardId = System.Text.Encoding.ASCII.GetString(descriptor, 1, 5);
            if (standardId != "CD001")
                return null;

            if (typeCode == BootRecordTypeCode)
            {
                var systemId = System.Text.Encoding.ASCII.GetString(descriptor, 7, 23).TrimEnd('\0');
                if (systemId.StartsWith("EL TORITO", StringComparison.Ordinal))
                    return sector;
            }
        }
    }

    private static byte[] ReadSector(Stream stream, int sector) => ReadBytes(stream, sector, SectorSize);

    private static byte[] ReadBytes(Stream stream, int startSector, int byteCount)
    {
        stream.Seek((long)startSector * SectorSize, SeekOrigin.Begin);
        var buffer = new byte[byteCount];
        var totalRead = 0;
        while (totalRead < byteCount)
        {
            var read = stream.Read(buffer, totalRead, byteCount - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }
        return buffer;
    }

    private static void WriteSector(Stream stream, int sector, byte[] data) => WriteBytes(stream, sector, data);

    private static void WriteBytes(Stream stream, int startSector, byte[] data)
    {
        stream.Seek((long)startSector * SectorSize, SeekOrigin.Begin);
        stream.Write(data, 0, data.Length);
    }
}

public enum BootPlatformId : byte
{
    X86 = 0x00,
    PowerPc = 0x01,
    Mac = 0x02,
    Uefi = 0xEF
}

public sealed record BootImageInfo(int CatalogEntryOffset, int OriginalLba, int SizeInBytes, byte[] Data, BootPlatformId PlatformId);

public sealed record BootCatalogInfo(
    int BootRecordSector,
    byte[] RawBootRecordSector,
    byte[] RawCatalogSector,
    IReadOnlyList<BootImageInfo> BootImages);
