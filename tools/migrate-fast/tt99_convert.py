"""Converts FAST exports (fast_export.py, TT200-style accounts) to the TT 99/2025 chart with an account mapping, and writes
the review tables for the chief accountant.

    python tt99_convert.py <export folder[,folder…]> <year[,year…]> <mapping.csv> <TT99 import chart.csv> <output folder>

One export folder per year (same order). Inputs per year: he-thong-tk.csv, so-du-dau-ky-<year>.csv, chung-tu-<year>.csv,
can-doi-<year>.csv; mapping columns: TK cũ, TK TT99, TK chi tiết đề xuất, Tên TK chi tiết đề xuất, Nhãn BC, Căn cứ / ghi chú,
Cần chị xác nhận. Outputs (same file names as the export, so hak_import.mjs runs on the output folder):
bang-doi-chieu-tk-tt99-<year>.csv (review), he-thong-tk.csv, doi-tuong.csv, so-du-dau-ky-<year>.csv, chung-tu-<year>.csv,
can-doi-<year>.csv (expected trial balance on the new accounts). Every old account with a balance or a movement must be mapped,
and each converted opening balance must still balance — otherwise the script stops.
"""
import collections, csv, os, shutil, sys
from decimal import Decimal as D

exps, years = sys.argv[1].split(","), [int(y) for y in sys.argv[2].split(",")]
map_file, chart_file, out = sys.argv[3], sys.argv[4], sys.argv[5]
if len(exps) != len(years): raise SystemExit("Số thư mục xuất phải bằng số năm.")
os.makedirs(out, exist_ok=True)
def read(path, delim=","):
    with open(path, encoding="utf-8-sig", newline="") as f: return list(csv.reader(f, delimiter=delim))
def write(name, rows):
    with open(os.path.join(out, name), "w", encoding="utf-8-sig", newline="") as f: csv.writer(f).writerows(rows)
    print(f"  {name}: {len(rows) - 1} dòng")
def num(s): return D(s) if s else D(0)
def amt(x): return "" if not x else f"{x:.2f}".rstrip("0").rstrip(".")

old_chart = read(os.path.join(exps[-1], "he-thong-tk.csv"))[1:]
old_names = {r[0]: r[1] for r in old_chart}
old_partner = {r[0]: r[4] == "Có" for r in old_chart}
mapping = {r[0]: r for r in read(map_file)[1:] if r and r[0]}
chart = read(chart_file, ";")
header, chart = chart[0], chart[1:]
tt99 = {r[0]: r for r in chart}
def target(old):
    m = mapping.get(old)
    if m is None: raise SystemExit(f"TK {old} có số liệu nhưng chưa có trong bảng ánh xạ.")
    if m[1] not in tt99: raise SystemExit(f"TK TT99 {m[1]} (từ {old}) không có trong danh mục TT99.")
    return m[2] or m[1]

# HAK's suggested TT99 attributes (đối tượng / công nợ) are management choices: where FAST did not follow an account by partner,
# the converted account does not require one either (its history has no partner detail).
no_partner = set()
for exp, year in zip(exps, years):
    opening = collections.defaultdict(D); mov = collections.defaultdict(lambda: [D(0), D(0)])
    op_rows = read(os.path.join(exp, f"so-du-dau-ky-{year}.csv"))
    for r in op_rows[1:]: opening[r[0]] += num(r[2]) - num(r[3])
    ct_rows = read(os.path.join(exp, f"chung-tu-{year}.csv"))
    for r in ct_rows[1:]: mov[r[4]][0] += num(r[6]); mov[r[4]][1] += num(r[7])
    used = sorted(set(k for k, v in opening.items() if v) | set(mov))
    no_partner |= {target(o) for o in used if not old_partner.get(o, False)}

    review = [["TK cũ (FAST)", "Tên TK cũ", f"Dư Nợ 01/01/{year}", f"Dư Có 01/01/{year}", f"PS Nợ {year}", f"PS Có {year}",
               "TK TT99", "Tên TK TT99", "TK hạch toán trên HAK", "Tên TK chi tiết", "Nhãn BC", "Căn cứ / ghi chú", "Cần chị xác nhận"]]
    for old in used:
        m = mapping[old] if old in mapping else None
        t = target(old)
        o = opening[old]
        review.append([old, old_names.get(old, ""), amt(o) if o > 0 else "", amt(-o) if o < 0 else "", amt(mov[old][0]), amt(mov[old][1]),
                       m[1], tt99[m[1]][1], t, m[3], m[4], m[5], m[6]])
    review.append(["Cộng", "", amt(sum(v for v in opening.values() if v > 0)), amt(-sum(v for v in opening.values() if v < 0)),
                   amt(sum(v[0] for v in mov.values())), amt(sum(v[1] for v in mov.values())), "", "", "", "", "", "", ""])
    write(f"bang-doi-chieu-tk-tt99-{year}.csv", review)

    write(f"so-du-dau-ky-{year}.csv", [op_rows[0]] + [[target(r[0])] + r[1:] for r in op_rows[1:]])
    write(f"chung-tu-{year}.csv", [ct_rows[0]] + [r[:4] + [target(r[4])] + r[5:] for r in ct_rows[1:]])
    tb = read(os.path.join(exp, f"can-doi-{year}.csv"))
    agg = collections.defaultdict(lambda: [D(0)] * 4)
    for m, acc, o, d, c, cl in tb[1:]:
        a = agg[(m, target(acc))]
        for i, x in enumerate((o, d, c, cl)): a[i] += num(x)
    write(f"can-doi-{year}.csv", [tb[0]] + [[m, acc] + [amt(x) for x in v] for (m, acc), v in sorted(agg.items())])
    bal = sum(num(r[2]) - num(r[3]) for r in op_rows[1:])
    print(f"{year}: số dư đầu năm sau chuyển đổi Nợ − Có = {bal}; {len(used)} TK cũ → {len({target(o) for o in used})} TK trên HAK")
    if bal: raise SystemExit("Số dư sau chuyển đổi không cân.")

# ---- TT99 chart + company detail accounts (parent becomes a summary account; its attributes move to the detail)
details = {m[2]: (m[1], m[3], m[4]) for m in mapping.values() if m[2]}
parents = {p for p, _, _ in details.values()}
rows = [header + ["Nhãn BC"]]
for r in chart:
    code = r[0]
    if code in ("41111", "41112"): continue                               # joint-stock only; a TNHH posts to 4111
    r = r + [""]
    if code == "4111": r[3] = "Có"
    if code == "141": r[8] = "Lưỡng tính"                                 # FAST shows advances gross by person
    if code in parents:
        attrs = r[4:9]
        rows.append([r[0], r[1], r[2], "Không", "", "", "", "", "", ""])
        for d, (p, name, tag) in sorted(details.items()):
            if p == code: rows.append([d, name, code, "Có"] + attrs + [tag])
        continue
    tag = next((m[4] for m in mapping.values() if m[1] == code and not m[2] and m[4]), "")
    if code in no_partner and (r[4] or r[5]):
        print(f"  TK {code}: FAST không theo dõi theo đối tượng → bỏ 'bắt buộc đối tượng / công nợ' của tệp đề xuất")
        r[4] = r[5] = ""
    rows.append(r[:9] + [tag])
write("he-thong-tk.csv", [r[:10] for r in rows])
shutil.copy(os.path.join(exps[-1], "doi-tuong.csv"), os.path.join(out, "doi-tuong.csv"))
