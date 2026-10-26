# Chống gửi lại thao tác của phiên đăng nhập cũ

Đợt tiếp theo sau main `2640519b84c85c8ce782d1d8d73901447aefb5a6` (PR #65). Phạm vi thực thi đợt này là retry phía web; hai nâng cấp nghiệp vụ tiếp theo nằm trong `2026-10-26-next-business-prompts.md`, chưa được coi là đã triển khai.

## Lỗi đã tái hiện

Một yêu cầu ghi mất phản hồi có thể hoàn tất sau khi người dùng đăng xuất. Callback cũ đưa thao tác trở lại `hak.pending`. Bản cũ chỉ lưu nội dung/khóa/công ty, rồi khi tải lại trang gắn token đang đăng nhập vào mọi thao tác cũ. Trong khi đó server giới hạn khóa chống trùng theo token và công ty; cùng khóa dưới token mới không còn là cùng phạm vi chống trùng. Có nguy cơ gửi dữ liệu phiên cũ bằng quyền của tài khoản mới hoặc tạo chứng từ lần hai.

Kiểm thử cũ còn chấp nhận khôi phục thao tác của `T-old` bằng `T-new`; đã thay bằng yêu cầu từ chối. Ngoài ra client trước đây tự retry cả đường dẫn quản trị/mật khẩu/sao lưu dù server chủ động loại chúng khỏi idempotency.

## Cơ chế mới

- Mỗi lần đăng nhập có nonce riêng. Đăng xuất rồi đăng nhập lại dù token có cùng giá trị vẫn là chủ sở hữu khác.
- Dữ liệu chờ phiên bản 2 lưu nonce và SHA-256 của token, không lưu token thô trong hàng chờ. Chỉ khôi phục nếu cả hai trùng; không chuyển thao tác sang phiên mới.
- Lưu khóa/nội dung/ngữ cảnh trước lần gửi đầu để tải lại trang khi đang chờ phản hồi vẫn có thể đối soát bằng đúng khóa. Chỉ các thao tác mất phản hồi mới hiện nút thử lại; lưu bình thường không hiện cảnh báo chưa xác định.
- Đổi công ty làm dừng các lần tự gửi tiếp. Thao tác chưa xác định vẫn thuộc công ty cũ trong cùng phiên; chỉ thử lại khi quay về đúng công ty. Đổi token xóa nội dung chờ của phiên cũ, callback muộn không được thêm lại hoặc xóa hàng chờ của phiên mới.
- Phản hồi thành công muộn không được đưa dữ liệu cũ vào màn hình công ty/phiên mới. Phản hồi 401 cũ không đăng xuất phiên mới.
- Nhiều lần bấm thử lại cùng một thao tác dùng chung một lần thực thi. Nội dung JSON đã chụp, công ty và khóa không thay đổi khi sửa biểu mẫu.
- Yêu cầu auth/setup, users, backups, me, exchange keys không có tự retry/khóa idempotency và không lưu nội dung vào hàng chờ. Kiểm tra đường dẫn có chuẩn hóa dấu chấm và mã hóa URI. `postRaw` giữ nguyên byte và chỉ gửi một lần.
- Mất phần thân phản hồi, HTTP 5xx và JSON thành công bị hỏng được coi là kết quả chưa xác định; thử lại dùng cùng khóa. Từ chối nghiệp vụ rõ ràng vẫn kết thúc hàng chờ.
- Không thử lại bản ghi đã đủ 6 ngày hoặc có thời gian bất thường; server hiện giữ idempotency 7 ngày. Bản ghi cũ không chứng minh được chủ sở hữu, bản ghi hỏng và nội dung thuộc đường dẫn nhạy cảm bị loại, chỉ giữ nhắc nhở đối chiếu chung.
- Giao diện cho xác nhận đã đọc nhắc nhở, không gọi đó là xác nhận đã lưu. Nút thử lại bị khóa khi đang chạy và chỉ báo thành công khi server thật sự xác nhận.

SHA-256 dùng dependency khóa phiên bản `@noble/hashes@2.4.0`, hoạt động cả truy cập HTTP trong LAN khi `crypto.subtle` không có. Đây là kiểm tra phạm vi token, không phải mã hóa dữ liệu nghiệp vụ.

## Kiểm chứng

- Thêm 13 kiểm thử ownership: callback sau logout, token ABA, 401 muộn, callback cũ không phá hàng chờ mới, đổi công ty + tải lại, reload trong lần gửi đầu, khôi phục đúng phiên, retry đồng thời, token trong storage bị đổi, đường dẫn nhạy cảm, dữ liệu cũ/hết hạn, phản hồi hỏng và signed raw.
- E2E bằng trình duyệt thật/API SQLite tạm: cho server ghi thành công, chặn phản hồi ở trình duyệt, đổi tài khoản và tải lại; hàng chờ không phục hồi, không gửi lại, chỉ có một đối tượng đã lưu. Thông báo đối chiếu vẫn xuất hiện.
- Toàn bộ backend 541/541 (126 domain + 415 SQLite), frontend 77/77, E2E 15/15, build đạt; lint 0 lỗi/25 cảnh báo cũ. Không dùng cơ sở dữ liệu thật.

## Giới hạn vận hành

Đóng tab/xóa sessionStorage có thể mất hàng chờ. Đăng nhập lại không tự nhận lại quyền replay của phiên cũ; phải đối chiếu danh sách/audit bằng đúng công ty trước khi nhập lại. Nhắc nhở không khẳng định thao tác đã thành công hoặc thất bại. Không sửa dữ liệu đã ghi sổ hay migration database trong đợt này.
