using Hak.Application.Accounting;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Equity;
using Hak.Domain.Security;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Equity;

public sealed record ShareholderInput(Guid PartnerId, decimal CommittedCapital, bool IsIndividual = true, string? Note = null);
public sealed record CapitalInput(Guid ShareholderId, CapitalMovementKind Kind, DateOnly Date, decimal Amount, Guid CashAccountId, string? Note = null);
public sealed record DividendInput(DateOnly ResolutionDate, string Description, decimal TotalAmount, decimal PitRatePercent);

public sealed record ShareholderRow(Guid Id, Guid PartnerId, string PartnerCode, string PartnerName, bool IsIndividual, bool IsActive, decimal CommittedCapital, decimal Contributed, decimal Remaining, decimal? OwnershipPercent);
public sealed record CapitalMovementRow(Guid Id, Guid ShareholderId, string PartnerCode, string PartnerName, CapitalMovementKind Kind, DateOnly Date, decimal Amount, string? VoucherNumber, bool VoucherPending, string? Note);
public sealed record DividendLineView(Guid ShareholderId, string PartnerCode, string PartnerName, bool IsIndividual, decimal CapitalBasis, decimal? Percent, decimal Gross, decimal PitWithheld, decimal Net, string? PaymentVoucherNumber, bool PaymentPending, bool Paid);
public sealed record DividendRow(Guid Id, string Number, DateOnly ResolutionDate, string Description, decimal TotalAmount, decimal PitRatePercent, DividendStatus Status, string? DeclarationVoucherNumber, bool DeclarationPending, decimal PitTotal, decimal NetTotal);
public sealed record DividendView(DividendRow Row, string? DecisionNote, IReadOnlyList<DividendLineView> Lines);

/// <summary>
/// Vốn góp và cổ tức. Góp vốn: chứng từ nháp Nợ tiền / Có vốn góp (vai trò CAPITAL_CONTRIBUTED); rút vốn ngược lại — số thực góp của từng chủ sở hữu lấy từ các lần góp
/// có chứng từ còn hiệu lực. Cổ tức: dự thảo chia theo tỷ lệ vốn thực góp đến ngày nghị quyết (làm tròn đồng, phần dư vào người góp nhiều nhất), thuế TNCN khấu trừ cho
/// cá nhân theo thuế suất khai báo; duyệt (người khác) → chứng từ ghi nhận Nợ lợi nhuận chưa phân phối / Có cổ tức phải trả theo từng người (số thực nhận) và Có thuế TNCN
/// phải nộp → chứng từ chi từng người bù trừ đúng khoản phải trả. Không seed tài khoản: mọi tài khoản qua vai trò do kế toán ánh xạ.
/// </summary>
public sealed class EquityService(HakDbContext db, AccountingEngine engine, AuditService audit, IClock clock)
{
    public const string Source = "HAK.EQUITY";

    // ------------------------------------------------------------------ chủ sở hữu

    public async Task<Shareholder> AddShareholderAsync(SessionContext ctx, ShareholderInput input)
    {
        ctx.Require(Permissions.CatalogManage);
        if (input.CommittedCapital < 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Vốn cam kết không âm.");
        return await db.InTransactionAsync(async () =>
        {
            if (!await db.BusinessPartners.AnyAsync(p => p.Id == input.PartnerId && p.CompanyId == ctx.CompanyId)) throw new DomainException(ErrorCodes.PartnerWrongCompany, "Đối tượng không tồn tại.");
            if (await db.Shareholders.AnyAsync(s => s.CompanyId == ctx.CompanyId && s.PartnerId == input.PartnerId)) throw new DomainException(ErrorCodes.DuplicateCode, "Đối tượng này đã là chủ sở hữu.");
            var s = new Shareholder { Id = Ids.New(), CompanyId = ctx.CompanyId, PartnerId = input.PartnerId, CommittedCapital = input.CommittedCapital, IsIndividual = input.IsIndividual, Note = input.Note, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.Shareholders.Add(s);
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.shareholder_add", "shareholder", s.Id.ToString(), input);
            await db.SaveChangesAsync();
            return s;
        });
    }

    public async Task UpdateShareholderAsync(SessionContext ctx, Guid id, decimal committedCapital, bool isIndividual, bool isActive, string? note)
    {
        ctx.Require(Permissions.CatalogManage);
        if (committedCapital < 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Vốn cam kết không âm.");
        await db.InTransactionAsync(async () =>
        {
            var s = await db.Shareholders.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Chủ sở hữu không tồn tại.");
            s.CommittedCapital = committedCapital; s.IsIndividual = isIndividual; s.IsActive = isActive; s.Note = note;
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.shareholder_update", "shareholder", id.ToString(), new { committedCapital, isIndividual, isActive, note });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<ShareholderRow>> ShareholdersAsync(SessionContext ctx, DateOnly asOf)
    {
        ctx.Require(Permissions.ReportView);
        var list = await (from s in db.Shareholders join p in db.BusinessPartners on s.PartnerId equals p.Id where s.CompanyId == ctx.CompanyId
                          select new { s, p.Code, p.Name }).AsNoTracking().ToListAsync();
        var paid = await ContributedAsync(ctx.CompanyId, asOf);
        var total = list.Sum(x => paid.GetValueOrDefault(x.s.Id));
        return list.Select(x =>
        {
            var c = paid.GetValueOrDefault(x.s.Id);
            return new ShareholderRow(x.s.Id, x.s.PartnerId, x.Code, x.Name, x.s.IsIndividual, x.s.IsActive, x.s.CommittedCapital, c, Math.Max(x.s.CommittedCapital - c, 0), total > 0 ? Math.Round(c / total * 100, 4) : null);
        }).OrderByDescending(r => r.Contributed).ThenBy(r => r.PartnerCode, StringComparer.Ordinal).ToList();
    }

    /// <summary>Thực góp ròng (góp − rút) của từng chủ sở hữu đến ngày <paramref name="asOf"/>; chỉ tính các lần có chứng từ còn hiệu lực (không bị hủy/đảo).</summary>
    private async Task<Dictionary<Guid, decimal>> ContributedAsync(Guid companyId, DateOnly asOf)
    {
        var rows = await (from m in db.CapitalMovements join v in db.Vouchers on m.VoucherId equals v.Id
                          where m.CompanyId == companyId && m.Date <= asOf && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed
                          select new { m.ShareholderId, m.Kind, m.Amount }).AsNoTracking().ToListAsync();
        return rows.GroupBy(r => r.ShareholderId).ToDictionary(g => g.Key, g => g.Sum(r => r.Kind == CapitalMovementKind.Contribution ? r.Amount : -r.Amount));
    }

    // ------------------------------------------------------------------ góp / rút vốn

    public async Task<Voucher> RecordMovementAsync(SessionContext ctx, CapitalInput input)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        if (input.Amount <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Số tiền phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var sh = await db.Shareholders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.ShareholderId && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Chủ sở hữu không tồn tại.");
            if (!sh.IsActive) throw new DomainException(ErrorCodes.ValidationFailed, "Chủ sở hữu đã ngừng theo dõi.");
            var partner = await db.BusinessPartners.AsNoTracking().FirstAsync(p => p.Id == sh.PartnerId);
            var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.CashAccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
            if (cash.CashKind is not (CashKind.Cash or CashKind.Bank)) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");
            var capital = await RoleAccountAsync(ctx, "CAPITAL_CONTRIBUTED", "vốn góp của chủ sở hữu");
            if (input.Kind == CapitalMovementKind.Withdrawal)
            {
                var net = (await ContributedAsync(ctx.CompanyId, input.Date)).GetValueOrDefault(sh.Id);
                if (input.Amount > net) throw new DomainException(ErrorCodes.ValidationFailed, $"Rút vốn {input.Amount} vượt số thực góp {net} của {partner.Code} đến ngày {input.Date.Vn()}.");
            }
            var contribution = input.Kind == CapitalMovementKind.Contribution;
            var label = contribution ? "Góp vốn" : "Rút vốn";
            var id = Ids.New();
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, input.Date, input.Date, $"{label} – {partner.Name}{(string.IsNullOrWhiteSpace(input.Note) ? "" : $": {input.Note}")}",
                contribution
                    ? [new VoucherLineInput(cash.Id, Side.Debit, input.Amount, Description: $"{label} {partner.Code}"), new VoucherLineInput(capital.Id, Side.Credit, input.Amount, capital.RequiresPartner ? partner.Id : null, Description: $"{label} {partner.Code}")]
                    : [new VoucherLineInput(capital.Id, Side.Debit, input.Amount, capital.RequiresPartner ? partner.Id : null, Description: $"{label} {partner.Code}"), new VoucherLineInput(cash.Id, Side.Credit, input.Amount, Description: $"{label} {partner.Code}")],
                SourceSystem: Source, SourceId: $"capital:{id}"));
            db.CapitalMovements.Add(new CapitalMovement { Id = id, CompanyId = ctx.CompanyId, ShareholderId = sh.Id, Kind = input.Kind, Date = input.Date, Amount = input.Amount, Note = input.Note, VoucherId = v.Id, CashAccountId = cash.Id, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId });
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.capital_movement", "capital_movement", id.ToString(), new { sh.PartnerId, input.Kind, input.Date, input.Amount, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    public async Task<IReadOnlyList<CapitalMovementRow>> MovementsAsync(SessionContext ctx, DateOnly? fromDate, DateOnly? toDate)
    {
        ctx.Require(Permissions.ReportView);
        var rows = await (from m in db.CapitalMovements join s in db.Shareholders on m.ShareholderId equals s.Id join p in db.BusinessPartners on s.PartnerId equals p.Id
                          join v in db.Vouchers on m.VoucherId equals v.Id
                          where m.CompanyId == ctx.CompanyId && (fromDate == null || m.Date >= fromDate) && (toDate == null || m.Date <= toDate) && v.Status != VoucherStatus.Cancelled && v.Status != VoucherStatus.Reversed
                          select new CapitalMovementRow(m.Id, m.ShareholderId, p.Code, p.Name, m.Kind, m.Date, m.Amount, v.Number, v.Status != VoucherStatus.Posted, m.Note)).AsNoTracking().ToListAsync();
        return rows.OrderByDescending(r => r.Date).ThenBy(r => r.PartnerCode, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ cổ tức

    /// <summary>Dự thảo phương án chia cổ tức theo tỷ lệ vốn thực góp đến ngày nghị quyết; chênh lệch làm tròn dồn vào người góp nhiều nhất.</summary>
    public async Task<DividendResolution> CreateDividendAsync(SessionContext ctx, DividendInput input)
    {
        ctx.Require(Permissions.CashDocument);
        if (input.TotalAmount <= 0) throw new DomainException(ErrorCodes.AmountNotPositive, "Tổng cổ tức phải lớn hơn 0.");
        if (input.PitRatePercent is < 0 or > 100) throw new DomainException(ErrorCodes.ValidationFailed, "Thuế suất từ 0 đến 100%.");
        if (string.IsNullOrWhiteSpace(input.Description)) throw new DomainException(ErrorCodes.ValidationFailed, "Phải ghi căn cứ (số nghị quyết, kỳ lợi nhuận).");
        return await db.InTransactionAsync(async () =>
        {
            var holders = await db.Shareholders.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId && s.IsActive).ToListAsync();
            var paid = await ContributedAsync(ctx.CompanyId, input.ResolutionDate);
            var basis = holders.Select(h => (h, c: paid.GetValueOrDefault(h.Id))).Where(x => x.c > 0).ToList();
            var total = basis.Sum(x => x.c);
            if (total <= 0) throw new DomainException(ErrorCodes.ValidationFailed, $"Chưa có vốn thực góp đến ngày {input.ResolutionDate.Vn()}.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            var dp = await db.Currencies.Where(c => c.Code == company.FunctionalCurrency).Select(c => c.DecimalPlaces).FirstAsync();
            var shares = basis.Select(x => (x.h, x.c, gross: MoneyMath.Round(input.TotalAmount * x.c / total, dp))).ToList();
            var rest = input.TotalAmount - shares.Sum(x => x.gross);
            var maxIdx = shares.IndexOf(shares.OrderByDescending(x => x.c).ThenBy(x => x.h.Id).First());
            shares[maxIdx] = (shares[maxIdx].h, shares[maxIdx].c, shares[maxIdx].gross + rest);

            var d = new DividendResolution { Id = Ids.New(), CompanyId = ctx.CompanyId, ResolutionDate = input.ResolutionDate, Description = input.Description.Trim(), TotalAmount = input.TotalAmount,
                PitRatePercent = input.PitRatePercent, CreatedBy = ctx.UserId, CreatedAt = clock.UtcNow };
            d.Number = await NextNumberAsync(ctx.CompanyId, input.ResolutionDate);
            foreach (var (h, c, gross) in shares)
            {
                var pit = h.IsIndividual ? MoneyMath.Round(gross * input.PitRatePercent / 100m, dp) : 0m;
                d.Lines.Add(new DividendLine { Id = Ids.New(), ResolutionId = d.Id, ShareholderId = h.Id, CapitalBasis = c, Gross = gross, PitWithheld = pit, Net = gross - pit });
            }
            db.DividendResolutions.Add(d);
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.dividend_create", "dividend", d.Id.ToString(), new { d.Number, d.TotalAmount, d.PitRatePercent, holders = shares.Count });
            await db.SaveChangesAsync();
            return d;
        });
    }

    public async Task<DividendResolution> DecideDividendAsync(SessionContext ctx, Guid id, bool approve, string? note)
    {
        ctx.Require(Permissions.VoucherApprove);
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối phải ghi lý do.");
        return await db.InTransactionAsync(async () =>
        {
            var d = await LoadAsync(ctx, id);
            if (d.Status != DividendStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phương án {d.Number} không ở trạng thái dự thảo.");
            var company = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == ctx.CompanyId);
            if (company.RequireDistinctApprover && d.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt phương án chia cổ tức phải khác người lập.");
            d.Status = approve ? DividendStatus.Approved : DividendStatus.Cancelled; d.DecisionNote = note; d.ApprovedBy = ctx.UserId; d.ApprovedAt = clock.UtcNow;
            await audit.AppendAsync(ctx.CompanyId, ctx, approve ? "equity.dividend_approve" : "equity.dividend_reject", "dividend", d.Id.ToString(), new { d.Number, note });
            await db.SaveChangesAsync();
            return d;
        });
    }

    /// <summary>Chứng từ ghi nhận cổ tức phải trả (nháp): Nợ lợi nhuận chưa phân phối = tổng gộp; Có phải trả cổ đông (theo người, số thực nhận); Có thuế TNCN khấu trừ.</summary>
    public async Task<Voucher> DeclareAsync(SessionContext ctx, Guid id, DateOnly date)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        return await db.InTransactionAsync(async () =>
        {
            var d = await db.DividendResolutions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phương án chia cổ tức không tồn tại.");
            var live = await LiveVoucherAsync(d.DeclarationVoucherId);
            if (live is not null) throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ ghi nhận {live.Number}.");
            if (d.Status != DividendStatus.Approved && !(d.Status == DividendStatus.Declared && live is null)) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Phương án {d.Number} chưa được duyệt.");
            var source = await RoleAccountAsync(ctx, "DIVIDEND_SOURCE", "lợi nhuận chưa phân phối");
            var payable = await RoleAccountAsync(ctx, "DIVIDEND_PAYABLE", "cổ tức phải trả");
            if (!payable.RequiresPartner || payable.OpenItemNature == OpenItemNature.None) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {payable.Code} (cổ tức phải trả) phải theo dõi theo đối tượng và theo từng khoản để bù trừ khi chi.");
            var pitTotal = d.Lines.Sum(l => l.PitWithheld);
            var pitAcc = pitTotal > 0 ? await RoleAccountAsync(ctx, "DIVIDEND_PIT_PAYABLE", "thuế TNCN khấu trừ trên cổ tức") : null;
            var partners = await (from l in db.DividendLines join s in db.Shareholders on l.ShareholderId equals s.Id join p in db.BusinessPartners on s.PartnerId equals p.Id
                                  where l.ResolutionId == d.Id select new { l.ShareholderId, PartnerId = p.Id, p.Code }).AsNoTracking().ToDictionaryAsync(x => x.ShareholderId);
            var lines = new List<VoucherLineInput> { new(source.Id, Side.Debit, d.TotalAmount, Description: $"Chia cổ tức, lợi nhuận theo {d.Number}") };
            foreach (var l in d.Lines.Where(x => x.Net > 0).OrderBy(x => partners[x.ShareholderId].Code, StringComparer.Ordinal))
                lines.Add(new VoucherLineInput(payable.Id, Side.Credit, l.Net, partners[l.ShareholderId].PartnerId, Description: $"Cổ tức {d.Number} – {partners[l.ShareholderId].Code}"));
            if (pitAcc is not null) lines.Add(new VoucherLineInput(pitAcc.Id, Side.Credit, pitTotal, null, Description: $"Thuế TNCN khấu trừ trên cổ tức {d.Number}"));
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Ghi nhận cổ tức, lợi nhuận phải trả {d.Number}: {d.Description}", lines, SourceSystem: Source, SourceId: $"dividend:{d.Id}:declare"));
            d.DeclarationVoucherId = v.Id; d.Status = DividendStatus.Declared;
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.dividend_declare", "dividend", d.Id.ToString(), new { d.Number, d.TotalAmount, pit = pitTotal, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    /// <summary>Chứng từ chi cổ tức nháp cho một chủ sở hữu: Nợ phải trả cổ tức (bù trừ khoản đã ghi nhận) / Có tiền, đúng số thực nhận. Chỉ sau khi chứng từ ghi nhận đã ghi sổ.</summary>
    public async Task<Voucher> CreatePaymentAsync(SessionContext ctx, Guid id, Guid shareholderId, Guid cashAccountId, DateOnly date)
    {
        ctx.Require(Permissions.VoucherCreate); ctx.Require(Permissions.CashDocument);
        return await db.InTransactionAsync(async () =>
        {
            var d = await db.DividendResolutions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phương án chia cổ tức không tồn tại.");
            var decl = await LiveVoucherAsync(d.DeclarationVoucherId);
            if (d.Status != DividendStatus.Declared || decl is not { Status: VoucherStatus.Posted }) throw new DomainException(ErrorCodes.InvalidStateTransition, "Chứng từ ghi nhận cổ tức phải trả chưa được ghi sổ.");
            var line = d.Lines.FirstOrDefault(l => l.ShareholderId == shareholderId) ?? throw new DomainException(ErrorCodes.NotFound, "Chủ sở hữu không có trong phương án.");
            if (line.Net <= 0) throw new DomainException(ErrorCodes.InvalidStateTransition, "Không có số thực nhận để chi.");
            var existing = await LiveVoucherAsync(line.PaymentVoucherId);
            if (existing is not null) throw new DomainException(ErrorCodes.DuplicateSource, $"Đã có chứng từ chi {existing.Number}.");
            var cash = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cashAccountId && a.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.AccountNotFound, "Tài khoản tiền không tồn tại.");
            if (cash.CashKind is not (CashKind.Cash or CashKind.Bank)) throw new DomainException(ErrorCodes.ValidationFailed, $"TK {cash.Code} không phải tài khoản tiền.");
            var payable = await RoleAccountAsync(ctx, "DIVIDEND_PAYABLE", "cổ tức phải trả");
            var partner = await (from s in db.Shareholders join p in db.BusinessPartners on s.PartnerId equals p.Id where s.Id == shareholderId select p).AsNoTracking().FirstAsync();
            var target = await (from l in db.JournalLines join e in db.JournalEntries on l.JournalEntryId equals e.Id
                                where e.VoucherId == decl.Id && l.AccountId == payable.Id && l.PartnerId == partner.Id && l.Side == Side.Credit select l.Id).FirstAsync();
            var v = await engine.CreateDraftAsync(ctx, new VoucherInput(VoucherTypes.General, date, date, $"Chi cổ tức {d.Number} – {partner.Name}",
                [new VoucherLineInput(payable.Id, Side.Debit, line.Net, partner.Id, Description: $"Chi cổ tức {d.Number}", SettleJournalLineId: target), new VoucherLineInput(cash.Id, Side.Credit, line.Net, Description: $"Chi cổ tức {partner.Code}")],
                SourceSystem: Source, SourceId: $"dividend:{d.Id}:pay:{shareholderId}"));
            line.PaymentVoucherId = v.Id;
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.dividend_pay_voucher", "dividend", d.Id.ToString(), new { d.Number, partner = partner.Code, line.Net, voucher = v.Number });
            await db.SaveChangesAsync();
            return v;
        });
    }

    public async Task CancelDividendAsync(SessionContext ctx, Guid id, string reason)
    {
        ctx.Require(Permissions.CashDocument);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Phải ghi lý do hủy.");
        await db.InTransactionAsync(async () =>
        {
            var d = await db.DividendResolutions.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phương án chia cổ tức không tồn tại.");
            if (d.Status is DividendStatus.Cancelled) throw new DomainException(ErrorCodes.InvalidStateTransition, "Phương án đã hủy.");
            if (await LiveVoucherAsync(d.DeclarationVoucherId) is not null) throw new DomainException(ErrorCodes.InvalidStateTransition, "Đã có chứng từ ghi nhận cổ tức; hủy hoặc đảo chứng từ đó trước.");
            d.Status = DividendStatus.Cancelled; d.DecisionNote = reason;
            await audit.AppendAsync(ctx.CompanyId, ctx, "equity.dividend_cancel", "dividend", d.Id.ToString(), new { d.Number, reason });
            await db.SaveChangesAsync();
        });
    }

    public async Task<IReadOnlyList<DividendRow>> DividendsAsync(SessionContext ctx)
    {
        ctx.Require(Permissions.ReportView);
        var list = await db.DividendResolutions.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == ctx.CompanyId).ToListAsync();
        var vids = list.Where(x => x.DeclarationVoucherId != null).Select(x => x.DeclarationVoucherId!.Value).ToList();
        var vs = await db.Vouchers.AsNoTracking().Where(v => vids.Contains(v.Id)).Select(v => new { v.Id, v.Number, v.Status }).ToDictionaryAsync(v => v.Id);
        return list.Select(d =>
        {
            var v = d.DeclarationVoucherId is { } id && vs.TryGetValue(id, out var x) && x.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed) ? x : null;
            var status = d.Status == DividendStatus.Declared && v is null ? DividendStatus.Approved : d.Status;
            return new DividendRow(d.Id, d.Number, d.ResolutionDate, d.Description, d.TotalAmount, d.PitRatePercent, status, v?.Number, v is { Status: not VoucherStatus.Posted }, d.Lines.Sum(l => l.PitWithheld), d.Lines.Sum(l => l.Net));
        }).OrderByDescending(r => r.ResolutionDate).ThenByDescending(r => r.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<DividendView> DividendAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var row = (await DividendsAsync(ctx)).FirstOrDefault(r => r.Id == id) ?? throw new DomainException(ErrorCodes.NotFound, "Phương án chia cổ tức không tồn tại.");
        var d = await db.DividendResolutions.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == id);
        var holders = await (from s in db.Shareholders join p in db.BusinessPartners on s.PartnerId equals p.Id where s.CompanyId == ctx.CompanyId select new { s.Id, p.Code, p.Name, s.IsIndividual }).AsNoTracking().ToDictionaryAsync(x => x.Id);
        var pids = d.Lines.Where(l => l.PaymentVoucherId != null).Select(l => l.PaymentVoucherId!.Value).ToList();
        var pv = await db.Vouchers.AsNoTracking().Where(v => pids.Contains(v.Id)).Select(v => new { v.Id, v.Number, v.Status }).ToDictionaryAsync(v => v.Id);
        var basis = d.Lines.Sum(l => l.CapitalBasis);
        var lines = d.Lines.OrderByDescending(l => l.CapitalBasis).Select(l =>
        {
            var h = holders[l.ShareholderId];
            var v = l.PaymentVoucherId is { } vid && pv.TryGetValue(vid, out var x) && x.Status is not (VoucherStatus.Cancelled or VoucherStatus.Reversed) ? x : null;
            return new DividendLineView(l.ShareholderId, h.Code, h.Name, h.IsIndividual, l.CapitalBasis, basis > 0 ? Math.Round(l.CapitalBasis / basis * 100, 4) : null, l.Gross, l.PitWithheld, l.Net, v?.Number, v is { Status: not VoucherStatus.Posted }, v is { Status: VoucherStatus.Posted });
        }).ToList();
        return new DividendView(row, d.DecisionNote, lines);
    }

    private async Task<Voucher?> LiveVoucherAsync(Guid? id)
    {
        if (id is null) return null;
        var v = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return v is null || v.Status is VoucherStatus.Cancelled or VoucherStatus.Reversed ? null : v;
    }

    private async Task<DividendResolution> LoadAsync(SessionContext ctx, Guid id)
        => await db.DividendResolutions.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId) ?? throw new DomainException(ErrorCodes.NotFound, "Phương án chia cổ tức không tồn tại.");

    private async Task<Account> RoleAccountAsync(SessionContext ctx, string role, string what)
    {
        var m = await db.AccountRoleMappings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == ctx.CompanyId && x.RoleCode == role)
                ?? throw new DomainException(ErrorCodes.RuleRoleUnmapped, $"Chưa ánh xạ vai trò tài khoản {role} ({what}).");
        return await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == m.AccountId);
    }

    private Task<string> NextNumberAsync(Guid companyId, DateOnly date) => Numbering.NextAsync(db, companyId, date, "CTC");
}
