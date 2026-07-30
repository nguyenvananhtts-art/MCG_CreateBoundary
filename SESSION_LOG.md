# SESSION_LOG.md — MCG_CreateBoundary

> Nhật ký từng phiên làm việc, entry mới nhất ở ĐẦU file. Đây là điểm nối trạng thái giữa Claude web
> và Claude Code — luôn cập nhật cuối mỗi phiên trước khi commit/push.

---

## Session 2026-07-24 (Claude Code) — Bỏ gate checkbox trừ lỗ + sửa tiêu chí bắn tia Case2

### Đã làm
Hai thay đổi độc lập, có bằng chứng log CAD thật (thảo luận kỹ trên Claude web trước khi code):

**Việc 1 — `BoundaryUtils.ProcessRegionToPlate`:**
- Đổi `if (vm.IsSubtractHole && holeCandidates != null)` → `if (holeCandidates != null)`.
- Lý do: RegionClassifier (tầng 2) đã xác định đâu là lỗ thật, nên việc trừ lỗ phải luôn tự động —
  đúng nghĩa "Automatically" trong tên checkbox. Trước đây checkbox tắt thì lỗ không bị trừ dù đã
  phân loại đúng là lỗ.
- Giữ nguyên toàn bộ logic bên trong (Boolean subtract, vẽ viền đỏ, tính lại Area/COG).
- Checkbox `IsSubtractHole` trong XAML/ViewModel **vẫn giữ nguyên** (chỉ ngắt kết nối, dành quyết định
  sau) — không xóa.

**Việc 2 — `Case2_ExtensionSplitLine.FireRayWithMidpointCheck`:**
- Đổi tiêu chí chọn `bestHit`: từ điểm cắt có `distToMid` (gần midpoint đường chia) nhỏ nhất → sang
  điểm cắt có `distFromOrigin` (gần gốc tia) nhỏ nhất = chướng ngại vật ĐẦU TIÊN dọc theo tia.
- Giữ nguyên bộ lọc `distFromOrigin > 1e-3` (tránh tự chạm chính nó).
- Lý do: tiêu chí cũ "gần midpoint" khiến tia vượt qua lỗ (tròn/chữ nhật) nằm sát đầu mút đường chia
  dở dang rồi nối nhầm ra biên xa, gây sai hình khi có lỗ gần đường chia.

### Trạng thái
- Build `dotnet build -c Debug` **PASS** (0 error, 0 warning CS). Warning MSB3061 duy nhất là do
  AutoCAD đang khoá DLL cũ, không liên quan thay đổi.
- **CHƯA test trong AutoCAD** (Claude Code không có AutoCAD) — cần user tự test 2 case dưới.

### Phát sinh ngoài kế hoạch
- Sau khi đổi Việc 2, tham số `midPoint` của `FireRayWithMidpointCheck` trở thành **không còn được
  dùng** (IDE báo Hint "Remove unused parameter"). Theo constraint "không refactor thêm, không đổi
  tên biến/hàm khác" → **cố ý giữ nguyên** tham số, không bỏ (bỏ sẽ phải đổi cả signature + call
  site, vượt phạm vi yêu cầu). Chỉ là Hint, không phải warning/error, không ảnh hưởng build.

### Giá trị kỳ vọng để user đối chiếu khi test
**TEST A — case lỗ chữ nhật (kỳ vọng hết lỗi):**
- Quét chọn: khung bao + đường chia dọc dở dang + 3 khấc lược + lỗ chữ nhật.
- Kỳ vọng: 2 region chính, tổng diện tích khớp khung ngoài (logic "khung bị chia"). KHÔNG còn region
  diện tích ~29177.6, KHÔNG còn hiện tượng cả khung sụp thành 1 TẤM duy nhất.

**TEST B — hồi quy hình PL-198 phức tạp (sai lệch >0.1% coi như FAIL, báo ngay):**
- Baseline (trước sửa, đã xác nhận đúng): 5 region rời, không lồng nhau:
  `16,105,747.7 | 9,963,112.7 | 9,195,149.5 | 8,695,822.2 | 6,519,922.9`
- Kỳ vọng sau sửa: log `[DBG]` ra đúng 5 giá trị trên (thứ tự có thể khác, tập giá trị phải khớp),
  KHÔNG ít/nhiều hơn 5 region, không region nào lồng nhau.

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
