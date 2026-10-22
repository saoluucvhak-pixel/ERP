#!/usr/bin/env bash
# npm audit cho CI: lỗ hổng mức high trở lên luôn làm hỏng CI; máy chủ audit của npm lỗi tạm (503, hết giờ, mất mạng)
# thì thử lại tối đa 3 lần (4 lần chạy), vì lỗi đó không nói gì về mã nguồn (ci #333: 503 từ registry.npmjs.org).
# Chạy trong thư mục web/. Biến môi trường để test: NPM (mặc định npm), AUDIT_RETRY_DELAY (giây chờ cơ sở, mặc định 20).
set -uo pipefail

npm_cmd="${NPM:-npm}"
delay="${AUDIT_RETRY_DELAY:-20}"
retries=3
out="$(mktemp)"
trap 'rm -f "$out"' EXIT

# Dấu hiệu lỗi tạm phía máy chủ / mạng. Không khớp mẫu nào mà vẫn lỗi thì coi là lỗi thật (lỗ hổng hoặc lỗi khác) và dừng ngay.
transient='audit endpoint returned an error|Service Unavailable|Bad Gateway|Gateway Time-?out|Internal Server Error|\b50[0-9]\b|ETIMEDOUT|ESOCKETTIMEDOUT|ECONNRESET|ECONNREFUSED|EAI_AGAIN|ENOTFOUND|socket hang up|network (request|timeout)'
# Kết quả quét thật: npm in bảng lỗ hổng / dòng tổng kết. Có thì không thử lại dù kèm lỗi mạng ở chỗ khác.
findings='[0-9]+ (low|moderate|high|critical) severity vulnerabilit|^# npm audit report|Severity: (high|critical)'

for attempt in $(seq 1 $((retries + 1))); do
  # Mỗi lần chạy giới hạn 60 giây và không để npm tự thử lại bên trong (trước đây một lần chờ gần 3 phút).
  "$npm_cmd" audit --audit-level=high --fetch-retries=0 --fetch-timeout=60000 >"$out" 2>&1
  code=$?
  cat "$out"
  if [ "$code" -eq 0 ]; then
    exit 0
  fi
  if grep -qE "$findings" "$out"; then
    echo "::error::npm audit: có gói phụ thuộc với lỗ hổng mức high trở lên."
    exit "$code"
  fi
  if ! grep -qiE "$transient" "$out"; then
    echo "::error::npm audit lỗi (mã $code) không phải lỗi tạm của máy chủ npm."
    exit "$code"
  fi
  if [ "$attempt" -gt "$retries" ]; then
    echo "::error::Máy chủ npm audit vẫn lỗi sau $retries lần thử lại; chạy lại job khi npm hoạt động bình thường."
    exit "$code"
  fi
  wait=$((delay * attempt))
  echo "::warning::Máy chủ npm audit lỗi tạm (lần $attempt/$((retries + 1))); thử lại sau ${wait} giây."
  sleep "$wait"
done
