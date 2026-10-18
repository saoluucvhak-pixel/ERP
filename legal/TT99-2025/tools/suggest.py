import csv, json, sys, os
d = sys.argv[1]
rows = json.load(open(os.path.join(d, "phu-luc-II-A-danh-muc-tai-khoan.json"), encoding="utf-8"))
# Suggested management attributes (company configuration, NOT part of the Circular) — chief accountant decides.
attrs = {
  "111": dict(cash="Tiền mặt", neg="Chặn"),
  "112": dict(cash="Ngân hàng", neg="Cảnh báo"),
  "113": dict(cash="Đang chuyển"),
  "131": dict(partner="Có", open="Phải thu", display="Lưỡng tính"),
  "331": dict(partner="Có", open="Phải trả", display="Lưỡng tính"),
  "141": dict(partner="Có", open="Phải thu"),
  "334": dict(partner="Có"),
}
with open(os.path.join(d, "nhap-he-thong-tk-de-xuat.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, delimiter=";")
    w.writerow(["Mã TK", "Tên TK", "TK cấp trên", "Chi tiết", "Theo dõi đối tượng", "Công nợ", "Loại tiền", "Số dư âm", "Trình bày số dư"])
    for r in rows:
        a = attrs.get(r["code"], {})
        if a and not r["postable"]: raise SystemExit(f"{r['code']} is a summary account but has attributes")
        w.writerow([r["code"], r["name"], r["parent"], "Có" if r["postable"] else "Không", a.get("partner", ""), a.get("open", ""), a.get("cash", ""), a.get("neg", ""), a.get("display", "")])
print("ok", len(rows))
