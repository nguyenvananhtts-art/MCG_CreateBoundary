# MCG_CreateBoundary

Plugin AutoCAD .NET (C#, .NET Framework 4.8) — tự động tạo boundary (Plate) khép kín từ tập hợp
đường line/arc/polyline rời rạc, tính diện tích và trọng tâm (COG), gắn nhãn, tùy chọn đục lỗ và
xuất Excel.

## Cài đặt

1. Build project (`dotnet build -c Release` hoặc mở bằng Visual Studio, build cấu hình Release).
2. Trong AutoCAD, chạy `NETLOAD` và trỏ tới file `.dll` output.

## Lệnh

| Lệnh | Chức năng |
|---|---|
| `MCG_CreateBoundary` | Mở palette "MCG Boundary Manager" |

## Sử dụng

Palette có 2 chế độ tạo Plate, chọn bằng radio button:

- **Split Lines**: quét chọn khung bao + toàn bộ đường chia trong 1 lần, tool tự tách thành các Plate
  khép kín.
- **Pick Point**: quét chọn vùng làm việc, sau đó click từng điểm để tạo Plate lần lượt tại vị trí đó.

Các tùy chọn:
- **Create Text**: tạo nhãn MText tên Plate tại tâm.
- **Delete Original**: xóa các đường gốc sau khi tạo Plate.
- **Insert COG**: chèn điểm gốc tọa độ CNC và block đánh dấu trọng tâm mỗi Plate.
- **Subtract Hole**: nếu vùng chọn có các region lồng nhau, tự động đục lỗ (đang có vấn đề đã biết —
  xem `CLAUDE.md`).

Nút **SYNC DATA** quét lại bản vẽ, dựng lại bảng dữ liệu. Nút **EXCEL** xuất bảng ra file Excel.

## Tài liệu phát triển

Xem `CLAUDE.md` (quy tắc/kiến trúc bắt buộc), `CONTEXT.md` (tổng quan hiện trạng), `SESSION_LOG.md`
(nhật ký phát triển) — dành cho người phát triển tiếp/Claude Code, không cần đọc để dùng plugin.
