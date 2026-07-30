# Bàn với Claude web — sửa tầng khép khe hở Case2 (đường chia dở dang chỉ khép 1 bên)

> Copy TOÀN BỘ file này dán sang Claude web. Đã gói đủ: bối cảnh, log CAD thật, chẩn đoán, code 2 hàm
> nghi vấn, và câu hỏi cần web trả lời.

---

## 1. Bối cảnh project (1 đoạn)

Plugin AutoCAD .NET (C#, .NET Framework 4.8). Chức năng: từ tập line/arc/polyline rời (khung bao +
đường chia + lỗ), tự dựng các Plate (region kín), tính diện tích/trọng tâm.

Lõi dựng region là chuỗi fallback 3 tầng:
- `Case1_BasicSplit` — shatter tại giao điểm + `Region.CreateFromCurves` gốc AutoCAD.
- `Case2_ExtensionSplitLine` — ray-cast kéo dài đường chia bị hở để khép khe, rồi tự dựng graph,
  duyệt vòng kín theo right-hand rule. **(File đang bàn.)**
- `Case3_EndpointBridging` — bắc cầu điểm mồ côi.

Điều phối (RegionSolver): chạy Case1; nếu có đầu mút hở thì chạy thêm Case2 và **giữ kết quả có nhiều
region hơn** (`KeepBetter`).

Sau khi có danh sách region, `RegionClassifier` dựng cây bao hàm và phân loại tấm/lỗ/khung-bị-chia.
Tiêu chí "khung bị chia": tổng diện tích các con trực tiếp ≥ 99% diện tích cha → cha là khung bị chia,
bỏ cha, xuất các con thành tấm.

---

## 2. Triệu chứng + LOG CAD THẬT

Hình test (TEST A): khung bao ngoài + **1 đường chia dọc DỞ DANG** (không chạm khít cả 2 biên) +
3 khấc lược + 1 lỗ chữ nhật nằm sát đầu mút đường chia.

Mong muốn: đường chia chia khung thành **2 tấm**; lỗ chữ nhật + 3 khấc bị trừ.

Thực tế: **sụp thành 1 tấm duy nhất.** Log `[DBG]` in ra:

```
Solver trả 6 region: [29177.6, 113104.5, 3795, 3795, 3795, 234556.4]
#0 area=234556.4 ROOT con=[1,2,3,4,5]   -> phân loại: là TẤM, lỗ=[1,2,3,4,5]
#1 area=113104.5 parent=#0   (chỉ MỘT nửa của khung)
#2 area=29177.6  parent=#0   (lỗ chữ nhật)
#3,4,5 = 3795.0  parent=#0   (3 khấc)
```

## 3. CHẨN ĐOÁN (đã chứng minh bằng số)

Solver trả về **cả khung nguyên (234556.4) LẪN một nửa (113104.5) cùng lúc**. Đường chia dở dang chỉ
khép được **MỘT bên** (sinh ra nửa 113104.5); nửa còn lại (~121452 = 234556.4 − 113104.5) **không khép**
nên phần đó vẫn dính trong khung nguyên #0.

→ Phép thử "khung bị chia" thất bại:
- Tổng con của #0 = 113104.5 + 29177.6 + 3×3795 = **153667.1**
- 153667.1 / 234556.4 = **0.655** < ngưỡng 0.99 → KHÔNG phải khung bị chia → #0 thành 1 tấm.

**Điểm cốt lõi:** `RegionClassifier` KHÔNG thể tự cứu, vì **thiếu hẳn hình học của nửa thứ 2** —
không tầng phân loại nào tạo lại được miền không tồn tại. Fix BẮT BUỘC ở tầng khép khe hở của Case2:
phải làm đường chia dở dang khép **ĐỦ CẢ 2 BÊN** để `CreateFromCurves`/graph-walk sinh ra đúng 2 nửa.

Ghi chú: TEST B (một hình phức tạp khác, PL-198, 5 region rời) vẫn PASS — mọi thay đổi không được phá vỡ nó.

## 4. Đã thử gì rồi (và vì sao chưa đủ)

Đã đổi `FireRayWithMidpointCheck`: tiêu chí chọn điểm nối từ "gần midpoint đường chia nhất"
(`distToMid`) sang "chướng ngại đầu tiên dọc tia" (`distFromOrigin` nhỏ nhất). Kết quả: **cải thiện
0→1 nửa khép** (trước đó nhiều khả năng 0 nửa), nhưng vẫn thiếu nửa thứ 2 → chưa đủ.

---

## 5. TOÀN BỘ CODE Case2_ExtensionSplitLine.cs (để web đọc và đề xuất sửa)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_CreateBoundary.Services
{
    public class Case2_ExtensionSplitLine
    {
        public static List<Region> GetRegions(List<Curve> curves, Editor ed)
        {
            List<Region> result = new List<Region>();
            List<Curve> cleanCurves = BoundaryUtils.PurifyInputCurves(curves);

            SmartTopologyGapClosure(cleanCurves, ed);

            DBObjectCollection curveCol = new DBObjectCollection();
            foreach (var c in cleanCurves) curveCol.Add(c);

            DBObjectCollection shattered = BoundaryUtils.ShatterCurves(curveCol);

            try
            {
                List<Polyline> closedLoops = ExtractClosedLoopsByRightHandRule(shattered);

                if (closedLoops.Count == 0) return result;

                DBObjectCollection loopRegionsCol = new DBObjectCollection();

                foreach (var loop in closedLoops)
                {
                    Polyline purified = BoundaryUtils.PurifyPolyline(loop);
                    if (purified != null && purified.NumberOfVertices > 2)
                    {
                        try
                        {
                            DBObjectCollection tempCol = new DBObjectCollection { purified };
                            var regs = Region.CreateFromCurves(tempCol);
                            if (regs != null && regs.Count > 0) loopRegionsCol.Add(regs[0]);
                        }
                        catch { }
                    }
                    loop.Dispose();
                    if (purified != null && purified != loop) purified.Dispose();
                }

                if (loopRegionsCol.Count == 0) return result;

                // ĐÃ XÓA FilterInnerMost: Giữ lại toàn bộ các Đảo (Islands) và Khung bao (Outer)
                var validRegs = loopRegionsCol.Cast<Region>().Where(r => r.Area > 1.0).ToList();

                foreach (Region reg in validRegs)
                {
                    result.Add((Region)reg.Clone());
                }
                foreach (DBObject obj in loopRegionsCol) obj.Dispose();
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[MCG] LỖI ĐỒ THỊ (Case 2): {ex.Message}");
            }
            return result;
        }

        // Kéo dài các đầu mút hở của đường chia (không phải khung ngoài dài nhất) bằng cách bắn tia
        // theo hướng tiếp tuyến để chạm vào curve khác, đóng khe hở.
        private static void SmartTopologyGapClosure(List<Curve> curves, Editor ed)
        {
            List<Curve> extensions = new List<Curve>();
            Curve outerBoundary = curves.OrderByDescending(c => GetCurveLength(c)).FirstOrDefault();

            foreach (Curve c in curves)
            {
                if (c == outerBoundary) continue;
                Point3d midPoint = GetMidPoint(c);

                if (!IsTouching(c.StartPoint, c, curves))
                {
                    Vector3d dirStart = GetDirectionAtStart(c);
                    if (!dirStart.IsZeroLength())
                    {
                        Point3d? hit = FireRayWithMidpointCheck(c.StartPoint, dirStart, midPoint, c, curves);
                        if (hit.HasValue)
                        {
                            if (c is Line line) line.StartPoint = hit.Value;
                            else extensions.Add(new Line(c.StartPoint, hit.Value));
                        }
                    }
                }

                if (!IsTouching(c.EndPoint, c, curves))
                {
                    Vector3d dirEnd = GetDirectionAtEnd(c);
                    if (!dirEnd.IsZeroLength())
                    {
                        Point3d? hit = FireRayWithMidpointCheck(c.EndPoint, dirEnd, midPoint, c, curves);
                        if (hit.HasValue)
                        {
                            if (c is Line line) line.EndPoint = hit.Value;
                            else extensions.Add(new Line(c.EndPoint, hit.Value));
                        }
                    }
                }
            }
            curves.AddRange(extensions);
        }

        // Tiêu chí HIỆN TẠI (đã đổi): chọn chướng ngại ĐẦU TIÊN dọc tia (distFromOrigin nhỏ nhất).
        // Tham số midPoint hiện KHÔNG còn dùng (giữ nguyên signature theo yêu cầu, chưa refactor).
        private static Point3d? FireRayWithMidpointCheck(Point3d origin, Vector3d dir, Point3d midPoint, Curve sourceCurve, List<Curve> others)
        {
            using (Ray ray = new Ray { BasePoint = origin, UnitDir = dir })
            {
                Point3d? bestHit = null;
                double minOriginDistance = double.MaxValue;

                foreach (Curve target in others)
                {
                    if (target == sourceCurve) continue;
                    Point3dCollection pts = new Point3dCollection();
                    ray.IntersectWith(target, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);

                    foreach (Point3d hit in pts)
                    {
                        double distFromOrigin = origin.DistanceTo(hit);
                        if (distFromOrigin > 1e-3)
                        {
                            if (distFromOrigin < minOriginDistance)
                            {
                                minOriginDistance = distFromOrigin;
                                bestHit = hit;
                            }
                        }
                    }
                }
                return bestHit;
            }
        }

        // Dựng graph từ các đoạn đã shatter, duyệt vòng kín theo right-hand rule (rẽ trái tối đa).
        private static List<Polyline> ExtractClosedLoopsByRightHandRule(DBObjectCollection shatteredCurves)
        {
            List<Polyline> loops = new List<Polyline>();
            List<Curve> segments = shatteredCurves.Cast<Curve>().ToList();
            if (segments.Count == 0) return loops;

            double tolerance = 0.05;
            var nodes = new List<Point3d>();
            var edges = new List<GraphEdge>();

            for (int i = 0; i < segments.Count; i++)
            {
                Curve seg = segments[i];
                Point3d p1 = seg.StartPoint;
                Point3d p2 = seg.EndPoint;

                int n1 = GetOrAddNode(nodes, p1, tolerance);
                int n2 = GetOrAddNode(nodes, p2, tolerance);

                if (n1 != n2)
                {
                    edges.Add(new GraphEdge { From = n1, To = n2, Curve = seg, IsForward = true });
                    edges.Add(new GraphEdge { From = n2, To = n1, Curve = seg, IsForward = false });
                }
            }

            HashSet<GraphEdge> visited = new HashSet<GraphEdge>();

            foreach (var startEdge in edges)
            {
                if (visited.Contains(startEdge)) continue;

                List<GraphEdge> currentLoop = new List<GraphEdge>();
                GraphEdge curr = startEdge;

                while (curr != null && !visited.Contains(curr))
                {
                    visited.Add(curr);
                    currentLoop.Add(curr);

                    Vector3d inDir = GetEdgeDirection(curr, false);
                    var nextEdges = edges.Where(e => e.From == curr.To && e != curr && e.To != curr.From).ToList();

                    if (nextEdges.Count == 0) break;

                    curr = nextEdges.OrderByDescending(e => GetTurnAngle(inDir, GetEdgeDirection(e, true))).FirstOrDefault();
                }

                if (currentLoop.Count > 2 && currentLoop.First().From == currentLoop.Last().To)
                {
                    Polyline pl = BuildPolylineFromEdges(currentLoop, nodes);
                    if (pl != null) loops.Add(pl);
                }
            }

            return loops;
        }

        private static int GetOrAddNode(List<Point3d> nodes, Point3d pt, double tol)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].DistanceTo(pt) < tol) return i;
            nodes.Add(pt);
            return nodes.Count - 1;
        }

        private static Vector3d GetEdgeDirection(GraphEdge edge, bool isOutward)
        {
            double param = isOutward ?
                (edge.IsForward ? edge.Curve.StartParam : edge.Curve.EndParam) :
                (edge.IsForward ? edge.Curve.EndParam : edge.Curve.StartParam);

            Vector3d dir = edge.Curve.GetFirstDerivative(param).GetNormal();
            if (!isOutward && edge.IsForward) dir = -dir;
            if (isOutward && !edge.IsForward) dir = -dir;
            return dir;
        }

        private static double GetTurnAngle(Vector3d vIn, Vector3d vOut) => vIn.GetAngleTo(vOut, Vector3d.ZAxis);

        private static Polyline BuildPolylineFromEdges(List<GraphEdge> loop, List<Point3d> nodes)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < loop.Count; i++)
            {
                Point3d pt = nodes[loop[i].From];
                double bulge = BoundaryUtils.GetBulge(loop[i].Curve, !loop[i].IsForward);
                pl.AddVertexAt(i, new Point2d(pt.X, pt.Y), bulge, 0, 0);
            }
            pl.Closed = true;
            return pl;
        }

        private static bool IsTouching(Point3d pt, Curve ignoreCurve, List<Curve> others)
        {
            foreach (Curve target in others)
            {
                if (target == ignoreCurve) continue;
                if (target.GetClosestPointTo(pt, false).DistanceTo(pt) < 1e-3) return true;
            }
            return false;
        }

        private static Point3d GetMidPoint(Curve c) => c.GetPointAtParameter((c.StartParam + c.EndParam) / 2.0);
        private static double GetCurveLength(Curve c) => c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
        private static Vector3d GetDirectionAtStart(Curve c) { try { return -c.GetFirstDerivative(c.StartParam).GetNormal(); } catch { return new Vector3d(0, 0, 0); } }
        private static Vector3d GetDirectionAtEnd(Curve c) { try { return c.GetFirstDerivative(c.EndParam).GetNormal(); } catch { return new Vector3d(0, 0, 0); } }

        private class GraphEdge { public int From { get; set; } public int To { get; set; } public Curve Curve { get; set; } public bool IsForward { get; set; } }
    }
}
```

---

## 6. CÂU HỎI CHO CLAUDE WEB

1. Vì sao đường chia dở dang chỉ khép được **một** đầu mà không cả hai? Nghi vấn: `SmartTopologyGapClosure`
   duyệt từng đầu mút, nhưng có thể (a) một đầu đã bị `IsTouching` báo "chạm" nhầm (tolerance 1e-3),
   hoặc (b) tia bắn ra không trúng biên đối diện, hoặc (c) graph-walk `ExtractClosedLoopsByRightHandRule`
   chỉ dựng được 1 vòng con vì đầu mút chưa nối đúng node (tolerance node = 0.05).

2. Đề xuất hướng sửa cụ thể để đường chia dở dang khép **đủ cả 2 bên**, ra đúng 2 nửa — mà KHÔNG phá
   vỡ TEST B (hình PL-198 phức tạp, 5 region rời phải giữ nguyên).

3. Có nên đổi cách tiếp cận: thay vì kéo dài đầu mút rồi dựng lại graph, thì sau khi có khung + đường
   chia, chủ động "snap" đầu mút đường chia vào biên gần nhất trong bán kính nhỏ rồi mới shatter?

4. Ràng buộc: KHÔNG gọi lại `FilterInnerMost` (dead code), KHÔNG đổi GUID/RegAppName, KHÔNG improvise
   ngoài phạm vi được chốt. Ưu tiên thay đổi tối thiểu, có thể giải thích rõ đánh đổi.

Sau khi web chốt hướng → viết task-prompt cụ thể cho Claude Code thực thi.
