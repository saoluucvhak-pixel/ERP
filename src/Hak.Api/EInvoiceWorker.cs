using Hak.Application.EInvoice;
using Hak.Application.Security;
using Hak.Domain.Common;

namespace Hak.Api;

/// <summary>
/// Dịch vụ nền gửi lại các lệnh phát hành hóa đơn điện tử đang chờ (sau lỗi mạng), theo thời điểm gửi lại của hàng đợi. Chỉ chạy khi đã cấu hình nhà cung cấp.
/// An toàn khi chạy lặp hoặc chạy cùng nút "Gửi các lệnh đang chờ": mọi lần gửi dùng cùng khóa chống trùng, nhà cung cấp trả lại kết quả cũ nếu đã nhận.
/// Kết quả được ghi sổ bằng quyền hiện tại của NGƯỜI đã yêu cầu phát hành (mất quyền ⇒ hóa đơn ở trạng thái "đã phát hành, chưa ghi sổ" chờ xử lý).
/// Chu kỳ: Hak:EInvoice:WorkerSeconds (mặc định 60; 0 = tắt).
/// </summary>
public sealed class EInvoiceWorker(IServiceScopeFactory scopes, IEInvoiceProvider provider, IConfiguration config, ILogger<EInvoiceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var seconds = int.TryParse(config["Hak:EInvoice:WorkerSeconds"], out var s) ? s : 60;
        if (seconds <= 0 || !provider.Configured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
        {
            try { await RunOnceAsync(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Dịch vụ nền hóa đơn điện tử gặp lỗi; thử lại ở chu kỳ sau."); }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<EInvoiceIssueService>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var due = await svc.DueForWorkerAsync();
        var n = 0;
        foreach (var (companyId, documentId, requestedBy) in due)
        {
            ct.ThrowIfCancellationRequested();
            try { await svc.DispatchOneAsync(await auth.ResolveContextAsync(requestedBy, companyId), documentId); n++; }
            catch (DomainException ex) { log.LogWarning("Hóa đơn điện tử {Document}: {Message}", documentId, ex.Message); }
        }
        return n;
    }
}
