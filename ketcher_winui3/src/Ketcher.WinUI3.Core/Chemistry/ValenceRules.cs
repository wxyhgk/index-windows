namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>
/// 元素 valence（化合价）规则，移植自 ketcher-core Atom.calcValence。
/// 根据元素族、电荷、自由基和连接数推断 valence 和隐式氢。
/// </summary>
public static class ValenceRules
{
    /// <summary>
    /// 计算隐式氢数量。
    /// </summary>
    /// <param name="element">元素符号</param>
    /// <param name="charge">电荷</param>
    /// <param name="radical">自由基电子数（0/1/2）</param>
    /// <param name="connectionCount">连接键数（芳香键按 1 计）</param>
    /// <returns>隐式氢数量，负数表示连接数异常</returns>
    public static int CalcImplicitH(string element, int charge, int radical, int connectionCount)
    {
        int valence = CalcValence(element, charge, radical, connectionCount);

        if (valence < 0)
            return -1; // 连接数异常

        // 正电荷时不减 absCharge（valence 已反映电荷效应）
        int hydrogenCount = valence - radical - connectionCount;
        if (charge < 0)
            hydrogenCount -= Math.Abs(charge);
        return hydrogenCount;
    }

    /// <summary>
    /// 计算 valence。返回 -1 表示连接数异常。
    /// </summary>
    private static int CalcValence(string element, int charge, int radical, int conn)
    {
        int absCharge = Math.Abs(charge);
        int rad = radical;

        switch (element)
        {
            // 族 1: H, Li, Na, K, Rb, Cs, Fr
            case "H":
            case "Li":
            case "Na":
            case "K":
            case "Rb":
            case "Cs":
            case "Fr":
                return 1;

            // 族 2: Be, Mg, Ca, Sr, Ba, Ra
            case "Be":
            case "Mg":
            case "Ca":
            case "Sr":
            case "Ba":
            case "Ra":
                if (conn + rad + absCharge == 2 || conn + rad + absCharge == 0)
                    return 2;
                return -1;

            // 族 3: B, Al, Ga, In
            case "B":
            case "Al":
            case "Ga":
            case "In":
                if (charge == -1) return 4;
                return 3;

            // 族 4: C, Si, Ge
            case "C":
            case "Si":
            case "Ge":
                return 4;

            case "Sn":
            case "Pb":
                if (conn + rad + absCharge <= 2) return 2;
                return 4;

            // 族 5: N, P
            case "N":
            case "P":
                if (charge == 1) return 4;
                if (charge == 2) return 3;
                if (conn + rad + absCharge <= 3) return 3;
                return 5;

            case "As":
            case "Sb":
            case "Bi":
                if (charge == 1) return conn <= 2 ? 2 : 4;
                if (charge == 2) return 3;
                if (conn + rad <= 3) return 3;
                return 5;

            // 族 6: O, S, Se, Te, Po
            case "O":
                if (charge >= 1) return 3;
                return 2;

            case "S":
            case "Se":
            case "Po":
                if (charge == 1) return conn <= 2 ? 3 : 5;
                if (conn + rad + absCharge <= 2) return 2;
                if (conn + rad + absCharge <= 4) return 4;
                return 6;

            case "Te":
                if (charge == -1) return 2;
                if (charge == 0 || charge == 2)
                {
                    if (conn + rad <= 2) return 2;
                    if (conn + rad <= 4) return 4;
                    return charge == 0 ? 6 : -1;
                }
                return -1;

            // 族 7: F, Cl, Br, I, At
            case "F":
                return 1;

            case "Cl":
            case "Br":
            case "I":
            case "At":
                if (charge == 1 && conn <= 2) return 2;
                if (charge == 0 && conn <= 1) return 1;
                if ((conn == 2 || conn == 4 || conn == 6) && rad == 1) return conn;
                return -1;

            // 族 8: 稀有气体
            case "He":
            case "Ne":
            case "Ar":
            case "Kr":
            case "Xe":
            case "Rn":
                if (conn + rad + absCharge == 0) return 1;
                return -1;

            // 其他
            default:
                return 4;
        }
    }

    /// <summary>
    /// 判断是否为"普通碳"（不显示 C 标签的条件）。
    /// 移植自 ketcher-core Atom.isPlainCarbon。
    /// </summary>
    public static bool IsPlainCarbon(string element, int charge, int isotope, int radical, int explicitHCount)
    {
        return element == "C"
            && charge == 0
            && isotope == 0
            && radical == 0
            && explicitHCount == 0;
    }
}
