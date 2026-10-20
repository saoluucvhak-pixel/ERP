using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Advances;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Advances;

public sealed record AdvanceInput(Guid EmployeeId, string Purpose, decimal Amount, DateOnly RequestDate, DateOnly DueDate);
public sealed record AdvanceLineInput(Guid ExpenseAccountId, decimal Amount, string? Description = null, string? DocumentNo = null, DateOnly? DocumentDate = null);

public sealed record AdvanceRow(Guid Id, string Number, Guid EmployeeId, string EmployeeCode, string EmployeeName, string Purpose, decimal Amount, DateOnly RequestDate, DateOnly DueDate,
    AdvanceStatus Status, string? PaymentVoucherNumber, string? SettlementVoucherNumber, bool PaymentPending, bool SettlementPending, decimal Outstanding, decimal? SpentAmount, int OverdueDays);

public sealed record AdvanceLineView(int LineNo, Guid ExpenseAccountId, string ExpenseAccountCode, decimal Amount, string? Description, string? DocumentNo, DateOnly? DocumentDate);
public sealed record AdvanceView(AdvanceRow Row, string? DecisionNote, IReadOnlyList<AdvanceLineView> Lines);

/// <summary>Số dư tạm ứng còn mở của từng nhân viên (tuổi nợ theo hạn quyết toán).</summary>
public sealed record AdvanceAgingRow(Guid EmployeeId, string EmployeeCode, string EmployeeName, decimal NotDue, decimal Over1To30, decimal Over31To60, decimal Over60, decimal Total);

/// <summary>
/// Tạm ứng và hoàn ứng. Đề nghị (người lập) → duyệt (người khác) → chứng từ chi nháp (Nợ TK tạm ứng theo từng người / Có tiền) đi qua luồng duyệt – ghi sổ
/// → quyết toán: bảng kê chi tiết, chứng từ nháp Nợ chi phí / Có TK tạm ứng (bù trừ đúng khoản đã chi) và tiền hoàn lại (chi ít hơn tạm ứng) hoặc chi bổ sung
/// (chi nhiều hơn). TK tạm ứng lấy từ vai trò ADVANCE, phải theo dõi theo đối tượng. Trạng thái Đã chi / Đã quyết toán lấy từ trạng thái hai chứng từ, không ghi riêng.
/// </summary>
public sealed class AdvanceService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string Role = "ADVANCE";
    public const string Source = "HAK.ADVANCE";

    public async Task<AdvanceRequest> CreateAsync(SessionContext ctx, AdvanceInput input)
    {
        ctx.Require(Permissions.CashDocument);
        if (input.Amount <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số tiền tạm ứng phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(input.Purpose)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải ghi lý do tạm ứng.");
        if (input.DueDate < input.RequestDate) throw new DomainException(ErrorCodes.ValidationFailed, "Hạn quyết toán không được trước ngày đề nghị.");
        return await db.InTransactionAsync(async () =>
        {
            var emp = await db.BusinessPartners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.EmployeeId && p.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.PartnerWrongCompany, "Người nhận tạm ứng không tồn tại.");
            if ((emp.Roles & PartnerRoles.Employee) == 0) throw new DomainException(ErrorCodes.ValidationFailed, $"{emp.Code} không phải nhân viên (đối tượng cần có vai trò Nhân viên).");
            var open = await db.AdvanceRequests.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId && a.EmployeeId == emp.Id && a.DueDate < input.RequestDate
                                                                          && (a.Status == AdvanceStatus.Approved || a.Status == AdvanceStatus.Paid)).CountAsync();
            var a = new AdvanceRequest
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, EmployeeId = emp.Id, Purpose = input.Purpose.Trim(), Amount = input.Amount, RequestDate = input.RequestDate,
                DueDate = input.DueDate, CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow,
            };
            a.Number = await NextNumberAsync(ctx.CompanyId, input.RequestDate);
            db.AdvanceRequests.Add(a);
            await audit.AppendAsync(ctx.CompanyId, ctx, "advance.create", "advance", a.Id.ToString(), new { a.Number, emp = emp.Code, a.Amount, a.DueDate, overdueBefore = open });
            await db.SaveChangesAsync();
            return a;
        });
    }

    public async Task<AdvanceRequest> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.VoucherApprove);
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            if (a.Status != AdvanceStatus.Requested) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đề nghị {a.Number} không ở trạng thái chờ duyệt.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && a.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt tạm ứng phải khác người lập đề nghị.");
            a.Status = approve ? AdvanceStatus.Approved : AdvanceStatus.Rejected;
            a.DecisionNote = note; a.DecidedBy = ctx.UserId; a.DecidedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "advance.approve" : "advance.reject", "advance", a.Id.ToString(), new { a.Number, note });
            await db.SaveChangesAsync();
            return a;
        });
    }

    public async Task CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.CashDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            var row = (await RowsAsync(ctx, [a])).Single();
            if (row.Status is not (AdvanceStatus.Requested or AdvanceStatus.Approved) || row.PaymentPending)
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Đề nghị {a.Number} đã lập chứng từ chi hoặc đã chi; hủy chứng từ chi trước.");
            a.Status = AdvanceStatus.Cancelled; a.DecisionNote = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "advance.cancel", "advance", a.Id.ToString(), new { a.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Chứng từ chi tạm ứng nháp: Nợ TK tạm ứng (theo người nhận, hạn quyết toán) / Có TK tiền. Phải được duyệt và ghi sổ như mọi chứng từ.</summary>
    public async Task<Voucher> CreatePaymentVoucherAsync(SessionContext ctx, Guid id, Guid cashAccountId, DateOnly date)
    {
        ctx.Require(Permissions.CashDocument); ctx.Require(Permissions.VoucherCreate);
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            var row = (await RowsAsync(ctx, [a])).Single();
            if (row.Status != AdvanceStatus.Approved || row.PaymentPending)
                throw new DomainException(ErrorCodes.InvalidStateTransition, row.PaymentPending ? $"Đã có chứng từ chi {row.PaymentVoucherNumber} chờ ghi sổ." : $"Đề nghị {a.Number} chưa được duyệt hoặc đã chi.");
            var advance = await AdvanceAccountAsync(ctx);
            await CashAccountAsync(ctx, cashAccountId);
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Chi tạm ứng {a.Number} – {row.EmployeeName}: {a.Purpose}",
                [new VoucherLineInput(advance.Id, Side.Debit, a.Amount, a.EmployeeId, Description: $"Tạm ứng {a.Number}", DueDate: a.DueDate),
                 new VoucherLineInput(cashAccountId, Side.Credit, a.Amount, Description: $"Chi tạm ứng {a.Number}")],
                SourceSystem: Source, SourceId: $"{a.Id}:pay"));
            a.PaymentVoucherId = v.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "advance.payment_voucher", "advance", a.Id.ToString(), new { a.Number, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    /// <summary>
    /// Quyết toán: một chứng từ nháp gồm Nợ các khoản chi thực tế, Có TK tạm ứng (bù trừ khoản đã chi), và — chi ít hơn: Nợ tiền hoàn lại (Có TK tạm ứng phần dư);
    /// chi nhiều hơn: Có tiền chi bổ sung cho nhân viên.
    /// </summary>
    public async Task<Voucher> SettleAsync(SessionContext ctx, Guid id, DateOnly date, IReadOnlyList<AdvanceLineInput> lines, Guid? cashAccountId)
    {
        ctx.Require(Permissions.CashDocument); ctx.Require(Permissions.VoucherCreate);
        if (lines.Any(l => l.Amount <= 0)) throw new DomainException(ErrorCodes.AmountNotPositive, "Số tiền mỗi khoản chi phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var a = await LoadAsync(ctx, id);
            var row = (await RowsAsync(ctx, [a])).Single();
            if (row.Status != AdvanceStatus.Paid || row.SettlementPending)
                throw new DomainException(ErrorCodes.InvalidStateTransition, row.SettlementPending ? $"Đã có chứng từ quyết toán {row.SettlementVoucherNumber} chờ ghi sổ." : $"Khoản tạm ứng {a.Number} chưa chi hoặc đã quyết toán.");
            var advance = await AdvanceAccountAsync(ctx);
            var spent = lines.Sum(l => l.Amount);
            var diff = a.Amount - spent;                                    // > 0 hoàn lại, < 0 chi bổ sung
            if (diff != 0)
            {
                if (cashAccountId is null) throw new DomainException(ErrorCodes.ValidationFailed, diff > 0 ? "Chi ít hơn tạm ứng: chọn TK tiền nhận hoàn lại." : "Chi nhiều hơn tạm ứng: chọn TK tiền để chi bổ sung.");
                await CashAccountAsync(ctx, cashAccountId.Value);
            }
            foreach (var l in lines)
            {
                var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.ExpenseAccountId && x.CompanyId == ctx.CompanyId)
                          ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản chi phí không tồn tại.");
                if (!acc.IsPostable || acc.RequiresPartner || acc.OpenItemNature != OpenItemNature.None || acc.CashKind != CashKind.None)
                    throw new DomainException(ErrorCodes.ValidationFailed, $"TK {acc.Code} không dùng làm TK chi phí quyết toán (tổng hợp, theo đối tượng, công nợ hoặc tiền).");
            }
            var payLine = await PaymentJournalLineAsync(ctx, a, advance.Id);
            var vl = new List<VoucherLineInput>();
            foreach (var l in lines)
                vl.Add(new VoucherLineInput(l.ExpenseAccountId, Side.Debit, l.Amount, Description: string.Join(" – ", new[] { l.Description, l.DocumentNo is null ? null : $"CT {l.DocumentNo}" }.Where(x => !string.IsNullOrWhiteSpace(x)))));
            // Có TK tạm ứng đúng số tiền đã nhận, bù trừ khoản chi tạm ứng; phần dư hoàn lại ghi Nợ tiền, phần chi vượt ghi Có tiền.
            vl.Add(new VoucherLineInput(advance.Id, Side.Credit, a.Amount, a.EmployeeId, Description: $"Quyết toán tạm ứng {a.Number}", SettleJournalLineId: payLine));
            if (diff > 0) vl.Add(new VoucherLineInput(cashAccountId!.Value, Side.Debit, diff, Description: $"Hoàn ứng {a.Number}"));
            if (diff < 0) vl.Add(new VoucherLineInput(cashAccountId!.Value, Side.Credit, -diff, Description: $"Chi bổ sung theo quyết toán {a.Number}"));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Quyết toán tạm ứng {a.Number} – {row.EmployeeName}", vl,
                SourceSystem: Source, SourceId: $"{a.Id}:settle"));
            await ClearOldSettlementLinesAsync(a);
            a.SettlementVoucherId = v.Id; a.SpentAmount = spent;
            foreach (var (l, i) in lines.Select((l, i) => (l, i)))
                db.AdvanceSettlementLines.Add(new AdvanceSettlementLine { Id = Ids.New(), AdvanceId = a.Id, LineNo = i + 1, ExpenseAccountId = l.ExpenseAccountId, Amount = l.Amount,
                    Description = l.Description, DocumentNo = l.DocumentNo, DocumentDate = l.DocumentDate });
            await audit.AppendAsync(ctx.CompanyId, ctx, "advance.settle", "advance", a.Id.ToString(), new { a.Number, spent, advanced = a.Amount, diff, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    /// <summary>Lập lại quyết toán sau khi chứng từ quyết toán trước đó bị hủy: bảng kê cũ được thay bằng bảng kê mới (chứng từ cũ vẫn còn trong nhật ký).</summary>
    private async Task ClearOldSettlementLinesAsync(AdvanceRequest a)
    {
        var old = await db.AdvanceSettlementLines.Where(l => l.AdvanceId == a.Id).ToListAsync();
        db.AdvanceSettlementLines.RemoveRange(old);
    }

    // ------------------------------------------------------------------ queries

    public async Task<IReadOnlyList<AdvanceRow>> ListAsync(SessionContext ctx, DateOnly asOf, Guid? employeeId = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.AdvanceRequests.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId);
        if (employeeId is not null) q = q.Where(a => a.EmployeeId == employeeId);
        var list = await q.ToListAsync();
        return (await RowsAsync(ctx, list, asOf)).OrderByDescending(r => r.RequestDate).ThenByDescending(r => r.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<AdvanceView> GetAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var a = await LoadAsync(ctx, id);
        var row = (await RowsAsync(ctx, [a])).Single();
        var accs = await db.Accounts.AsNoTracking().Where(x => x.CompanyId == ctx.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code);
        var lines = await db.AdvanceSettlementLines.AsNoTracking().Where(l => l.AdvanceId == id).OrderBy(l => l.LineNo).ToListAsync();
        // Bảng kê chỉ hiển thị khi chứng từ quyết toán còn hiệu lực.
        var show = row.SettlementVoucherNumber is not null;
        return new AdvanceView(row, a.DecisionNote, show ? lines.Select(l => new AdvanceLineView(l.LineNo, l.ExpenseAccountId, accs[l.ExpenseAccountId], l.Amount, l.Description, l.DocumentNo, l.DocumentDate)).ToList() : []);
    }

    /// <summary>Tuổi nợ tạm ứng theo hạn quyết toán, mỗi nhân viên một dòng (chỉ các khoản đã chi, chưa quyết toán).</summary>
    public async Task<IReadOnlyList<AdvanceAgingRow>> AgingAsync(SessionContext ctx, DateOnly asOf)
    {
        var rows = (await ListAsync(ctx, asOf)).Where(r => r.Outstanding > 0).ToList();
        return rows.GroupBy(r => (r.EmployeeId, r.EmployeeCode, r.EmployeeName)).Select(g =>
        {
            decimal Sum(Func<AdvanceRow, bool> f) => g.Where(f).Sum(r => r.Outstanding);
            return new AdvanceAgingRow(g.Key.EmployeeId, g.Key.EmployeeCode, g.Key.EmployeeName, Sum(r => r.OverdueDays <= 0), Sum(r => r.OverdueDays is >= 1 and <= 30),
                Sum(r => r.OverdueDays is >= 31 and <= 60), Sum(r => r.OverdueDays > 60), g.Sum(r => r.Outstanding));
        }).OrderBy(r => r.EmployeeCode, StringComparer.Ordinal).ToList();
    }

    private async Task<List<AdvanceRow>> RowsAsync(SessionContext ctx, IReadOnlyList<AdvanceRequest> list, DateOnly? asOf = null)
    {
        var day = asOf ?? DateOnly.FromDateTime(clock.UtcNow);
        var vids = list.SelectMany(a => new[] { a.PaymentVoucherId, a.SettlementVoucherId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var vouchers = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).Select(v => new { v.Id, v.Number, v.Status }).ToDictionaryAsync(v => v.Id);
        var eids = list.Select(a => a.EmployeeId).Distinct().ToList();
        var emps = await db.BusinessPartners.AsNoTracking().Where(p => eids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        static bool Live(VoucherStatus s) => s is not (VoucherStatus.Cancelled or VoucherStatus.Reversed);
        return list.Select(a =>
        {
            var pay = a.PaymentVoucherId is { } pid && vouchers.TryGetValue(pid, out var pv) && Live(pv.Status) ? pv : null;
            var set = a.SettlementVoucherId is { } sid && vouchers.TryGetValue(sid, out var sv) && Live(sv.Status) ? sv : null;
            var status = a.Status;
            if (status is AdvanceStatus.Approved or AdvanceStatus.Paid or AdvanceStatus.Settled)
                status = set is { Status: VoucherStatus.Posted } ? AdvanceStatus.Settled : pay is { Status: VoucherStatus.Posted } ? AdvanceStatus.Paid : AdvanceStatus.Approved;
            var outstanding = status == AdvanceStatus.Paid ? a.Amount : 0m;
            var overdue = outstanding > 0 ? Math.Max(day.DayNumber - a.DueDate.DayNumber, 0) : 0;
            var emp = emps[a.EmployeeId];
            return new AdvanceRow(a.Id, a.Number, a.EmployeeId, emp.Code, emp.Name, a.Purpose, a.Amount, a.RequestDate, a.DueDate, status, pay?.Number, set?.Number,
                pay is { Status: not VoucherStatus.Posted }, set is { Status: not VoucherStatus.Posted }, outstanding, set is null ? null : a.SpentAmount, overdue);
        }).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<AdvanceRequest> LoadAsync(SessionContext ctx, Guid id)
        => await db.AdvanceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Đề nghị tạm ứng không tồn tại.");

    private async Task<Domain.Catalog.Account> AdvanceAccountAsync(SessionContext ctx)
    {
        var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == Role)
                  ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {Role} (TK tạm ứng nhân viên).");
        var acc = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == map.AccountId);
        if (!acc.RequiresPartner || acc.OpenItemNature == OpenItemNature.None)
            throw new DomainException(ErrorCodes.ValidationFailed, $"TK {acc.Code} (vai trò {Role}) phải theo dõi theo đối tượng và theo từng khoản (công nợ phải thu) để bù trừ khi quyết toán.");
        return acc;
    }

    private async Task CashAccountAsync(SessionContext ctx, Guid id)
    {
        var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
        if (acc.CashKind == CashKind.None) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {acc.Code} không phải tài khoản tiền.");
    }

    private async Task<Guid> PaymentJournalLineAsync(SessionContext ctx, AdvanceRequest a, Guid advanceAccountId)
    {
        var pid = a.PaymentVoucherId ?? throw new DomainException(ErrorCodes.InvalidStateTransition, "Chưa có chứng từ chi.");
        return await (from l in db.JournalLines join e in db.JournalEntries on l.JournalEntryId equals e.Id
                      where e.VoucherId == pid && l.CompanyId == ctx.CompanyId && l.AccountId == advanceAccountId && l.Side == Side.Debit && l.PartnerId == a.EmployeeId
                      select l.Id).FirstAsync();
    }

    private Task<string> NextNumberAsync(Guid companyId, DateOnly date) => Numbering.NextAsync(db, companyId, date, "TU");
}
