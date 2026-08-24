using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>
/// 环模板：预定义的环状分子坐标和键信息。
/// 移植自 Ketcher 的环创建逻辑（ketcher-core template actions）。
/// 键长标准化为 1.0 单位（与 Ketcher 一致）。
/// 苯环默认使用 Kekulé 表示（交替单键/双键），与 Ketcher 行为一致。
/// </summary>
public sealed record RingTemplate
{
    public required string Name { get; init; }
    public required int AtomCount { get; init; }
    public required IReadOnlyList<(Vector2 Position, string Element)> Atoms { get; init; }
    public required IReadOnlyList<(int Start, int End, int Order, bool Aromatic)> Bonds { get; init; }

    /// <summary>苯环（6 元环，Kekulé 表示：交替单键/双键）。</summary>
    public static RingTemplate Benzene { get; } = new()
    {
        Name = "Benzene",
        AtomCount = 6,
        Atoms = CreatePositions(6),
        Bonds = new[]
        {
            (1, 2, 1, false),  // 单键
            (2, 3, 2, false),  // 双键
            (3, 4, 1, false),  // 单键
            (4, 5, 2, false),  // 双键
            (5, 6, 1, false),  // 单键
            (6, 1, 2, false)   // 双键
        }
    };

    /// <summary>环戊二烯（5 元环，2 个双键 + 3 个单键）。</summary>
    public static RingTemplate Cyclopentadiene { get; } = new()
    {
        Name = "Cyclopentadiene",
        AtomCount = 5,
        Atoms = CreatePositions(5),
        Bonds = new[]
        {
            (1, 2, 2, false),  // 双键
            (2, 3, 1, false),  // 单键
            (3, 4, 2, false),  // 双键
            (4, 5, 1, false),  // 单键
            (5, 1, 1, false)   // 单键
        }
    };

    /// <summary>环己烷（6 元饱和环，全单键）。</summary>
    public static RingTemplate Cyclohexane { get; } = new()
    {
        Name = "Cyclohexane",
        AtomCount = 6,
        Atoms = CreatePositions(6),
        Bonds = CreateAllSingleBonds(6)
    };

    /// <summary>环戊烷（5 元环，全单键）。</summary>
    public static RingTemplate Cyclopentane { get; } = new()
    {
        Name = "Cyclopentane",
        AtomCount = 5,
        Atoms = CreatePositions(5),
        Bonds = CreateAllSingleBonds(5)
    };

    /// <summary>环丙烷（3 元环）。</summary>
    public static RingTemplate Cyclopropane { get; } = new()
    {
        Name = "Cyclopropane",
        AtomCount = 3,
        Atoms = CreatePositions(3),
        Bonds = CreateAllSingleBonds(3)
    };

    /// <summary>环丁烷（4 元环）。</summary>
    public static RingTemplate Cyclobutane { get; } = new()
    {
        Name = "Cyclobutane",
        AtomCount = 4,
        Atoms = CreatePositions(4),
        Bonds = CreateAllSingleBonds(4)
    };

    /// <summary>环庚烷（7 元环）。</summary>
    public static RingTemplate Cycloheptane { get; } = new()
    {
        Name = "Cycloheptane",
        AtomCount = 7,
        Atoms = CreatePositions(7),
        Bonds = CreateAllSingleBonds(7)
    };

    /// <summary>环辛烷（8 元环）。</summary>
    public static RingTemplate Cyclooctane { get; } = new()
    {
        Name = "Cyclooctane",
        AtomCount = 8,
        Atoms = CreatePositions(8),
        Bonds = CreateAllSingleBonds(8)
    };

    /// <summary>吡啶（6 元含 N 芳香环，Kekulé 表示）。</summary>
    public static RingTemplate Pyridine { get; } = new()
    {
        Name = "Pyridine",
        AtomCount = 6,
        Atoms = CreatePositions(6, elements: ["C", "C", "C", "C", "C", "N"]),
        Bonds = new[]
        {
            (1, 2, 1, false),
            (2, 3, 2, false),
            (3, 4, 1, false),
            (4, 5, 2, false),
            (5, 6, 1, false),
            (6, 1, 2, false)
        }
    };

    /// <summary>所有可用环模板（与 Ketcher 底部工具栏一致）。</summary>
    public static IReadOnlyList<RingTemplate> All { get; } = new[]
    {
        Benzene,
        Cyclopentadiene,
        Cyclohexane,
        Cyclopentane,
        Cyclopropane,
        Cyclobutane,
        Cycloheptane,
        Cyclooctane,
        Pyridine
    };

    private static IReadOnlyList<(Vector2 Position, string Element)> CreatePositions(int count, string[]? elements = null)
    {
        const double bondLength = 1.0;
        var result = new (Vector2, string)[count];
        for (int i = 0; i < count; i++)
        {
            double angle = 2.0 * Math.PI * i / count - Math.PI / 2;
            double x = bondLength * Math.Cos(angle);
            double y = bondLength * Math.Sin(angle);
            string elem = elements is not null && i < elements.Length ? elements[i] : "C";
            result[i] = (new Vector2(x, y), elem);
        }
        return result;
    }

    private static IReadOnlyList<(int Start, int End, int Order, bool Aromatic)> CreateAllSingleBonds(int count)
    {
        var result = new (int, int, int, bool)[count];
        for (int i = 0; i < count; i++)
        {
            int start = i + 1;
            int end = (i + 1) % count + 1;
            result[i] = (start, end, 1, false);
        }
        return result;
    }
}
