# SESSION_LOG.md — MCG_CreateBoundary

> Nhật ký từng phiên làm việc, entry mới nhất ở ĐẦU file. Đây là điểm nối trạng thái giữa Claude web
> và Claude Code — luôn cập nhật cuối mỗi phiên trước khi commit/push.

---

## Session 2026-07-23 (Claude web) — Phân tích logic 3-case, chưa code

### Đã làm
- Đọc lại toàn bộ `SplitBoundary.cs`, `BoundaryUtils.cs`, `Case1_BasicSplit.cs`,
  `Case2_ExtensionSplitLine.cs`, `CreateBoundary.cs`, `PaletteConnector.cs`, `BoundaryViewModel.cs`.
- Phân tích nguyên nhân 3 hiện tượng lỗi user báo cáo (test với hình PL-198 phức tạp + 2 hình chữ
  nhật đơn giản):
  - Case 1 & Case 3: chỉ ra 1 hình bao ngoài, không chia nhỏ theo đường chia cắt.
  - Case 2: ra dư 1 hình bao ngoài thay vì chỉ 2 hình nhỏ bên trong.
- Kết luận nguyên nhân gốc (2 vấn đề độc lập, xem chi tiết CLAUDE.md mục 6):
  1. Điều kiện chuyển tầng fallback `regs.Count == 0` sai — khung bao ngoài luôn tự nó là 1 region
     hợp lệ nên Case1 gần như không bao giờ trả về `Count == 0`, khiến Case2/Case3 không được gọi dù
     đường chia có khe hở nhỏ không chạm khít biên.
  2. Logic phân biệt "hole thật" vs "mảnh chia đôi khít nhau" chưa đủ (chỉ dựa centroid-containment,
     gated bởi `IsSubtractHole`) — xác nhận `FilterInnerMost` trong `BoundaryUtils.cs` là dead code,
     không được gọi ở đâu.

### Trạng thái
- Chỉ mới thảo luận, **CHƯA sửa code** cho 2 vấn đề trên.
- Đã tạo/thay thế 4 file tài liệu (`CLAUDE.md`, `CONTEXT.md`, `SESSION_LOG.md` này, `Readme.md`) cho
  đúng project `MCG_CreateBoundary` — trước đó là bản copy nguyên từ project TOC (MCGCadPlugin), nội
  dung sai hoàn toàn (mô tả nhầm kiến trúc 2-tab, module DetailDesign/FittingManagement/PanelData/Weight
  không tồn tại ở project này).

### Bước tiếp theo (đang thảo luận, chưa chốt hướng)
- Quyết định tiêu chí thay thế `Count == 0` cho điều kiện fallback (gợi ý đang cân nhắc: so sánh tổng
  diện tích các region trả về với diện tích khung ngoài lớn nhất, hoặc kiểm tra đường nào chưa được
  "tiêu thụ" vào region nào).
- Quyết định cách phân biệt hole thật vs mảnh chia (gợi ý đang cân nhắc: `Area(cha) ≈ Σ Area(con)`).
- Sau khi chốt hướng trên web → viết task-prompt cụ thể cho Claude Code thực thi.

### Ghi chú API / hằng số cần giữ nguyên
- Không đổi GUID PaletteSet `D23B5A6F-7C4E-4B12-9D8A-C7F4E6A3B123`.
- Không đổi `RegAppName = "MCG_PLATE_DATA"` (XData) — Plate cũ trong bản vẽ thật đang gắn theo tag này.

---

## Session 2026-07-23 (Claude web) — Sửa lỗi namespace build-breaking

### Đã làm
- Phát hiện lỗi: 3 file còn sót namespace `GetPropsTool.*` (tàn dư từ trước khi project được đổi tên
  từ `GetPropsTool` sang `MCG_CreateBoundary`, xem commit "Rename project to MCG_CreateBoundary"):
  - `Commands/CreateBoundary/CreateBoundaryCommand.cs`
  - `Views/CreateBoundary/BoundaryView.xaml`
  - `Views/CreateBoundary/BoundaryView.xaml.cs`
- Sửa cả 3 file: namespace + using → `MCG_CreateBoundary.*`, thêm `using MCG_CreateBoundary.Services;`
  còn thiếu ở 2 file (cần cho lời gọi `SplitBoundary.*`).
- Xuất patch `fix_namespace.patch` cho user áp dụng bằng `git apply`.

### Trạng thái
- Namespace nhất quán toàn repo (đã grep xác nhận không còn "GetPropsTool" ở đâu).
- CHƯA build thật (môi trường phân tích không có `dotnet`/AutoCAD SDK) — cần user build xác nhận trên
  máy thật.

### Bước tiếp theo
- User build lại `dotnet build -c Debug` trên máy có AutoCAD SDK, xác nhận pass.
