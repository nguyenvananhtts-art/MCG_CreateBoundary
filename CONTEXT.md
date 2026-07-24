# CONTEXT.md — MCG_CreateBoundary

> Bức tranh tổng quan hiện trạng, cập nhật khi kiến trúc thay đổi (không phải nhật ký từng phiên —
> xem SESSION_LOG.md cho việc đó).

## Cấu trúc file

```
MCG_CreateBoundary/
├── Commands/CreateBoundary/CreateBoundaryCommand.cs   # 2 lệnh CAD
├── UI/CreateBoundary/
│   ├── PaletteConnector.cs      # Singleton static, quản lý PaletteSet
│   └── BoundaryViewModel.cs     # INotifyPropertyChanged, binding cho View
├── Views/CreateBoundary/
│   ├── BoundaryView.xaml        # UserControl WPF
│   └── BoundaryView.xaml.cs     # code-behind: nút CREATE/SYNC/EXCEL, DataGrid selection
├── Models/CreateBoundary/BoundaryData.cs   # No, PlateName, Area, XCog, YCog, Category, Id
└── Services/CreateBoundary/
    ├── SplitBoundary.cs         # orchestration: RunMethodSplitLines, RunMethodPickPoint, ScanDocument
    ├── BoundaryUtils.cs         # ShatterCurves, CleanNetwork, IsPointInRegion, ProcessRegionToPlate...
    ├── FlattenUtils.cs          # chuẩn hóa curve 3D -> 2D, explode polyline thành segment
    ├── CreateBoundary.cs        # helper vẽ (MText, block COG), AddXData/HasOurXData, hằng số
    ├── Case1_BasicSplit.cs      # tầng 1: shatter + Region.CreateFromCurves gốc AutoCAD
    ├── Case2_ExtensionSplitLine.cs  # tầng 2: ray-cast nối khe hở + graph walk
    └── Case3_EndpointBridging.cs    # tầng 3: bridging điểm mồ côi
```

## Lệnh CAD

- `MCG_CreateBoundary` → `PaletteConnector.ShowPalette()` — mở palette (tạo mới nếu chưa có, singleton).
- `MCG_INTERNAL_CREATE` (Modal) → gọi nội bộ bởi nút "CREATE" trong palette qua `SendStringToExecute`,
  chạy `SplitBoundary.ExecuteCreate`.

## Luồng dữ liệu end-to-end (Option 1 — Split Lines)

1. User quét chọn khung bao + đường chia trong bản vẽ.
2. (Nếu bật Insert COG) user pick 1 điểm gốc tọa độ CNC → vẽ `BaseCOG_Block`.
3. `FlattenUtils.FlattenAndExtractCurves` chuẩn hóa tập curve đã chọn (project 3D→2D, explode).
4. Chạy fallback 3 tầng Case1→Case2→Case3 (chi tiết + vấn đề đã biết: xem CLAUDE.md mục 6).
5. Nếu `IsSubtractHole`: phân loại region nào là hole bằng centroid-containment (xem CLAUDE.md — có
   vấn đề chưa xử lý triệt để).
6. Với mỗi region hợp lệ: `BoundaryUtils.ProcessRegionToPlate` — vẽ Polyline biên trên layer
   `Mechanical-AM_5`, trừ hole (Boolean subtract) nếu có, gắn XData `MCG_PLATE_DATA`, tự nhận diện
   Category qua block "Cat XXX" (chỉ khi diện tích > 20 m²), vẽ MText nhãn + block COG.
7. (Tùy chọn) xóa các curve gốc đã chọn.
8. `PaletteConnector.SyncData()` → quét lại toàn bộ ModelSpace, dựng lại DataGrid.

## Trạng thái hiện tại

- **Build**: đã sửa lỗi namespace `GetPropsTool.*` → `MCG_CreateBoundary.*` (3 file: `CreateBoundaryCommand.cs`,
  `BoundaryView.xaml`, `BoundaryView.xaml.cs`). Chưa build thật trên máy có AutoCAD SDK để xác nhận cuối.
- **Vấn đề nghiệp vụ đang mở** (xem chi tiết CLAUDE.md mục 6, SESSION_LOG.md mục "Đang thảo luận"):
  điều kiện fallback `Count == 0` không đúng, khiến Case2/Case3 gần như không bao giờ chạy khi khung
  bao đã kín sẵn (rất phổ biến); và logic phân biệt "hole thật" vs "mảnh chia đôi" chưa đủ chính xác.
- Chưa có unit test.

## Điểm khác biệt quan trọng so với project MCGCadPlugin (TOC)

Hai project độc lập hoàn toàn — không dùng chung namespace, không dùng chung PaletteSet GUID, không
dùng chung cơ chế lưu trữ (TOC dùng cache JSON `C:\CustomTools\Temps\`, project này dùng XData thuần).
Nếu copy tài liệu/pattern từ TOC sang, phải viết lại nội dung, không chỉ đổi tên.
