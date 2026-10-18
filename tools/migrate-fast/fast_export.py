"""Exports a FAST Accounting (Visual FoxPro) data folder to HAK import files, read-only.

    python fast_export.py <fast folder> <output folder> <year> [unit]

Writes (UTF-8 with BOM, comma separated — opened by HAK Thiết lập → Nhập từ Excel/CSV):
  he-thong-tk.csv            chart of accounts (codes as in FAST; parents created before children)
  doi-tuong.csv              partners (DMKH)
  so-du-dau-ky-<year>.csv    opening balances on 01/01/<year> (cdtk; partner detail from cdkh)
  chung-tu-<year>.csv        every posted ledger line of <year> (CT00), one voucher per STT_REC
  can-doi-<year>.csv         expected trial balance per month and detail account, for reconciliation
  bao-cao.txt                checks and notes
Deleted FoxPro rows (*) are ignored. Text is TCVN3 (ABC) converted to Unicode. Nothing in the FAST folder is modified.
"""
import collections, csv, datetime, os, sys
from decimal import Decimal as D
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dbf import read_dbf

src, out, year = sys.argv[1], sys.argv[2], int(sys.argv[3])
unit = sys.argv[4] if len(sys.argv) > 4 else "VP"
os.makedirs(out, exist_ok=True)
report = []
def note(s): report.append(s); print(s)
def live(path): return [r for r in read_dbf(os.path.join(src, path))["rows"] if not r["_deleted"]]
def write(name, header, rows):
    with open(os.path.join(out, name), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f); w.writerow(header); w.writerows(rows)
    note(f"  {name}: {len(rows)} dòng")
def amt(x): return "" if not x else f"{x:.2f}".rstrip("0").rstrip(".")

dmtk = {r["TK"]: r for r in live(r"COD\DMTK.DBF")}
dmkh = {r["MA_KH"]: r for r in live(r"COD\DMKH.DBF")}
ct = [r for r in live(r"DBF\CT00.DBF") if r["MA_DVCS"] == unit and r["NGAY_CT"] and r["NGAY_CT"].year == year
      and ((r["PS_NO"] or 0) != 0 or (r["PS_CO"] or 0) != 0)]
cdtk = [r for r in live(r"DBF\cdtk.dbf") if r["MA_DVCS"] == unit and int(r["NAM"]) == year]
cdkh = [r for r in live(r"DBF\cdkh.dbf") if r["MA_DVCS"] == unit and int(r["NAM"]) == year]
note(f"FAST {src} — đơn vị {unit}, năm {year}: {len(ct)} dòng sổ, {len({r['STT_REC'] for r in ct})} chứng từ; "
     f"{len(dmtk)} TK, {len(dmkh)} đối tượng")

# ---- accounts: a FAST account with children is a summary account in HAK; postings made directly on it go to <TK>.00
children = collections.defaultdict(list)
for t, r in dmtk.items():
    if r["TK_ME"] and r["TK_ME"] in dmtk and r["TK_ME"] != t: children[r["TK_ME"]].append(t)
opening = collections.defaultdict(D)                      # (tk, partner) -> Dr-positive
for r in cdtk: opening[(r["TK"], "")] += (r["DU_NO00"] or 0) - (r["DU_CO00"] or 0)
by_partner = collections.defaultdict(D)
for r in cdkh: by_partner[(r["TK"], r["MA_KH"])] += (r["DU_NO00"] or 0) - (r["DU_CO00"] or 0)
used = {r["TK"] for r in ct} | {t for (t, _), v in opening.items() if v}
direct = {t: t + ".00" for t in used if t in children}
for t, c in direct.items():
    note(f"  TK {t} có TK con nhưng có số liệu trực tiếp → chuyển vào TK chi tiết {c}")
def acc(t): return direct.get(t, t)

# Partner-tracked: FAST TK_CN = 1 and every line / opening of the year has a partner (else HAK would refuse the line).
tracked = {t for t, r in dmtk.items() if r["TK_CN"] == 1}
missing = collections.Counter(r["TK"] for r in ct if r["TK"] in tracked and not r["MA_KH"].strip())
for t, n in missing.items(): note(f"  TK {t} theo dõi công nợ trong FAST nhưng {n} dòng không có mã khách → HAK không bắt buộc đối tượng cho TK này")
requires = tracked - set(missing)

def cash(t): return "Tiền mặt" if t.startswith("111") else "Ngân hàng" if t.startswith("112") else "Đang chuyển" if t.startswith("113") else ""
rows, order = [], []
def visit(t, seen=set()):
    if t in seen: return
    seen.add(t)
    p = dmtk[t]["TK_ME"]
    if p and p in dmtk and p != t: visit(p)
    order.append(t)
for t in sorted(dmtk): visit(t)
for t in order:
    r = dmtk[t]; summary = t in children; p = r["TK_ME"] if r["TK_ME"] in dmtk and r["TK_ME"] != t else ""
    # FAST shows partner-tracked accounts (TK_CN) gross by partner (dư Nợ và dư Có cùng lúc) — same in HAK ("Lưỡng tính").
    gross = "Lưỡng tính" if t in tracked else ""
    rows.append([t, r["TEN_TK"] or t, p, "Không" if summary else "Có", "" if summary or t not in requires else "Có", "", "" if summary else cash(t), "",
                 "" if summary else gross])
    if t in direct:
        rows.append([direct[t], f"{r['TEN_TK']} (số liệu ghi trực tiếp trên TK {t} ở FAST)", t, "Có", "Có" if t in requires else "", "", cash(t), "", gross])
write("he-thong-tk.csv", ["Mã TK", "Tên TK", "TK cấp trên", "Chi tiết", "Theo dõi đối tượng", "Công nợ", "Loại tiền", "Số dư âm", "Trình bày số dư"], rows)

# ---- partners
def roles(r): return "+".join(x for x, f in (("KH", "KH_YN"), ("NCC", "CC_YN"), ("NV", "NVBP_YN")) if r.get(f) == 1) or "KHAC"
write("doi-tuong.csv", ["Mã đối tượng", "Tên", "Vai trò", "Mã số thuế"],
      [[k, r["TEN_KH"] or k, roles(r), r["MA_SO_THUE"]] for k, r in sorted(dmkh.items())])
known = set(dmkh)

# ---- opening balances: partner detail where FAST keeps it (cdkh) and it adds up to the account total
op_rows, total = [], D(0)
for t in sorted({t for (t, _) in opening} | {t for (t, _), x in by_partner.items() if x}):
    v = opening[(t, "")]
    detail = {k: x for (tt, k), x in by_partner.items() if tt == t and x}
    if not v and not detail: continue
    if detail and sum(detail.values()) == v:
        for k, x in sorted(detail.items()):
            op_rows.append([acc(t), k, amt(x) if x > 0 else "", amt(-x) if x < 0 else "", f"Số dư đầu năm {year} chuyển từ FAST"])
    else:
        if detail: note(f"  TK {t}: chi tiết theo khách ({sum(detail.values()):,.2f}) khác số dư TK ({v:,.2f}) → nhập theo TK")
        if t in requires: note(f"  CẢNH BÁO TK {t} bắt buộc đối tượng nhưng số dư đầu năm không có chi tiết khách")
        op_rows.append([acc(t), "", amt(v) if v > 0 else "", amt(-v) if v < 0 else "", f"Số dư đầu năm {year} chuyển từ FAST"])
    total += v
note(f"  số dư đầu năm: tổng Nợ − Có = {total}")
write(f"so-du-dau-ky-{year}.csv", ["Mã TK", "Mã đối tượng", "Dư Nợ", "Dư Có", "Diễn giải"], op_rows)

# ---- vouchers: one per STT_REC; negative amounts move to the other side; entries touching 911 are closing entries (CLS)
closing = {r["STT_REC"] for r in ct if r["TK"].startswith("911")}
# Within a day, vouchers that bring money in (cash/bank debited) come first: FAST never checked the order inside a day, HAK
# checks cash funds against the balance posted so far (rút tiền về quỹ rồi chi lương cùng ngày).
cash_in = collections.defaultdict(D)
for r in ct:
    if r["TK"].startswith(("111", "112", "113")): cash_in[r["STT_REC"]] += (r["PS_NO"] or 0) - (r["PS_CO"] or 0)
v_rows, unknown = [], collections.Counter()
for r in sorted(ct, key=lambda r: (r["NGAY_CT"], 0 if cash_in[r["STT_REC"]] > 0 else 1, r["STT_REC"], r["STT_REC0"], r["TK"])):
    no, co = (r["PS_NO"] or 0) - (r["PS_CO"] or 0), D(0)
    if no < 0: no, co = D(0), -no
    kh = r["MA_KH"].strip()
    if kh and kh not in known: unknown[kh] += 1; kh = ""
    if r["TK"] not in requires and r["TK"] not in tracked: pass
    nt = r["MA_NT"].strip()
    fx = (r["PS_NO_NT"] or 0) + (r["PS_CO_NT"] or 0) if nt and nt != "VND" else 0
    v_rows.append([r["STT_REC"], r["NGAY_CT"].isoformat(), f"{r['MA_CT']} {r['SO_CT'].strip()}", r["DIEN_GIAI"], acc(r["TK"]), kh,
                   amt(no), amt(co), nt if fx else "", amt(abs(fx)) if fx else "", "", "CLS" if r["STT_REC"] in closing else ""])
for k, n in unknown.items(): note(f"  mã khách {k} không có trong DMKH ({n} dòng) → bỏ đối tượng")
note(f"  chứng từ kết chuyển (có TK 911) → loại CLS: {len(closing)}")
write(f"chung-tu-{year}.csv", ["Mã chứng từ", "Ngày", "Số chứng từ gốc", "Diễn giải", "Mã TK", "Mã đối tượng", "Nợ", "Có", "Loại tiền", "Số nguyên tệ", "Diễn giải dòng", "Loại chứng từ"], v_rows)
recs = collections.defaultdict(D)
for r in ct: recs[r["STT_REC"]] += (r["PS_NO"] or 0) - (r["PS_CO"] or 0)
bad = [k for k, x in recs.items() if x]
note(f"  chứng từ không cân: {len(bad)} {bad[:10]}")

# ---- expected trial balance per month (detail accounts as imported)
bal = collections.defaultdict(D)
for (t, _), v in opening.items(): bal[acc(t)] += v
tb = []
for m in range(1, 13):
    mov = collections.defaultdict(lambda: [D(0), D(0)])
    for r in ct:
        if r["NGAY_CT"].month == m:
            x = (r["PS_NO"] or 0) - (r["PS_CO"] or 0)
            mov[acc(r["TK"])][0 if x > 0 else 1] += abs(x)
    if not mov and m > max(r["NGAY_CT"].month for r in ct): break
    for t in sorted(set(bal) | set(mov)):
        o = bal[t]; d, c = mov[t]; bal[t] = o + d - c
        if o or d or c or bal[t]: tb.append([f"{year}-{m:02d}", t, amt(o), amt(d), amt(c), amt(bal[t])])
write(f"can-doi-{year}.csv", ["Tháng", "Mã TK", "Dư đầu (Nợ+)", "PS Nợ", "PS Có", "Dư cuối (Nợ+)"], tb)
with open(os.path.join(out, "bao-cao.txt"), "w", encoding="utf-8-sig") as f:
    f.write(f"Xuất lúc {datetime.datetime.now():%Y-%m-%d %H:%M}\n" + "\n".join(report) + "\n")
