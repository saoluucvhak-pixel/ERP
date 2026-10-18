using Hak.Application.Common;
using Hak.Application.Inventory;
using Hak.Application.Security;
using Hak.Domain.Catalog;
using Hak.Domain.Common;
using Hak.Domain.Inventory;
using Hak.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Setup;

public sealed record ItemImportRow(int Line, string? Code, string? Name, string? Unit = null, string? InventoryAccount = null);

public sealed record OpeningStockRow(int Line, string? Warehouse, string? Item, string? Quantity, string? Value, string? Description = null);

public sealed partial class CatalogImportService
{
    /// <summary>1234567.5 → "1.234.567,5" for messages (the server runs with invariant globalization).</summary>
    internal static string VietnameseNumber(decimal v) =>
        string.Concat(v.ToString("#,0.####", System.Globalization.CultureInfo.InvariantCulture).Select(c => c == ',' ? '.' : c == '.' ? ',' : c));

    /// <summary>Item catalogue (vật tư, hàng hóa): code, name, unit, inventory account. Existing codes are skipped.</summary>
    public async Task<ImportResult> ImportItemsAsync(SessionContext ctx, IReadOnlyList<ItemImportRow> rows, bool commit)
    {
        ctx.Require(Permissions.CatalogManage);
        RequireSize(rows.Count);
        var issues = new List<ImportIssue>();
        var notes = new List<string>();
        var existing = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Code, i => i.Name, StringComparer.Ordinal);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == ctx.CompanyId).ToDictionaryAsync(a => a.Code, StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var valid = new List<(int Line, string Code, string Name, string Unit, string Account)>();
        var skipped = 0;
        foreach (var r in rows)
        {
            var code = Norm(r.Code)?.ToUpperInvariant();
            var name = Norm(r.Name);
            if (code is null) { issues.Add(new(r.Line, "Mã", "Thiếu mã vật tư.")); continue; }
            if (!SetupService.IsValidCode(code)) { issues.Add(new(r.Line, "Mã", $"Mã '{code}' không hợp lệ (chỉ A–Z, 0–9, _ . -; tối đa 30 ký tự).")); continue; }
            if (seen.TryGetValue(code, out var first)) { issues.Add(new(r.Line, "Mã", $"Mã {code} trùng với dòng {first}.")); continue; }
            seen[code] = r.Line;
            if (existing.TryGetValue(code, out var oldName))
            {
                skipped++;
                if (name is not null && name != oldName) notes.Add($"Dòng {r.Line}: vật tư {code} đã có tên '{oldName}' — giữ nguyên, không ghi đè.");
                continue;
            }
            var before = issues.Count;
            if (name is null) issues.Add(new(r.Line, "Tên", "Thiếu tên vật tư."));
            var unit = Norm(r.Unit);
            if (unit is null) issues.Add(new(r.Line, "Đơn vị tính", "Thiếu đơn vị tính."));
            var accCode = Norm(r.InventoryAccount)?.ToUpperInvariant();
            if (accCode is null) issues.Add(new(r.Line, "TK kho", "Thiếu tài khoản hàng tồn kho."));
            else if (!accounts.TryGetValue(accCode, out var acc)) issues.Add(new(r.Line, "TK kho", $"TK {accCode} không tồn tại."));
            else if (!acc.IsPostable || acc.CashKind != CashKind.None || acc.OpenItemNature != OpenItemNature.None || acc.RequiresPartner)
                issues.Add(new(r.Line, "TK kho", $"TK {accCode} không dùng làm TK hàng tồn kho (phải là TK chi tiết, không phải tiền/công nợ)."));
            if (issues.Count == before) valid.Add((r.Line, code, name!, unit!, accCode!));
        }
        var result = new ImportResult(false, valid.Count, skipped, Sorted(issues), notes);
        if (!commit || issues.Count > 0 || valid.Count == 0) return result;
        await db.InTransactionAsync(async () =>
        {
            foreach (var v in valid) await WithLine(v.Line, () => stock.CreateItemAsync(ctx, v.Code, v.Name, v.Unit, v.Account));
            await audit.AppendAsync(ctx.CompanyId, ctx, "import.items", "company", ctx.CompanyId.ToString(), new { created = valid.Count, skipped });
            await db.SaveChangesAsync();
        });
        return result with { Committed = true };
    }

    /// <summary>
    /// Opening stock: one draft receipt per warehouse, Dr each item's inventory account / Cr <paramref name="counterAccountCode"/>
    /// (an opening clearing account that the opening-balance voucher debits instead of the inventory accounts, so it nets to
    /// zero and inventory is not counted twice). The receipts are posted through the normal stock workflow.
    /// </summary>
    public async Task<ImportResult> ImportOpeningStockAsync(SessionContext ctx, DateOnly date, string counterAccountCode, string? description,
        IReadOnlyList<OpeningStockRow> rows, bool commit)
    {
        ctx.Require(Permissions.StockDocument);
        RequireSize(rows.Count);
        var issues = new List<ImportIssue>();
        var notes = new List<string>();
        var counterCode = counterAccountCode.Trim().ToUpperInvariant();
        var counter = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == ctx.CompanyId && a.Code == counterCode);
        if (counter is null) issues.Add(new(0, "TK đối ứng", $"TK {counterCode} không tồn tại."));
        else if (!counter.IsPostable || !counter.IsActive || counter.RequiresPartner)
            issues.Add(new(0, "TK đối ứng", $"TK {counterCode} phải là TK chi tiết đang dùng, không theo dõi đối tượng."));
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == ctx.CompanyId).ToDictionaryAsync(w => w.Code, StringComparer.Ordinal);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == ctx.CompanyId).ToDictionaryAsync(i => i.Code, StringComparer.Ordinal);
        var functional = await db.Companies.Where(c => c.Id == ctx.CompanyId).Select(c => c.FunctionalCurrency).FirstAsync();
        var dp = await db.Currencies.Where(c => c.Code == functional).Select(c => c.DecimalPlaces).FirstAsync();
        var seen = new Dictionary<(string, string), int>();
        var valid = new List<(int Line, Warehouse Wh, Item Item, decimal Qty, decimal Value, string? Description)>();
        foreach (var r in rows)
        {
            var before = issues.Count;
            var whCode = Norm(r.Warehouse)?.ToUpperInvariant();
            var itemCode = Norm(r.Item)?.ToUpperInvariant();
            Warehouse? wh = null;
            Item? item = null;
            if (whCode is null) issues.Add(new(r.Line, "Kho", "Thiếu mã kho."));
            else if (!warehouses.TryGetValue(whCode, out wh)) issues.Add(new(r.Line, "Kho", $"Kho {whCode} không tồn tại."));
            if (itemCode is null) issues.Add(new(r.Line, "Vật tư", "Thiếu mã vật tư."));
            else if (!items.TryGetValue(itemCode, out item)) issues.Add(new(r.Line, "Vật tư", $"Vật tư {itemCode} không tồn tại (nhập danh mục vật tư trước)."));
            else if (!item.IsActive) issues.Add(new(r.Line, "Vật tư", $"Vật tư {itemCode} đã ngừng sử dụng."));
            if (whCode is not null && itemCode is not null)
            {
                if (seen.TryGetValue((whCode, itemCode), out var first)) issues.Add(new(r.Line, "Vật tư", $"{itemCode} tại kho {whCode} trùng với dòng {first}; gộp thành một dòng."));
                else seen[(whCode, itemCode)] = r.Line;
            }
            var parseBefore = issues.Count;
            var qty = Amount(r.Quantity, r.Line, "Số lượng", issues);
            var value = Amount(r.Value, r.Line, "Giá trị", issues);
            if (issues.Count == parseBefore)
            {
                if (qty <= 0 || !MoneyMath.HasAtMostDecimals(qty, MoneyMath.StorageScale)) issues.Add(new(r.Line, "Số lượng", "Số lượng phải lớn hơn 0, tối đa 4 chữ số thập phân."));
                if (value < 0 || !MoneyMath.HasAtMostDecimals(value, dp)) issues.Add(new(r.Line, "Giá trị", $"Giá trị ≥ 0, tối đa {dp} chữ số thập phân."));
            }
            if (issues.Count == before) valid.Add((r.Line, wh!, item!, qty, value, Norm(r.Description)));
        }
        if (valid.Count == 0 && issues.Count == 0) issues.Add(new(0, "Tổng", "Không có dòng tồn kho nào."));
        var whIds = valid.Select(v => v.Wh.Id).Distinct().ToList();
        var busy = await db.StockMovements.Where(m => m.CompanyId == ctx.CompanyId && whIds.Contains(m.WarehouseId)).Select(m => m.WarehouseId).Distinct().ToListAsync();
        foreach (var w in valid.Select(v => v.Wh).DistinctBy(w => w.Id).Where(w => busy.Contains(w.Id)))
            notes.Add($"Kho {w.Code} đã có phát sinh nhập/xuất — kiểm tra để tránh nhập trùng tồn đầu kỳ.");
        var total = valid.Sum(v => v.Value);
        notes.Add($"Tổng giá trị tồn đầu kỳ {VietnameseNumber(total)}:chứng từ số dư đầu kỳ (OPB) phải ghi Nợ TK {counterCode} đúng số này thay cho các TK hàng tồn kho.");
        var result = new ImportResult(false, valid.Count, 0, Sorted(issues), notes, TotalDebit: total, TotalCredit: total);
        if (!commit || issues.Count > 0) return result;

        var docs = new List<string>();
        await db.InTransactionAsync(async () =>
        {
            foreach (var g in valid.GroupBy(v => v.Wh.Id))
            {
                var wh = g.First().Wh;
                var doc = await stock.CreateDocumentAsync(ctx, new StockDocumentInput(StockDocumentType.Receipt, wh.Id, date, date,
                    Norm(description) ?? $"Tồn kho đầu kỳ {wh.Code} ngày {date:dd/MM/yyyy}",
                    g.Select(v => new StockLineInput(v.Item.Id, v.Qty, v.Value, counter!.Id, v.Description ?? "Tồn đầu kỳ")).ToList()));
                docs.Add(doc.Number);
            }
            await audit.AppendAsync(ctx.CompanyId, ctx, "import.opening_stock", "company", ctx.CompanyId.ToString(), new { date, counterCode, documents = docs, total });
            await db.SaveChangesAsync();
        });
        return result with { Committed = true, Documents = docs, VoucherNumber = string.Join(", ", docs) };
    }
}
