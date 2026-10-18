using System.Globalization;
using Hak.Domain.Common;

namespace Hak.Domain.Reports;

/// <summary>
/// Report formula language (docs/05 §1). Lines of a report definition hold expressions over posted balances:
/// <list type="bullet">
/// <item><c>BAL_D(p…)</c> / <c>BAL_C(p…)</c> / <c>BAL_NET(p…)</c> — closing balance at the end of the period, netted per account
///   (debit part, credit part, signed debit-positive); <c>OPEN_D/OPEN_C/OPEN_NET</c> — the same at the start of the period.</item>
/// <item><c>GROSS_D(p…)</c> / <c>GROSS_C(p…)</c> (and <c>OPEN_GROSS_D/C</c>) — netted per account AND partner for accounts presented
///   gross by partner (lưỡng tính, e.g. 131, 331); other accounts are netted per account.</item>
/// <item><c>MOV_D(p…)</c> / <c>MOV_C(p…)</c> — debit / credit movement in the period; <c>MOV_D(p… ; q…)</c> only the part whose
///   counter-account matches q (entries whose pairing is ambiguous are reported, never guessed).</item>
/// <item><c>NET_D(p…)</c> / <c>NET_C(p…)</c> — net movement in the period (debit − credit / credit − debit) leaving out closing
///   vouchers (kết chuyển, type CLS): the income-statement figure of a revenue / expense account whatever way it is closed.</item>
/// <item><c>PS_D(p…)</c> / <c>PS_C(p…)</c> — debit / credit movement in the period with reversal vouchers (đảo bút toán, type REV)
///   subtracted from the side they cancel: e.g. depreciation charged, whatever was later reversed.</item>
/// <item><c>VT_D(p… ; type…)</c> / <c>VT_C</c> — debit / credit movement in the period on vouchers of these types only, e.g.
///   <c>VT_C(211* ; FAD)</c> cost written off by asset disposals, <c>VT_C(214* ; DEP)</c> depreciation runs.</item>
/// <item><c>FXR(p…)</c> — debit − credit of the period's exchange-rate revaluation vouchers (FXR) on the accounts: removes the
///   non-cash revaluation from balance changes (B03-DN indirect method, working-capital lines).</item>
/// <item><c>CF(01, 02…)</c> — cash flows of the period with these B03-DN codes (+ inflow, − outflow), see <see cref="CashFlowBuilder"/>.</item>
/// <item><c>MAX0(expr)</c> — the expression when positive, else 0 (net presentation of gains vs losses).</item>
/// <item><c>LINE(code)</c>, numbers, <c>+ - *</c>, parentheses.</item>
/// </list>
/// A pattern is an account code, a prefix with <c>*</c> (<c>111*</c>), or an exclusion <c>!1388*</c>, optionally filtered by the
/// account's report tags: <c>131*[DH]</c> (tagged DH), <c>131*[!DH]</c> (not tagged DH), <c>128*[TDT,!HCSD]</c> (all conditions).
/// </summary>
public abstract record FormulaNode;
public sealed record NumberNode(decimal Value) : FormulaNode;
public sealed record NegateNode(FormulaNode Inner) : FormulaNode;
public sealed record BinaryNode(char Op, FormulaNode Left, FormulaNode Right) : FormulaNode;
public sealed record LineNode(string Code) : FormulaNode;
public sealed record FunctionNode(string Name, IReadOnlyList<AccountPattern> Accounts, IReadOnlyList<AccountPattern> Counters) : FormulaNode;
public sealed record MaxZeroNode(FormulaNode Inner) : FormulaNode;

public sealed record AccountPattern(string Code, bool Prefix, bool Exclude, IReadOnlyList<string>? With = null, IReadOnlyList<string>? Without = null)
{
    private static readonly IReadOnlySet<string> NoTags = new HashSet<string>();

    public bool Matches(string account, IReadOnlySet<string>? tags = null)
    {
        if (!(Prefix ? account.StartsWith(Code, StringComparison.Ordinal) : account == Code)) return false;
        tags ??= NoTags;
        return (With ?? []).All(tags.Contains) && !(Without ?? []).Any(tags.Contains);
    }

    public override string ToString()
    {
        var filters = (With ?? []).Concat((Without ?? []).Select(t => "!" + t)).ToList();
        return (Exclude ? "!" : "") + Code + (Prefix ? "*" : "") + (filters.Count > 0 ? "[" + string.Join(",", filters) + "]" : "");
    }
}

/// <summary>Account set from patterns: matches an include pattern and no exclude pattern.</summary>
public static class AccountSet
{
    public static bool Contains(IReadOnlyList<AccountPattern> patterns, string account, IReadOnlySet<string>? tags = null) =>
        patterns.Any(p => !p.Exclude && p.Matches(account, tags)) && !patterns.Any(p => p.Exclude && p.Matches(account, tags));
}

public static class ReportFormula
{
    public static readonly IReadOnlySet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
    {
        "BAL_D", "BAL_C", "BAL_NET", "OPEN_D", "OPEN_C", "OPEN_NET", "GROSS_D", "GROSS_C", "OPEN_GROSS_D", "OPEN_GROSS_C", "MOV_D", "MOV_C",
        "NET_D", "NET_C", "PS_D", "PS_C", "CF", "FXR", "VT_D", "VT_C",
    };

    public static FormulaNode Parse(string text) => new Parser(text).ParseAll();

    /// <summary>"left = right" check rule.</summary>
    public static (FormulaNode Left, FormulaNode Right) ParseCheck(string text)
    {
        var i = text.IndexOf('=');
        if (i < 0 || text.IndexOf('=', i + 1) >= 0)
            throw new DomainException(ErrorCodes.ReportFormulaInvalid, $"Quy tắc kiểm tra phải có dạng 'biểu thức = biểu thức': {text}");
        return (Parse(text[..i]), Parse(text[(i + 1)..]));
    }

    /// <summary>LINE(...) codes referenced by a formula.</summary>
    public static IEnumerable<string> LineRefs(FormulaNode node) => node switch
    {
        LineNode l => [l.Code],
        NegateNode n => LineRefs(n.Inner),
        MaxZeroNode m => LineRefs(m.Inner),
        BinaryNode b => LineRefs(b.Left).Concat(LineRefs(b.Right)),
        _ => [],
    };

    private sealed class Parser(string text)
    {
        private int pos;

        public FormulaNode ParseAll()
        {
            if (string.IsNullOrWhiteSpace(text)) throw Error("Công thức trống");
            var node = Expr();
            Skip();
            if (pos < text.Length) throw Error($"Ký tự thừa '{text[pos]}'");
            return node;
        }

        private DomainException Error(string what) =>
            new(ErrorCodes.ReportFormulaInvalid, $"{what} tại vị trí {pos + 1} trong công thức: {text.Trim()}");

        private void Skip() { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }
        private bool Eat(char c) { Skip(); if (pos < text.Length && text[pos] == c) { pos++; return true; } return false; }

        private FormulaNode Expr()
        {
            var left = Term();
            while (true)
            {
                if (Eat('+')) left = new BinaryNode('+', left, Term());
                else if (Eat('-')) left = new BinaryNode('-', left, Term());
                else return left;
            }
        }

        private FormulaNode Term()
        {
            var left = Factor();
            while (Eat('*')) left = new BinaryNode('*', left, Factor());
            return left;
        }

        private FormulaNode Factor()
        {
            Skip();
            if (Eat('-')) return new NegateNode(Factor());
            if (Eat('('))
            {
                var inner = Expr();
                if (!Eat(')')) throw Error("Thiếu ')'");
                return inner;
            }
            if (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.'))
            {
                var start = pos;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.')) pos++;
                if (!decimal.TryParse(text[start..pos], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)) throw Error("Số không hợp lệ");
                return new NumberNode(n);
            }
            var nameStart = pos;
            while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
            var name = text[nameStart..pos].ToUpperInvariant();
            if (name.Length == 0) throw Error(pos < text.Length ? $"Không hiểu '{text[pos]}'" : "Công thức kết thúc đột ngột");
            if (!Eat('(')) throw Error($"Thiếu '(' sau {name}");
            if (name == "LINE")
            {
                Skip();
                var code = Token();
                if (code.Length == 0) throw Error("LINE cần mã chỉ tiêu");
                if (!Eat(')')) throw Error("Thiếu ')'");
                return new LineNode(code);
            }
            if (name == "MAX0")
            {
                var inner = Expr();
                if (!Eat(')')) throw Error("Thiếu ')'");
                return new MaxZeroNode(inner);
            }
            if (!Functions.Contains(name)) throw Error($"Hàm {name} không có (dùng: {string.Join(", ", Functions.Order())}, LINE, MAX0)");
            var accounts = Patterns();
            var counters = Eat(';') ? Patterns() : [];
            if (!Eat(')')) throw Error("Thiếu ')'");
            if (counters.Count > 0 && !name.StartsWith("MOV_", StringComparison.Ordinal) && !name.StartsWith("VT_", StringComparison.Ordinal))
                throw Error($"Chỉ MOV_D / MOV_C (TK đối ứng) và VT_D / VT_C (loại chứng từ) có phần sau dấu ';'");
            if (name.StartsWith("VT_", StringComparison.Ordinal) && counters.Count == 0) throw Error($"{name} cần loại chứng từ sau dấu ';', vd {name}(2141* ; FAD)");
            return new FunctionNode(name, accounts, counters);
        }

        private List<AccountPattern> Patterns()
        {
            var list = new List<AccountPattern>();
            do
            {
                Skip();
                var exclude = Eat('!');
                Skip();
                var code = Token();
                var prefix = Eat('*');
                if (code.Length == 0) throw Error("Thiếu mã tài khoản");
                List<string> with = [], without = [];
                if (Eat('['))
                {
                    do
                    {
                        Skip();
                        var not = Eat('!');
                        Skip();
                        var tag = Token();
                        if (tag.Length == 0) throw Error("Thiếu nhãn trong [...]");
                        (not ? without : with).Add(tag);
                    } while (Eat(','));
                    if (!Eat(']')) throw Error("Thiếu ']'");
                }
                list.Add(new AccountPattern(code, prefix, exclude, with, without));
            } while (Eat(','));
            if (list.All(p => p.Exclude)) throw Error("Cần ít nhất một mẫu tài khoản không có '!'");
            return list;
        }

        private string Token()
        {
            var start = pos;
            while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] is '_' or '.' or '-')) pos++;
            return text[start..pos].ToUpperInvariant();
        }
    }
}

/// <summary>
/// Balances of one account (and partner, for partner-tracked lines) for the report period. <see cref="NetDebit"/> /
/// <see cref="NetCredit"/> are the period's movements without closing (kết chuyển) vouchers; null = same as Debit / Credit.
/// <see cref="Revaluation"/> is the period's debit − credit on exchange-rate revaluation vouchers (type FXR);
/// <see cref="ReversalDebit"/> / <see cref="ReversalCredit"/> the period's movements on reversal vouchers (type REV).
/// </summary>
public sealed record AccountFact(string AccountCode, Guid? PartnerId, decimal Opening, decimal Debit, decimal Credit,
    decimal? NetDebit = null, decimal? NetCredit = null, decimal Revaluation = 0, decimal ReversalDebit = 0, decimal ReversalCredit = 0)
{
    public decimal Closing => Opening + Debit - Credit;
    /// <summary>Debit movement with reversal vouchers (đảo bút toán) counted as negative amounts on the side they cancel.</summary>
    public decimal DebitLessReversals => Debit - ReversalDebit - ReversalCredit;
    public decimal CreditLessReversals => Credit - ReversalCredit - ReversalDebit;
    public decimal MovementDebitWithoutClosing => NetDebit ?? Debit;
    public decimal MovementCreditWithoutClosing => NetCredit ?? Credit;
}

/// <summary>Debit/credit pairing of the period's entries, for MOV_x(p ; q). Ambiguous entries cannot be paired without guessing.</summary>
public sealed record CounterMove(string DebitAccount, string CreditAccount, decimal Amount);
/// <summary>Period movement of one account on vouchers of one type, for VT_D / VT_C.</summary>
public sealed record TypeMove(string AccountCode, string VoucherType, decimal Debit, decimal Credit);
public sealed record AmbiguousEntry(string VoucherNumber, IReadOnlyList<string> DebitAccounts, IReadOnlyList<string> CreditAccounts);

public sealed class ReportFacts(IReadOnlyList<AccountFact> facts, Func<(IReadOnlyList<CounterMove> Moves, IReadOnlyList<AmbiguousEntry> Ambiguous)> counterMoves,
    IReadOnlyDictionary<string, IReadOnlySet<string>>? accountTags = null,
    Func<(IReadOnlyList<CashFlowItem> Items, IReadOnlyList<string> Unclassified)>? cashFlows = null,
    Func<IReadOnlyList<TypeMove>>? typeMoves = null, IReadOnlySet<string>? grossByPartner = null)
{
    /// <summary>
    /// GROSS_* splits an account by partner only when it is presented gross by partner (lưỡng tính); any other account is netted
    /// per account, as on the trial balance — a partner code put on a few lines of an account not followed by partner must not
    /// turn one balance into a debit and a credit. Null (no information) = every account by partner.
    /// </summary>
    public bool IsGrossByPartner(string account) => grossByPartner?.Contains(account) ?? true;
    private IReadOnlyList<TypeMove>? byType;
    public IReadOnlyList<TypeMove> TypeMoves => byType ??= typeMoves?.Invoke() ?? [];
    private (IReadOnlyList<CashFlowItem> Items, IReadOnlyList<string> Unclassified)? flows;
    public (IReadOnlyList<CashFlowItem> Items, IReadOnlyList<string> Unclassified) CashFlows => flows ??= cashFlows?.Invoke() ?? ([], []);
    private static readonly IReadOnlySet<string> NoTags = new HashSet<string>();
    private (IReadOnlyList<CounterMove> Moves, IReadOnlyList<AmbiguousEntry> Ambiguous)? pairs;
    public IReadOnlyList<AccountFact> Facts { get; } = facts;

    /// <summary>Report tags of an account (DH, TDT, …), used by pattern filters such as 131*[DH].</summary>
    public IReadOnlySet<string> TagsOf(string account) => accountTags?.GetValueOrDefault(account) ?? NoTags;
    public (IReadOnlyList<CounterMove> Moves, IReadOnlyList<AmbiguousEntry> Ambiguous) Pairs => pairs ??= counterMoves();

    /// <summary>
    /// Pairs the lines of one entry: when one side has a single account the other side's lines pair with it exactly;
    /// otherwise the entry is ambiguous (several debit and several credit accounts).
    /// </summary>
    public static void PairEntry(string voucherNumber, IReadOnlyList<(string Account, bool Debit, decimal Amount)> lines,
        List<CounterMove> moves, List<AmbiguousEntry> ambiguous)
    {
        var debits = lines.Where(l => l.Debit).GroupBy(l => l.Account).Select(g => (Account: g.Key, Amount: g.Sum(x => x.Amount))).ToList();
        var credits = lines.Where(l => !l.Debit).GroupBy(l => l.Account).Select(g => (Account: g.Key, Amount: g.Sum(x => x.Amount))).ToList();
        if (debits.Count == 0 || credits.Count == 0) return;
        if (credits.Count == 1) moves.AddRange(debits.Select(d => new CounterMove(d.Account, credits[0].Account, d.Amount)));
        else if (debits.Count == 1) moves.AddRange(credits.Select(c => new CounterMove(debits[0].Account, c.Account, c.Amount)));
        else ambiguous.Add(new AmbiguousEntry(voucherNumber, debits.Select(d => d.Account).ToList(), credits.Select(c => c.Account).ToList()));
    }
}

/// <summary>Where a line's amount comes from: an account (drill-down), another line, or a constant / product.</summary>
public sealed record Contribution(string Source, decimal Amount);

public sealed record LineResult(string Code, decimal? Value, IReadOnlyList<Contribution> Contributions, string? Error);
public sealed record CheckResult(string Rule, string Message, decimal Left, decimal Right, bool Passed, string? Error);

public static class ReportEvaluator
{
    /// <summary>Evaluates every line (a line with no formula is a heading) and every check. Errors are per line, never thrown.</summary>
    public static (IReadOnlyList<LineResult> Lines, IReadOnlyList<CheckResult> Checks) Evaluate(
        IReadOnlyList<(string Code, string? Formula)> lines, IReadOnlyList<(string Rule, string Message)> checks, ReportFacts facts)
    {
        var parsed = new Dictionary<string, FormulaNode?>(StringComparer.Ordinal);
        var parseErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, formula) in lines)
        {
            if (string.IsNullOrWhiteSpace(formula)) { parsed[code] = null; continue; }
            try { parsed[code] = ReportFormula.Parse(formula); }
            catch (DomainException ex) { parsed[code] = null; parseErrors[code] = ex.Message; }
        }
        var done = new Dictionary<string, (decimal Value, List<Contribution> Parts, string? Error)>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        (decimal Value, List<Contribution> Parts, string? Error) Line(string code)
        {
            if (done.TryGetValue(code, out var r)) return r;
            if (!parsed.ContainsKey(code)) return (0, [], $"Không có chỉ tiêu {code}");
            if (parseErrors.TryGetValue(code, out var pe)) return done[code] = (0, [], pe);
            if (!visiting.Add(code)) return (0, [], $"Vòng lặp tham chiếu tại chỉ tiêu {code}");
            var node = parsed[code];
            var result = node is null ? (0m, new List<Contribution>(), (string?)null) : Eval(node);
            visiting.Remove(code);
            return done[code] = result;
        }

        (decimal, List<Contribution>, string?) Eval(FormulaNode node)
        {
            switch (node)
            {
                case NumberNode n: return (n.Value, [new("hằng số", n.Value)], null);
                case NegateNode neg:
                {
                    var (v, parts, e) = Eval(neg.Inner);
                    return (-v, parts.Select(p => p with { Amount = -p.Amount }).ToList(), e);
                }
                case BinaryNode b:
                {
                    var (lv, lp, le) = Eval(b.Left);
                    var (rv, rp, re) = Eval(b.Right);
                    var err = le ?? re;
                    return b.Op switch
                    {
                        '+' => (lv + rv, [.. lp, .. rp], err),
                        '-' => (lv - rv, [.. lp, .. rp.Select(p => p with { Amount = -p.Amount })], err),
                        _ => (lv * rv, b.Left is NumberNode ? rp.Select(p => p with { Amount = p.Amount * lv }).ToList()
                            : b.Right is NumberNode ? lp.Select(p => p with { Amount = p.Amount * rv }).ToList()
                            : [new("(tích)", lv * rv)], err),
                    };
                }
                case LineNode l:
                {
                    var (v, _, e) = Line(l.Code);
                    return (v, [new($"LINE {l.Code}", v)], e is null ? null : $"Chỉ tiêu {l.Code}: {e}");
                }
                case FunctionNode f: return Function(f);
                case MaxZeroNode m:
                {
                    var (v, parts, e) = Eval(m.Inner);
                    return v > 0 ? (v, parts, e) : (0m, [], e);
                }
                default: throw new InvalidOperationException();
            }
        }

        bool In(IReadOnlyList<AccountPattern> patterns, string account) => AccountSet.Contains(patterns, account, facts.TagsOf(account));

        (decimal, List<Contribution>, string?) Function(FunctionNode f)
        {
            var name = f.Name;
            if (name == "CF")
            {
                var codes = f.Accounts.Where(p => !p.Exclude).Select(p => p.Code.Length == 1 ? "0" + p.Code : p.Code).ToHashSet(StringComparer.Ordinal);
                var (items, unclassified) = facts.CashFlows;
                var parts = items.Where(i => codes.Contains(i.Code)).GroupBy(i => i.Source, StringComparer.Ordinal)
                    .Select(g => new Contribution(g.Key, g.Sum(i => i.Amount))).Where(c => c.Amount != 0).OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
                var error = unclassified.Count == 0 ? null
                    : $"{unclassified.Count} chứng từ có dòng tiền nhưng nhiều TK Nợ và nhiều TK Có ({string.Join(", ", unclassified.Take(5))}{(unclassified.Count > 5 ? ", …" : "")}): chọn mã luồng tiền trên dòng tiền của các chứng từ này.";
                return (parts.Sum(p => p.Amount), parts, error);
            }
            if (name.StartsWith("PS_", StringComparison.Ordinal))
            {
                var parts = facts.Facts.Where(x => In(f.Accounts, x.AccountCode)).GroupBy(x => x.AccountCode, StringComparer.Ordinal)
                    .Select(g => new Contribution(g.Key, g.Sum(x => name == "PS_D" ? x.DebitLessReversals : x.CreditLessReversals)))
                    .Where(c => c.Amount != 0).OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
                return (parts.Sum(p => p.Amount), parts, null);
            }
            if (name.StartsWith("VT_", StringComparison.Ordinal))
            {
                var types = f.Counters.Where(c => !c.Exclude).Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
                var parts = facts.TypeMoves.Where(m => types.Contains(m.VoucherType) && In(f.Accounts, m.AccountCode)).GroupBy(m => m.AccountCode, StringComparer.Ordinal)
                    .Select(g => new Contribution($"{g.Key} ({string.Join(", ", types.Order())})", g.Sum(m => name == "VT_D" ? m.Debit : m.Credit)))
                    .Where(c => c.Amount != 0).OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
                return (parts.Sum(p => p.Amount), parts, null);
            }
            if (name == "FXR")
            {
                var parts = facts.Facts.Where(x => x.Revaluation != 0 && In(f.Accounts, x.AccountCode)).GroupBy(x => x.AccountCode, StringComparer.Ordinal)
                    .Select(g => new Contribution(g.Key, g.Sum(x => x.Revaluation))).Where(c => c.Amount != 0).OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
                return (parts.Sum(p => p.Amount), parts, null);
            }
            if (name.StartsWith("NET_", StringComparison.Ordinal))
            {
                var credit = name == "NET_C";
                var parts = facts.Facts.Where(x => In(f.Accounts, x.AccountCode)).GroupBy(x => x.AccountCode, StringComparer.Ordinal)
                    .Select(g => new Contribution(g.Key, g.Sum(x => credit ? x.MovementCreditWithoutClosing - x.MovementDebitWithoutClosing
                                                                       : x.MovementDebitWithoutClosing - x.MovementCreditWithoutClosing)))
                    .Where(c => c.Amount != 0).OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
                return (parts.Sum(p => p.Amount), parts, null);
            }
            if (name.StartsWith("MOV_", StringComparison.Ordinal))
            {
                var debit = name == "MOV_D";
                if (f.Counters.Count == 0)
                {
                    var parts = facts.Facts.Where(x => In(f.Accounts, x.AccountCode)).GroupBy(x => x.AccountCode, StringComparer.Ordinal)
                        .Select(g => new Contribution(g.Key, g.Sum(x => debit ? x.Debit : x.Credit))).Where(c => c.Amount != 0).ToList();
                    return (parts.Sum(p => p.Amount), parts, null);
                }
                var (moves, ambiguous) = facts.Pairs;
                var hits = moves.Where(m => debit
                        ? In(f.Accounts, m.DebitAccount) && In(f.Counters, m.CreditAccount)
                        : In(f.Accounts, m.CreditAccount) && In(f.Counters, m.DebitAccount))
                    .GroupBy(m => debit ? m.DebitAccount + " / " + m.CreditAccount : m.CreditAccount + " / " + m.DebitAccount, StringComparer.Ordinal)
                    .Select(g => new Contribution(g.Key, g.Sum(m => m.Amount))).ToList();
                var unclear = ambiguous.Where(a => (debit ? a.DebitAccounts : a.CreditAccounts).Any(x => In(f.Accounts, x))
                                                   && (debit ? a.CreditAccounts : a.DebitAccounts).Any(x => In(f.Counters, x)))
                    .Select(a => a.VoucherNumber).ToList();
                var error = unclear.Count == 0 ? null
                    : $"Không xác định được đối ứng của {unclear.Count} chứng từ có nhiều TK Nợ và nhiều TK Có ({string.Join(", ", unclear.Take(5))}{(unclear.Count > 5 ? ", …" : "")}); tách chứng từ để đối ứng rõ ràng.";
                return (hits.Sum(h => h.Amount), hits, error);
            }
            var opening = name.StartsWith("OPEN_", StringComparison.Ordinal);
            var gross = name.Contains("GROSS", StringComparison.Ordinal);
            var side = name.EndsWith("_D", StringComparison.Ordinal) ? 1 : name.EndsWith("_C", StringComparison.Ordinal) ? -1 : 0;
            var groups = facts.Facts.Where(x => In(f.Accounts, x.AccountCode))
                .GroupBy(x => (x.AccountCode, Partner: gross && facts.IsGrossByPartner(x.AccountCode) ? x.PartnerId : null))
                .Select(g => (g.Key.AccountCode, Net: g.Sum(x => opening ? x.Opening : x.Closing)));
            var contributions = groups
                .Select(g => (g.AccountCode, Amount: side == 0 ? g.Net : side > 0 ? Math.Max(g.Net, 0) : Math.Max(-g.Net, 0)))
                .GroupBy(g => g.AccountCode, StringComparer.Ordinal)
                .Select(g => new Contribution(g.Key, g.Sum(x => x.Amount))).Where(c => c.Amount != 0)
                .OrderBy(c => c.Source, StringComparer.Ordinal).ToList();
            return (contributions.Sum(c => c.Amount), contributions, null);
        }

        var results = lines.Select(l =>
        {
            var (v, parts, e) = Line(l.Code);
            return new LineResult(l.Code, parsed[l.Code] is null && e is null ? null : v, parts, e);
        }).ToList();

        var checkResults = new List<CheckResult>();
        foreach (var (rule, message) in checks)
        {
            try
            {
                var (left, right) = ReportFormula.ParseCheck(rule);
                var (lv, _, le) = Eval(left);
                var (rv, _, re) = Eval(right);
                checkResults.Add(new CheckResult(rule, message, lv, rv, le is null && re is null && lv == rv, le ?? re));
            }
            catch (DomainException ex) { checkResults.Add(new CheckResult(rule, message, 0, 0, false, ex.Message)); }
        }
        return (results, checkResults);
    }

    /// <summary>Definition-time validation: every formula parses, LINE references exist, no reference loops. Returns problems.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<(string Code, string? Formula)> lines, IReadOnlyList<string> checks)
    {
        var problems = new List<string>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, _) in lines)
            if (!codes.Add(code)) problems.Add($"Mã chỉ tiêu {code} bị trùng.");
        var refs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (code, formula) in lines)
        {
            if (string.IsNullOrWhiteSpace(formula)) continue;
            try
            {
                var node = ReportFormula.Parse(formula);
                refs[code] = ReportFormula.LineRefs(node).Distinct().ToList();
                foreach (var r in refs[code].Where(r => !codes.Contains(r))) problems.Add($"Chỉ tiêu {code}: LINE({r}) không tồn tại.");
            }
            catch (DomainException ex) { problems.Add($"Chỉ tiêu {code}: {ex.Message}"); }
        }
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Loop(string c)
        {
            if (state.TryGetValue(c, out var s)) return s == 1;
            state[c] = 1;
            foreach (var r in refs.GetValueOrDefault(c) ?? []) if (refs.ContainsKey(r) && Loop(r)) { state[c] = 2; return true; }
            state[c] = 2;
            return false;
        }
        foreach (var c in refs.Keys.ToList())
            if (!state.ContainsKey(c) && Loop(c)) problems.Add($"Vòng lặp tham chiếu LINE liên quan chỉ tiêu {c}.");
        foreach (var rule in checks)
        {
            try
            {
                var (l, r) = ReportFormula.ParseCheck(rule);
                foreach (var x in ReportFormula.LineRefs(l).Concat(ReportFormula.LineRefs(r)).Where(x => !codes.Contains(x)).Distinct())
                    problems.Add($"Quy tắc '{rule}': LINE({x}) không tồn tại.");
            }
            catch (DomainException ex) { problems.Add(ex.Message); }
        }
        return problems;
    }
}
