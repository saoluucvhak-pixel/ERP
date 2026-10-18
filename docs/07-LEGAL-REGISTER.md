# 07 — Sổ đăng ký xác minh pháp lý

Quy ước: **Đã xác minh** = đã đọc văn bản gốc và có người ký xác nhận; **Đã tra cứu thứ cấp** = chỉ đọc qua nguồn tổng hợp (báo, trang tư vấn), chưa đủ để cấu hình; **Chưa tra cứu**.

| # | Nội dung | Phục vụ | Trạng thái | Ghi chú |
|---|----------|---------|-----------|---------|
| L-01 | TT 99/2025/TT-BTC: ban hành 27/10/2025; áp dụng năm tài chính bắt đầu từ/sau 01/01/2026; thay thế TT 200/2014/TT-BTC; Phụ lục I chứng từ, II tài khoản, III sổ, IV mẫu BCTC | REG | **Đã có văn bản gốc** (16/10/2026) | Bản PDF có chữ ký số (778 trang) và Công báo bản Word số 1563–1582 (đủ), do người dùng cung cấp. Điều 31: hiệu lực 01/01/2026. Mã băm và phạm vi: `legal/TT99-2025/README.md` |
| L-02 | Hệ thống tài khoản (Phụ lục II TT99) | CAT, REG | **Đã trích từ bản gốc, chờ người ký xác minh** | 184 TK (71 cấp 1). Trích từ bản Word, đối chiếu từng trang với PDF ký số (trang 93–99): khớp. Kết quả: `legal/TT99-2025/phu-luc-II-A-danh-muc-tai-khoan.csv`. Tệp nhập `nhap-he-thong-tk-de-xuat.csv` có kiểm thử tự động. Không tự seed vào công ty nào; doanh nghiệp tự nhập, kế toán trưởng xác minh |
| L-03 | Mẫu BCTC (Phụ lục IV TT99): Báo cáo tình hình tài chính, KQHĐKD, LCTT, Thuyết minh | RPT | Có bản gốc mẫu biểu (Công báo 1577 + 1578) | Mẫu B01-DN, B02-DN, B03-DN, B09-DN (và bản DNKLT). Hướng dẫn lập B03 và B02 từ Mã số 70 có trong số 1581 + 1582. Đã nhận đủ (16/10/2026). **Mẫu B01-DN (126 dòng) và B02-DN (21 dòng) đã dựng thành công thức** trong `legal/TT99-2025/mau-B0x-DN.json`, có kiểm thử. Đã dựng thêm **B03-DN (trực tiếp)**, 30 dòng, dùng mã luồng tiền. Chờ kế toán trưởng xác minh. Chưa làm: B03 gián tiếp, B09 |
| L-04 | Điều kiện tối thiểu đối với phần mềm kế toán theo TT99 | NFR | **Đã đối chiếu bản gốc (Điều 28)** | Các yêu cầu: (a) tuân thủ pháp luật; (b) chính xác, không trùng lặp, sửa phải lưu dấu vết theo thời gian; (c) bảo mật, cảnh báo hoặc ngăn chặn việc cố ý can thiệp số liệu đã ghi sổ; (d) cung cấp dữ liệu đầu ra; (đ) kết nối hóa đơn điện tử, chữ ký số; (e) nâng cấp được. HAK đáp ứng (b)(c) bằng sổ cái bất biến, đảo/điều chỉnh, trigger và nhật ký băm; (đ) còn hạn chế (hóa đơn điện tử qua tệp, chưa có chữ ký số) |
| L-05 | Thời hạn nộp BCTC năm (nguồn thứ cấp: 90 ngày) | RPT | **Đã đối chiếu bản gốc (Điều 25)** | BCTC năm nộp chậm nhất 90 ngày kể từ ngày kết thúc kỳ kế toán năm |
| L-06 | Chuyển đổi đồng tiền hạch toán (TT99, tỷ giá mua bán chuyển khoản trung bình) | FX, SYS | **Đã đối chiếu bản gốc (Điều 4–6)** | Tỷ giá mua bán chuyển khoản trung bình của ngân hàng thương mại nơi doanh nghiệp thường xuyên giao dịch; BCTC công bố và kiểm toán phải trình bày bằng VND |
| L-07 | Chế độ kế toán DNNVV (TT 133/2016 và văn bản thay thế/sửa đổi nếu có) | REG | Chưa tra cứu | |
| L-08 | Tỷ giá áp dụng theo loại giao dịch, đánh giá lại cuối kỳ | FX | Chưa tra cứu | |
| L-09 | Hóa đơn điện tử: cấu trúc XML, khóa trùng, thay thế/điều chỉnh | TAX | Chưa tra cứu | Nghị định về hóa đơn chứng từ hiện hành + văn bản sửa đổi |
| L-10 | Luật Kế toán: kỳ kế toán năm đầu/cuối tối đa bao nhiêu tháng, lưu trữ chứng từ điện tử | SYS | Chưa tra cứu | |
| L-11 | Phương pháp tính giá xuất kho được phép | INV | Chưa tra cứu | |
| L-12 | Ghi nhận doanh thu (thời điểm) theo TT99/chuẩn mực | SAL | Chưa tra cứu | |
| L-13 | Khấu hao TSCĐ (khung thời gian, phương pháp) | FA | Chưa tra cứu | |
| L-14 | Định dạng mã số thuế hiện hành | SYS | Chưa tra cứu | |

Quy trình đưa một mục sang “Đã xác minh”: lưu bản sao văn bản gốc + SHA-256 → ghi `legal_sources` → nhập cấu hình → chạy bộ test của cấu hình → kế toán trưởng ký → `VerifyRegimeVersion` (hệ thống chặn ghi sổ Production nếu chưa bước này).
