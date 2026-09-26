using System.Text;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Recopie le nom de volume et les dates du volume de l'ISO source dans l'ISO générée, au
/// niveau binaire (ECMA-119 §8.4 et §8.5) :
/// <list type="bullet">
/// <item>le nom de volume : certaines ISO retrouvent leur système par lui (<c>LABEL=</c>). Il est
/// écrit tel quel, même hors des "d-characters" que CDBuilder impose (ex. "LineageOS_21.0") ;</item>
/// <item>les dates : GRUB calcule l'UUID d'un volume ISO9660 à partir de sa date de modification,
/// et les images EFI générées par grub-mkrescue peuvent chercher leur racine par cet UUID.</item>
/// </list>
/// Les deux sont écrits dans le Primary Volume Descriptor et dans chaque Supplementary Volume
/// Descriptor (Joliet), que Linux et GRUB lisent en priorité quand il existe.
/// </summary>
public static class VolumeIdentityPreserver
{
    private const int SectorSize = 2048;
    private const int FirstDescriptorSector = 16;
    private const byte PrimaryDescriptorType = 1;
    private const byte SupplementaryDescriptorType = 2;
    private const byte TerminatorType = 255;

    private const int VolumeIdentifierOffset = 40;
    private const int VolumeIdentifierLength = 32;

    /// <summary>Dates de création, modification, expiration et mise en service : 4 × 17 octets.</summary>
    private const int DatesOffset = 813;
    private const int DatesLength = 4 * 17;

    /// <summary>Identité lue dans le Primary Volume Descriptor ; null si l'image n'en a pas.</summary>
    public static VolumeIdentity? Read(Stream isoStream)
    {
        foreach (var (_, descriptor) in EnumerateDescriptors(isoStream))
        {
            if (descriptor[0] != PrimaryDescriptorType)
                continue;

            var label = Encoding.ASCII.GetString(descriptor, VolumeIdentifierOffset, VolumeIdentifierLength).TrimEnd(' ', '\0');
            return new VolumeIdentity(label, descriptor.AsSpan(DatesOffset, DatesLength).ToArray());
        }
        return null;
    }

    public static void Apply(Stream isoStream, VolumeIdentity identity)
    {
        foreach (var (sector, descriptor) in EnumerateDescriptors(isoStream).ToList())
        {
            if (descriptor[0] == PrimaryDescriptorType)
            {
                var label = Encoding.ASCII.GetBytes(Truncate(identity.Label, VolumeIdentifierLength).PadRight(VolumeIdentifierLength));
                label.CopyTo(descriptor, VolumeIdentifierOffset);
            }
            else if (descriptor[0] == SupplementaryDescriptorType)
            {
                // Joliet : UCS-2 big-endian, 16 caractères au plus.
                var label = Encoding.BigEndianUnicode.GetBytes(Truncate(identity.Label, VolumeIdentifierLength / 2).PadRight(VolumeIdentifierLength / 2));
                label.CopyTo(descriptor, VolumeIdentifierOffset);
            }
            else
            {
                continue;
            }

            identity.Dates.CopyTo(descriptor, DatesOffset);
            isoStream.Seek((long)sector * SectorSize, SeekOrigin.Begin);
            isoStream.Write(descriptor, 0, SectorSize);
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length > maxLength ? value[..maxLength] : value;

    private static IEnumerable<(int Sector, byte[] Descriptor)> EnumerateDescriptors(Stream isoStream)
    {
        for (var sector = FirstDescriptorSector; ; sector++)
        {
            var descriptor = new byte[SectorSize];
            isoStream.Seek((long)sector * SectorSize, SeekOrigin.Begin);
            if (isoStream.Read(descriptor, 0, SectorSize) < SectorSize)
                yield break;
            if (Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001" || descriptor[0] == TerminatorType)
                yield break;
            yield return (sector, descriptor);
        }
    }
}

public sealed record VolumeIdentity(string Label, byte[] Dates);
