"""Builds the numeric parts of Mẫu B09-DN (Thuyết minh BCTC, TT 99/2025 Phụ lục IV, Công báo 1577+1578 tr. B09 mục V, VII, VIII)
as HAK report-engine templates. Item names are copied from the form; amounts that the ledger cannot know (fair value,
recoverable value, related parties, overdue debts, share counts…) are left to the narrative note (mau-B09-DN-thuyet-minh.md).

Usage: python b09.py <output dir>"""
import json, os, sys

out = sys.argv[1]
LEGAL = "Mẫu số B 09 - DN, Phụ lục IV TT 99/2025/TT-BTC (Công báo 1577+1578); hướng dẫn lập: Phần E (Công báo 1581+1582)"
CASH_P = "111*, 112*, 113*"

def line(code, name, formula=None, indent=1, bold=False, note=None):
    return {"code": code, "name": name, "formula": formula, "indent": indent, "bold": bold, "note": note}

def head(code, name, indent=0):
    return line(code, name, None, indent, True)

def total(code, name, parts, indent=1):
    return line(code, name, " + ".join(f"LINE({p})" for p in parts), indent, True)

def save(code, name, file, lines, columns=None, checks=(), note=""):
    t = {"code": code, "name": name, "category": "BCTC", "legalReference": LEGAL + (f"; {note}" if note else ""),
         "columns": columns, "lines": lines, "checks": list(checks)}
    codes = [l["code"] for l in lines]
    assert len(codes) == len(set(codes)), f"{code}: trùng mã {[c for c in codes if codes.count(c) > 1]}"
    with open(os.path.join(out, file), "w", encoding="utf-8") as f:
        json.dump(t, f, ensure_ascii=False, indent=1)
    print(code, len(lines), "dòng", f"{len(columns)} cột" if columns else "")

# ------------------------------------------------------------------ V. Số dư (Cuối năm | Đầu năm)
def bal(side, pats):                       # closing | opening, netted per account
    return f"BAL_{side}({pats}) | OPEN_{side}({pats})"

def gross(side, pats):                     # per account and partner (dual-nature accounts)
    return f"GROSS_{side}({pats}) | OPEN_GROSS_{side}({pats})"

V = [
    head("V1", "1. Tiền và các khoản tương đương tiền (không bị hạn chế sử dụng)"),
    line("V1.1", "- Tiền mặt", bal("D", "111*[!HCSD]")),
    line("V1.2", "- Tiền gửi không kỳ hạn", bal("D", "112*[!HCSD]")),
    line("V1.3", "- Tiền đang chuyển", bal("D", "113*[!HCSD]")),
    line("V1.4", "- Tương đương tiền", bal("D", "1281*[TDT,!HCSD], 1288*[TDT,!HCSD]")),
    total("V1.9", "Cộng", ["V1.1", "V1.2", "V1.3", "V1.4"]),
    head("V2", "2. Các khoản đầu tư tài chính (giá gốc; giá trị hợp lý, giá trị có thể thu hồi: thuyết minh)"),
    line("V2.A", "a) Chứng khoán kinh doanh — giá gốc", bal("D", "121*"), bold=True),
    line("V2.A1", "   Giá trị dự phòng", bal("C", "2291*"), indent=2),
    line("V2.B", "b) Đầu tư nắm giữ đến ngày đáo hạn — giá gốc", "LINE(V2.B1) + LINE(V2.B2)", bold=True),
    line("V2.B1", "- Ngắn hạn", bal("D", "128*[!TDT,!DH]"), indent=2),
    line("V2.B2", "- Dài hạn", bal("D", "128*[DH,!TDT]"), indent=2),
    line("V2.B3", "+ Tiền gửi có kỳ hạn", bal("D", "1281*[!TDT]"), indent=3),
    line("V2.B4", "+ Trái phiếu", bal("D", "1282*"), indent=3),
    line("V2.B5", "+ Cho vay", bal("D", "1283*"), indent=3),
    line("V2.B6", "+ Các khoản đầu tư khác", bal("D", "1288*[!TDT]"), indent=3),
    line("V2.B7", "   Giá trị dự phòng", bal("C", "2292*[HTM]"), indent=2),
    line("V2.C", "c) Đầu tư góp vốn vào đơn vị khác — giá gốc", "LINE(V2.C1) + LINE(V2.C2) + LINE(V2.C3)", bold=True),
    line("V2.C1", "- Đầu tư vào công ty con", bal("D", "221*"), indent=2),
    line("V2.C2", "- Đầu tư vào công ty liên doanh, liên kết", bal("D", "222*"), indent=2),
    line("V2.C3", "- Đầu tư vào đơn vị khác", bal("D", "2281*"), indent=2),
    line("V2.C4", "   Giá trị dự phòng", bal("C", "2292*[!HTM]"), indent=2),
    head("V3", "3. Phải thu của khách hàng (giá trị ghi sổ)"),
    line("V3.1", "a) Phải thu của khách hàng ngắn hạn", gross("D", "131*[!DH]")),
    line("V3.2", "b) Phải thu của khách hàng dài hạn", gross("D", "131*[DH]")),
    total("V3.9", "Cộng", ["V3.1", "V3.2"]),
    line("V3.D", "   Giá trị dự phòng phải thu khó đòi (2293)", bal("C", "2293*"), indent=2),
    head("V4", "4. Phải thu khác (giá trị ghi sổ)"),
    line("V4.A", "a) Ngắn hạn", "LINE(V4.A1) + LINE(V4.A2) + LINE(V4.A3)", bold=True),
    line("V4.A1", "- Phải thu của người lao động (tạm ứng, 334 dư Nợ)", gross("D", "141*[!DH], 334*[!DH]"), indent=2),
    line("V4.A2", "- Ký cược, ký quỹ", gross("D", "244*[!DH]"), indent=2),
    line("V4.A3", "- Phải thu khác (1388, 338 dư Nợ)", gross("D", "1388*[!DH], 338*[!DH]"), indent=2),
    line("V4.B", "b) Dài hạn", gross("D", "1388*[DH], 334*[DH], 338*[DH], 141*[DH], 244*[DH]"), bold=True),
    total("V4.9", "Cộng", ["V4.A", "V4.B"]),
    line("V5", "5. Tài sản thiếu chờ xử lý", bal("D", "1381*"), indent=0, bold=True),
    head("V7", "7. Hàng tồn kho (giá gốc)"),
    line("V7.1", "- Hàng mua đang đi đường", bal("D", "151*")),
    line("V7.2", "- Nguyên liệu, vật liệu", bal("D", "152*")),
    line("V7.3", "- Công cụ, dụng cụ", bal("D", "153*")),
    line("V7.4", "- Chi phí sản xuất kinh doanh dở dang", bal("D", "154*[!DH]")),
    line("V7.5", "- Sản phẩm", bal("D", "155*")),
    line("V7.6", "- Hàng hóa", bal("D", "156*")),
    line("V7.7", "- Hàng gửi đi bán", bal("D", "157*")),
    line("V7.8", "- Nguyên liệu, vật tư tại kho bảo thuế", bal("D", "158*")),
    total("V7.9", "Cộng giá gốc", [f"V7.{i}" for i in range(1, 9)]),
    line("V7.D", "   Dự phòng giảm giá hàng tồn kho (2294)", bal("C", "2294*"), indent=2),
    head("V8", "8. Tài sản dở dang dài hạn"),
    line("V8.A", "a) Chi phí sản xuất, kinh doanh dở dang dài hạn", bal("D", "154*[DH]")),
    line("V8.B", "b) Xây dựng cơ bản dở dang", "LINE(V8.B1) + LINE(V8.B2) + LINE(V8.B3) + LINE(V8.B4)", bold=True),
    line("V8.B1", "- Mua sắm", bal("D", "2411*"), indent=2),
    line("V8.B2", "- XDCB", bal("D", "2412*"), indent=2),
    line("V8.B3", "- Sửa chữa, bảo dưỡng định kỳ", bal("D", "2413*"), indent=2),
    line("V8.B4", "- Nâng cấp cải tạo TSCĐ", bal("D", "2414*"), indent=2),
    total("V8.9", "Cộng", ["V8.A", "V8.B"]),
    head("V14", "14. Chi phí chờ phân bổ"),
    line("V14.1", "a) Ngắn hạn", bal("D", "242*[!DH]")),
    line("V14.2", "b) Dài hạn", bal("D", "242*[DH]")),
    total("V14.9", "Cộng", ["V14.1", "V14.2"]),
    head("V17", "17. Phải trả người bán"),
    line("V17.1", "a) Phải trả người bán ngắn hạn", gross("C", "331*[!DH]")),
    line("V17.2", "b) Phải trả người bán dài hạn", gross("C", "331*[DH]")),
    total("V17.9", "Cộng", ["V17.1", "V17.2"]),
    line("V18", "18. Phải trả về cổ tức, lợi nhuận", bal("C", "332*"), indent=0, bold=True),
    head("V20", "20. Chi phí phải trả"),
    line("V20.1", "a) Ngắn hạn", bal("C", "335*[!DH]")),
    line("V20.2", "b) Dài hạn", bal("C", "335*[DH]")),
    total("V20.9", "Cộng", ["V20.1", "V20.2"]),
    head("V21", "21. Phải trả khác"),
    line("V21.A", "a) Ngắn hạn", "LINE(V21.A1) + LINE(V21.A2) + LINE(V21.A3) + LINE(V21.A4) + LINE(V21.A5) + LINE(V21.A6) + LINE(V21.A7)", bold=True),
    line("V21.A1", "- Tài sản thừa chờ giải quyết", bal("C", "3381*[!DH]"), indent=2),
    line("V21.A2", "- Kinh phí công đoàn", bal("C", "3382*[!DH]"), indent=2),
    line("V21.A3", "- Bảo hiểm xã hội", bal("C", "3383*[!DH]"), indent=2),
    line("V21.A4", "- Bảo hiểm y tế", bal("C", "3384*[!DH]"), indent=2),
    line("V21.A5", "- Bảo hiểm thất nghiệp", bal("C", "3386*[!DH]"), indent=2),
    line("V21.A6", "- Nhận ký quỹ, ký cược ngắn hạn", gross("C", "344*[!DH]"), indent=2),
    line("V21.A7", "- Các khoản phải trả, phải nộp khác", gross("C", "3388*[!DH], 138*[!DH]"), indent=2),
    line("V21.B", "b) Dài hạn", "LINE(V21.B1) + LINE(V21.B2)", bold=True),
    line("V21.B1", "- Nhận ký quỹ, ký cược dài hạn", gross("C", "344*[DH]"), indent=2),
    line("V21.B2", "- Các khoản phải trả, phải nộp khác", gross("C", "338*[DH], !3387*"), indent=2),
    head("V22", "22. Doanh thu chờ phân bổ"),
    line("V22.1", "a) Ngắn hạn", bal("C", "3387*[!DH]")),
    line("V22.2", "b) Dài hạn", bal("C", "3387*[DH]")),
    total("V22.9", "Cộng", ["V22.1", "V22.2"]),
    head("V23", "23. Trái phiếu phát hành"),
    line("V23.1", "23.1. Trái phiếu thường", bal("C", "3431*")),
    line("V23.2", "23.2. Trái phiếu chuyển đổi", bal("C", "3432*")),
    head("V26", "26. Tài sản thuế thu nhập hoãn lại và thuế thu nhập hoãn lại phải trả"),
    line("V26.A", "a) Tài sản thuế thu nhập hoãn lại", bal("D", "243*")),
    line("V26.B", "b) Thuế thu nhập hoãn lại phải trả", bal("C", "347*")),
    head("V27B", "27.b) Chi tiết vốn góp của chủ sở hữu"),
    line("V27B.9", "Vốn góp của chủ sở hữu (4111) — tách vốn công ty mẹ / đối tượng khác trong thuyết minh", bal("C", "4111*")),
]
save("B09-DN-V", "Thuyết minh BCTC — V. Số dư các khoản mục trên Báo cáo tình hình tài chính", "mau-B09-DN-V-so-du.json", V,
     ["Cuối năm", "Đầu năm"], note="mục V.1–V.27 phần số dư")

# ------------------------------------------------------------------ V. Biến động (Đầu năm | Tăng | Giảm | Cuối năm)
def mov(side, pats):                       # credit-nature (side C) or debit-nature (side D) account movement table
    inc, dec = ("PS_C", "PS_D") if side == "C" else ("PS_D", "PS_C")
    return f"OPEN_{side}({pats}) | {inc}({pats}) | {dec}({pats}) | BAL_{side}({pats})"

BD = [
    head("V16", "16. Vay và nợ thuê tài chính"),
    line("V16.A", "a) Vay ngắn hạn", mov("C", "3411*[!DH]")),
    line("V16.B", "b) Vay dài hạn", mov("C", "3411*[DH]")),
    line("V16.D", "d) Các khoản nợ thuê tài chính", mov("C", "3412*")),
    total("V16.9", "Cộng", ["V16.A", "V16.B", "V16.D"]),
    head("V25", "25. Dự phòng phải trả"),
    line("V25.A", "a) Ngắn hạn", mov("C", "352*[!DH]")),
    line("V25.B", "b) Dài hạn", mov("C", "352*[DH]")),
    total("V25.9", "Cộng", ["V25.A", "V25.B"]),
    head("V13", "13. Bất động sản đầu tư"),
    line("V13.1", "Nguyên giá", mov("D", "217*")),
    line("V13.2", "Giá trị hao mòn lũy kế", mov("C", "2147*")),
    line("V13.3", "Giá trị còn lại", "LINE(V13.1) - LINE(V13.2) | | | LINE(V13.1) - LINE(V13.2)"),   # only opening and closing
]
save("B09-DN-V-BD", "Thuyết minh BCTC — V. Biến động vay, dự phòng phải trả, bất động sản đầu tư", "mau-B09-DN-V-bien-dong.json", BD,
     ["Đầu năm", "Tăng trong năm", "Giảm trong năm", "Cuối năm"], note="mục V.13, V.16, V.25 (chuyển ngắn hạn ↔ dài hạn thể hiện ở cột tăng/giảm)")

# ------------------------------------------------------------------ V.19 Thuế (Đầu năm | Số phải nộp | Số đã thực nộp | Cuối năm)
TAXES = [("3331", "Thuế giá trị gia tăng"), ("3332", "Thuế tiêu thụ đặc biệt"), ("3333", "Thuế xuất, nhập khẩu"), ("3334", "Thuế thu nhập doanh nghiệp"),
         ("3335", "Thuế thu nhập cá nhân"), ("3336", "Thuế tài nguyên"), ("3337", "Thuế nhà đất, tiền thuê đất"),
         ("3338", "Thuế bảo vệ môi trường và các loại thuế khác"), ("3339", "Phí, lệ phí và các khoản phải nộp khác")]
def tax(acc):
    p = f"{acc}*"
    paid = f"MOV_D({p} ; {CASH_P}) - MOV_C({p} ; {CASH_P})"
    return f"-OPEN_NET({p}) | OPEN_NET({p}) - BAL_NET({p}) + {paid} | {paid} | -BAL_NET({p})"
TX = [head("V19", "19. Thuế và các khoản phải nộp nhà nước (số dương: phải nộp; số âm: phải thu)")]
TX += [line(f"V19.{a}", f"- {n}", tax(a)) for a, n in TAXES]
TX += [total("V19.9", "Cộng", [f"V19.{a}" for a, _ in TAXES])]
save("B09-DN-V19", "Thuyết minh BCTC — V.19 Thuế và các khoản phải nộp nhà nước", "mau-B09-DN-V19-thue.json", TX,
     ["Đầu năm", "Số phải nộp trong năm", "Số đã thực nộp trong năm", "Cuối năm"],
     note="mục V.19; trình bày thuần theo từng loại thuế; đã nộp = phát sinh Nợ 333 đối ứng TK tiền, phải nộp = phần còn lại của biến động")

# ------------------------------------------------------------------ V.9 – V.11 Tăng, giảm TSCĐ (cột theo nhóm tài sản — nhãn TK)
def fixed_assets(code, title, cost, acc, groups, rows_cost, rows_dep):
    cols = [g[1] for g in groups] + ["Tổng cộng"]
    pats = [f"[{g[0]}]" for g in groups] + [""]
    def per(fn):                           # one slot per column, the pattern filtered by the group's tag
        return " | ".join(fn(f"{cost}*{t}", f"{acc}*{t}") for t in pats)
    L = [head(code, title), head(f"{code}.NG", "Nguyên giá", 1)]
    L.append(line(f"{code}.NG0", "Số dư đầu năm", per(lambda c, a: f"OPEN_D({c})"), 2))
    for rc, name, fn in rows_cost:
        L.append(line(f"{code}.NG{rc}", name, per(fn), 3))
    inc = [rc for rc, _, _ in rows_cost if rc.startswith("T")]
    dec = [rc for rc, _, _ in rows_cost if rc.startswith("G")]
    L.append(line(f"{code}.NG9", "Số dư cuối năm", per(lambda c, a: f"BAL_D({c})"), 2, True))
    L.append(head(f"{code}.HM", "Giá trị hao mòn lũy kế", 1))
    L.append(line(f"{code}.HM0", "Số dư đầu năm", per(lambda c, a: f"OPEN_C({a})"), 2))
    for rc, name, fn in rows_dep:
        L.append(line(f"{code}.HM{rc}", name, per(fn), 3))
    L.append(line(f"{code}.HM9", "Số dư cuối năm", per(lambda c, a: f"BAL_C({a})"), 2, True))
    L.append(head(f"{code}.CL", "Giá trị còn lại", 1))
    L.append(line(f"{code}.CL0", "- Tại ngày đầu năm", f"LINE({code}.NG0) - LINE({code}.HM0)", 2))
    L.append(line(f"{code}.CL9", "- Tại ngày cuối năm", f"LINE({code}.NG9) - LINE({code}.HM9)", 2))
    checks = [{"rule": f"LINE({code}.NG9) = LINE({code}.NG0) + " + " + ".join(f"LINE({code}.NG{r})" for r in inc) + " - " + " - ".join(f"LINE({code}.NG{r})" for r in dec),
               "message": "Nguyên giá: đầu năm + tăng − giảm = cuối năm"},
              {"rule": f"LINE({code}.HM9) = LINE({code}.HM0) + " + " + ".join(f"LINE({code}.HM{r})" for r, _, _ in rows_dep if r.startswith("T"))
                       + " - " + " - ".join(f"LINE({code}.HM{r})" for r, _, _ in rows_dep if r.startswith("G")),
               "message": "Hao mòn: đầu năm + tăng − giảm = cuối năm"}]
    return L, cols, checks

CASH_PAY = "331*, 111*, 112*, 113*, 141*, 3388*, 341*"
HH_GROUPS = [("NHA", "Nhà cửa, vật kiến trúc"), ("MAY", "Máy móc, thiết bị"), ("PTVT", "Phương tiện vận tải, truyền dẫn"),
             ("CLN", "Cây lâu năm cho sản phẩm định kỳ"), ("KHAC", "TSCĐ hữu hình khác")]
hh, cols, checks = fixed_assets("V9", "9. Tăng, giảm tài sản cố định hữu hình", "211", "2141", HH_GROUPS,
    [("T1", "- Mua trong năm", lambda c, a: f"MOV_D({c} ; {CASH_PAY})"),
     ("T2", "- Đầu tư XDCB hoàn thành", lambda c, a: f"MOV_D({c} ; 241*)"),
     ("T3", "- Tăng khác", lambda c, a: f"PS_D({c}) - MOV_D({c} ; {CASH_PAY}) - MOV_D({c} ; 241*)"),
     ("G1", "- Chuyển sang bất động sản đầu tư", lambda c, a: f"MOV_C({c} ; 217*)"),
     ("G2", "- Thanh lý, nhượng bán", lambda c, a: f"VT_C({c} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_C({c}) - MOV_C({c} ; 217*) - VT_C({c} ; FAD)")],
    [("T1", "- Khấu hao trong năm", lambda c, a: f"PS_C({a})"),
     ("G1", "- Chuyển sang bất động sản đầu tư", lambda c, a: f"MOV_D({a} ; 2147*)"),
     ("G2", "- Thanh lý, nhượng bán", lambda c, a: f"VT_D({a} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_D({a}) - MOV_D({a} ; 2147*) - VT_D({a} ; FAD)")])
save("B09-DN-V9", "Thuyết minh BCTC — V.9 Tăng, giảm tài sản cố định hữu hình", "mau-B09-DN-V9-tscd-huu-hinh.json", hh, cols, checks,
     note="cột theo nhóm: TK chi tiết 211 và 2141 gắn nhãn NHA / MAY / PTVT / CLN / KHAC")

VH_GROUPS = [("QSDD", "Quyền sử dụng đất"), ("QTG", "Quyền tác giả"), ("SHCN", "Quyền sở hữu công nghiệp"), ("PM", "Phần mềm"), ("KHAC", "TSCĐ vô hình khác")]
vh, cols, checks = fixed_assets("V10", "10. Tăng, giảm tài sản cố định vô hình", "213", "2143", VH_GROUPS,
    [("T1", "- Mua trong năm", lambda c, a: f"MOV_D({c} ; {CASH_PAY})"),
     ("T2", "- Tạo ra từ nội bộ DN", lambda c, a: f"MOV_D({c} ; 241*)"),
     ("T3", "- Tăng khác", lambda c, a: f"PS_D({c}) - MOV_D({c} ; {CASH_PAY}) - MOV_D({c} ; 241*)"),
     ("G2", "- Thanh lý, nhượng bán", lambda c, a: f"VT_C({c} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_C({c}) - VT_C({c} ; FAD)")],
    [("T1", "- Khấu hao trong năm", lambda c, a: f"PS_C({a})"),
     ("G2", "- Thanh lý, nhượng bán", lambda c, a: f"VT_D({a} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_D({a}) - VT_D({a} ; FAD)")])
save("B09-DN-V10", "Thuyết minh BCTC — V.10 Tăng, giảm tài sản cố định vô hình", "mau-B09-DN-V10-tscd-vo-hinh.json", vh, cols, checks,
     note="cột theo nhóm: TK chi tiết 213 và 2143 gắn nhãn QSDD / QTG / SHCN / PM / KHAC")

TC_GROUPS = [("NHA", "Nhà cửa, vật kiến trúc"), ("MAY", "Máy móc, thiết bị"), ("PTVT", "Phương tiện vận tải, truyền dẫn"), ("KHAC", "TSCĐ thuê tài chính khác")]
tc, cols, checks = fixed_assets("V11", "11. Tăng, giảm tài sản cố định thuê tài chính", "212", "2142", TC_GROUPS,
    [("T1", "- Thuê tài chính trong năm", lambda c, a: f"MOV_D({c} ; 3412*)"),
     ("T3", "- Tăng khác", lambda c, a: f"PS_D({c}) - MOV_D({c} ; 3412*)"),
     ("G1", "- Mua lại TSCĐ thuê tài chính", lambda c, a: f"MOV_C({c} ; 211*)"),
     ("G2", "- Trả lại TSCĐ thuê tài chính", lambda c, a: f"VT_C({c} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_C({c}) - MOV_C({c} ; 211*) - VT_C({c} ; FAD)")],
    [("T1", "- Khấu hao trong năm", lambda c, a: f"PS_C({a})"),
     ("G1", "- Mua lại TSCĐ thuê tài chính", lambda c, a: f"MOV_D({a} ; 2141*)"),
     ("G2", "- Trả lại TSCĐ thuê tài chính", lambda c, a: f"VT_D({a} ; FAD)"),
     ("G3", "- Giảm khác", lambda c, a: f"PS_D({a}) - MOV_D({a} ; 2141*) - VT_D({a} ; FAD)")])
save("B09-DN-V11", "Thuyết minh BCTC — V.11 Tăng, giảm tài sản cố định thuê tài chính", "mau-B09-DN-V11-tscd-thue-tai-chinh.json", tc, cols, checks,
     note="cột theo nhóm: TK chi tiết 212 và 2142 gắn nhãn NHA / MAY / PTVT / KHAC")

# ------------------------------------------------------------------ V.27a Biến động vốn chủ sở hữu (năm nay; chạy lại cho năm trước)
EQ = [("4111*", "Vốn góp của chủ sở hữu"), ("4112*", "Thặng dư vốn"), ("4113*", "Quyền chọn chuyển đổi trái phiếu"), ("4118*", "Vốn khác của chủ sở hữu"),
      ("412*", "Chênh lệch đánh giá lại tài sản"), ("413*", "Chênh lệch tỷ giá hối đoái"), ("421*, 414*, 418*", "LNST chưa phân phối và các quỹ"),
      ("419*", "Các khoản mục khác")]
PROFIT = "NET_C(511*, 515*, 711*) - NET_D(521*, 632*, 635*, 641*, 642*, 811*, 821*)"
def row(code, name, slots, indent=2, bold=False):
    # LINE() cannot add across columns, so the "Cộng" slot repeats the formula over all equity accounts.
    assert len(slots) == len(EQ) + 1, code
    return line(code, name, " | ".join(slots), indent, bold)
ALL_EQ = "411*, 412*, 413*, 414*, 418*, 419*, 421*"
VCSH = [
    head("V27A", "27.a) Bảng đối chiếu biến động của vốn chủ sở hữu (năm báo cáo; chạy mẫu cho năm trước để lấy phần năm trước)"),
    row("V27A.0", "Số dư đầu năm", [f"-OPEN_NET({p})" for p, _ in EQ] + [f"-OPEN_NET({ALL_EQ})"], 1, True),
    row("V27A.1", "- Tăng vốn trong năm", [f"PS_C({p})" for p, _ in EQ[:4]] + ["", "", "", ""] + [f"PS_C(411*)"]),
    row("V27A.2", "- Lãi trong năm", ["", "", "", "", "", "", f"MAX0({PROFIT})", "", f"MAX0({PROFIT})"]),
    row("V27A.3", "- Tăng khác", ["", "", "", "", "PS_C(412*)", "PS_C(413*)", f"PS_C(421*, 414*, 418*) - MAX0({PROFIT})", "PS_C(419*)",
                                   f"PS_C(412*, 413*, 414*, 418*, 419*, 421*) - MAX0({PROFIT})"]),
    row("V27A.4", "- Giảm vốn trong năm", [f"PS_D({p})" for p, _ in EQ[:4]] + ["", "", "", ""] + ["PS_D(411*)"]),
    row("V27A.5", "- Lỗ trong năm", ["", "", "", "", "", "", f"MAX0(-({PROFIT}))", "", f"MAX0(-({PROFIT}))"]),
    row("V27A.6", "- Giảm khác", ["", "", "", "", "PS_D(412*)", "PS_D(413*)", f"PS_D(421*, 414*, 418*) - MAX0(-({PROFIT}))", "PS_D(419*)",
                                   f"PS_D(412*, 413*, 414*, 418*, 419*, 421*) - MAX0(-({PROFIT}))"]),
    row("V27A.9", "Số dư cuối năm", [f"-BAL_NET({p})" for p, _ in EQ] + [f"-BAL_NET({ALL_EQ})"], 1, True),
]
save("B09-DN-V27", "Thuyết minh BCTC — V.27a Biến động vốn chủ sở hữu", "mau-B09-DN-V27-von-chu-so-huu.json", VCSH,
     [n for _, n in EQ] + ["Cộng"],
     [{"rule": "LINE(V27A.9) = LINE(V27A.0) + LINE(V27A.1) + LINE(V27A.2) + LINE(V27A.3) - LINE(V27A.4) - LINE(V27A.5) - LINE(V27A.6)",
       "message": "Số dư đầu năm + tăng − giảm = Số dư cuối năm"}],
     note="mục V.27a; lãi/lỗ trong năm = lợi nhuận sau thuế tính như Mã 60 B02-DN")

# ------------------------------------------------------------------ VII, VIII (Năm nay; chạy kèm kỳ so sánh = Năm trước)
TAGS_511 = "DV, XD, TCTG, BDSDT"
VII = [
    head("VII1", "VII.1. Tổng doanh thu bán hàng và cung cấp dịch vụ"),
    line("VII1.1", "- Doanh thu bán sản phẩm, hàng hóa (trừ doanh thu bán, thanh lý bất động sản đầu tư)", "NET_C(511*[!DV,!XD,!TCTG,!BDSDT])"),
    line("VII1.2", "- Doanh thu cung cấp dịch vụ (trừ dịch vụ xây dựng)", "NET_C(511*[DV])"),
    line("VII1.3", "- Doanh thu dịch vụ xây dựng", "NET_C(511*[XD])"),
    line("VII1.4", "- Doanh thu trợ cấp, trợ giá", "NET_C(511*[TCTG])"),
    total("VII1.9", "Cộng", ["VII1.1", "VII1.2", "VII1.3", "VII1.4"]),
    head("VII2", "VII.2. Các khoản giảm trừ doanh thu"),
    line("VII2.1", "- Khoản chiết khấu thương mại", "NET_D(521*[CKTM])"),
    line("VII2.2", "- Khoản giảm giá hàng bán", "NET_D(521*[GGHB])"),
    line("VII2.3", "- Khoản doanh thu hàng bán bị trả lại", "NET_D(521*[HBTL])"),
    line("VII2.4", "- Chưa phân loại (TK 521 chưa gắn nhãn)", "NET_D(521*[!CKTM,!GGHB,!HBTL])"),
    total("VII2.9", "Cộng", ["VII2.1", "VII2.2", "VII2.3", "VII2.4"]),
    head("VII3", "VII.3. Giá vốn hàng bán"),
    line("VII3.1", "- Giá vốn của sản phẩm, hàng hóa đã bán (trừ giá trị còn lại và chi phí bán, thanh lý bất động sản đầu tư)", "NET_D(632*[!DV,!BDSDT,!DPHTK])"),
    line("VII3.2", "- Giá vốn của dịch vụ đã cung cấp (bao gồm cả dịch vụ xây dựng)", "NET_D(632*[DV])"),
    line("VII3.6", "- Dự phòng giảm giá hàng tồn kho, dự phòng giảm giá tài sản sinh học", "NET_D(632*[DPHTK])"),
    total("VII3.9", "Cộng", ["VII3.1", "VII3.2", "VII3.6"]),
    head("VII4", "VII.4. Lãi/lỗ của hoạt động bán, thanh lý BĐSĐT"),
    line("VII4.1", "- Doanh thu bán, thanh lý BĐSĐT", "NET_C(511*[BDSDT])"),
    line("VII4.2", "- Giá trị còn lại và chi phí bán, thanh lý BĐSĐT", "NET_D(632*[BDSDT])"),
    line("VII4.9", "Lãi/lỗ của hoạt động bán, thanh lý BĐSĐT", "LINE(VII4.1) - LINE(VII4.2)", 1, True),
    head("VII5", "VII.5. Doanh thu hoạt động tài chính"),
    line("VII5.1", "- Lãi tiền gửi, tiền cho vay", "NET_C(515*[LTG])"),
    line("VII5.2", "- Lãi do bán, thanh lý các khoản đầu tư tài chính", "NET_C(515*[LBDT])"),
    line("VII5.3", "- Cổ tức, lợi nhuận được chia bằng tiền hoặc tài sản phi tiền tệ", "NET_C(515*[CTLN])"),
    line("VII5.4", "- Lãi chênh lệch tỷ giá", "NET_C(515*[TG]) + NET_C(515*[DGL])"),
    line("VII5.5", "- Lãi bán hàng trả chậm, trả góp", "NET_C(515*[TTC])"),
    line("VII5.6", "- Chiết khấu thanh toán được hưởng", "NET_C(515*[CKTT])"),
    line("VII5.7", "- Doanh thu hoạt động tài chính khác", "NET_C(515*[!LTG,!LBDT,!CTLN,!TG,!DGL,!TTC,!CKTT])"),
    total("VII5.9", "Cộng", [f"VII5.{i}" for i in range(1, 8)]),
    head("VII6", "VII.6. Chi phí tài chính"),
    line("VII6.1", "- Chi phí đi vay", "NET_D(635*[LV])"),
    line("VII6.2", "- Lỗ do bán, thanh lý các khoản đầu tư tài chính", "NET_D(635*[LBDT])"),
    line("VII6.3", "- Lỗ chênh lệch tỷ giá", "NET_D(635*[TG]) + NET_D(635*[DGL])"),
    line("VII6.4", "- Lãi mua hàng trả chậm, trả góp", "NET_D(635*[TTC])"),
    line("VII6.5", "- Chiết khấu thanh toán phải trả", "NET_D(635*[CKTT])"),
    line("VII6.6", "- Dự phòng giảm giá chứng khoán kinh doanh và dự phòng tổn thất đầu tư vào đơn vị khác", "NET_D(635*[DPDT])"),
    line("VII6.8", "- Chi phí tài chính khác", "NET_D(635*[!LV,!LBDT,!TG,!DGL,!TTC,!CKTT,!DPDT])"),
    total("VII6.9", "Cộng", ["VII6.1", "VII6.2", "VII6.3", "VII6.4", "VII6.5", "VII6.6", "VII6.8"]),
    head("VII7", "VII.7. Thu nhập khác"),
    line("VII7.1", "- Thanh lý, nhượng bán TSCĐ", "NET_C(711*[TLTS])"),
    line("VII7.6", "- Các khoản khác", "NET_C(711*[!TLTS])"),
    total("VII7.9", "Cộng", ["VII7.1", "VII7.6"]),
    head("VII8", "VII.8. Chi phí khác"),
    line("VII8.1", "- Giá trị còn lại TSCĐ và chi phí thanh lý, nhượng bán TSCĐ", "NET_D(811*[TLTS])"),
    line("VII8.4", "- Các khoản chi phí khác", "NET_D(811*[!TLTS])"),
    total("VII8.9", "Cộng", ["VII8.1", "VII8.4"]),
    head("VII9", "VII.9. Chi phí bán hàng và chi phí quản lý doanh nghiệp"),
    line("VII9.A", "a) Các khoản chi phí quản lý doanh nghiệp phát sinh trong kỳ", "PS_D(642*)"),
    line("VII9.B", "b) Các khoản chi phí bán hàng phát sinh trong kỳ", "PS_D(641*)"),
    line("VII9.C", "c) Các khoản ghi giảm chi phí bán hàng và chi phí quản lý doanh nghiệp", "PS_D(641*, 642*) - NET_D(641*, 642*)"),
    head("VII10", "VII.10. Chi phí sản xuất, kinh doanh theo yếu tố (phát sinh Nợ TK chi phí, trừ bút toán đảo)"),
    line("VII10.1", "- Chi phí nguyên liệu, vật liệu", "PS_D(621*, 6232*, 6272*, 6412*, 6422*)"),
    line("VII10.2", "- Chi phí nhân công", "PS_D(622*, 6231*, 6271*, 6411*, 6421*)"),
    line("VII10.3", "- Chi phí khấu hao tài sản cố định", "PS_D(6234*, 6274*, 6414*, 6424*)"),
    line("VII10.4", "- Chi phí dịch vụ mua ngoài", "PS_D(6237*, 6277*, 6417*, 6427*)"),
    line("VII10.5", "- Chi phí khác bằng tiền", "PS_D(6233*, 6238*, 6273*, 6275*, 6278*, 6413*, 6415*, 6418*, 6423*, 6425*, 6426*, 6428*)"),
    total("VII10.9", "Cộng", [f"VII10.{i}" for i in range(1, 6)]),
    head("VII11", "VII.11. Chi phí thuế thu nhập doanh nghiệp"),
    line("VII11.1", "- Lợi nhuận kế toán trước thuế", "NET_C(511*, 515*, 711*) - NET_D(521*, 632*, 635*, 641*, 642*, 811*)"),
    line("VII11.7", "Chi phí thuế TNDN hiện hành", "NET_D(8211*)"),
    line("VII11.8", "Chi phí thuế TNDN hoãn lại", "NET_D(8212*)"),
    total("VII11.9", "Chi phí thuế TNDN", ["VII11.7", "VII11.8"]),
    head("VIII", "VIII. Thông tin bổ sung cho các khoản mục trình bày trong Báo cáo lưu chuyển tiền tệ"),
    line("VIII3", "3. Số tiền đi vay thực thu trong kỳ", "CF(33)"),
    line("VIII4", "4. Số tiền đã thực trả gốc vay trong kỳ", "-CF(34)"),
]
save("B09-DN-VII", "Thuyết minh BCTC — VII, VIII. Thông tin bổ sung cho Báo cáo kết quả hoạt động kinh doanh và Báo cáo lưu chuyển tiền tệ",
     "mau-B09-DN-VII-VIII.json", VII, None,
     note="mục VII, VIII; chạy kèm kỳ so sánh để có cột Năm trước")
