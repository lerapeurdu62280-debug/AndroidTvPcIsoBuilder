using System.Buffers.Binary;
using System.Text;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Lecteur ext4 minimal en lecture seule (dossiers et fichiers à extents), pour extraire des
/// fichiers d'un system.img Android. Écrit parce que DiscUtils.Ext renvoie le contenu brut des
/// extents « non initialisés » au lieu de zéros, ce qui corrompt les gros APK (vérifié sur
/// PrebuiltGmsCorePano.apk : signature Google invalide après extraction par DiscUtils).
/// </summary>
public sealed class Ext4Reader : IReadOnlyFileTree
{
    private const ushort Ext4Magic = 0xEF53;
    private const uint ExtentsFlag = 0x80000;
    private const uint InlineDataFlag = 0x10000000;
    private const ushort ExtentHeaderMagic = 0xF30A;
    private const int RootInode = 2;

    private readonly Stream _stream;
    private readonly int _blockSize;
    private readonly uint _inodesPerGroup;
    private readonly int _inodeSize;
    private readonly int _descriptorSize;
    private readonly long _descriptorTable;
    private readonly bool _is64Bit;

    public Ext4Reader(Stream stream)
    {
        _stream = stream;
        var sb = ReadAt(1024, 1024);
        if (BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(56)) != Ext4Magic)
            throw new InvalidDataException("Ce fichier n'est pas une image ext2/3/4.");

        _blockSize = 1024 << (int)BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(24));
        _inodesPerGroup = BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(40));
        _inodeSize = BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(88));
        _is64Bit = (BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(96)) & 0x80) != 0;
        _descriptorSize = _is64Bit ? Math.Max((int)BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(254)), 32) : 32;
        var firstDataBlock = BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(20));
        _descriptorTable = (firstDataBlock + 1L) * _blockSize;
    }

    public bool FileExists(string path) => Lookup(path) is { } inode && IsRegularFile(inode);
    public bool DirectoryExists(string path) => Lookup(path) is { } inode && IsDirectory(inode);

    public IEnumerable<string> GetDirectories(string path) => List(path, directories: true);
    public IEnumerable<string> GetFiles(string path) => List(path, directories: false);

    public Stream OpenFile(string path)
    {
        var inode = Lookup(path) ?? throw new FileNotFoundException($"Fichier introuvable dans l'image ext4 : {path}");
        if (!IsRegularFile(inode))
            throw new InvalidDataException($"Ce n'est pas un fichier : {path}");
        return OpenInodeData(inode);
    }

    private IEnumerable<string> List(string path, bool directories)
    {
        var inode = Lookup(path);
        if (inode is null || !IsDirectory(inode))
            return Array.Empty<string>();

        var prefix = path.Trim('\\', '/');
        return ReadDirectory(inode)
            .Where(e => e.Name is not "." and not "..")
            .Where(e => directories ? IsDirectory(ReadInode(e.Inode)) : IsRegularFile(ReadInode(e.Inode)))
            .Select(e => prefix.Length == 0 ? e.Name : $"{prefix}\\{e.Name}")
            .ToList();
    }

    private byte[]? Lookup(string path)
    {
        var inode = ReadInode(RootInode);
        foreach (var part in path.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsDirectory(inode))
                return null;
            var entry = ReadDirectory(inode).FirstOrDefault(e => e.Name == part);
            if (entry.Inode == 0)
                return null;
            inode = ReadInode(entry.Inode);
        }
        return inode;
    }

    private static bool IsDirectory(byte[] inode) => (BinaryPrimitives.ReadUInt16LittleEndian(inode) & 0xF000) == 0x4000;
    private static bool IsRegularFile(byte[] inode) => (BinaryPrimitives.ReadUInt16LittleEndian(inode) & 0xF000) == 0x8000;

    private List<(uint Inode, string Name)> ReadDirectory(byte[] inode)
    {
        using var data = OpenInodeData(inode);
        var content = new byte[data.Length];
        data.ReadExactly(content);

        // Lecture linéaire de chaque bloc : valable aussi pour les dossiers indexés (htree),
        // dont les blocs d'index apparaissent comme une entrée vide (inode 0).
        var entries = new List<(uint, string)>();
        for (var blockStart = 0; blockStart < content.Length; blockStart += _blockSize)
        {
            var position = blockStart;
            var blockEnd = Math.Min(blockStart + _blockSize, content.Length);
            while (position + 8 <= blockEnd)
            {
                var entryInode = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(position));
                var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(position + 4));
                var nameLength = content[position + 6];
                if (recordLength < 8)
                    break;
                if (entryInode != 0 && nameLength > 0 && position + 8 + nameLength <= blockEnd)
                    entries.Add((entryInode, Encoding.UTF8.GetString(content, position + 8, nameLength)));
                position += recordLength;
            }
        }
        return entries;
    }

    private byte[] ReadInode(uint number)
    {
        var group = (number - 1) / _inodesPerGroup;
        var index = (number - 1) % _inodesPerGroup;
        var descriptor = ReadAt(_descriptorTable + (long)group * _descriptorSize, _descriptorSize);
        long inodeTable = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(8));
        if (_is64Bit && _descriptorSize >= 64)
            inodeTable |= (long)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(0x28)) << 32;
        return ReadAt(inodeTable * _blockSize + (long)index * _inodeSize, Math.Min(_inodeSize, 256));
    }

    private Stream OpenInodeData(byte[] inode)
    {
        long size = BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(4))
                  | (long)BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(0x6C)) << 32;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(0x20));
        if ((flags & InlineDataFlag) != 0)
            return new MemoryStream(inode.AsSpan(0x28, (int)Math.Min(size, 60)).ToArray()); // petites données dans l'inode
        if ((flags & ExtentsFlag) == 0)
            throw new NotSupportedException("Fichier ext4 sans extents (blocs indirects) non pris en charge.");

        var extents = new List<Extent>();
        CollectExtents(inode.AsSpan(0x28, 60).ToArray(), extents);
        return new ExtentStream(this, size, extents);
    }

    private readonly record struct Extent(long LogicalBlock, int Length, long PhysicalBlock, bool Initialized);

    private void CollectExtents(byte[] node, List<Extent> extents)
    {
        if (BinaryPrimitives.ReadUInt16LittleEndian(node) != ExtentHeaderMagic)
            throw new InvalidDataException("Arbre d'extents ext4 invalide.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(2));
        var depth = BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(6));
        for (var i = 0; i < count; i++)
        {
            var entry = node.AsSpan(12 + i * 12, 12);
            if (depth == 0)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
                var initialized = length <= 32768;
                if (!initialized)
                    length -= 32768; // extent réservé mais jamais écrit : se lit comme des zéros
                long physical = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..])
                              | (long)BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) << 32;
                extents.Add(new Extent(BinaryPrimitives.ReadUInt32LittleEndian(entry), length, physical, initialized));
            }
            else
            {
                long child = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..])
                           | (long)BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]) << 32;
                CollectExtents(ReadAt(child * _blockSize, _blockSize), extents);
            }
        }
    }

    private byte[] ReadAt(long position, int count)
    {
        var buffer = new byte[count];
        _stream.Seek(position, SeekOrigin.Begin);
        _stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Contenu d'un fichier : blocs des extents initialisés, zéros ailleurs (trous, extents non écrits).</summary>
    private sealed class ExtentStream : Stream
    {
        private readonly Ext4Reader _reader;
        private readonly List<Extent> _extents;
        private long _position;

        public ExtentStream(Ext4Reader reader, long length, List<Extent> extents)
        {
            _reader = reader;
            Length = length;
            _extents = extents.OrderBy(e => e.LogicalBlock).ToList();
        }

        public override long Length { get; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Position { get => _position; set => _position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var blockSize = _reader._blockSize;
            var total = 0;
            while (count > 0 && _position < Length)
            {
                var logicalBlock = _position / blockSize;
                var inBlock = (int)(_position % blockSize);
                var extent = FindExtent(logicalBlock);

                // Longueur contiguë lisible d'un coup (jusqu'à la fin de l'extent ou du trou).
                long runBlocks = extent is { } e
                    ? e.LogicalBlock + e.Length - logicalBlock
                    : NextExtentStart(logicalBlock) - logicalBlock;
                var chunk = (int)Math.Min(Math.Min(count, runBlocks * blockSize - inBlock), Length - _position);

                if (extent is { Initialized: true } initialized)
                {
                    var physical = (initialized.PhysicalBlock + (logicalBlock - initialized.LogicalBlock)) * blockSize + inBlock;
                    _reader._stream.Seek(physical, SeekOrigin.Begin);
                    _reader._stream.ReadExactly(buffer, offset, chunk);
                }
                else
                {
                    Array.Clear(buffer, offset, chunk);
                }

                _position += chunk;
                offset += chunk;
                count -= chunk;
                total += chunk;
            }
            return total;
        }

        private Extent? FindExtent(long logicalBlock)
        {
            foreach (var extent in _extents)
            {
                if (logicalBlock >= extent.LogicalBlock && logicalBlock < extent.LogicalBlock + extent.Length)
                    return extent;
            }
            return null;
        }

        private long NextExtentStart(long logicalBlock)
        {
            var next = _extents.Where(e => e.LogicalBlock > logicalBlock).Select(e => e.LogicalBlock).DefaultIfEmpty(long.MaxValue / _reader._blockSize).Min();
            return next;
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

/// <summary>Arborescence en lecture seule (chemins séparés par '\\', relatifs à la racine).</summary>
public interface IReadOnlyFileTree
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> GetDirectories(string path);
    IEnumerable<string> GetFiles(string path);
    Stream OpenFile(string path);
}

/// <summary>Adaptateur pour une arborescence DiscUtils (ex. squashfs contenant directement le système).</summary>
public sealed class DiscFileTree(DiscUtils.DiscFileSystem fileSystem) : IReadOnlyFileTree
{
    public bool FileExists(string path) => fileSystem.FileExists(path);
    public bool DirectoryExists(string path) => fileSystem.DirectoryExists(path);
    public IEnumerable<string> GetDirectories(string path) => fileSystem.GetDirectories(path);
    public IEnumerable<string> GetFiles(string path) => fileSystem.GetFiles(path);
    public Stream OpenFile(string path) => fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read);
}
