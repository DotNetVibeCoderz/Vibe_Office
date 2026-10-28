// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;

namespace OfficeNet.Core.Packaging;

/// <summary>
/// A compound file (MS-CFB): the "file system in a file" that legacy Office documents, encrypted
/// OOXML packages and embedded OLE objects are stored in.
/// </summary>
/// <remarks>
/// <para>
/// This is the minimum both of its uses need — named streams in nested storages, read and written
/// whole. It is <b>not</b> a reader for <c>.doc</c>/<c>.xls</c>/<c>.ppt</c>: their content is a
/// binary format of its own inside the streams, and nothing here interprets it.
/// </para>
/// <para>
/// Streams are named by path, storages separated by <c>/</c>: <c>\u0006DataSpaces/Version</c>.
/// Storages are implied by the paths that pass through them. Writing produces version 3 (512-byte
/// sectors), which every reader accepts; reading takes version 3 and 4.
/// </para>
/// </remarks>
public sealed class CompoundFile
{
    private static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;
    private const uint NoStream = 0xFFFFFFFF;

    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    private const int EntriesPerSector = SectorSize / 128;
    private const int IdsPerSector = SectorSize / 4;
    private const int HeaderDifatCount = 109;

    private const byte TypeStorage = 1;
    private const byte TypeStream = 2;
    private const byte TypeRoot = 5;

    /// <summary>Creates an empty compound file.</summary>
    public CompoundFile()
    {
    }

    /// <summary>The streams, keyed by path. Order is kept as added.</summary>
    public IDictionary<string, byte[]> Streams { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>
    /// The class id of the root storage — for an embedded OLE object, the application that owns it.
    /// </summary>
    public Guid RootClassId { get; set; }

    /// <summary>Class ids of storages, keyed by path, for the ones that have one.</summary>
    public IDictionary<string, Guid> StorageClassIds { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal);

    /// <summary>Whether bytes begin with the compound-file signature.</summary>
    public static bool IsCompoundFile(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length && bytes[..Signature.Length].SequenceEqual(Signature);

    // ---- Reading -------------------------------------------------------------------------------

    /// <summary>Reads a compound file.</summary>
    /// <exception cref="OfficeNetException">The bytes are not a compound file, or it is damaged.</exception>
    public static CompoundFile Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (!IsCompoundFile(bytes) || bytes.Length < 512)
        {
            throw new OfficeNetException("This is not a compound file: the signature is missing.");
        }

        try
        {
            return new Reader(bytes).Read();
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new OfficeNetException("The compound file is damaged: " + ex.Message, ex);
        }
    }

    private sealed class Reader(byte[] bytes)
    {
        private int _sectorSize;
        private uint[] _fat = [];
        private uint[] _miniFat = [];
        private byte[] _miniStream = [];

        private int SectorCount => (bytes.Length - _sectorSize) / _sectorSize + 1;

        public CompoundFile Read()
        {
            var header = bytes.AsSpan(0, 512);
            var shift = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);

            if (shift is not (9 or 12))
            {
                throw new OfficeNetException($"The compound file has an unsupported sector size (2^{shift}).");
            }

            _sectorSize = 1 << shift;

            var fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
            var firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);
            var cutoff = BinaryPrimitives.ReadUInt32LittleEndian(header[56..]);
            var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(header[60..]);
            var firstDifat = BinaryPrimitives.ReadUInt32LittleEndian(header[68..]);

            // The FAT's own sectors: the first 109 listed in the header, the rest in a DIFAT chain.
            var fatList = new List<uint>();

            for (var i = 0; i < HeaderDifatCount && fatList.Count < fatSectors; i++)
            {
                fatList.Add(BinaryPrimitives.ReadUInt32LittleEndian(header[(76 + i * 4)..]));
            }

            var difat = firstDifat;
            var guard = 0;

            while (fatList.Count < fatSectors && difat < EndOfChain)
            {
                Guard(ref guard, SectorCount);
                var sector = Sector(difat);
                var ids = _sectorSize / 4 - 1;

                for (var i = 0; i < ids && fatList.Count < fatSectors; i++)
                {
                    fatList.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector[(i * 4)..]));
                }

                difat = BinaryPrimitives.ReadUInt32LittleEndian(sector[(ids * 4)..]);
            }

            _fat = [.. fatList.SelectMany(s => Ids(Sector(s)))];

            var directory = Chain(firstDirectory, _fat, _sectorSize, Sector, long.MaxValue);
            var entries = new List<Entry>();

            for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
            {
                entries.Add(Entry.Parse(directory.AsSpan(offset, 128)));
            }

            if (entries.Count == 0 || entries[0].Type != TypeRoot)
            {
                throw new OfficeNetException("The compound file has no root storage.");
            }

            if (firstMiniFat < EndOfChain)
            {
                _miniFat = Ids(Chain(firstMiniFat, _fat, _sectorSize, Sector, long.MaxValue));
            }

            var root = entries[0];
            _miniStream = root.Start < EndOfChain
                ? Chain(root.Start, _fat, _sectorSize, Sector, (long)root.Size)
                : [];

            var file = new CompoundFile { RootClassId = root.ClassId };
            var visited = new HashSet<uint>();
            Walk(root.Child, string.Empty);
            return file;

            void Walk(uint index, string prefix)
            {
                if (index == NoStream || index >= entries.Count || !visited.Add(index))
                {
                    return;
                }

                var entry = entries[(int)index];
                var path = prefix + entry.Name;

                Walk(entry.Left, prefix);

                if (entry.Type == TypeStream)
                {
                    file.Streams[path] = entry.Size < cutoff
                        ? Chain(entry.Start, _miniFat, MiniSectorSize, MiniSector, (long)entry.Size)
                        : Chain(entry.Start, _fat, _sectorSize, Sector, (long)entry.Size);
                }
                else if (entry.Type == TypeStorage)
                {
                    if (entry.ClassId != Guid.Empty)
                    {
                        file.StorageClassIds[path] = entry.ClassId;
                    }

                    Walk(entry.Child, path + "/");
                }

                Walk(entry.Right, prefix);
            }
        }

        private static void Guard(ref int steps, int limit)
        {
            if (++steps > limit + 1)
            {
                throw new OfficeNetException("The compound file has a sector chain that loops.");
            }
        }

        private ReadOnlySpan<byte> Sector(uint id)
        {
            var offset = (long)(id + 1) * _sectorSize;

            if (offset + _sectorSize > bytes.Length)
            {
                // The last sector may be short in a truncated but otherwise readable file.
                return offset < bytes.Length ? bytes.AsSpan((int)offset) : throw new OfficeNetException(
                    $"The compound file refers to sector {id}, past its end.");
            }

            return bytes.AsSpan((int)offset, _sectorSize);
        }

        private ReadOnlySpan<byte> MiniSector(uint id)
        {
            var offset = (long)id * MiniSectorSize;

            if (offset + MiniSectorSize > _miniStream.Length)
            {
                return offset < _miniStream.Length ? _miniStream.AsSpan((int)offset) : throw new OfficeNetException(
                    $"The compound file refers to mini sector {id}, past the mini stream's end.");
            }

            return _miniStream.AsSpan((int)offset, MiniSectorSize);
        }

        private delegate ReadOnlySpan<byte> SectorReader(uint id);

        private byte[] Chain(uint start, uint[] table, int size, SectorReader read, long length)
        {
            var output = new MemoryStream();
            var visited = new HashSet<uint>();

            for (var id = start; id < EndOfChain && output.Length < length; id = id < table.Length ? table[id] : EndOfChain)
            {
                // A chain visits each sector at most once. A cycle shorter than the stream would
                // otherwise just repeat data until the length is reached, and pass for a stream.
                if (!visited.Add(id))
                {
                    throw new OfficeNetException("The compound file has a sector chain that loops.");
                }

                var sector = read(id);
                output.Write(sector[..(int)Math.Min(sector.Length, length - output.Length)]);
            }

            if (length != long.MaxValue && output.Length < length)
            {
                throw new OfficeNetException("The compound file has a stream shorter than its size says.");
            }

            return output.ToArray();
        }

        private static uint[] Ids(ReadOnlySpan<byte> data)
        {
            var ids = new uint[data.Length / 4];

            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
            }

            return ids;
        }
    }

    private sealed record Entry(string Name, byte Type, uint Left, uint Right, uint Child, Guid ClassId, uint Start, ulong Size)
    {
        public static Entry Parse(ReadOnlySpan<byte> raw)
        {
            var nameLength = Math.Clamp(BinaryPrimitives.ReadUInt16LittleEndian(raw[64..]), (ushort)2, (ushort)64);
            var name = Encoding.Unicode.GetString(raw[..(nameLength - 2)]);

            return new Entry(name, raw[66],
                BinaryPrimitives.ReadUInt32LittleEndian(raw[68..]),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[72..]),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[76..]),
                new Guid(raw.Slice(80, 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[116..]),
                // Version 3 files may leave garbage in the high half; only 4096-byte-sector files use it.
                BinaryPrimitives.ReadUInt32LittleEndian(raw[120..]));
        }
    }

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>Writes the compound file.</summary>
    /// <exception cref="ArgumentException">A name is longer than 31 characters, or a path is both a storage and a stream.</exception>
    public byte[] ToArray()
    {
        var root = new Node(string.Empty, TypeRoot) { ClassId = RootClassId };

        foreach (var (path, data) in Streams)
        {
            var parts = path.Split('/');
            var storage = root;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                storage = storage.Child(parts[i], TypeStorage);
                storage.ClassId = StorageClassIds.TryGetValue(string.Join('/', parts[..(i + 1)]), out var id) ? id : storage.ClassId;
            }

            storage.Child(parts[^1], TypeStream).Data = data;
        }

        // Directory order: root first, then depth-first; ids are positions in this list.
        var nodes = new List<Node>();
        Number(root);

        void Number(Node node)
        {
            node.Id = (uint)nodes.Count;
            nodes.Add(node);

            foreach (var child in node.Children)
            {
                Number(child);
            }
        }

        // Small streams live in the mini stream, in 64-byte sectors; the rest in 512-byte sectors.
        var mini = new MemoryStream();
        var miniFat = new List<uint>();

        foreach (var node in nodes.Where(n => n.Type == TypeStream && n.Data.Length is > 0 and < MiniStreamCutoff))
        {
            node.Start = (uint)miniFat.Count;
            var count = (node.Data.Length + MiniSectorSize - 1) / MiniSectorSize;

            for (var i = 0; i < count; i++)
            {
                miniFat.Add(i == count - 1 ? EndOfChain : (uint)(miniFat.Count + 1));
            }

            mini.Write(node.Data);
            mini.Write(new byte[count * MiniSectorSize - node.Data.Length]);
        }

        // Regular sectors, in file order: big streams, the mini stream, the mini FAT, the directory.
        var sectors = new MemoryStream();
        var fat = new List<uint>();

        uint Place(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                return EndOfChain;
            }

            var start = (uint)fat.Count;
            var count = (data.Length + SectorSize - 1) / SectorSize;

            for (var i = 0; i < count; i++)
            {
                fat.Add(i == count - 1 ? EndOfChain : (uint)(fat.Count + 1));
            }

            sectors.Write(data);
            sectors.Write(new byte[count * SectorSize - data.Length]);
            return start;
        }

        foreach (var node in nodes.Where(n => n.Type == TypeStream && n.Data.Length >= MiniStreamCutoff))
        {
            node.Start = Place(node.Data);
        }

        root.Start = Place(mini.ToArray());
        root.Size = (ulong)mini.Length;

        var miniFatBytes = new byte[miniFat.Count * 4];

        for (var i = 0; i < miniFat.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(miniFatBytes.AsSpan(i * 4), miniFat[i]);
        }

        var firstMiniFat = Place(PadToSector(miniFatBytes, 0xFF));
        var miniFatSectors = (miniFatBytes.Length + SectorSize - 1) / SectorSize;

        foreach (var node in nodes.Where(n => n.Type == TypeStream))
        {
            node.Size = (ulong)node.Data.Length;

            if (node.Data.Length == 0)
            {
                node.Start = EndOfChain;
            }
        }

        var directory = new byte[(nodes.Count + EntriesPerSector - 1) / EntriesPerSector * EntriesPerSector * 128];

        for (var i = 0; i < directory.Length / 128; i++)
        {
            var slot = directory.AsSpan(i * 128, 128);

            if (i < nodes.Count)
            {
                nodes[i].Write(slot);
            }
            else
            {
                // An unused entry: no name, no type, and every link empty.
                BinaryPrimitives.WriteUInt32LittleEndian(slot[68..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(slot[72..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(slot[76..], NoStream);
            }
        }

        var firstDirectory = Place(directory);

        // The FAT describes every sector, its own and the DIFAT's included, so their count is found
        // by iterating to a fixed point.
        var used = fat.Count;
        int fatCount = 0, difatCount = 0;

        while (true)
        {
            var neededFat = (used + fatCount + difatCount + IdsPerSector - 1) / IdsPerSector;
            var neededDifat = neededFat <= HeaderDifatCount ? 0 : (neededFat - HeaderDifatCount + IdsPerSector - 2) / (IdsPerSector - 1);

            if (neededFat == fatCount && neededDifat == difatCount)
            {
                break;
            }

            (fatCount, difatCount) = (neededFat, neededDifat);
        }

        var fatStart = (uint)used;
        var difatStart = (uint)(used + fatCount);

        for (var i = 0; i < fatCount; i++)
        {
            fat.Add(FatSector);
        }

        for (var i = 0; i < difatCount; i++)
        {
            fat.Add(DifatSector);
        }

        var output = new MemoryStream();
        var header = new byte[SectorSize];
        Signature.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)fatCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), firstDirectory);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), MiniStreamCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), firstMiniFat);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), difatCount > 0 ? difatStart : EndOfChain);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), (uint)difatCount);

        for (var i = 0; i < HeaderDifatCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76 + i * 4), i < fatCount ? fatStart + (uint)i : FreeSector);
        }

        output.Write(header);
        output.Write(sectors.ToArray());

        var fatBytes = new byte[fatCount * SectorSize];
        fatBytes.AsSpan().Fill(0xFF);

        for (var i = 0; i < fat.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fatBytes.AsSpan(i * 4), fat[i]);
        }

        output.Write(fatBytes);

        // DIFAT sectors: the FAT sectors past the header's 109, then the next DIFAT sector.
        for (var d = 0; d < difatCount; d++)
        {
            var sector = new byte[SectorSize];
            sector.AsSpan().Fill(0xFF);

            for (var i = 0; i < IdsPerSector - 1; i++)
            {
                var index = HeaderDifatCount + d * (IdsPerSector - 1) + i;

                if (index < fatCount)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(i * 4), fatStart + (uint)index);
                }
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(SectorSize - 4),
                d == difatCount - 1 ? EndOfChain : difatStart + (uint)d + 1);
            output.Write(sector);
        }

        return output.ToArray();
    }

    private static byte[] PadToSector(byte[] data, byte fill)
    {
        if (data.Length % SectorSize == 0)
        {
            return data;
        }

        var padded = new byte[(data.Length / SectorSize + 1) * SectorSize];
        padded.AsSpan().Fill(fill);
        data.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>
    /// The order siblings are kept in: shorter names first, then by upper-case code unit — MS-CFB
    /// 2.6.4. A reader looking a name up walks the tree with this comparison.
    /// </summary>
    private static int CompareNames(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return a.Length.CompareTo(b.Length);
        }

        return string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant());
    }

    private sealed class Node(string name, byte type)
    {
        private readonly List<Node> _children = [];

        public string Name { get; } = name.Length <= 31
            ? name
            : throw new ArgumentException($"'{name}' is longer than the 31 characters a compound-file name may have.");

        public byte Type { get; } = type;
        public byte[] Data { get; set; } = [];
        public Guid ClassId { get; set; }
        public uint Id { get; set; }
        public uint Start { get; set; } = EndOfChain;
        public ulong Size { get; set; }

        private uint _left = NoStream;
        private uint _right = NoStream;
        private uint _child = NoStream;

        public IEnumerable<Node> Children => _children;

        public Node Child(string childName, byte childType)
        {
            if (_children.FirstOrDefault(c => CompareNames(c.Name, childName) == 0) is { } existing)
            {
                return existing.Type == childType
                    ? existing
                    : throw new ArgumentException($"'{childName}' is used as both a storage and a stream.");
            }

            var created = new Node(childName, childType);
            _children.Add(created);
            return created;
        }

        public void Write(Span<byte> slot)
        {
            // Siblings form a binary search tree under their parent's child link. Built balanced
            // from the sorted list and coloured all black, which every reader accepts.
            if (_children.Count > 0 && _child == NoStream)
            {
                var sorted = _children.OrderBy(c => c, Comparer<Node>.Create((a, b) => CompareNames(a.Name, b.Name))).ToList();
                _child = Link(sorted, 0, sorted.Count);
            }

            var name = Encoding.Unicode.GetBytes(Name);
            name.CopyTo(slot);
            BinaryPrimitives.WriteUInt16LittleEndian(slot[64..], (ushort)(name.Length + 2));
            slot[66] = Type;
            slot[67] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(slot[68..], _left);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[72..], _right);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[76..], _child);
            ClassId.TryWriteBytes(slot.Slice(80, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(slot[116..], Type == TypeStorage ? 0 : Start);
            BinaryPrimitives.WriteUInt64LittleEndian(slot[120..], Type == TypeStorage ? 0 : Size);
        }

        private static uint Link(List<Node> sorted, int from, int to)
        {
            if (from >= to)
            {
                return NoStream;
            }

            var middle = (from + to) / 2;
            var node = sorted[middle];
            node._left = Link(sorted, from, middle);
            node._right = Link(sorted, middle + 1, to);
            return node.Id;
        }
    }
}
