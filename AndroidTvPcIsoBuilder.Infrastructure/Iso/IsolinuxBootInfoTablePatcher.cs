namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Répare la "Boot Info Table" d'un fichier isolinux.bin après reconstruction d'une image ISO.
/// ISOLINUX embarque dans son propre binaire de boot un checksum et son emplacement (LBA)
/// calculés au moment où l'ISO d'origine a été assemblée (via mkisofs -boot-info-table). Cette
/// table devient invalide dès que l'outil de reconstruction replace le fichier à un autre secteur
/// — même sans modifier un seul octet de son contenu — ce qui fait échouer le boot avec
/// "ISOLINUX: Image checksum error, sorry...". La corriger consiste à réécrire cette table avec
/// le vrai LBA final du fichier et un checksum recalculé sur son contenu réel.
/// Référence : format "Boot Info Table" du projet Syslinux/ISOLINUX (mkisofs -boot-info-table).
/// </summary>
public static class IsolinuxBootInfoTablePatcher
{
    private const int SectorSize = 2048;
    private const int PrimaryVolumeDescriptorSector = 16;
    private const int BootInfoTableOffset = 8;
    private const int ChecksumStartOffset = 64;

    /// <summary>
    /// mkisofs (-boot-info-table) écrit, à l'offset 8 de la Boot Info Table, le LBA du Primary
    /// Volume Descriptor — invariablement le secteur 16 pour toute image ISO9660 standard. Sa
    /// présence à cet offset dans les données d'origine signale de façon fiable qu'une Boot Info
    /// Table existe déjà à cet emplacement (donc que l'image est un loader de type ISOLINUX) :
    /// une image de boot UEFI, qui n'a pas cette structure, aura presque certainement une autre
    /// valeur à cet offset et ne sera pas modifiée.
    /// </summary>
    public static byte[]? TryPatchInMemory(byte[] imageData, int finalLba)
    {
        if (imageData.Length < ChecksumStartOffset)
            return null;

        var existingPvdLba = BitConverter.ToInt32(imageData, BootInfoTableOffset);
        if (existingPvdLba != PrimaryVolumeDescriptorSector)
            return null;

        var patched = (byte[])imageData.Clone();

        // Checksum ISOLINUX : somme (mod 2^32) de tous les mots 32-bit à partir de l'offset 64.
        uint checksum = 0;
        for (var i = ChecksumStartOffset; i + 4 <= patched.Length; i += 4)
        {
            checksum = unchecked(checksum + BitConverter.ToUInt32(patched, i));
        }

        BitConverter.GetBytes(PrimaryVolumeDescriptorSector).CopyTo(patched, BootInfoTableOffset);
        BitConverter.GetBytes(finalLba).CopyTo(patched, BootInfoTableOffset + 4);
        BitConverter.GetBytes(patched.Length).CopyTo(patched, BootInfoTableOffset + 8);
        BitConverter.GetBytes(checksum).CopyTo(patched, BootInfoTableOffset + 12);
        for (var i = BootInfoTableOffset + 16; i < ChecksumStartOffset; i++)
            patched[i] = 0;

        return patched;
    }
}
