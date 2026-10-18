using Hak.Domain.Common;
using Hak.Domain.Reports;

namespace Hak.Domain.Tests;

/// <summary>Report formula language. Account codes are test codes, not a legal chart of accounts.</summary>
public class ReportFormulaTests
{
    private static readonly Guid P1 = Guid.NewGuid(), P2 = Guid.NewGuid();

    // Closing: C1 = 100 Dr, C2 = 30 Cr (cash overdraft-like), AR: P1 70 Dr, P2 20 Cr (advance) → net 50 Dr; REV credit movement 500.
    private static ReportFacts Facts() => new(
    [
        new("C1", null, 40, 160, 100),
        new("C2", null, 0, 0, 30),
        new("AR", P1, 0, 300, 230),
        new("AR", P2, 0, 0, 20),
        new("REV", null, 0, 0, 500),
        new("EXP", null, 0, 120, 0),
        new("EQ", null, -40, 0, 0),
    ], () =>
    {
        var moves = new List<CounterMove>();
        var ambiguous = new List<AmbiguousEntry>();
        ReportFacts.PairEntry("V1", [("AR", true, 300), ("REV", false, 300)], moves, ambiguous);
        ReportFacts.PairEntry("V2", [("C1", true, 160), ("AR", false, 160), ("REV", false, 0.0m)], moves, ambiguous);
        ReportFacts.PairEntry("V3", [("C1", true, 50), ("EXP", true, 70), ("REV", false, 100), ("AR", false, 20)], moves, ambiguous);
        return (moves, ambiguous);
    });

    private static (IReadOnlyList<LineResult> Lines, IReadOnlyList<CheckResult> Checks) Run(params (string, string?)[] lines) =>
        ReportEvaluator.Evaluate(lines, [], Facts());

    [Fact]
    public void Balances_NetPerAccount_GrossPerPartner_Patterns()
    {
        var (lines, _) = Run(("A", "BAL_D(C*)"), ("B", "BAL_C(C*)"), ("C", "BAL_NET(C*)"), ("D", "GROSS_D(AR)"), ("E", "GROSS_C(AR)"),
            ("F", "BAL_D(AR)"), ("G", "OPEN_NET(C*, EQ)"), ("H", "BAL_D(C*, !C1)"), ("I", "-MOV_C(REV) + 2 * MOV_D(EXP)"));
        Assert.Equal([100m, 30m, 70m, 70m, 20m, 50m, 0m, 0m, -260m], lines.Select(l => l.Value!.Value));
        Assert.Equal([new Contribution("C1", 100)], lines[0].Contributions);
        Assert.Equal([new Contribution("REV", -500), new Contribution("EXP", 240)], lines[8].Contributions);
    }

    [Fact]
    public void Lines_ReferenceEachOther_InAnyOrder_HeadingsHaveNoValue()
    {
        var (lines, _) = ReportEvaluator.Evaluate([("100", null), ("110", "LINE(120) + LINE(130)"), ("120", "BAL_D(C*)"), ("130", "GROSS_D(AR)")],
            [("LINE(110) = BAL_D(C*) + GROSS_D(AR)", "Tổng khớp"), ("LINE(110) = 1", "Sai cố ý")], Facts()) is var r ? r : default;
        Assert.Null(lines[0].Value);
        Assert.Equal(170m, lines[1].Value);
        Assert.Equal([new Contribution("LINE 120", 100), new Contribution("LINE 130", 70)], lines[1].Contributions);
        var checks = ReportEvaluator.Evaluate([("110", "1")], [("LINE(110) = 1", "ok"), ("LINE(110) = 2", "sai")], Facts()).Checks;
        Assert.Equal([true, false], checks.Select(c => c.Passed));
    }

    [Fact]
    public void CounterMovements_PairExactly_AndReportAmbiguousEntries()
    {
        var (lines, _) = Run(("A", "MOV_D(AR ; REV)"), ("B", "MOV_C(AR ; C*)"), ("C", "MOV_D(EXP ; REV)"));
        Assert.Equal(300m, lines[0].Value);
        Assert.Null(lines[0].Error);
        Assert.Equal(160m, lines[1].Value);
        Assert.Contains("V3", lines[1].Error);           // V3 has 2 debit and 2 credit accounts: C1/AR pairing unknown
        Assert.Contains("V3", lines[2].Error);
    }

    [Fact]
    public void TagFilters_NetMovementWithoutClosing_AndMaxZero()
    {
        // 1311 short-term, 1312 tagged DH; 632 moved 900 Dr in the period of which 900 was closed to 911 (closing excluded from NET).
        var facts = new ReportFacts(
        [
            new("1311", P1, 0, 500, 100),
            new("1312", P2, 0, 300, 0),
            new("632", null, 0, 900, 900, NetDebit: 900, NetCredit: 0),
            new("511", null, 0, 50, 1500, NetDebit: 50, NetCredit: 1200),
            new("5151", null, 0, 0, 40),
            new("6351", null, 0, 70, 0),
        ], () => ([], []), new Dictionary<string, IReadOnlySet<string>> { ["1312"] = new HashSet<string> { "DH" }, ["5151"] = new HashSet<string> { "DGL" }, ["6351"] = new HashSet<string> { "DGL" } });
        var (lines, _) = ReportEvaluator.Evaluate(
        [
            ("131", "GROSS_D(131*[!DH])"), ("211", "GROSS_D(131*[DH])"), ("11", "NET_D(632)"), ("01", "NET_C(511)"),
            ("22", "MAX0(NET_C(515*[DGL]) - NET_D(635*[DGL]))"), ("23", "MAX0(NET_D(635*[DGL]) - NET_C(515*[DGL]))"),
        ], [], facts);
        Assert.Equal([400m, 300m, 900m, 1150m, 0m, 30m], lines.Select(l => l.Value!.Value));
        Assert.Empty(lines.Single(l => l.Code == "22").Contributions);
        Assert.Equal("131*[TDT,!DH]", new AccountPattern("131", true, false, ["TDT"], ["DH"]).ToString());
        Assert.Equal(ErrorCodes.ReportFormulaInvalid, Assert.Throws<DomainException>(() => ReportFormula.Parse("BAL_D(131*[DH)")).Code);
    }

    [Fact]
    public void CashFlows_ClassifyByCounterAccount_TagOrChosenCode_AndReportAmbiguousEntries()
    {
        var tags = new Dictionary<string, IReadOnlySet<string>> { ["3312"] = new HashSet<string> { "CF21" } };
        IReadOnlySet<string> T(string a) => tags.GetValueOrDefault(a) ?? new HashSet<string>();
        bool Cash(string a) => a.StartsWith("11", StringComparison.Ordinal) || a == "1281";
        var items = new List<CashFlowItem>();
        var unclassified = new List<string>();
        CashFlowBuilder.AddEntry("V1", [("112", true, 550, null), ("131", false, 550, null)], Cash, T, items, unclassified);           // thu nợ → 01
        CashFlowBuilder.AddEntry("V2", [("3311", true, 300, null), ("3312", true, 200, null), ("112", false, 500, null)], Cash, T, items, unclassified);
        CashFlowBuilder.AddEntry("V3", [("1281", true, 100, null), ("112", false, 100, null)], Cash, T, items, unclassified);          // tiền ↔ tương đương tiền
        CashFlowBuilder.AddEntry("V4", [("112", true, 90, "33"), ("131", false, 90, null)], Cash, T, items, unclassified);             // mã chọn tay
        CashFlowBuilder.AddEntry("V5", [("112", true, 10, null), ("6427", true, 5, null), ("131", false, 10, null), ("711", false, 5, null)], Cash, T, items, unclassified);
        Assert.Equal([("01", 550m), ("02", -300m), ("21", -200m), ("33", 90m)], items.Select(i => (i.Code, i.Amount)));
        Assert.Equal(["V5"], unclassified);
        Assert.Equal(("34", "33", "05", "61", "07"), (CashFlowClassifier.Classify("3411", T("x"), false), CashFlowClassifier.Classify("3411", T("x"), true),
            CashFlowClassifier.Classify("3334", T("x"), false), CashFlowClassifier.Classify("413", T("x"), true), CashFlowClassifier.Classify("811", T("x"), false)));
        IReadOnlySet<string> Tags(params string[] t) => t.ToHashSet();
        Assert.Equal(("04", "02", "22", "22", "27", "06"), (CashFlowClassifier.Classify("3358", Tags("LV"), false), CashFlowClassifier.Classify("335", Tags(), false),
            CashFlowClassifier.Classify("7118", Tags("TLTS"), true), CashFlowClassifier.Classify("8118", Tags("TLTS"), false),
            CashFlowClassifier.Classify("5158", Tags("DT"), true), CashFlowClassifier.Classify("515", Tags(), true)));
        Assert.Equal("01", CashFlowCodes.Normalize("1"));
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => CashFlowCodes.Normalize("99")).Code);
    }

    [Fact]
    public void MovementsLessReversals_AndRevaluation()
    {
        // 6424: 85 debited of which 25 reversed (REV credit); 131: +15 from revaluation vouchers (FXR).
        var facts = new ReportFacts(
        [
            new("6424", null, 0, 85, 25, ReversalCredit: 25),
            new("2141", null, 0, 25, 85, ReversalDebit: 25),
            new("131", P1, 300, 1_115, 800, Revaluation: 15),
        ], () => ([], []));
        var (lines, _) = ReportEvaluator.Evaluate([("02", "PS_D(6424*)"), ("K", "PS_C(2141)"), ("09", "OPEN_NET(131*) - BAL_NET(131*) + FXR(131*)")], [], facts);
        Assert.Equal([60m, 60m, -300m], lines.Select(l => l.Value!.Value));
    }

    [Fact]
    public void Gross_SplitsByPartner_OnlyAccountsPresentedGross()
    {
        // 244 (net): opening without partner, settled by a line that carries a partner — one balance of 0, not Dr 100 / Cr 100.
        // 131 (gross): customers keep separate debit and credit balances.
        var facts = new ReportFacts(
        [
            new("244", null, 100, 0, 0), new("244", P1, 0, 0, 100),
            new("131", P1, 0, 70, 0), new("131", P2, 0, 0, 20),
        ], () => ([], []), grossByPartner: new HashSet<string> { "131" });
        var (lines, _) = ReportEvaluator.Evaluate([("A", "GROSS_D(244)"), ("B", "GROSS_C(244)"), ("C", "GROSS_D(131)"), ("D", "GROSS_C(131)")], [], facts);
        Assert.Equal([0m, 0m, 70m, 20m], lines.Select(l => l.Value!.Value));
    }

    [Theory]
    [InlineData("BAL_D(")]
    [InlineData("FOO(111)")]
    [InlineData("BAL_D(!111*)")]
    [InlineData("BAL_D(111 ; 911)")]
    [InlineData("LINE(10) LINE(20)")]
    [InlineData("1 / 2")]
    public void BadFormulas_AreRefusedWithAPosition(string formula)
    {
        var ex = Assert.Throws<DomainException>(() => ReportFormula.Parse(formula));
        Assert.Equal(ErrorCodes.ReportFormulaInvalid, ex.Code);
    }

    [Fact]
    public void Validate_FindsUnknownLines_Duplicates_AndLoops()
    {
        var problems = ReportEvaluator.Validate([("1", "LINE(2)"), ("2", "LINE(3)"), ("3", "LINE(1)"), ("4", "LINE(9)"), ("4", null)], ["LINE(8) = 0", "x"]);
        Assert.Contains(problems, p => p.Contains("trùng"));
        Assert.Contains(problems, p => p.Contains("LINE(9)"));
        Assert.Contains(problems, p => p.Contains("Vòng lặp"));
        Assert.Contains(problems, p => p.Contains("LINE(8)"));
        Assert.Contains(problems, p => p.Contains("biểu thức = biểu thức"));
        Assert.Empty(ReportEvaluator.Validate([("1", "BAL_D(111*)"), ("2", "LINE(1) * 2")], ["LINE(2) = 2 * LINE(1)"]));

        var (lines, _) = ReportEvaluator.Evaluate([("1", "LINE(2)"), ("2", "LINE(1)")], [], Facts());
        Assert.All(lines, l => Assert.NotNull(l.Error));
    }
}
