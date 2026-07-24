using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_CreateBoundary.Services
{
    // =========================================================================
    // TẦNG 1 - SINH MIỀN
    // Nhiệm vụ duy nhất: khôi phục MỌI miền kín từ hình học thô, KỂ CẢ khung bao
    // ngoài. Việc phân loại tấm / lỗ / khung bị chia là của RegionClassifier.
    // =========================================================================
    public static class RegionSolver
    {
        public static List<Region> Solve(List<Curve> curves, Editor ed)
        {
            List<Region> best = Case1_BasicSplit.GetRegions(curves, ed);

            // Hình học đã kín thì Case1 là đủ. Chỉ leo thang khi thực sự có đầu mút
            // hở, tránh chạy Case2 vô ích trên tấm kín thông thường.
            if (!HasDanglingEndpoint(curves)) return best;

            best = KeepBetter(best, Case2_ExtensionSplitLine.GetRegions(curves, ed));
            if (best.Count == 0) best = KeepBetter(best, Case3_EndpointBridging.GetRegions(curves, ed));
            return best;
        }

        // Tiêu chí leo thang là "chia được nhiều hơn", không phải "có ra được gì
        // không". Cổng cũ dùng Count == 0 nên Case2/Case3 gần như không bao giờ chạy:
        // Case1 luôn dựng được ít nhất khung bao ngoài từ polyline kín.
        private static List<Region> KeepBetter(List<Region> current, List<Region> candidate)
        {
            if (candidate.Count > current.Count) { DisposeAll(current); return candidate; }
            DisposeAll(candidate); return current;
        }

        private static void DisposeAll(List<Region> regs)
        {
            foreach (Region r in regs) { if (r != null && !r.IsDisposed) r.Dispose(); }
        }

        private static bool HasDanglingEndpoint(List<Curve> curves)
        {
            List<Curve> clean = BoundaryUtils.PurifyInputCurves(curves);
            foreach (Curve c in clean)
            {
                if (c.Closed || c.StartPoint.DistanceTo(c.EndPoint) < 1e-3) continue;
                if (!IsTouched(c.StartPoint, c, clean)) return true;
                if (!IsTouched(c.EndPoint, c, clean)) return true;
            }
            return false;
        }

        private static bool IsTouched(Point3d pt, Curve self, List<Curve> others)
        {
            foreach (Curve target in others)
            {
                if (target == self) continue;
                try { if (target.GetClosestPointTo(pt, false).DistanceTo(pt) < 1e-3) return true; } catch { }
            }
            return false;
        }
    }
}
