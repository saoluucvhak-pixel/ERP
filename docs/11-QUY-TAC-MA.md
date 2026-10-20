# Quy tắc bộ mã (chứng từ và danh mục)

Mục tiêu: mã **logic** (nhìn là biết loại và năm), **dễ nhớ** (tiền tố là chữ cái đầu tên tiếng Việt), **mềm mại** (công ty đổi được tiền tố, độ rộng) và **không trùng lắp** (hệ thống chặn, test giữ quy tắc).

## 1. Số chứng từ

```
{TIỀN TỐ}{NĂM TÀI CHÍNH}-{SỐ THỨ TỰ}      ví dụ  HDB2026-000123   PN2026-000045   YCSC2026-000007
```

1. **Tiền tố**: 2–5 chữ cái in hoa, không dấu, là chữ cái đầu tên tiếng Việt của chứng từ (Hóa Đơn Bán = HDB, Phiếu Nhập = PN, Yêu Cầu Sửa Chữa = YCSC). Chứng từ ghi sổ kế toán giữ mã quốc tế đã dùng từ đầu (GJ, CR, CP, BR, BP…).
2. **Mỗi loại chứng từ một tiền tố**, không tiền tố nào là **phần đầu** của tiền tố khác (HDB và HDBX không cùng tồn tại) để nhìn mã là phân biệt được ngay, kể cả khi bị cắt chữ.
3. **Nhóm theo chữ đầu cho dễ nhớ**: hóa đơn bắt đầu `HD`/`HM` (bán / mua), đơn `D..`, yêu cầu `YC..`, hợp đồng `HDK..`, biên bản `BB..`, phiếu `P..`.
4. **Năm tài chính** lấy theo năm tài chính của ngày chứng từ (không phải ngày nhập), nên chứng từ chuyển năm không lẫn.
5. **Số thứ tự** đánh liên tục theo từng loại và từng năm tài chính, mặc định 6 chữ số; **không bao giờ dùng lại** (chứng từ hủy vẫn giữ số). Cấp số nằm trong cùng giao dịch ghi dữ liệu (SQLite một người ghi; PostgreSQL khóa tuần tự) nên không có hai người cùng nhận một số.
6. **Khóa bộ đếm** của mỗi loại là cố định (cột cuối bảng dưới); đổi tiền tố không làm đứt số thứ tự.

## 2. Công ty đổi bộ mã (Thiết lập › Bộ mã chứng từ)

- Đổi **tiền tố** và/hoặc **độ rộng** (4–8 chữ số) từng loại. Chỉ áp dụng cho số cấp sau khi đổi; số đã cấp giữ nguyên; số thứ tự tiếp tục.
- Hệ thống từ chối tiền tố sai quy tắc, trùng, hoặc là phần đầu của tiền tố khác (và ngược lại).
- Mỗi lần đổi ghi nhật ký kiểm toán. Cần quyền quản lý danh mục (kế toán trưởng).
- **Khuyến nghị**: chỉ đổi khi bắt đầu năm tài chính mới để mỗi năm một kiểu.

## 3. Thêm loại chứng từ mới (cho lập trình)

Khai báo ở `src/Hak.Application/Common/DocumentKinds.cs` (khóa, tiền tố mặc định, tên, chu trình) rồi cấp số bằng `Numbering.NextAsync(db, companyId, ngày, khóa)` — không tự viết hàm cấp số. Test `NumberingTests` kiểm: khóa và tiền tố duy nhất, đúng quy tắc, không là phần đầu của nhau, mọi loại chứng từ ghi sổ đều có trong danh mục.

## 4. Mã danh mục (khuyến nghị, người dùng tự đặt khi tạo)

| Danh mục | Dạng khuyến nghị | Ví dụ |
|---|---|---|
| Khách hàng | `KH` + số 4 chữ số | KH0001 |
| Nhà cung cấp | `NCC` + số | NCC0001 |
| Nhân viên | `NV` + số | NV0001 |
| Chủ sở hữu, cổ đông | `CD` + số | CD0001 |
| Vật tư, hàng hóa | nhóm viết tắt + `-` + tên ngắn | GO-KEO, DAM-GO |
| Kho | `K-` + tên ngắn | K-BAI, K-CANG |
| Tài sản cố định | `TS-` + nhóm + số | TS-MAY-001 |
| Công cụ dụng cụ | `CC-` + nhóm + số | CC-DAO-001 |
| Thẻ / khế ước vay | `VAY-` + ngân hàng + số | VAY-VCB-01 |

Quy ước chung: chữ hoa không dấu, chữ số và dấu `-`, tối đa 20 ký tự, **không đổi mã đã phát sinh** (muốn đổi tên thì sửa tên, không sửa mã). Hệ thống đã chặn trùng mã trong cùng công ty; quy ước dạng chỉ là khuyến nghị, không bắt buộc.

## 5. Danh mục loại chứng từ

**Kế toán tổng hợp**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `GJ` | Chứng từ ghi sổ chung | `GJ` |
| `CLS` | Bút toán kết chuyển | `CLS` |
| `ALC` | Bút toán phân bổ | `ALC` |
| `ADJ` | Bút toán điều chỉnh | `ADJ` |
| `REV` | Bút toán đảo | `REV` |
| `FXR` | Đánh giá lại ngoại tệ | `FXR` |
| `OPB` | Số dư đầu kỳ | `OPB` |

**Tiền**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `CR` | Phiếu thu | `CR` |
| `CP` | Phiếu chi | `CP` |
| `BR` | Giấy báo Có | `BR` |
| `BP` | Giấy báo Nợ | `BP` |
| `UNC` | Ủy nhiệm chi (lệnh thanh toán) | `UNC` |
| `TU` | Đề nghị tạm ứng | `TU` |
| `KKQ` | Biên bản kiểm kê quỹ | `KKQ` |
| `LIA` | Trích lãi vay | `LIA` |
| `CTC` | Phương án chia cổ tức, lợi nhuận | `CTC` |

**Mua hàng**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `DMH` | Đơn mua hàng | `ORD-DMH` |
| `HDKM` | Hợp đồng mua | `CTR-PUR` |
| `HDM` | Hóa đơn mua | `INV-HDM` |
| `HMTL` | Hóa đơn mua trả lại | `INV-HMTL` |
| `HMGG` | Hóa đơn mua giảm giá | `INV-HMGG` |
| `HMDT` | Hóa đơn mua điều chỉnh tăng | `INV-HMDT` |
| `PI` | Chứng từ ghi sổ hóa đơn mua | `PI` |
| `BBKN` | Biên bản kiểm nghiệm | `BBKN` |

**Bán hàng**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `DBH` | Đơn bán hàng | `ORD-DBH` |
| `HDKB` | Hợp đồng bán | `CTR-SAL` |
| `HDB` | Hóa đơn bán | `INV-HDB` |
| `HDTL` | Hóa đơn bán trả lại | `INV-HDTL` |
| `HDGG` | Hóa đơn bán giảm giá | `INV-HDGG` |
| `HDDT` | Hóa đơn bán điều chỉnh tăng | `INV-HDDT` |
| `LBH` | Lệnh bán hàng | `LBH` |
| `PGH` | Phiếu giao hàng | `PGH` |
| `YCTH` | Yêu cầu trả hàng bán | `YCTH` |
| `YCGG` | Yêu cầu giảm giá hàng bán | `YCGG` |
| `XND` | Phiếu xóa nợ khó đòi | `XND` |
| `SI` | Chứng từ ghi sổ hóa đơn bán | `SI` |

**Kho**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `PN` | Phiếu nhập kho | `STK-PN` |
| `PX` | Phiếu xuất kho | `STK-PX` |
| `DC` | Phiếu điều chuyển kho | `STK-DC` |
| `DP` | Phiếu đảo (kho) | `STK-DP` |
| `IR` | Chứng từ ghi sổ nhập kho | `IR` |
| `II` | Chứng từ ghi sổ xuất kho | `II` |
| `IC` | Chứng từ tính giá xuất kho | `IC` |

**Sản xuất**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `LSX` | Lệnh sản xuất | `PROD-LSX` |

**Tài sản**

| Tiền tố | Chứng từ | Khóa bộ đếm |
|---|---|---|
| `DNMS` | Đề nghị mua sắm tài sản | `AST-DNMS` |
| `KKTS` | Phiếu kiểm kê tài sản | `AST-KKTS` |
| `YCSC` | Yêu cầu sửa chữa / báo sự cố | `AST-YCSC` |
| `SCTS` | Phiếu thi công sửa chữa | `AST-SCTS` |
| `DEP` | Khấu hao, phân bổ | `DEP` |
| `FAD` | Ghi giảm tài sản | `FAD` |
