"""Minimal read-only Visual FoxPro / dBase reader. Usage: python dbf.py <file> [n_rows] [--raw]"""
import struct, sys, datetime, decimal

_TCVN3 = {
    0xA1: "Ă", 0xA2: "Â", 0xA3: "Ê", 0xA4: "Ô", 0xA5: "Ơ", 0xA6: "Ư", 0xA7: "Đ",
    0xA8: "ă", 0xA9: "â", 0xAA: "ê", 0xAB: "ô", 0xAC: "ơ", 0xAD: "ư", 0xAE: "đ",
    0xB5: "à", 0xB6: "ả", 0xB7: "ã", 0xB8: "á", 0xB9: "ạ",
    0xBB: "ằ", 0xBC: "ẳ", 0xBD: "ẵ", 0xBE: "ắ", 0xC6: "ặ",
    0xC7: "ầ", 0xC8: "ẩ", 0xC9: "ẫ", 0xCA: "ấ", 0xCB: "ậ",
    0xCC: "è", 0xCE: "ẻ", 0xCF: "ẽ", 0xD0: "é", 0xD1: "ẹ",
    0xD2: "ề", 0xD3: "ể", 0xD4: "ễ", 0xD5: "ế", 0xD6: "ệ",
    0xD7: "ì", 0xD8: "ỉ", 0xDC: "ĩ", 0xDD: "í", 0xDE: "ị",
    0xDF: "ò", 0xE1: "ỏ", 0xE2: "õ", 0xE3: "ó", 0xE4: "ọ",
    0xE5: "ồ", 0xE6: "ổ", 0xE7: "ỗ", 0xE8: "ố", 0xE9: "ộ",
    0xEA: "ờ", 0xEB: "ở", 0xEC: "ỡ", 0xED: "ớ", 0xEE: "ợ",
    0xEF: "ù", 0xF1: "ủ", 0xF2: "ũ", 0xF3: "ú", 0xF4: "ụ",
    0xF5: "ừ", 0xF6: "ử", 0xF7: "ữ", 0xF8: "ứ", 0xF9: "ự",
    0xFA: "ỳ", 0xFB: "ỷ", 0xFC: "ỹ", 0xFD: "ý", 0xFE: "ỵ",
}

def tcvn3(b: bytes) -> str:
    """TCVN3 (ABC / .VnTime) bytes → Unicode text."""
    return "".join(_TCVN3.get(c, chr(c)) for c in b)

def read_dbf(path, limit=None):
    with open(path, "rb") as f:
        data = f.read()
    version, = struct.unpack_from("<B", data, 0)
    nrec, hlen, rlen = struct.unpack_from("<IHH", data, 4)
    cp = data[29]
    fields, off = [], 32
    pos = 1
    while data[off] != 0x0D:
        name = data[off:off + 11].split(b"\0")[0].decode("ascii", "replace")
        typ = chr(data[off + 11]); length = data[off + 16]; dec = data[off + 17]
        fields.append((name, typ, pos, length, dec)); pos += length; off += 32
    rows = []
    for i in range(nrec if limit is None else min(limit, nrec)):
        r = data[hlen + i * rlen: hlen + (i + 1) * rlen]
        if not r: break
        rec = {"_deleted": r[0:1] == b"*"}
        for name, typ, p, ln, dec in fields:
            raw = r[p:p + ln]
            if typ in "CV": v = tcvn3(raw.rstrip(b" \0"))
            elif typ in "NF":
                s = raw.strip().decode("ascii", "replace"); v = decimal.Decimal(s) if s and s not in ("-", ".") and not s.startswith("*") else None
            elif typ == "D":
                s = raw.decode("ascii", "replace").strip(); v = datetime.date(int(s[:4]), int(s[4:6]), int(s[6:8])) if s.strip("0") else None
            elif typ == "L": v = raw in (b"T", b"t", b"Y", b"y")
            elif typ == "I": v, = struct.unpack("<i", raw)
            elif typ == "Y": v = decimal.Decimal(struct.unpack("<q", raw)[0]) / 10000
            elif typ == "B": v, = struct.unpack("<d", raw)
            elif typ == "T":
                jd, ms = struct.unpack("<ii", raw)
                v = None if jd == 0 else datetime.datetime(1, 1, 1) + datetime.timedelta(days=jd - 1721426, milliseconds=ms)
            else: v = raw  # M/G/W memo pointers etc.
            rec[name] = v
        rows.append(rec)
    return {"version": version, "records": nrec, "codepage": cp, "fields": fields, "rows": rows}

if __name__ == "__main__":
    t = read_dbf(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 3)
    print(f"version=0x{t['version']:02x} records={t['records']} codepage=0x{t['codepage']:02x}")
    print(", ".join(f"{n}:{ty}{l}" + (f".{d}" if d else "") for n, ty, _, l, d in t["fields"]))
    for r in t["rows"]:
        print({k: (v.decode("latin-1") if isinstance(v, bytes) else v) for k, v in r.items()})
