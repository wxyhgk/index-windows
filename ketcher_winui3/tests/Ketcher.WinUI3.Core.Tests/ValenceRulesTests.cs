using Ketcher.WinUI3.Core.Chemistry;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class ValenceRulesTests
{
    [Theory]
    [InlineData("C", 0, 0, 4, 0)]   // C 4 键无隐式 H
    [InlineData("C", 0, 0, 3, 1)]   // C 3 键 → 1 个隐式 H
    [InlineData("C", 0, 0, 2, 2)]   // C 2 键 → 2 个隐式 H
    [InlineData("C", 0, 0, 1, 3)]   // C 1 键 → 3 个隐式 H（甲烷）
    [InlineData("N", 0, 0, 3, 0)]   // N 3 键 → 0 个隐式 H
    [InlineData("N", 0, 0, 2, 1)]   // N 2 键 → 1 个隐式 H
    [InlineData("N", 0, 0, 1, 2)]   // N 1 键 → 2 个隐式 H
    [InlineData("O", 0, 0, 2, 0)]   // O 2 键 → 0 个隐式 H
    [InlineData("O", 0, 0, 1, 1)]   // O 1 键 → 1 个隐式 H
    [InlineData("F", 0, 0, 1, 0)]   // F 1 键 → 0 个隐式 H
    [InlineData("Cl", 0, 0, 1, 0)]  // Cl 1 键 → 0 个隐式 H
    [InlineData("H", 0, 0, 1, 0)]   // H 1 键 → 0 个隐式 H
    [InlineData("S", 0, 0, 2, 0)]   // S 2 键 → 0 个隐式 H
    [InlineData("S", 0, 0, 4, 0)]   // S 4 键 → 0 个隐式 H（磺酰基）
    [InlineData("P", 0, 0, 3, 0)]   // P 3 键 → 0 个隐式 H
    [InlineData("P", 0, 0, 5, 0)]   // P 5 键 → 0 个隐式 H
    public void CalcImplicitH_CommonElements(string element, int charge, int radical, int conn, int expected)
    {
        int result = ValenceRules.CalcImplicitH(element, charge, radical, conn);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("C", 1, 0, 3, 1)]   // C+ 3 键 → 1 隐式 H（Ketcher 中 C valence 固定 4）
    [InlineData("N", 1, 0, 4, 0)]   // N+ 4 键 → 0 隐式 H（铵）
    [InlineData("N", -1, 0, 2, 0)]  // N- 2 键 → 0 隐式 H
    [InlineData("O", 1, 0, 3, 0)]   // O+ 3 键 → 0 隐式 H
    [InlineData("O", -1, 0, 1, 0)]  // O- 1 键 → 0 隐式 H
    public void CalcImplicitH_ChargedAtoms(string element, int charge, int radical, int conn, int expected)
    {
        int result = ValenceRules.CalcImplicitH(element, charge, radical, conn);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalcImplicitH_OverconnectedCarbon_ReturnsNegative()
    {
        int result = ValenceRules.CalcImplicitH("C", 0, 0, 5);
        Assert.True(result < 0, "5 键碳应标记为连接数异常");
    }

    [Fact]
    public void CalcImplicitH_OverconnectedOxygen_ReturnsNegative()
    {
        int result = ValenceRules.CalcImplicitH("O", 0, 0, 3);
        Assert.True(result < 0, "3 键中性氧应标记为连接数异常");
    }

    [Fact]
    public void IsPlainCarbon_PlainCarbon_ReturnsTrue()
    {
        Assert.True(ValenceRules.IsPlainCarbon("C", 0, 0, 0, 0));
    }

    [Fact]
    public void IsPlainCarbon_ChargedCarbon_ReturnsFalse()
    {
        Assert.False(ValenceRules.IsPlainCarbon("C", 1, 0, 0, 0));
    }

    [Fact]
    public void IsPlainCarbon_Nitrogen_ReturnsFalse()
    {
        Assert.False(ValenceRules.IsPlainCarbon("N", 0, 0, 0, 0));
    }

    [Fact]
    public void IsPlainCarbon_ExplicitH_ReturnsFalse()
    {
        Assert.False(ValenceRules.IsPlainCarbon("C", 0, 0, 0, 1));
    }
}
