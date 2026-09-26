using System.Buffers.Binary;
using System.IO.Compression;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Lecteur squashfs 4.0 minimal qui ouvre un fichier du dossier racine, y compris un fichier de
/// plus de 4 Go (inode « extended file ») que DiscUtils ne sait pas lire : c'est le cas de
/// system.img (ext4, ~5 Go) dans le system.sfs des ISO Android-x86 / Google TV x86.
/// Compression gzip (zlib) uniquement, la plus répandue dans ces images.
/// </summary>
public static class SquashFsLargeFileReader
{
    private const uint Magic = 0x73717368; // "hsqs"
    private const ushort CompressionGzip = 1;
    private const int MetadataBlockSize = 8192;

    /// <summary>Ouvre le fichier <paramref name="fileName"/> du dossier racine, ou null s'il n'y est pas.</summary>
    public static Stream? OpenRootFile(Stream squashfs, string fileName)
    {
        var superblock = new byte[96];
        squashfs.Seek(0, SeekOrigin.Begin);
        squashfs.ReadExactly(superblock);
        if (BinaryPrimitives.ReadUInt32LittleEndian(superblock) != Magic)
            throw new InvalidDataException("Ce fichier n'est pas une image squashfs.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(superblock.AsSpan(28)) != 4)
            throw new InvalidDataException("Seul le format squashfs 4.0 est pris en charge.");
        var compression = BinaryPrimitives.ReadUInt16LittleEndian(superblock.AsSpan(20));
        if (compression != CompressionGzip)
            throw new NotSupportedException($"Compression squashfs non prise en charge (type {compression}), seul gzip est lu.");

        var image = new Image(
            squashfs,
            BlockSize: BinaryPrimitives.ReadUInt32LittleEndian(superblock.AsSpan(12)),
            RootInodeRef: BinaryPrimitives.ReadUInt64LittleEndian(superblock.AsSpan(32)),
            InodeTableStart: BinaryPrimitives.ReadInt64LittleEndian(superblock.AsSpan(64)),
            DirectoryTableStart: BinaryPrimitives.ReadInt64LittleEndian(superblock.AsSpan(72)),
            FragmentTableStart: BinaryPrimitives.ReadInt64LittleEndian(superblock.AsSpan(80)));

        // Inode du dossier racine → emplacement de sa liste d'entrées.
        var root = image.OpenInode(image.RootInodeRef);
        var rootType = root.ReadUInt16();
        root.Skip(14);
        uint listingBlock, listingSize;
        ushort listingOffset;
        switch (rootType)
        {
            case 1: // dossier simple
                listingBlock = root.ReadUInt32();
                root.Skip(4);
                listingSize = root.ReadUInt16();
                listingOffset = root.ReadUInt16();
                break;
            case 8: // dossier étendu
                root.Skip(4);
                listingSize = root.ReadUInt32();
                listingBlock = root.ReadUInt32();
                root.Skip(6);
                listingOffset = root.ReadUInt16();
                break;
            default:
                throw new InvalidDataException($"Inode racine inattendu (type {rootType}).");
        }

        // La taille enregistrée compte 3 octets de plus que la liste réelle ("." et "..").
        var listing = new MetadataReader(image, image.DirectoryTableStart + listingBlock, listingOffset);
        long remaining = listingSize - 3;
        while (remaining > 0)
        {
            var count = listing.ReadUInt32() + 1;
            var inodeBlock = listing.ReadUInt32();
            listing.Skip(4);
            remaining -= 12;
            for (var i = 0; i < count; i++)
            {
                var inodeOffset = listing.ReadUInt16();
                listing.Skip(4);
                var nameSize = listing.ReadUInt16() + 1;
                var name = System.Text.Encoding.UTF8.GetString(listing.ReadBytes(nameSize));
                remaining -= 8 + nameSize;
                if (name == fileName)
                    return OpenFile(image, ((ulong)inodeBlock << 16) | inodeOffset);
            }
        }

        return null;
    }

    private static Stream OpenFile(Image image, ulong inodeRef)
    {
        var inode = image.OpenInode(inodeRef);
        var type = inode.ReadUInt16();
        inode.Skip(14);

        long blocksStart, fileSize;
        uint fragmentIndex, fragmentOffset;
        switch (type)
        {
            case 2: // fichier simple
                blocksStart = inode.ReadUInt32();
                fragmentIndex = inode.ReadUInt32();
                fragmentOffset = inode.ReadUInt32();
                fileSize = inode.ReadUInt32();
                break;
            case 9: // fichier étendu (> 4 Go, fragments, clairsemé...)
                blocksStart = (long)inode.ReadUInt64();
                fileSize = (long)inode.ReadUInt64();
                inode.Skip(12); // sparse + nlink
                fragmentIndex = inode.ReadUInt32();
                fragmentOffset = inode.ReadUInt32();
                inode.Skip(4); // xattr
                break;
            default:
                throw new InvalidDataException($"L'entrée demandée n'est pas un fichier (type d'inode {type}).");
        }

        var hasFragment = fragmentIndex != uint.MaxValue;
        var blockCount = hasFragment ? fileSize / image.BlockSize : (fileSize + image.BlockSize - 1) / image.BlockSize;
        var blockSizes = new uint[blockCount];
        var blockPositions = new long[blockCount];
        var position = blocksStart;
        for (var i = 0; i < blockCount; i++)
        {
            blockSizes[i] = inode.ReadUInt32();
            blockPositions[i] = position;
            position += blockSizes[i] & 0x00FFFFFF;
        }

        (long Start, uint Size, uint Offset)? fragment = null;
        if (hasFragment)
        {
            // Table des fragments : pointeurs vers des blocs de métadonnées de 512 entrées de 16 octets.
            var pointer = new byte[8];
            image.Source.Seek(image.FragmentTableStart + fragmentIndex / 512 * 8, SeekOrigin.Begin);
            image.Source.ReadExactly(pointer);
            var entries = new MetadataReader(image, BinaryPrimitives.ReadInt64LittleEndian(pointer), (int)(fragmentIndex % 512 * 16));
            fragment = ((long)entries.ReadUInt64(), entries.ReadUInt32(), fragmentOffset);
        }

        return new FileStreamView(image, fileSize, blockSizes, blockPositions, fragment);
    }

    private sealed record Image(Stream Source, uint BlockSize, ulong RootInodeRef, long InodeTableStart, long DirectoryTableStart, long FragmentTableStart)
    {
        public MetadataReader OpenInode(ulong inodeRef)
            => new(this, InodeTableStart + (long)(inodeRef >> 16), (int)(inodeRef & 0xFFFF));

        /// <summary>Lit un bloc de données (compressé si le bit 24 de sa taille est à 0).</summary>
        public byte[] ReadDataBlock(long position, uint sizeField, int expectedLength)
        {
            var size = (int)(sizeField & 0x00FFFFFF);
            if (size == 0)
                return new byte[expectedLength]; // bloc clairsemé : que des zéros

            var raw = new byte[size];
            Source.Seek(position, SeekOrigin.Begin);
            Source.ReadExactly(raw);
            return (sizeField & 0x01000000) != 0 ? raw : Inflate(raw, expectedLength);
        }

        public static byte[] Inflate(byte[] compressed, int maxLength)
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(maxLength);
            zlib.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>Lecture séquentielle à travers les blocs de métadonnées (8 Kio, en-tête de 2 octets).</summary>
    private sealed class MetadataReader
    {
        private readonly Image _image;
        private long _nextBlock;
        private byte[] _data = Array.Empty<byte>();
        private int _position;

        public MetadataReader(Image image, long blockPosition, int offset)
        {
            _image = image;
            _nextBlock = blockPosition;
            LoadNext();
            _position = offset;
        }

        private void LoadNext()
        {
            var header = new byte[2];
            _image.Source.Seek(_nextBlock, SeekOrigin.Begin);
            _image.Source.ReadExactly(header);
            var sizeField = BinaryPrimitives.ReadUInt16LittleEndian(header);
            var size = sizeField & 0x7FFF;
            var raw = new byte[size];
            _image.Source.ReadExactly(raw);
            _data = (sizeField & 0x8000) != 0 ? raw : Image.Inflate(raw, MetadataBlockSize);
            _nextBlock += 2 + size;
            _position = 0;
        }

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            var written = 0;
            while (written < count)
            {
                if (_position >= _data.Length)
                    LoadNext();
                var chunk = Math.Min(count - written, _data.Length - _position);
                Array.Copy(_data, _position, result, written, chunk);
                _position += chunk;
                written += chunk;
            }
            return result;
        }

        public void Skip(int count) => ReadBytes(count);
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8));
    }

    /// <summary>Flux en lecture seule, avec accès aléatoire, sur le contenu décompressé d'un fichier.</summary>
    private sealed class FileStreamView : Stream
    {
        private readonly Image _image;
        private readonly uint[] _blockSizes;
        private readonly long[] _blockPositions;
        private readonly (long Start, uint Size, uint Offset)? _fragment;
        private long _cachedBlock = -1;
        private byte[] _cache = Array.Empty<byte>();
        private long _position;

        public FileStreamView(Image image, long length, uint[] blockSizes, long[] blockPositions, (long Start, uint Size, uint Offset)? fragment)
        {
            _image = image;
            Length = length;
            _blockSizes = blockSizes;
            _blockPositions = blockPositions;
            _fragment = fragment;
        }

        public override long Length { get; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (count > 0 && _position < Length)
            {
                var blockIndex = _position / _image.BlockSize;
                var block = GetBlock(blockIndex);
                var inBlock = (int)(_position % _image.BlockSize);
                var chunk = (int)Math.Min(Math.Min(count, block.Length - inBlock), Length - _position);
                if (chunk <= 0)
                    break;
                Array.Copy(block, inBlock, buffer, offset, chunk);
                _position += chunk;
                offset += chunk;
                count -= chunk;
                total += chunk;
            }
            return total;
        }

        private byte[] GetBlock(long index)
        {
            if (index == _cachedBlock)
                return _cache;

            var blockLength = (int)Math.Min(_image.BlockSize, Length - index * _image.BlockSize);
            if (index < _blockSizes.Length)
            {
                _cache = _image.ReadDataBlock(_blockPositions[index], _blockSizes[index], blockLength);
            }
            else
            {
                // Dernier morceau du fichier stocké dans un bloc de fragments partagé.
                var (start, size, fragmentOffset) = _fragment!.Value;
                var fragmentBlock = _image.ReadDataBlock(start, size, (int)_image.BlockSize);
                _cache = fragmentBlock.AsSpan((int)fragmentOffset, blockLength).ToArray();
            }
            _cachedBlock = index;
            return _cache;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => Length + offset
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
