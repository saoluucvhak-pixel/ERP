using System.Text.Json;
using Hak.Application.Audit;
using Hak.Application.Common;
using Hak.Application.Security;
using Hak.Data;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Security;
using Hak.Domain.Trade;
using Hak.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Trade;

public sealed record ContractLineInput(Guid ItemId, decimal CommittedQty, decimal UnitPrice, decimal DiscountRate = 0, decimal TaxRate = 0, string? Notes = null);
public sealed record ContractMilestoneInput(string Name, decimal? Percentage, decimal DueAmount, DateOnly DueDate);
public sealed record ContractInput(InvoiceKind Kind, string Name, ContractCategory Category, Guid PartnerId, DateOnly EffectiveDate, DateOnly ExpirationDate,
    IReadOnlyList<ContractLineInput> Lines, DateOnly? SignedDate = null, string Currency = "VND", decimal ExchangeRate = 1, string? PaymentTerms = null,
    decimal? TotalAmount = null, decimal TaxAmount = 0, IReadOnlyList<ContractMilestoneInput>? Milestones = null);
public sealed record ContractLineChange(Guid ItemId, decimal QtyDelta);

public sealed record ContractLineView(Guid Id, int LineNo, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal CommittedQty, decimal UnitPrice, decimal DiscountRate, decimal TaxRate,
    decimal NetPrice, decimal CommittedAmount, decimal OrderedQty, decimal OrderedAmount, decimal RemainingQty, decimal? PercentDone, string? Notes);
public sealed record ContractMilestoneView(Guid Id, string Name, decimal? Percentage, decimal DueAmount, DateOnly DueDate, decimal PaidAmount, string PaymentStatus, DateOnly? LastPaidDate);
public sealed record ContractAmendmentView(Guid Id, string Code, DateOnly AmendmentDate, string Summary, decimal ValueChange, DateOnly? NewExpirationDate);
public sealed record ContractDocumentView(Guid Id, string FileName, string FilePath, string? FileType, DateTime UploadedAt);
public sealed record ContractOrderView(Guid OrderId, string Number, DateOnly OrderDate, OrderStatus Status, decimal Amount);
public sealed record ContractRow(Guid Id, string Code, string Name, InvoiceKind Kind, ContractCategory Category, Guid PartnerId, string PartnerCode, string PartnerName, string Currency,
    DateOnly EffectiveDate, DateOnly ExpirationDate, decimal TotalAmount, decimal OrderedAmount, decimal? PercentDone, ContractStatus Status, bool Expired, int DaysToExpire, int Version);
public sealed record ContractPage(IReadOnlyList<ContractRow> Items, int Total, bool HasMore);
public sealed record ContractView(ContractRow Header, string? PaymentTerms, DateOnly? SignedDate, decimal ExchangeRate, decimal TaxAmount, string? StatusReason,
    IReadOnlyList<ContractLineView> Lines, IReadOnlyList<ContractMilestoneView> Milestones, IReadOnlyList<ContractAmendmentView> Amendments,
    IReadOnlyList<ContractDocumentView> Documents, IReadOnlyList<ContractOrderView> Orders);

public sealed record PriceVarianceRow(Guid ContractId, string ContractCode, string ItemCode, string ItemName, decimal ContractPrice, decimal OrderedQty, decimal AveragePrice, decimal MinPrice, decimal MaxPrice, decimal VariancePercent);
public sealed record MilestoneScheduleRow(Guid ContractId, string ContractCode, InvoiceKind Kind, string PartnerName, Guid MilestoneId, string Name, DateOnly DueDate, decimal DueAmount, decimal PaidAmount, decimal Remaining, string PaymentStatus);
public sealed record ContractAlert(Guid ContractId, string ContractCode, string Kind, string Message, DateOnly? Date);

/// <summary>
/// Quản lý hợp đồng mua bán (tệp phân tích quản lý hợp đồng trong ERP): dự thảo → duyệt (người duyệt khác người lập) → hiệu lực → đơn kế thừa trừ lùi → phụ lục → thanh lý.
/// Không ghi Nhật ký. Số đã thực hiện lấy từ đơn mua / đơn bán không hủy gắn với hợp đồng (một nguồn dữ liệu, không có sổ trừ lùi riêng).
/// </summary>
public sealed class ContractService(HakDbContext db, AuditService audit, IClock clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow);

    // ------------------------------------------------------------ dự thảo, duyệt

    public async Task<Contract> CreateAsync(SessionContext ctx, ContractInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        return await db.InTransactionAsync(async () =>
        {
            var c = new Contract { Id = Ids.New(), CompanyId = ctx.CompanyId, Kind = input.Kind, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId, RowVersion = 1 };
            await ApplyAsync(ctx, c, input);
            c.Code = await NextCodeAsync(ctx.CompanyId, c.Kind, c.EffectiveDate);
            db.Contracts.Add(c);
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.create", "contract", c.Id.ToString(), new { c.Code, c.Kind, c.TotalAmount }, c.RowVersion);
            await db.SaveChangesAsync();
            return c;
        });
    }

    /// <summary>Sửa nội dung khi còn Nháp; mỗi lần sửa tăng số phiên bản.</summary>
    public async Task<Contract> UpdateAsync(SessionContext ctx, Guid id, ContractInput input)
    {
        ctx.Require(Permissions.InvoiceDocument);
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            if (c.Status != ContractStatus.Draft) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} đã gửi duyệt; chỉnh sửa bằng phụ lục.");
            if (input.Kind != c.Kind) throw new DomainException(ErrorCodes.ValidationFailed, "Không đổi loại hợp đồng mua / bán.");
            foreach (var l in c.Lines.ToList()) db.ContractLines.Remove(l);
            foreach (var m in c.Milestones.ToList()) db.ContractMilestones.Remove(m);
            c.Lines.Clear(); c.Milestones.Clear();
            await ApplyAsync(ctx, c, input);
            c.Version++; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.update", "contract", c.Id.ToString(), new { c.Code, c.Version, c.TotalAmount }, c.RowVersion);
            await db.SaveChangesAsync();
            return c;
        });
    }

    public Task<Contract> SubmitAsync(SessionContext ctx, Guid id, int? expectedVersion = null) => TransitionAsync(ctx, id, Permissions.InvoiceDocument, "contract.submit", (c, _) =>
    {
        if (c.Lines.Count == 0 && c.Category != ContractCategory.Value) throw new DomainException(ErrorCodes.VoucherNoLines, "Hợp đồng chưa có dòng hàng hóa.");
        c.Move(ContractStatus.Draft, ContractStatus.PendingApproval); c.SubmittedBy = ctx.UserId;
        return Task.CompletedTask;
    }, expectedVersion);

    /// <summary>Duyệt (→ Hiệu lực) hoặc từ chối (→ Nháp, cần lý do). Người duyệt khác người lập khi công ty bắt buộc tách người.</summary>
    public Task<Contract> DecideAsync(SessionContext ctx, Guid id, bool approve, string? note, int? expectedVersion = null) => TransitionAsync(ctx, id, Permissions.VoucherApprove, approve ? "contract.approve" : "contract.reject", (c, company) =>
    {
        if (c.Status != ContractStatus.PendingApproval) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} không ở trạng thái chờ duyệt.");
        if (company.RequireDistinctApprover && c.CreatedBy == ctx.UserId) throw new DomainException(ErrorCodes.SodViolation, "Người duyệt hợp đồng không được trùng người lập.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new DomainException(ErrorCodes.ReasonRequired, "Từ chối hợp đồng phải có lý do.");
        if (approve) { c.Move(ContractStatus.PendingApproval, ContractStatus.Active); c.ApprovedBy = ctx.UserId; c.ApprovedAt = clock.UtcNow; c.SignedDate ??= Today; }
        else { c.Move(ContractStatus.PendingApproval, ContractStatus.Draft); c.StatusReason = note!.Trim(); }
        return Task.CompletedTask;
    }, expectedVersion);

    /// <summary>Thanh lý / đóng hợp đồng đã thực hiện đủ hoặc hết hiệu lực.</summary>
    public Task<Contract> CompleteAsync(SessionContext ctx, Guid id, string? note, int? expectedVersion = null) => TransitionAsync(ctx, id, Permissions.InvoiceDocument, "contract.complete", (c, _) =>
    {
        c.Move(ContractStatus.Active, ContractStatus.Completed); c.StatusReason = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Task.CompletedTask;
    }, expectedVersion);

    public async Task<Contract> CancelAsync(SessionContext ctx, Guid id, string reason, int? expectedVersion = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(ErrorCodes.ReasonRequired, "Hủy hợp đồng phải có lý do.");
        return await TransitionAsync(ctx, id, Permissions.InvoiceDocument, "contract.cancel", async (c, _) =>
        {
            if (c.Status is not (ContractStatus.Draft or ContractStatus.PendingApproval or ContractStatus.Active))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} không hủy được ở trạng thái hiện tại.");
            if (await db.TradeOrders.AnyAsync(o => o.ContractId == c.Id && o.Status != OrderStatus.Cancelled))
                throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} đã có đơn hàng kế thừa; hủy các đơn đó hoặc thanh lý hợp đồng.");
            c.Status = ContractStatus.Cancelled; c.StatusReason = reason.Trim();
        }, expectedVersion);
    }

    // ------------------------------------------------------------ phụ lục, mốc, tài liệu

    /// <summary>Phụ lục cho hợp đồng đang hiệu lực: tăng / giảm giá trị, đổi số lượng cam kết theo dòng, gia hạn. Mã {mã HĐ}-A{số}.</summary>
    public async Task<ContractAmendment> AmendAsync(SessionContext ctx, Guid id, DateOnly date, string summary, decimal valueChange, DateOnly? newExpiration, IReadOnlyList<ContractLineChange>? lineChanges = null)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(summary)) throw new DomainException(ErrorCodes.ValidationFailed, "Tóm tắt nội dung phụ lục bắt buộc.");
        if (!MoneyMath.HasAtMostDecimals(valueChange, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Giá trị thay đổi không hợp lệ.");
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            if (c.Status != ContractStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Chỉ lập phụ lục cho hợp đồng đang hiệu lực; {c.Code} đang ở trạng thái {c.Status}.");
            if (newExpiration is { } ne && ne <= c.ExpirationDate) throw new DomainException(ErrorCodes.ValidationFailed, "Hạn mới phải sau hạn hiện tại.");
            var ordered = await OrderedByItemAsync(c.Id);
            decimal fromLines = 0;
            foreach (var ch in lineChanges ?? [])
            {
                var line = c.Lines.FirstOrDefault(l => l.ItemId == ch.ItemId) ?? throw new DomainException(ErrorCodes.NotFound, "Vật tư không có trong hợp đồng.");
                var qty = line.CommittedQty + ch.QtyDelta;
                if (qty < 0 || !MoneyMath.HasAtMostDecimals(ch.QtyDelta, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng cam kết sau phụ lục không hợp lệ.");
                if (qty < ordered.GetValueOrDefault(line.ItemId).Qty) throw new DomainException(ErrorCodes.ValidationFailed, "Số lượng cam kết không được thấp hơn số đã đặt hàng.");
                line.CommittedQty = qty; fromLines += ch.QtyDelta * line.NetPrice;
            }
            if (c.TotalAmount + valueChange + fromLines < 0) throw new DomainException(ErrorCodes.ValidationFailed, "Giá trị hợp đồng sau phụ lục không được âm.");
            c.TotalAmount += valueChange + fromLines;
            if (newExpiration is { } e) c.ExpirationDate = e;
            c.RowVersion++;
            var n = await db.ContractAmendments.CountAsync(a => a.ContractId == c.Id) + 1;
            var a = new ContractAmendment { Id = Ids.New(), ContractId = c.Id, CompanyId = ctx.CompanyId, Code = $"{c.Code}-A{n:D2}", AmendmentDate = date, Summary = summary.Trim(), ValueChange = valueChange + fromLines,
                NewExpirationDate = newExpiration, LineChangesJson = lineChanges is { Count: > 0 } ? JsonSerializer.Serialize(lineChanges) : null, CreatedAt = clock.UtcNow, CreatedBy = ctx.UserId };
            db.ContractAmendments.Add(a);
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.amend", "contract", c.Id.ToString(), new { a.Code, a.ValueChange, newExpiration }, c.RowVersion);
            await db.SaveChangesAsync();
            return a;
        });
    }

    /// <summary>Ghi nhận số đã thu / chi của một mốc thanh toán (cộng dồn).</summary>
    public async Task<ContractMilestone> RecordPaymentAsync(SessionContext ctx, Guid contractId, Guid milestoneId, decimal amount, DateOnly date)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (amount <= 0 || !MoneyMath.HasAtMostDecimals(amount, MoneyMath.StorageScale)) throw new DomainException(ErrorCodes.AmountNotPositive, "Số tiền phải lớn hơn 0.");
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, contractId);
            if (c.Status is not (ContractStatus.Active or ContractStatus.Completed)) throw new DomainException(ErrorCodes.InvalidStateTransition, "Hợp đồng chưa hiệu lực.");
            var m = c.Milestones.FirstOrDefault(x => x.Id == milestoneId) ?? throw new DomainException(ErrorCodes.NotFound, "Mốc thanh toán không tồn tại.");
            if (m.PaidAmount + amount > m.DueAmount) throw new DomainException(ErrorCodes.ValidationFailed, "Tổng đã thanh toán vượt số phải thanh toán của mốc.");
            m.PaidAmount += amount; m.LastPaidDate = date; c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.milestone_paid", "contract", c.Id.ToString(), new { c.Code, m.Name, amount, date }, c.RowVersion);
            await db.SaveChangesAsync();
            return m;
        });
    }

    public async Task<ContractDocument> AddDocumentAsync(SessionContext ctx, Guid contractId, string fileName, string filePath, string? fileType)
    {
        ctx.Require(Permissions.InvoiceDocument);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(filePath)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên tệp và đường dẫn bắt buộc.");
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, contractId);
            var d = new ContractDocument { Id = Ids.New(), ContractId = c.Id, CompanyId = ctx.CompanyId, FileName = fileName.Trim(), FilePath = filePath.Trim(), FileType = string.IsNullOrWhiteSpace(fileType) ? null : fileType.Trim(), UploadedAt = clock.UtcNow, UploadedBy = ctx.UserId };
            db.ContractDocuments.Add(d);
            await audit.AppendAsync(ctx.CompanyId, ctx, "contract.document", "contract", c.Id.ToString(), new { c.Code, d.FileName }, c.RowVersion);
            await db.SaveChangesAsync();
            return d;
        });
    }

    // ------------------------------------------------------------ xem, báo cáo

    /// <summary>Danh sách hiển thị: tối đa 500 hợp đồng (mã giảm dần). KHÔNG dùng cho cảnh báo hay quyết định nghiệp vụ — dùng <see cref="ListCoreAsync"/> với đúng bộ lọc.</summary>
    public Task<IReadOnlyList<ContractRow>> ListAsync(SessionContext ctx, InvoiceKind? kind = null)
        => ListCoreAsync(ctx, kind, null, 500);

    /// <summary>Phân trang phía máy chủ (mã giảm dần): trả đúng một trang kèm tổng số hợp đồng khớp và cờ còn thêm, để giao diện tải được MỌI hợp đồng.</summary>
    public async Task<ContractPage> ListPageAsync(SessionContext ctx, InvoiceKind? kind, int skip, int take)
    {
        ctx.Require(Permissions.ReportView);
        skip = Math.Max(0, skip); take = Math.Clamp(take, 1, 500);
        var q = db.Contracts.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId);
        if (kind != null) q = q.Where(c => c.Kind == kind);
        var total = await q.CountAsync();
        var rows = await q.Include(c => c.Lines).OrderByDescending(c => c.Code).Skip(skip).Take(take).ToListAsync();
        return new ContractPage(await RowsAsync(rows), total, skip + rows.Count < total);
    }

    /// <summary>Lọc theo công ty/loại/trạng thái TRƯỚC khi giới hạn số dòng; <paramref name="take"/> null = toàn bộ tập khớp.</summary>
    private async Task<IReadOnlyList<ContractRow>> ListCoreAsync(SessionContext ctx, InvoiceKind? kind, ContractStatus[]? statuses, int? take)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.Contracts.AsNoTracking().Include(c => c.Lines).Where(c => c.CompanyId == ctx.CompanyId);
        if (kind != null) q = q.Where(c => c.Kind == kind);
        if (statuses != null) q = q.Where(c => statuses.Contains(c.Status));
        var ordered = q.OrderByDescending(c => c.Code);
        var rows = take is { } n ? await ordered.Take(n).ToListAsync() : await ordered.ToListAsync();
        return await RowsAsync(rows);
    }

    public async Task<ContractView> GetAsync(SessionContext ctx, Guid id)
    {
        ctx.Require(Permissions.ReportView);
        var c = await db.Contracts.AsNoTracking().Include(x => x.Lines).Include(x => x.Milestones).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == ctx.CompanyId)
                ?? throw new DomainException(ErrorCodes.NotFound, "Hợp đồng không tồn tại.");
        var header = (await RowsAsync([c]))[0];
        var ordered = await OrderedByItemAsync(c.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var lines = c.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var o = ordered.GetValueOrDefault(l.ItemId);
            var it = items[l.ItemId];
            return new ContractLineView(l.Id, l.LineNo, l.ItemId, it.Code, it.Name, it.Unit, l.CommittedQty, l.UnitPrice, l.DiscountRate, l.TaxRate, l.NetPrice, l.CommittedAmount, o.Qty, o.Amount,
                l.CommittedQty - o.Qty, l.CommittedQty > 0 ? Math.Round(o.Qty / l.CommittedQty * 100, 2) : null, l.Notes);
        }).ToList();
        var ms = c.Milestones.OrderBy(m => m.DueDate).Select(m => new ContractMilestoneView(m.Id, m.Name, m.Percentage, m.DueAmount, m.DueDate, m.PaidAmount, m.PaymentStatus(Today), m.LastPaidDate)).ToList();
        var am = await db.ContractAmendments.AsNoTracking().Where(a => a.ContractId == id).OrderBy(a => a.Code).Select(a => new ContractAmendmentView(a.Id, a.Code, a.AmendmentDate, a.Summary, a.ValueChange, a.NewExpirationDate)).ToListAsync();
        var docs = await db.ContractDocuments.AsNoTracking().Where(d => d.ContractId == id).OrderBy(d => d.UploadedAt).Select(d => new ContractDocumentView(d.Id, d.FileName, d.FilePath, d.FileType, d.UploadedAt)).ToListAsync();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.ContractId == id).OrderBy(o => o.OrderDate).ToListAsync();
        return new ContractView(header, c.PaymentTerms, c.SignedDate, c.ExchangeRate, c.TaxAmount, c.StatusReason, lines, ms, am, docs,
            orders.Select(o => new ContractOrderView(o.Id, o.Number, o.OrderDate, o.Status, o.Lines.Sum(l => l.Quantity * l.UnitPrice))).ToList());
    }

    /// <summary>Tiến độ thực hiện: cam kết so với đã đặt qua đơn kế thừa, phần trăm hoàn thành và phần còn lại.</summary>
    public async Task<IReadOnlyList<ContractRow>> FulfilmentReportAsync(SessionContext ctx, InvoiceKind? kind = null)
        => await ListCoreAsync(ctx, kind, [ContractStatus.Active, ContractStatus.Completed], 500);

    public async Task<IReadOnlyList<MilestoneScheduleRow>> MilestoneScheduleAsync(SessionContext ctx, InvoiceKind? kind = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.Contracts.AsNoTracking().Include(c => c.Milestones).Where(c => c.CompanyId == ctx.CompanyId && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Completed));
        if (kind != null) q = q.Where(c => c.Kind == kind);
        var cs = await q.ToListAsync();
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId).ToDictionaryAsync(p => p.Id, p => p.Name);
        return cs.SelectMany(c => c.Milestones.Select(m => new MilestoneScheduleRow(c.Id, c.Code, c.Kind, partners.GetValueOrDefault(c.PartnerId) ?? "", m.Id, m.Name, m.DueDate, m.DueAmount, m.PaidAmount,
            m.DueAmount - m.PaidAmount, m.PaymentStatus(Today)))).OrderBy(r => r.DueDate).ToList();
    }

    /// <summary>So đơn giá cam kết (sau chiết khấu) với đơn giá thực tế trên đơn kế thừa.</summary>
    public async Task<IReadOnlyList<PriceVarianceRow>> PriceVarianceAsync(SessionContext ctx, Guid? contractId = null, InvoiceKind? kind = null)
    {
        ctx.Require(Permissions.ReportView);
        var q = db.Contracts.AsNoTracking().Include(c => c.Lines).Where(c => c.CompanyId == ctx.CompanyId && c.Status != ContractStatus.Draft && c.Status != ContractStatus.Cancelled);
        if (contractId != null) q = q.Where(c => c.Id == contractId);
        if (kind != null) q = q.Where(c => c.Kind == kind);   // hợp đồng mua và bán là hai chu trình: báo cáo theo từng chiều
        var cs = await q.ToListAsync();
        var ids = cs.Select(c => c.Id).ToList();
        var orders = await db.TradeOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.ContractId != null && ids.Contains(o.ContractId.Value) && o.Status != OrderStatus.Cancelled).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Id);
        var rows = new List<PriceVarianceRow>();
        foreach (var c in cs)
            foreach (var l in c.Lines)
            {
                var ls = orders.Where(o => o.ContractId == c.Id).SelectMany(o => o.Lines).Where(x => x.ItemId == l.ItemId).ToList();
                if (ls.Count == 0) continue;
                var qty = ls.Sum(x => x.Quantity);
                var avg = ls.Sum(x => x.Quantity * x.UnitPrice) / qty;
                rows.Add(new PriceVarianceRow(c.Id, c.Code, items[l.ItemId].Code, items[l.ItemId].Name, l.NetPrice, qty, Math.Round(avg, 4), ls.Min(x => x.UnitPrice), ls.Max(x => x.UnitPrice),
                    l.NetPrice == 0 ? 0 : Math.Round((avg - l.NetPrice) / l.NetPrice * 100, 2)));
            }
        return rows.OrderByDescending(r => Math.Abs(r.VariancePercent)).ToList();
    }

    /// <summary>Cảnh báo: sắp hết hạn (60 ngày; nhấn mạnh 30 ngày), đã hết hạn còn hiệu lực, mốc thanh toán quá hạn, dùng hết ≥ 90% hạn mức.</summary>
    public async Task<IReadOnlyList<ContractAlert>> AlertsAsync(SessionContext ctx, InvoiceKind? kind = null)
    {
        ctx.Require(Permissions.ReportView);
        var list = new List<ContractAlert>();
        var rows = await ListCoreAsync(ctx, kind, [ContractStatus.Active], null);   // toàn bộ hợp đồng còn hiệu lực của công ty, không bị cắt 500 của danh sách
        foreach (var r in rows)
        {
            if (r.Expired) list.Add(new(r.Id, r.Code, "expired", $"Hợp đồng {r.Code} đã hết hạn từ {r.ExpirationDate:dd/MM/yyyy} nhưng chưa thanh lý.", r.ExpirationDate));
            else if (r.DaysToExpire <= 60) list.Add(new(r.Id, r.Code, r.DaysToExpire <= 30 ? "expiring30" : "expiring60", $"Hợp đồng {r.Code} còn {r.DaysToExpire} ngày đến hạn ({r.ExpirationDate:dd/MM/yyyy}).", r.ExpirationDate));
            if (r.PercentDone is >= 90) list.Add(new(r.Id, r.Code, "limit", $"Hợp đồng {r.Code} đã dùng {r.PercentDone.Vn(2)}% hạn mức cam kết.", null));
        }
        foreach (var m in (await MilestoneScheduleAsync(ctx, kind)).Where(m => m.PaymentStatus == "OVERDUE"))
            list.Add(new(m.ContractId, m.ContractCode, "milestone_overdue", $"Mốc \"{m.Name}\" của {m.ContractCode} quá hạn từ {m.DueDate:dd/MM/yyyy}, còn {m.Remaining.Vn(0)}.", m.DueDate));
        return list;
    }

    // ------------------------------------------------------------ internals

    private async Task<Contract> TransitionAsync(SessionContext ctx, Guid id, string permission, string action, Func<Contract, Hak.Domain.Organization.Company, Task> change, int? expectedVersion = null)
    {
        ctx.Require(permission);
        return await db.InTransactionAsync(async () =>
        {
            var c = await LoadAsync(ctx, id);
            // Phiên bản NỘI DUNG hợp đồng (tăng mỗi lần sửa) mà người dùng đã xem; người khác sửa sau đó thì phải xem lại.
            if (expectedVersion is { } ev && c.Version != ev)
                throw new DomainException(ErrorCodes.ConcurrencyConflict, $"Hợp đồng {c.Code} đã được sửa (phiên bản {c.Version}, bạn đang xem phiên bản {ev}). Vui lòng tải lại.");
            var company = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == ctx.CompanyId);
            await change(c, company);
            c.RowVersion++;
            await audit.AppendAsync(ctx.CompanyId, ctx, action, "contract", c.Id.ToString(), new { c.Code, status = c.Status.ToString() }, c.RowVersion);
            await db.SaveChangesAsync();
            return c;
        });
    }

    private async Task ApplyAsync(SessionContext ctx, Contract c, ContractInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new DomainException(ErrorCodes.ValidationFailed, "Tên hợp đồng bắt buộc.");
        if (input.ExpirationDate < input.EffectiveDate) throw new DomainException(ErrorCodes.ValidationFailed, "Ngày hết hạn không được trước ngày hiệu lực.");
        if (input.ExchangeRate <= 0) throw new DomainException(ErrorCodes.ValidationFailed, "Tỷ giá phải lớn hơn 0.");
        var partner = await db.BusinessPartners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PartnerId && p.CompanyId == ctx.CompanyId)
                      ?? throw new DomainException(ErrorCodes.PartnerWrongCompany, "Đối tượng không thuộc công ty.");
        var need = input.Kind == InvoiceKind.Sales ? PartnerRoles.Customer : PartnerRoles.Supplier;
        if (!partner.Roles.HasFlag(need)) throw new DomainException(ErrorCodes.ValidationFailed, $"Đối tượng {partner.Code} không phải {(need == PartnerRoles.Customer ? "khách hàng" : "nhà cung cấp")}.");
        if (!await db.Currencies.AnyAsync(x => x.Code == input.Currency)) throw new DomainException(ErrorCodes.ValidationFailed, $"Đồng tiền {input.Currency} chưa khai báo.");
        var ids = input.Lines.Select(l => l.ItemId).ToList();
        if (ids.Distinct().Count() != ids.Count) throw new DomainException(ErrorCodes.ValidationFailed, "Mỗi vật tư chỉ một dòng trong hợp đồng.");
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId && ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var n = 0;
        foreach (var l in input.Lines)
        {
            n++;
            if (!items.ContainsKey(l.ItemId)) throw new DomainException(ErrorCodes.NotFound, $"Dòng {n}: vật tư không tồn tại.");
            if (l.CommittedQty <= 0 || l.UnitPrice < 0 || l.DiscountRate is < 0 or > 100 || l.TaxRate < 0
                || !MoneyMath.HasAtMostDecimals(l.CommittedQty, MoneyMath.StorageScale) || !MoneyMath.HasAtMostDecimals(l.UnitPrice, MoneyMath.StorageScale))
                throw new DomainException(ErrorCodes.ValidationFailed, $"Dòng {n}: số lượng, đơn giá, chiết khấu hoặc thuế suất không hợp lệ.");
            c.Lines.Add(new ContractLine { Id = Ids.New(), ContractId = c.Id, CompanyId = ctx.CompanyId, LineNo = n, ItemId = l.ItemId, CommittedQty = l.CommittedQty, UnitPrice = l.UnitPrice,
                DiscountRate = l.DiscountRate, TaxRate = l.TaxRate, Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim() });
        }
        c.Name = input.Name.Trim(); c.Category = input.Category; c.PartnerId = input.PartnerId; c.Currency = input.Currency; c.ExchangeRate = input.ExchangeRate;
        c.SignedDate = input.SignedDate; c.EffectiveDate = input.EffectiveDate; c.ExpirationDate = input.ExpirationDate;
        c.PaymentTerms = string.IsNullOrWhiteSpace(input.PaymentTerms) ? null : input.PaymentTerms.Trim();
        c.TaxAmount = input.TaxAmount;
        c.TotalAmount = input.Category == ContractCategory.Value ? input.TotalAmount ?? throw new DomainException(ErrorCodes.ValidationFailed, "Hợp đồng khung giá trị phải khai tổng giá trị.") : c.Lines.Sum(l => l.CommittedAmount);
        if (c.TotalAmount < 0 || c.TaxAmount < 0) throw new DomainException(ErrorCodes.ValidationFailed, "Giá trị hợp đồng không được âm.");
        var ms = input.Milestones ?? [];
        if (ms.Any(m => string.IsNullOrWhiteSpace(m.Name) || m.DueAmount <= 0 || m.Percentage is < 0 or > 100)) throw new DomainException(ErrorCodes.ValidationFailed, "Mốc thanh toán: tên, số tiền (> 0) và % (0–100) không hợp lệ.");
        if (ms.Sum(m => m.DueAmount) > c.TotalAmount + c.TaxAmount) throw new DomainException(ErrorCodes.ValidationFailed, "Tổng các mốc thanh toán vượt giá trị hợp đồng (gồm thuế).");
        var k = 0;
        foreach (var m in ms)
            c.Milestones.Add(new ContractMilestone { Id = Ids.New(), ContractId = c.Id, CompanyId = ctx.CompanyId, LineNo = ++k, Name = m.Name.Trim(), Percentage = m.Percentage, DueAmount = m.DueAmount, DueDate = m.DueDate });
    }

    private async Task<Contract> LoadAsync(SessionContext ctx, Guid id)
        => await db.Contracts.Include(c => c.Lines).Include(c => c.Milestones).FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == ctx.CompanyId)
           ?? throw new DomainException(ErrorCodes.NotFound, "Hợp đồng không tồn tại.");

    private async Task<Dictionary<Guid, (decimal Qty, decimal Amount)>> OrderedByItemAsync(Guid contractId)
    {
        var lines = await (from l in db.TradeOrderLines join o in db.TradeOrders on l.OrderId equals o.Id
                           where o.ContractId == contractId && o.Status != OrderStatus.Cancelled select new { l.ItemId, l.Quantity, l.UnitPrice }).AsNoTracking().ToListAsync();
        return lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => (g.Sum(x => x.Quantity), g.Sum(x => x.Quantity * x.UnitPrice)));
    }

    private async Task<IReadOnlyList<ContractRow>> RowsAsync(List<Contract> cs)
    {
        var partners = await db.BusinessPartners.AsNoTracking().Where(p => cs.Select(c => c.PartnerId).Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var ids = cs.Select(c => c.Id).ToList();
        var lines = await (from l in db.TradeOrderLines join o in db.TradeOrders on l.OrderId equals o.Id
                           where o.ContractId != null && ids.Contains(o.ContractId.Value) && o.Status != OrderStatus.Cancelled
                           select new { o.ContractId, l.ItemId, l.Quantity, l.UnitPrice }).AsNoTracking().ToListAsync();
        return cs.Select(c =>
        {
            var mine = lines.Where(l => l.ContractId == c.Id).ToList();
            var amount = mine.Sum(l => l.Quantity * l.UnitPrice);
            decimal? pct = c.Category == ContractCategory.Quantity
                ? (c.Lines.Sum(l => l.CommittedQty) is var tq && tq > 0 ? Math.Round(c.Lines.Sum(l => Math.Min(mine.Where(m => m.ItemId == l.ItemId).Sum(m => m.Quantity), l.CommittedQty)) / tq * 100, 2) : null)
                : c.TotalAmount > 0 ? Math.Round(amount / c.TotalAmount * 100, 2) : null;
            var p = partners[c.PartnerId];
            return new ContractRow(c.Id, c.Code, c.Name, c.Kind, c.Category, c.PartnerId, p.Code, p.Name, c.Currency, c.EffectiveDate, c.ExpirationDate, c.TotalAmount, amount, pct, c.Status,
                c.Status == ContractStatus.Active && c.IsExpiredAt(Today), c.ExpirationDate.DayNumber - Today.DayNumber, c.Version);
        }).ToList();
    }

    private Task<string> NextCodeAsync(Guid companyId, InvoiceKind kind, DateOnly date) => Numbering.NextAsync(db, companyId, date, kind == InvoiceKind.Sales ? "CTR-SAL" : "CTR-PUR");
}

/// <summary>Kiểm soát đơn kế thừa hợp đồng (giai đoạn 3–4): hợp đồng đang hiệu lực, chưa hết hạn, đúng đối tác và loại, vật tư có trong hợp đồng, không vượt số lượng / giá trị cam kết còn lại.</summary>
public static class ContractControl
{
    public static async Task ValidateCallOffAsync(HakDbContext db, Guid companyId, Guid contractId, InvoiceKind kind, Guid partnerId, DateOnly orderDate, IReadOnlyList<OrderLineInput> lines, Guid? excludeOrderId = null)
    {
        var c = await db.Contracts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == contractId && x.CompanyId == companyId)
                ?? throw new DomainException(ErrorCodes.NotFound, "Hợp đồng không tồn tại.");
        if (c.Status != ContractStatus.Active) throw new DomainException(ErrorCodes.InvalidStateTransition, $"Hợp đồng {c.Code} chưa hiệu lực hoặc đã đóng ({c.Status}).");
        if (c.Kind != kind) throw new DomainException(ErrorCodes.ValidationFailed, $"Hợp đồng {c.Code} là hợp đồng {(c.Kind == InvoiceKind.Sales ? "bán" : "mua")}, không dùng cho loại đơn này.");
        if (c.PartnerId != partnerId) throw new DomainException(ErrorCodes.ValidationFailed, $"Đối tác của đơn khác đối tác của hợp đồng {c.Code}.");
        if (orderDate < c.EffectiveDate || orderDate > c.ExpirationDate)
            throw new DomainException(ErrorCodes.ContractExpired, $"Ngày đơn {orderDate:dd/MM/yyyy} nằm ngoài hiệu lực hợp đồng {c.Code} ({c.EffectiveDate:dd/MM/yyyy} – {c.ExpirationDate:dd/MM/yyyy}).");
        var prior = await (from l in db.TradeOrderLines join o in db.TradeOrders on l.OrderId equals o.Id
                           where o.ContractId == contractId && o.Status != OrderStatus.Cancelled && o.Id != excludeOrderId select new { l.ItemId, l.Quantity, l.UnitPrice }).AsNoTracking().ToListAsync();
        var thisQty = lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var itemId in thisQty.Keys)
            if (c.Category != ContractCategory.Value && c.Lines.All(l => l.ItemId != itemId))
                throw new DomainException(ErrorCodes.ValidationFailed, $"Vật tư không có trong danh mục cam kết của hợp đồng {c.Code}.");
        if (c.Category == ContractCategory.Quantity)
            foreach (var l in c.Lines)
            {
                var used = prior.Where(p => p.ItemId == l.ItemId).Sum(p => p.Quantity) + thisQty.GetValueOrDefault(l.ItemId);
                if (used > l.CommittedQty) throw new DomainException(ErrorCodes.ContractLimitExceeded,
                    $"Vượt số lượng cam kết của hợp đồng {c.Code}: đã đặt (cả đơn này) {used.Vn()} / cam kết {l.CommittedQty.Vn()}.");
            }
        else if (c.Category == ContractCategory.Value)
        {
            var used = prior.Sum(p => p.Quantity * p.UnitPrice) + lines.Sum(l => l.Quantity * l.UnitPrice);
            if (used > c.TotalAmount) throw new DomainException(ErrorCodes.ContractLimitExceeded, $"Vượt giá trị cam kết của hợp đồng {c.Code}: {used.Vn(0)} / {c.TotalAmount.Vn(0)}.");
        }
    }
}
