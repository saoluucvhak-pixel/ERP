import csv, json, sys, os
src, outdir = sys.argv[1], sys.argv[2]
cells = {}
for line in open(src, encoding="utf-8"):
    parts = line.rstrip("\n").split("\t")
    if len(parts) < 3: continue
    r, c, t = int(parts[0]), int(parts[1]), parts[2].strip()
    cells.setdefault(r, {})[c] = t
rows, codes, loai = [], {}, ""
for r in sorted(cells):
    if r <= 3: continue
    row = cells[r]
    stt, c1, c2, name = row.get(1, ""), row.get(2, ""), row.get(3, ""), row.get(4, "")
    if not (c1 or c2):
        if name.upper().startswith(("LOẠI", "TÀI KHOẢN XÁC ĐỊNH")): loai = name
        continue
    code = c1 or c2
    if not code.isdigit(): raise SystemExit(f"row {r}: bad code {code!r}")
    parent = ""
    if c2:
        for k in range(len(code) - 1, 2, -1):
            if code[:k] in codes: parent = code[:k]; break
        if not parent: raise SystemExit(f"row {r}: no parent for {code}")
    level = 1 if not parent else codes[parent]["level"] + 1
    item = {"stt": stt, "code": code, "name": name, "parent": parent, "level": level, "group": loai}
    if code in codes: raise SystemExit(f"duplicate {code}")
    codes[code] = item; rows.append(item)
for it in rows: it["postable"] = not any(o["parent"] == it["code"] for o in rows)
os.makedirs(outdir, exist_ok=True)
with open(os.path.join(outdir, "phu-luc-II-A-danh-muc-tai-khoan.json"), "w", encoding="utf-8") as f:
    json.dump(rows, f, ensure_ascii=False, indent=1)
with open(os.path.join(outdir, "phu-luc-II-A-danh-muc-tai-khoan.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, delimiter=";")
    w.writerow(["STT", "Số hiệu TK", "Tên tài khoản", "TK cấp trên", "Cấp", "Chi tiết", "Loại tài khoản"])
    for it in rows: w.writerow([it["stt"], it["code"], it["name"], it["parent"], it["level"], "Có" if it["postable"] else "Không", it["group"]])
lv1 = [r for r in rows if r["level"] == 1]
print(len(rows), "TK;", len(lv1), "cấp 1;", sum(r["postable"] for r in rows), "TK chi tiết; STT cuối", lv1[-1]["stt"])
print("STT liên tục:", [int(r["stt"]) for r in lv1] == list(range(1, len(lv1) + 1)))
