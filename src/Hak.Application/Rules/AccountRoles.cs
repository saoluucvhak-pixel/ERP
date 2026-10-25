namespace Hak.Application.Rules;

/// <summary>Optional roles fall back to another role when unmapped (payroll expense groups → PAYROLL_EXPENSE_GENERAL).</summary>
public sealed record AccountRoleInfo(string Code, string Module, string Description, bool Optional = false);

/// <summary>
/// Every account role the modules book through. Roles keep legal account codes out of the program: each company maps a role
/// to an account of its own chart (verified against the regime by the chief accountant). Used by the setup screen to show
/// what still has to be mapped; a module refuses to post with RULE_ROLE_UNMAPPED when a role it needs is missing.
/// </summary>
public static class AccountRoles
{
    public static readonly IReadOnlyList<AccountRoleInfo> Known =
    [
        new("AR_TRADE", "Bán hàng / công nợ", "Phải thu khách hàng (hóa đơn bán, thu tiền)"),
        new("AP_TRADE", "Mua hàng / công nợ", "Phải trả người bán (hóa đơn mua, trả tiền)"),
        new("BANK_PAYMENT_DEFAULT", "Thanh toán", "Tài khoản ngân hàng mặc định để chu trình thanh toán TỰ tạo lệnh thanh toán Nháp khi nhận bàn giao từ Cung ứng (không ánh xạ thì lệnh chờ trong hộp thư, người thanh toán chọn tài khoản)", Optional: true),
        new("CASH_DEFAULT", "Thu chi", "Tài khoản tiền mặc định của quy tắc định khoản thu/chi"),
        new("GRNI", "Mua hàng / kho", "Hàng về chưa có hóa đơn (đối ứng phiếu nhập chờ hóa đơn, xuất trả nhà cung cấp)"),
        new("VAT_INPUT", "Thuế GTGT", "Thuế GTGT đầu vào được khấu trừ"),
        new("VAT_OUTPUT", "Thuế GTGT", "Thuế GTGT đầu ra phải nộp"),
        new("SALES_REVENUE", "Bán hàng", "Doanh thu bán hàng (mặc định khi dòng hóa đơn không chọn TK)"),
        new("SALES_RETURNS", "Bán hàng", "Hàng bán bị trả lại"),
        new("SALES_DISCOUNT", "Bán hàng", "Giảm giá hàng bán (và chiết khấu thương mại nếu chưa tách tài khoản riêng)"),
        new("SALES_TRADE_DISCOUNT", "Bán hàng", "Chiết khấu thương mại (giảm trừ doanh thu) — Nợ khi điều chỉnh giảm loại chiết khấu thương mại sau bán; để trống thì dùng SALES_DISCOUNT", Optional: true),
        new("REBATE_PAYABLE", "Bán hàng", "Phải trả chiết khấu thương mại theo doanh số đã trích trước (chiều bán; theo đối tượng) — Có khi trích trước, Nợ khi quyết toán bù trừ công nợ hoặc hoàn nhập", Optional: true),
        new("COGS", "Kho / giá thành", "Giá vốn: chênh lệch giá mua phần đã xuất, chênh lệch trả hàng mua, chênh lệch giá thành phần đã xuất"),
        new("ADVANCE", "Tiền", "Tạm ứng cho nhân viên (theo từng người, theo dõi từng khoản để bù trừ khi quyết toán): phải theo dõi theo đối tượng và công nợ phải thu"),
        new("CASH_SHORTAGE", "Tiền", "Tài sản thiếu chờ xử lý — đối ứng xử lý chênh lệch thiếu khi kiểm kê quỹ", Optional: true),
        new("CASH_SURPLUS", "Tiền", "Tài sản thừa chờ xử lý — đối ứng xử lý chênh lệch thừa khi kiểm kê quỹ", Optional: true),
        new("CAPITAL_CONTRIBUTED", "Vốn chủ sở hữu", "Vốn góp của chủ sở hữu (ghi nhận góp vốn: Nợ tiền / Có vốn góp; rút vốn ngược lại)"),
        new("DIVIDEND_SOURCE", "Vốn chủ sở hữu", "Lợi nhuận sau thuế chưa phân phối — nguồn chia cổ tức, lợi nhuận (Nợ khi ghi nhận cổ tức phải trả)"),
        new("DIVIDEND_PAYABLE", "Vốn chủ sở hữu", "Cổ tức, lợi nhuận phải trả cổ đông / thành viên (theo từng người, theo dõi từng khoản để bù trừ khi chi)"),
        new("DIVIDEND_PIT_PAYABLE", "Vốn chủ sở hữu", "Thuế TNCN khấu trừ trên cổ tức phải nộp (khi có cổ đông cá nhân)", Optional: true),
        new("BAD_DEBT_PROVISION", "Bán hàng", "Dự phòng phải thu khó đòi — Nợ khi xóa nợ phần đã trích dự phòng", Optional: true),
        new("BAD_DEBT_EXPENSE", "Bán hàng", "Chi phí xóa nợ phải thu không có dự phòng — Nợ khi xóa nợ phần còn lại", Optional: true),
        new("PAYMENT_DISCOUNT_INCOME", "Mua hàng", "Chiết khấu thanh toán được hưởng (doanh thu hoạt động tài chính) — Có khi trả tiền nhà cung cấp trong hạn chiết khấu", Optional: true),
        new("PAYMENT_DISCOUNT_EXPENSE", "Bán hàng", "Chiết khấu thanh toán cho khách hàng (chi phí tài chính) — Nợ khi khách trả tiền trong hạn chiết khấu", Optional: true),
        new("BANK_FEE", "Ngân hàng", "Chi phí phí ngân hàng — chỉ dùng để GỢI Ý TK khi tạo chứng từ từ dòng sao kê", Optional: true),
        new("BANK_INTEREST_INCOME", "Ngân hàng", "Lãi tiền gửi — chỉ dùng để GỢI Ý TK khi tạo chứng từ từ dòng sao kê", Optional: true),
        new("ASSET_COST_DEFAULT", "TSCĐ / CCDC", "Tài khoản nguyên giá TSCĐ mặc định khi lập thẻ mới (điền sẵn, vẫn đổi được)", Optional: true),
        new("ASSET_ACCUMULATED_DEFAULT", "TSCĐ / CCDC", "Tài khoản hao mòn lũy kế TSCĐ mặc định khi lập thẻ mới", Optional: true),
        new("ASSET_EXPENSE_DEFAULT", "TSCĐ / CCDC", "Tài khoản chi phí khấu hao TSCĐ mặc định khi lập thẻ mới", Optional: true),
        new("TOOL_COST_DEFAULT", "TSCĐ / CCDC", "Tài khoản CCDC / chi phí trả trước mặc định khi lập thẻ CCDC (242, 153…)", Optional: true),
        new("TOOL_EXPENSE_DEFAULT", "TSCĐ / CCDC", "Tài khoản chi phí phân bổ CCDC mặc định khi lập thẻ CCDC", Optional: true),
        new("FX_REVALUATION", "Ngoại tệ", "Chênh lệch tỷ giá do đánh giá lại cuối kỳ"),
        new("FX_REALIZED_GAIN", "Ngoại tệ", "Lãi chênh lệch tỷ giá đã thực hiện (thu/chi ngoại tệ theo hóa đơn)"),
        new("FX_REALIZED_LOSS", "Ngoại tệ", "Lỗ chênh lệch tỷ giá đã thực hiện"),
        new("PROD_LABOR_APPLIED", "Sản xuất", "Chi phí nhân công trực tiếp phân bổ cho lệnh sản xuất theo giờ công (đối ứng Nợ TK dở dang); chỉ cần khi lệnh có báo công", Optional: true),
        new("PROD_OVERHEAD_APPLIED", "Sản xuất", "Chi phí máy và sản xuất chung phân bổ cho lệnh sản xuất theo giờ máy (đối ứng Nợ TK dở dang); chỉ cần khi lệnh có báo công", Optional: true),
        new("PAYROLL_EXPENSE_GENERAL", "Lương", "Chi phí lương — mặc định cho mọi nhóm chưa có vai trò riêng"),
        new("PAYROLL_EXPENSE_PRODUCTION", "Lương", "Chi phí lương nhóm sản xuất (không ánh xạ thì dùng GENERAL)", Optional: true),
        new("PAYROLL_EXPENSE_SELLING", "Lương", "Chi phí lương nhóm bán hàng (không ánh xạ thì dùng GENERAL)", Optional: true),
        new("PAYROLL_EXPENSE_ADMIN", "Lương", "Chi phí lương nhóm quản lý (không ánh xạ thì dùng GENERAL)", Optional: true),
        new("PAYROLL_PAYABLE", "Lương", "Phải trả người lao động (thực lĩnh, theo từng người)"),
        new("PAYROLL_SI_PAYABLE", "Lương", "Bảo hiểm xã hội phải nộp (phần NLĐ + DN)"),
        new("PAYROLL_HI_PAYABLE", "Lương", "Bảo hiểm y tế phải nộp"),
        new("PAYROLL_UI_PAYABLE", "Lương", "Bảo hiểm thất nghiệp phải nộp"),
        new("PAYROLL_PIT_PAYABLE", "Lương", "Thuế TNCN khấu trừ phải nộp"),
        new("PAYROLL_UNION_PAYABLE", "Lương", "Kinh phí công đoàn phải nộp"),
        new("PAYROLL_OTHER_DEDUCTION", "Lương", "Các khoản khấu trừ khác từ lương"),
    ];
}
