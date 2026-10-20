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

public sealed record CashCountLineInput(decimal Denomination, int Quantity);
public sealed record CashCountInput(Guid CashAccountId, DateOnly CountDate, IReadOnlyList<CashCountLineInput> Lines, string? Note = null, string? Participants = null);

public sealed record CashCountRow(Guid Id, string Number, Guid CashAccountId, string CashAccountCode, DateOnly CountDate, CashCountStatus Status, decimal CountedTotal,
    decimal? BookBalance, decimal? Difference, string? AdjustmentVoucherNumber, bool AdjustmentPending, string? Note);
public sealed record CashCountLineView(decimal Denomination, int Quantity, decimal Amount);
public sealed record CashCountView(CashCountRow Row, string? Participants, string? CancelReason, IReadOnlyList<CashCountLineView> Lines, string? CreatedByName, string? ConfirmedByName);

/// <summary>
/// Kiểm kê quỹ tiền mặt: thủ quỹ đếm theo mệnh giá (Nháp), người khác xác nhận — lúc đó số dư sổ quỹ tại ngày kiểm kê được chốt và tính chênh lệch
/// (thừa +, thiếu −). Xử lý chênh lệch bằng chứng từ nháp: thiếu → Nợ TK chờ xử lý thiếu (vai trò CASH_SHORTAGE) / Có quỹ; thừa → Nợ quỹ / Có TK chờ xử lý thừa
/// (CASH_SURPLUS). Cuối cùng là việc ghi sổ chứng từ đó theo luồng duyệt thông thường. Biên bản in ở màn hình (mẫu 08a-TT).
/// </summary>
public sealed class CashCountService(HakDbContext db, AccountingEngine engine, LedgerQueryService ledger, AuditService audit, IClock clock)
{
    public const string ShortageRole = "CASH_SHORTAGE";
    public const string SurplusRole = "CASH_SURPLUS";
    public const string Source = "HAK.CASHCOUNT";

    public async Task<CashCount> CreateAsync(SessionContext ctx, CashCountInput input)
    {
        ctx.Require(Permissions.CashDocument);
        if (input.Lines.Count == 0) throw new DomainException(ErrorCodes.ValidationFailed, "Chưa nhập mệnh giá nào.");
        if (input.Lines.Any(l => l.Denomination <= 0 || l.Quantity < 0)) throw new DomainException(ErrorCodes.ValidationFailed, "Mệnh giá phải lớn hơn 0, số tờ không âm.");
        if (input.Lines.Select(l => l.Denomination).Distinct().Count() != input.Lines.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi mệnh giá chỉ một dòng.");
        return await db.InTransactionAsync(async () =>
        {
            var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.CashAccountId && a.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản quỹ không tồn tại.");
            if (acc.CashKind != CashKind.Cash) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {acc.Code} không phải quỹ tiền mặt.");
            var c = new CashCount
            {
                Id = Ids.New(), CompanyId = ctx.CompanyId, CashAccountId = acc.Id, CountDate = input.CountDate, Note = input.Note, Participants = input.Participants,
                CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow, CountedTotal = input.Lines.Sum(l => l.Denomination * l.Quantity),
            };
            c.Number = await NextNumberAsync(ctx.CompanyId, input.CountDate);
            foreach (var l in input.Lines.Where(l => l.Quantity > 0))
                c.Lines.Add(new CashCountLine { Id = Ids.New(), CashCountId = c.Id, Denomination = l.Denomination, Quantity = l.Quantity });
            db.CashCounts.Add(c);
            await audit.AppendAsync(ctx.CompanyId, ctx, "cash_count.create", "cash_count", c.Id.ToString(), new { c.Number, acc = acc.Code, c.CountDate, c.CountedTotal });
            await db.SaveChangesAsync();
            return c;
        });
    }

    /// <summary>Xác nhận biên bản (người khác người đếm): chốt số dư sổ quỹ ở ngày kiểm kê và chênh lệch.</summary>
    public async Task<CashCount> ConfirmAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.VoucherApprove);
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            if (c.Status != CashCountStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Biên bản {c.Number} không ở trạng thái nháp.");
            var company = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && c.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người xác nhận biên bản kiểm kê phải khác người đếm quỹ.");
            c.BookBalance = await ledger.AccountBalanceAsync(ctx, c.CashAccountId, c.CountDate);
            c.Difference = c.CountedTotal - c.BookBalance;
            c.Status = CashCountStatus.Confirmed; c.ConfirmedBy = ctx.UserId; c.ConfirmedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, "cash_count.confirm", "cash_count", c.Id.ToString(), new { c.Number, c.CountedTotal, c.BookBalance, c.Difference });
            await db.SaveChangesAsync();
            return c;
        });
    }

    public async Task<Voucher> CreateAdjustmentVoucherAsync(SessionContext ctx, Guid id, DateOnly date)
    {
        ctx.Require(Permissions.CashDocument); ctx.Require(Permissions.VoucherCreate);
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            var row = (await RowsAsync(ctx, [c])).Single();
            if (c.Status != CashCountStatus.Confirmed) throw new DomainException(ErrorCodes.InvalidStateTransition, "Biên bản chưa được xác nhận.");
            if (c.Difference is null or 0) throw new DomainException(ErrorCodes.InvalidStateTransition, "Quỹ khớp sổ: không có chênh lệch để xử lý.");
            if (row.AdjustmentVoucherNumber is not null) throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ xử lý chênh lệch {row.AdjustmentVoucherNumber}.");
            var shortage = c.Difference < 0;
            var role = shortage ? ShortageRole : SurplusRole;
            var map = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == ctx.CompanyId && m.RoleCode == role)
                      ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role} ({(shortage ? "chờ xử lý tài sản thiếu" : "chờ xử lý tài sản thừa")}).");
            var amount = Math.Abs(c.Difference.Value);
            var lines = shortage
                ? new[] { new VoucherLineInput(map.AccountId, Side.Debit, amount, Description: $"Quỹ thiếu theo biên bản {c.Number}"), new VoucherLineInput(c.CashAccountId, Side.Credit, amount, Description: $"Quỹ thiếu {c.Number}") }
                : new[] { new VoucherLineInput(c.CashAccountId, Side.Debit, amount, Description: $"Quỹ thừa {c.Number}"), new VoucherLineInput(map.AccountId, Side.Credit, amount, Description: $"Quỹ thừa theo biên bản {c.Number}") };
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Xử lý chênh lệch kiểm kê quỹ {c.Number} ngày {c.CountDate.Vn()} ({(shortage ? "thiếu" : "thừa")})", lines,
                SourceSystem: Source, SourceId: c.Id.ToString()));
            c.AdjustmentVoucherId = v.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "cash_count.adjust", "cash_count", c.Id.ToString(), new { c.Number, c.Difference, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    public async Task CancelAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.CashDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy biên bản.");
        await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            if (c.Status == CashCountStatus.Cancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Biên bản đã hủy.");
            var row = (await RowsAsync(ctx, [c])).Single();
            if (row.AdjustmentVoucherNumber is not null) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Biên bản đã có chứng từ xử lý chênh lệch {row.AdjustmentVoucherNumber}; hủy chứng từ đó trước.");
            c.Status = CashCountStatus.Cancelled; c.CancelReason = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "cash_count.cancel", "cash_count", c.Id.ToString(), new { c.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<CashCountRow>> ListAsync(SessionContext ctx, DateOnly? from, DateOnly? to)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.CashCounts.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId);
        if (from is not null) q = q.Where(c => c.CountDate >= from);
        if (to is not null) q = q.Where(c => c.CountDate <= to);
        return (await RowsAsync(ctx, await q.ToListAsync())).OrderByDescending(r => r.CountDate).ThenByDescending(r => r.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<CashCountView> GetAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var c = await db.CashCounts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId)
                ?? throw new DomainException(ErrorCodes.NotFound, "Biên bản kiểm kê quỹ không tồn tại.");
        var row = (await RowsAsync(ctx, [c])).Single();
        var names = await db.Users.AsNoTracking().Where(u => u.Id == c.CreatedBy || u.Id == c.ConfirmedBy).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return new CashCountView(row, c.Participants, c.CancelReason, c.Lines.OrderByDescending(l => l.Denomination).Select(l => new CashCountLineView(l.Denomination, l.Quantity, l.Denomination * l.Quantity)).ToList(),
            names.GetValueOrDefault(c.CreatedBy), c.ConfirmedBy is { } cb ? names.GetValueOrDefault(cb) : null);
    }

    private async Task<List<CashCountRow>> RowsAsync(SessionContext ctx, IReadOnlyList<CashCount> list)
    {
        var vids = list.Where(c => c.AdjustmentVoucherId != null).Select(c => c.AdjustmentVoucherId!.Value).ToList();
        var vouchers = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).Select(v => new { v.Id, v.Number, v.Status }).ToDictionaryAsync(v => v.Id);
        var accs = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Id, a => a.Code);
        return list.Select(c =>
        {
            var v = c.AdjustmentVoucherId is { } id && vouchers.TryGetValue(id, out var x) && x.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed) ? x : null;
            return new CashCountRow(c.Id, c.Number, c.CashAccountId, accs[c.CashAccountId], c.CountDate, c.Status, c.CountedTotal, c.BookBalance, c.Difference, v?.Number, v is { Status: not VoucherStatus.Posted }, c.Note);
        }).ToList();
    }

    private async Task<CashCount> LoadAsync(SessionContext ctx, Guid id)
        => await db.CashCounts.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Biên bản kiểm kê quỹ không tồn tại.");

    private Task<string> NextNumberAsync(Guid companyId, DateOnly date) => Numbering.NextAsync(db, companyId, date, "KKQ");
}
