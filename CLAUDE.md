# CLAUDE.md — MCG_CreateBoundary

> File này là "hiến pháp" của project, Claude Code đọc đầu mỗi phiên. Đây là bản dành riêng cho
> `MCG_CreateBoundary` — KHÔNG phải bản copy từ project TOC (MCGCadPlugin). Hai project độc lập,
> khác kiến trúc, khác namespace, khác mục đích.

## 1. Project là gì

Plugin AutoCAD .NET (C#, .NET Framework 4.8, x64) — tự động **tạo Plate (region kín)** từ tập hợp
đường line/arc/polyline rời rạc do user vẽ (khung bao + đường chia cắt), tính diện tích, trọng tâm
(COG), gắn nhãn MText, tùy chọn đục lỗ (subtract hole), xuất Excel.

Không liên quan gì đến project `MCGCadPlugin` (Table of Contents) — khác repo, khác namespace gốc,
khác PaletteSet, khác GUID.

## 2. Namespace convention — BẮT BUỘC tuân thủ

Gốc namespace: `MCG_CreateBoundary`. Layer con:

| Layer | Namespace | Ghi chú |
|---|---|---|
| Commands | `MCG_CreateBoundary.Commands` | |
| UI (kết nối Palette) | `MCG_CreateBoundary.UI` | `PaletteConnector` |
| ViewModel | `MCG_CreateBoundary.ViewModels` | |
| Views (WPF) | `MCG_CreateBoundary.Views` | |
| Models | `MCG_CreateBoundary.Models` | |
| Services | `MCG_CreateBoundary.Services` | `SplitBoundary`, `BoundaryUtils`, `FlattenUtils` |
| Services (lõi thuật toán) | `MCG_CreateBoundary` (namespace gốc, KHÔNG có `.Services`) | `CreateBoundary`, `Case1_BasicSplit`, `Case2_ExtensionSplitLine`, `Case3_EndpointBridging` |

Lưu ý: `Case1/2/3` và `CreateBoundary` (helper vẽ/XData) nằm ở namespace gốc `MCG_CreateBoundary`,
KHÔNG phải `MCG_CreateBoundary.Services` — đây là quyết định thiết kế đã có sẵn (không phải lỗi),
giữ nguyên khi thêm code mới cùng nhóm.

**Lịch sử:** repo này được đổi tên từ project cũ tên `GetPropsTool`. Đã xảy ra bug build-breaking vì
2 file (`CreateBoundaryCommand.cs`, `BoundaryView.xaml(.cs)`) sót lại namespace `GetPropsTool.*` — đã
fix. Nếu tạo file mới bằng cách copy/paste từ file cũ, LUÔN kiểm tra lại namespace/using trước khi
build.

## 3. Hằng số quan trọng (không đổi tùy tiện — nếu đổi phải có lý do rõ, ghi vào SESSION_LOG.md)

```csharp
// CreateBoundary.cs
RegAppName      = "MCG_PLATE_DATA"        // XData tag để nhận diện Plate đã tạo
TargetLayer     = "Mechanical-AM_5"       // Layer vẽ Plate + label + hole
BaseCogLayer    = "Mechanical-AM_8"       // Layer vẽ block gốc tọa độ CNC
CogBlockName    = "COG Block"             // Block đánh dấu trọng tâm
BaseCogBlockName= "BaseCOG_Block"         // Block đánh dấu gốc tọa độ

// PaletteConnector.cs
Palette name    = "MCG Boundary Manager"
Palette GUID    = D23B5A6F-7C4E-4B12-9D8A-C7F4E6A3B123   // KHÔNG đổi — user mất vị trí dock đã quen
Palette ID str  = "SplitBoundary_Palette_V1"
Tab             = 1 tab duy nhất, tên "Plates"

// SplitBoundary.cs
ErrorLayerName  = "MCG_ERROR_GAPS"        // Layer tạm đánh dấu khe hở khi Option 2 không khép kín được
```

Ngưỡng "giant plate" (`areaM2 > 20.0`) xuất hiện 2 lần độc lập trong `BoundaryUtils.ProcessRegionToPlate`
(gating category auto-detect + format text/COG block to hơn) — nếu sửa, sửa cả 2 chỗ hoặc gom thành 1
named constant.

## 4. Quy tắc code bắt buộc

- Comment/log tiếng Việt cho method quan trọng (đúng phong cách hiện có trong `SplitBoundary.cs`).
- **KHÔNG swallow exception (catch rỗng) ở logic quyết định kết quả nghiệp vụ** (ví dụ: chọn region
  nào là hole, region nào là plate hợp lệ). Được phép swallow ở thao tác phụ trợ không ảnh hưởng kết
  quả cuối (ví dụ: `try { ext = c.GeometricExtents } catch {}` khi chỉ dùng để tính bounding box ước
  lượng) — nhưng phải có comment giải thích vì sao an toàn để bỏ qua.
- Không đổi GUID PaletteSet, không đổi `RegAppName` (XData) — mọi bản vẽ cũ đang gắn dữ liệu theo tag
  này, đổi sẽ làm SYNC DATA không đọc được Plate cũ.
- Không tự ý gọi lại `FilterInnerMost` (trong `BoundaryUtils.cs`) mà không thảo luận trước — hàm này
  hiện là dead code (không được gọi ở đâu), lý do kỹ thuật xem mục 6.

## 5. Luồng nghiệp vụ (2 chế độ tạo Plate)

**Option 1 — Split Lines** (`SplitBoundary.RunMethodSplitLines`): quét chọn 1 lần toàn bộ khung bao +
đường chia → chạy lõi thuật toán (mục 6) → phân loại hole nếu bật `IsSubtractHole` → mỗi region hợp
lệ thành 1 Plate cùng lúc.

**Option 2 — Pick Point** (`SplitBoundary.RunMethodPickPoint`): quét chọn vùng làm việc 1 lần, sau đó
user click từng điểm; mỗi click tự tính vùng cục bộ quanh điểm bằng ray-cast 4 hướng
(`BoundaryUtils.GetLocalIds`, tối ưu hiệu năng), chạy lõi thuật toán chỉ trên tập cục bộ, tìm region
chứa điểm click, tạo 1 Plate. Nếu không khép kín được, dò các "dangling gap" gần chuột
(`BoundaryUtils.FindLocalGaps`) và vẽ vòng tròn đỏ tạm trên layer `MCG_ERROR_GAPS` để user tự nối.

Dữ liệu Plate không lưu cache JSON — sống hoàn toàn trong XData (`RegAppName`) gắn trên Polyline.
`SYNC DATA` quét lại `ModelSpace` để dựng lại bảng. Đánh số `No` = `GetLastNumber(db) + 1` (không
trùng dù mở lại phiên).

## 6. Lõi thuật toán dựng region — chuỗi fallback 3 tầng

```
Case1_BasicSplit.GetRegions(curves)          // tầng 1: shatter tại giao điểm thực + Region.CreateFromCurves gốc AutoCAD
  → nếu Count == 0 →
Case2_ExtensionSplitLine.GetRegions(curves)  // tầng 2: ray-cast nối khe hở nhỏ + tự dựng graph, duyệt vòng kín (right-hand rule)
  → nếu Count == 0 →
Case3_EndpointBridging.GetRegions(curves)    // tầng 3: bridging điểm mồ côi trong bán kính 5000 đơn vị, rồi gọi lại Case1→Case2
```

### ⚠️ VẤN ĐỀ ĐÃ BIẾT — điều kiện fallback sai (đang thảo luận hướng sửa, CHƯA code)

Điều kiện chuyển tầng chỉ dựa vào `regs.Count == 0`. Nhưng khung bao ngoài luôn tự nó là 1 region hợp
lệ với `Region.CreateFromCurves`, **bất kể** đường chia bên trong có chạm khít biên hay không. Hệ quả:

- Nếu đường chia không chạm khít biên (dù chỉ lệch rất nhỏ) → Case1 trả `Count = 1` (không phải 0)
  → Case2/Case3 (vốn thiết kế đúng để xử lý khe hở này) **không bao giờ được gọi**.
- Case1/Case2 hiện **chủ động KHÔNG lọc** region bao thừa (comment `"ĐÃ XÓA FilterInnerMost: Trả về
  TẤT CẢ các miền kín"` trong cả 2 file) — filter duy nhất còn hoạt động nằm ở tầng orchestration
  (`SplitBoundary.RunMethodSplitLines`, dòng ~136-156), CHỈ chạy khi `vm.IsSubtractHole == true`, và
  dùng cách so sánh "tâm B nằm trong A" để quyết định A không phải Plate. Cách này **không phân biệt
  được** giữa lỗ thật (region nhỏ có khoảng vật liệu thật xung quanh) và mảnh chia đôi khít nhau
  (2 region con lấp đầy vừa đủ region cha) — cả 2 trường hợp đều "tâm B nằm trong A" là true.
  → Nếu tắt `IsSubtractHole`: khi đường chia chạm khít, ra dư 1 region bao thừa.
  → Nếu bật `IsSubtractHole`: khi đường chia chạm khít, có nguy cơ bị loại NHẦM cả 2 mảnh con
    (coi là "hole" của region bao), giữ lại đúng cái region bao — ngược hoàn toàn với mong muốn.

**Hàm `FilterInnerMost`** (`BoundaryUtils.cs`) vẫn còn nguyên vẹn nhưng là dead code — không được gọi
ở đâu. Trước khi gọi lại, cần thêm tiêu chí phân biệt hole thật vs mảnh chia (gợi ý: so sánh
`Area(cha) ≈ Σ Area(con)` — nếu đúng bằng, đó là "mảnh chia", luôn loại region cha; nếu con nhỏ hơn
nhiều so với cha và các con khác không lấp đầy hết cha, đó là "hole" thật).

**Chưa quyết định hướng sửa cuối cùng** — xem SESSION_LOG.md mục "Đang thảo luận" trước khi code.

## 7. Quy tắc đồng bộ Claude web ↔ Claude Code (BẮT BUỘC)

Project này được thiết kế/bàn bạc song song trên Claude web (kiến trúc, quyết định) và Claude Code
trong VS Code (thực thi). Để 2 bên không bị lệch trạng thái:

- Nếu trong lúc code phát hiện phải đổi khác so với kế hoạch đã bàn (dù nhỏ), **ghi rõ lý do vào
  `SESSION_LOG.md` mục `### Phát sinh ngoài kế hoạch`** trước khi commit. Không tự âm thầm sửa mà
  không ghi chú.
- Cuối mỗi phiên: cập nhật `SESSION_LOG.md` (đã làm gì, trạng thái, bước tiếp theo) rồi `git commit`
  + `git push`. Đây là "điểm nối" duy nhất để phiên Claude web sau đọc lại đúng trạng thái thật.
