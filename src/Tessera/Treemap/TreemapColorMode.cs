namespace Tessera.Treemap;

/// <summary>What a rectangle's fill colour tells you.</summary>
internal enum TreemapColorMode
{
    /// <summary>How deeply the item is nested, as SpaceMonger does it. The default.</summary>
    Depth,

    /// <summary>The file's extension, so one kind of file is one colour across the drive.</summary>
    Extension,
}
