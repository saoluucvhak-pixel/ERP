namespace Hak.Domain.Reports;

public sealed record RatioRow(string Group, string Name, string Unit, decimal? Previous, decimal? Current, string Formula);

/// <summary>
/// Các chỉ số phân tích tài chính tính từ số liệu của Bảng cân đối kế toán (B01) và Báo cáo kết quả kinh doanh (B02) theo mã số chỉ tiêu của mẫu
/// đã nhập (không tự lấy tài khoản). Mã số lấy từ mẫu TT99 B01-DN, B02-DN của công ty [CẦN XÁC MINH khi chị xác minh mẫu]. Chỉ số nào mẫu số bằng 0
/// hoặc thiếu số liệu thì để trống, không báo 0. Không có cột "tiêu chuẩn": ngưỡng tham chiếu do doanh nghiệp tự quyết định.
/// </summary>
public static class FinancialRatios
{
    /// <summary>b01: số liệu cuối kỳ theo mã số của B01; b02: số phát sinh kỳ theo mã số của B02; days: số ngày của kỳ (cho vòng quay).</summary>
    public static IReadOnlyList<RatioRow> Compute(Func<string, decimal?> b01Prev, Func<string, decimal?> b01Cur, Func<string, decimal?> b02Prev, Func<string, decimal?> b02Cur, int daysPrev, int daysCur)
    {
        static decimal? Div(decimal? a, decimal? b, decimal scale = 1) => a is null || b is null or 0 ? null : Math.Round(a.Value / b.Value * scale, 2, MidpointRounding.AwayFromZero);
        decimal? Sum(params decimal?[] v) => v.Any(x => x is null) ? null : v.Sum(x => x!.Value);
        RatioRow Row(string group, string name, string unit, string formula, Func<Func<string, decimal?>, Func<string, decimal?>, int, decimal?> f)
            => new(group, name, unit, f(b01Prev, b02Prev, daysPrev), f(b01Cur, b02Cur, daysCur), formula);

        const string G1 = "Cơ cấu tài chính và cơ cấu tài sản", G2 = "Hệ số thanh toán", G3 = "Khả năng hoạt động", G4 = "Khả năng sinh lời";
        return
        [
            Row(G1, "Hệ số nợ", "%", "Nợ phải trả (300) ÷ Tổng tài sản (280)", (b, _, _) => Div(b("300"), b("280"), 100)),
            Row(G1, "Hệ số vốn tự có", "%", "Vốn chủ sở hữu (400) ÷ Tổng tài sản (280)", (b, _, _) => Div(b("400"), b("280"), 100)),
            Row(G1, "Hệ số nợ trên vốn chủ sở hữu", "%", "Nợ phải trả (300) ÷ Vốn chủ sở hữu (400)", (b, _, _) => Div(b("300"), b("400"), 100)),
            Row(G1, "Hệ số nợ dài hạn trên tổng tài sản", "%", "Nợ dài hạn (330) ÷ Tổng tài sản (280)", (b, _, _) => Div(b("330"), b("280"), 100)),
            Row(G2, "Hệ số khả năng thanh toán hiện hành", "Lần", "Tài sản ngắn hạn (100) ÷ Nợ ngắn hạn (310)", (b, _, _) => Div(b("100"), b("310"))),
            Row(G2, "Hệ số khả năng thanh toán nhanh", "Lần", "(Tài sản ngắn hạn (100) − Hàng tồn kho (140)) ÷ Nợ ngắn hạn (310)", (b, _, _) => Div(b("100") - b("140"), b("310"))),
            Row(G2, "Hệ số khả năng thanh toán tức thời", "Lần", "Tiền và tương đương tiền (110) ÷ Nợ ngắn hạn (310)", (b, _, _) => Div(b("110"), b("310"))),
            Row(G2, "Hệ số khả năng thanh toán lãi vay", "Lần", "(Lợi nhuận trước thuế (50) + Chi phí đi vay (24)) ÷ Chi phí đi vay (24)", (_, p, _) => Div(Sum(p("50"), p("24")), p("24"))),
            Row(G3, "Vòng quay hàng tồn kho", "Vòng", "Giá vốn hàng bán (11) ÷ Hàng tồn kho cuối kỳ (140), theo kỳ đã chọn", (b, p, _) => Div(p("11"), b("140"))),
            Row(G3, "Số ngày lưu kho", "Ngày", "Số ngày của kỳ ÷ Vòng quay hàng tồn kho", (b, p, d) => Div(d, Div(p("11"), b("140")))),
            Row(G3, "Vòng quay khoản phải thu", "Vòng", "Doanh thu thuần (10) ÷ Phải thu ngắn hạn (130), theo kỳ đã chọn", (b, p, _) => Div(p("10"), b("130"))),
            Row(G3, "Kỳ thu tiền bình quân", "Ngày", "Số ngày của kỳ ÷ Vòng quay khoản phải thu", (b, p, d) => Div(d, Div(p("10"), b("130")))),
            Row(G4, "Tỷ suất lợi nhuận gộp trên doanh thu", "%", "Lợi nhuận gộp (20) ÷ Doanh thu thuần (10)", (_, p, _) => Div(p("20"), p("10"), 100)),
            Row(G4, "Tỷ suất lợi nhuận sau thuế trên doanh thu (ROS)", "%", "Lợi nhuận sau thuế (60) ÷ Doanh thu thuần (10)", (_, p, _) => Div(p("60"), p("10"), 100)),
            Row(G4, "Tỷ suất lợi nhuận sau thuế trên tổng tài sản (ROA)", "%", "Lợi nhuận sau thuế (60) ÷ Tổng tài sản cuối kỳ (280)", (b, p, _) => Div(p("60"), b("280"), 100)),
            Row(G4, "Tỷ suất lợi nhuận sau thuế trên vốn chủ sở hữu (ROE)", "%", "Lợi nhuận sau thuế (60) ÷ Vốn chủ sở hữu cuối kỳ (400)", (b, p, _) => Div(p("60"), b("400"), 100)),
        ];
    }
}
