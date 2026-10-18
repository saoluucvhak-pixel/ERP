using Hak.Domain.Common;

namespace Hak.Domain.Reports;

/// <summary>Cash-flow statement line codes of Mẫu B 03 - DN (direct method), TT 99/2025 Phụ lục IV.</summary>
public static class CashFlowCodes
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["01"] = "Tiền thu từ bán hàng, cung cấp dịch vụ và doanh thu khác",
        ["02"] = "Tiền chi trả cho người cung cấp hàng hóa và dịch vụ",
        ["03"] = "Tiền chi trả cho người lao động",
        ["04"] = "Chi phí đi vay đã trả",
        ["05"] = "Thuế thu nhập doanh nghiệp đã nộp",
        ["06"] = "Tiền thu khác từ hoạt động kinh doanh",
        ["07"] = "Tiền chi khác cho hoạt động kinh doanh",
        ["21"] = "Tiền chi để mua sắm, xây dựng TSCĐ và các tài sản dài hạn khác",
        ["22"] = "Tiền thu từ thanh lý, nhượng bán TSCĐ và các tài sản dài hạn khác",
        ["23"] = "Tiền chi cho vay, mua các công cụ nợ của đơn vị khác",
        ["24"] = "Tiền thu hồi cho vay, bán lại các công cụ nợ của đơn vị khác",
        ["25"] = "Tiền chi đầu tư góp vốn vào đơn vị khác",
        ["26"] = "Tiền thu hồi đầu tư góp vốn vào đơn vị khác",
        ["27"] = "Tiền thu lãi cho vay, cổ tức và lợi nhuận được chia",
        ["31"] = "Tiền thu từ phát hành cổ phiếu, nhận vốn góp của chủ sở hữu",
        ["32"] = "Tiền trả lại vốn góp cho các chủ sở hữu, mua lại cổ phiếu đã phát hành",
        ["33"] = "Tiền thu từ đi vay",
        ["34"] = "Tiền trả nợ gốc vay",
        ["35"] = "Tiền trả nợ gốc thuê tài chính",
        ["36"] = "Cổ tức, lợi nhuận đã trả cho chủ sở hữu",
        ["61"] = "Ảnh hưởng của thay đổi tỷ giá hối đoái quy đổi ngoại tệ",
    };

    /// <summary>Trims; null/empty → null; otherwise must be one of <see cref="All"/>.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim();
        if (c.Length == 1) c = "0" + c;
        return All.ContainsKey(c) ? c : throw new DomainException(ErrorCodes.ValidationFailed,
            $"Mã luồng tiền '{code}' không có trong mẫu B03-DN (01–07, 21–27, 31–36, 61).");
    }
}

/// <summary>
/// Default classification of a cash movement by its counter-account (TT 99/2025, Phụ lục IV phần D mục 4). The counter-account's
/// report tag CFxx overrides it (e.g. a 331 detail for construction contractors tagged CF21); a code typed on the voucher line
/// overrides both. Longest matching account prefix wins.
/// </summary>
public static class CashFlowClassifier
{
    private static readonly (string Prefix, string Code)[] Inflow =
    [
        ("131", "01"), ("511", "01"), ("3331", "01"), ("121", "01"), ("337", "01"),
        ("128", "24"), ("1283", "24"), ("171", "24"),
        ("221", "26"), ("222", "26"), ("228", "26"),
        ("411", "31"), ("3411", "33"), ("343", "33"),
        ("413", "61"),
    ];

    private static readonly (string Prefix, string Code)[] Outflow =
    [
        ("331", "02"), ("15", "02"), ("62", "02"), ("632", "02"), ("641", "02"), ("642", "02"), ("1331", "02"), ("121", "02"), ("242", "02"),
        ("334", "03"),
        ("335", "02"), ("635", "02"),
        ("3334", "05"),
        ("21", "21"), ("241", "21"), ("1332", "21"), ("217", "21"),
        ("128", "23"), ("1283", "23"), ("171", "23"),
        ("221", "25"), ("222", "25"), ("228", "25"),
        ("411", "32"), ("419", "32"),
        ("3411", "34"), ("343", "34"), ("3412", "35"),
        ("332", "36"), ("421", "36"),
        ("413", "61"),
    ];

    /// <summary>
    /// Code for a cash movement whose other side is <paramref name="counterAccount"/>; inflow = cash debited. Besides CFxx, the
    /// tags shared with the indirect method decide: LV (borrowing cost) → 04 when paid; TLTS / BDSDT (disposal of fixed assets,
    /// investment property) → 22; DT on income 515 (interest, dividends) → 27. Untagged 515 receipts stay operating (06), the
    /// same as the indirect method, which only moves DT-tagged income to investing.
    /// </summary>
    public static string Classify(string counterAccount, IReadOnlySet<string> counterTags, bool inflow)
    {
        var tag = counterTags.FirstOrDefault(t => t.Length == 4 && t.StartsWith("CF", StringComparison.Ordinal) && CashFlowCodes.All.ContainsKey(t[2..]));
        if (tag is not null) return tag[2..];
        if (!inflow && counterTags.Contains("LV")) return "04";
        if (counterTags.Contains("TLTS") || counterTags.Contains("BDSDT")) return "22";
        if (inflow && counterTags.Contains("DT") && counterAccount.StartsWith("515", StringComparison.Ordinal)) return "27";
        var match = (inflow ? Inflow : Outflow).Where(r => counterAccount.StartsWith(r.Prefix, StringComparison.Ordinal))
            .OrderByDescending(r => r.Prefix.Length).FirstOrDefault();
        return match.Code ?? (inflow ? "06" : "07");
    }
}

/// <summary>One cash-flow amount (+ inflow, − outflow) of a posted entry.</summary>
public sealed record CashFlowItem(string Code, decimal Amount, string VoucherNumber, string Source);

public static class CashFlowBuilder
{
    /// <summary>
    /// Classifies the cash lines of one posted entry. Lines between two cash accounts (incl. cash equivalents) are not cash flows.
    /// A cash line with a code chosen on the voucher counts as such; the others are paired with their counter-accounts — when the
    /// entry has several debit and several credit accounts the pairing is unknown and the entry is reported, never guessed.
    /// </summary>
    public static void AddEntry(string voucherNumber,
        IReadOnlyList<(string Account, bool Debit, decimal Amount, string? Code)> lines, Func<string, bool> isCash,
        Func<string, IReadOnlySet<string>> tagsOf, List<CashFlowItem> items, List<string> unclassified)
    {
        if (!lines.Any(l => isCash(l.Account)) || lines.All(l => isCash(l.Account))) return;
        var rest = new List<(string Account, bool Debit, decimal Amount)>();
        foreach (var l in lines)
        {
            if (isCash(l.Account) && l.Code is not null) items.Add(new(l.Code, l.Debit ? l.Amount : -l.Amount, voucherNumber, l.Account + " (mã chọn tay)"));
            else rest.Add((l.Account, l.Debit, l.Amount));
        }
        if (!rest.Any(l => isCash(l.Account))) return;
        var moves = new List<CounterMove>();
        var ambiguous = new List<AmbiguousEntry>();
        ReportFacts.PairEntry(voucherNumber, rest, moves, ambiguous);
        if (ambiguous.Count > 0) { unclassified.Add(voucherNumber); return; }
        foreach (var m in moves)
        {
            bool dc = isCash(m.DebitAccount), cc = isCash(m.CreditAccount);
            if (dc == cc) continue;
            if (dc) items.Add(new(CashFlowClassifier.Classify(m.CreditAccount, tagsOf(m.CreditAccount), inflow: true), m.Amount, voucherNumber, m.DebitAccount + " / " + m.CreditAccount));
            else items.Add(new(CashFlowClassifier.Classify(m.DebitAccount, tagsOf(m.DebitAccount), inflow: false), -m.Amount, voucherNumber, m.CreditAccount + " / " + m.DebitAccount));
        }
    }
}
