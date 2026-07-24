using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_CreateBoundary.Services
{
    // Một tấm thép cùng các lỗ khoét cần trừ.
    public class PlateNode
    {
        public Region Plate;
        public List<Region> Holes = new List<Region>();
        public bool OwnsPlate;   // true => Plate là region MỚI (đã union), caller phải Dispose.
    }

    // =========================================================================
    // TẦNG 2 - PHÂN LOẠI
    // Dựng RỪNG BAO HÀM rồi quyết định miền nào là tấm, lỗ, hay khung bị chia.
    //
    // Quan hệ cha-con KHÔNG thể chỉ dựa vào "điểm nằm trong vật liệu": tâm của
    // một lỗ (hoặc khấc trên cạnh) nằm trong VÙNG RỖNG của khung, nên phép thử
    // vật liệu báo "không thuộc" và lỗ bị trôi thành tấm rời. Đây chính là lỗi
    // khiến TH2 sinh 6 plate (khung + 2 lỗ tròn + 3 khấc).
    //
    // Cách dựng cây ở đây gồm 2 lượt:
    //   1. Theo VẬT LIỆU  -> đúng cho các mảnh chia rời nhau (không lồng nhầm).
    //   2. Fallback theo BOUNDING BOX cho miền lượt 1 bỏ sót (lỗ / khấc trong rỗng).
    //
    // Khi đã có cây, tại mỗi tấm:
    //   - LÁ (không có con)      = lỗ khoét cần trừ.
    //   - NÚT TRUNG GIAN (có con) = vật liệu, gộp (union) vào thân tấm.
    // Nhờ vậy TH5 (tấm -> chữ nhật -> tròn): chữ nhật là trung gian nên gộp vào
    // tấm, chỉ trừ đúng hình tròn trong cùng.
    // =========================================================================
    public static class RegionClassifier
    {
        // Đi kèm giả định: không có lỗ khoét chiếm >= 99% diện tích tấm.
        public const double PartitionRatio = 0.99;

        public static List<PlateNode> Classify(List<Region> regions, Editor ed = null)
        {
            var result = new List<PlateNode>();
            if (regions == null) return result;

            List<Region> regs = regions.Where(r => r != null && !r.IsDisposed && r.Area > 1.0)
                                       .OrderByDescending(r => r.Area).ToList();
            int n = regs.Count;
            ed?.WriteMessage($"\n[DBG] Classify: {regions.Count} region thô -> {n} region (area>1).");
            if (n == 0) return result;

            var children = new List<int>[n];
            var parent = new int[n];
            for (int i = 0; i < n; i++) { children[i] = new List<int>(); parent[i] = -1; }

            var centroid = new Point3d[n];
            for (int i = 0; i < n; i++) centroid[i] = BoundaryUtils.GetCentroidFromRegion(regs[i]);

            // Lượt 1 - VẬT LIỆU: cha = miền nhỏ nhất mà VẬT LIỆU của nó chứa tâm i.
            // Các mảnh chia rời nhau không lồng nhầm vào nhau vì tâm mảnh này không
            // nằm trong vật liệu mảnh kia.
            for (int i = 0; i < n; i++)
            {
                int best = -1;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || regs[j].Area <= regs[i].Area) continue;
                    if (!BoundaryUtils.IsPointInRegion(regs[j], centroid[i])) continue;
                    if (best == -1 || regs[j].Area < regs[best].Area) best = j;
                }
                parent[i] = best;
            }

            // Lượt 2 - BOUNDING BOX: chỉ cho miền chưa có cha (lỗ / khấc nằm trong rỗng).
            // Không đụng tới mảnh chia (đã có cha ở lượt 1) nên không gây lồng nhầm.
            for (int i = 0; i < n; i++)
            {
                if (parent[i] != -1) continue;
                int best = -1;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || regs[j].Area <= regs[i].Area) continue;
                    if (!BboxContains(regs[j], regs[i])) continue;
                    if (best == -1 || regs[j].Area < regs[best].Area) best = j;
                }
                parent[i] = best;
            }

            for (int i = 0; i < n; i++) if (parent[i] >= 0) children[parent[i]].Add(i);

            if (ed != null)
                for (int i = 0; i < n; i++)
                    ed.WriteMessage($"\n[DBG]  #{i} area={regs[i].Area:F1} parent={(parent[i] < 0 ? "ROOT" : "#" + parent[i])} con=[{string.Join(",", children[i])}]");

            // Nhiều gốc là bình thường: một lần quét có thể có nhiều tấm rời nhau.
            for (int i = 0; i < n; i++) if (parent[i] < 0) Walk(i, regs, children, result, ed);
            return result;
        }

        private static void Walk(int idx, List<Region> regs, List<int>[] children, List<PlateNode> result, Editor ed)
        {
            List<int> kids = children[idx];
            double sumKids = kids.Sum(k => regs[k].Area);

            // Khung bao bị chia thành nhiều mảnh: bỏ khung, xuất từng mảnh.
            if (kids.Count >= 2 && sumKids >= regs[idx].Area * PartitionRatio)
            {
                ed?.WriteMessage($"\n[DBG]  -> #{idx} là KHUNG BỊ CHIA (sumCon={sumKids:F1} ~ area={regs[idx].Area:F1}), bỏ khung.");
                foreach (int k in kids) Walk(k, regs, children, result, ed);
                return;
            }

            var leaves = new List<int>();
            var intermediates = new List<int>();
            CollectSubtree(idx, children, leaves, intermediates);

            // Gộp các nút trung gian (vật liệu) vào thân tấm; chỉ lá mới là lỗ.
            Region plate = regs[idx];
            bool owns = false;
            if (intermediates.Count > 0)
            {
                Region merged = (Region)regs[idx].Clone();
                foreach (int m in intermediates)
                {
                    try { using (Region mc = (Region)regs[m].Clone()) merged.BooleanOperation(BooleanOperationType.BoolUnite, mc); }
                    catch { }
                }
                plate = merged; owns = true;
            }

            var node = new PlateNode { Plate = plate, OwnsPlate = owns };
            foreach (int k in leaves) node.Holes.Add(regs[k]);
            result.Add(node);
            ed?.WriteMessage($"\n[DBG]  -> #{idx} là TẤM (plateArea={plate.Area:F1}, gộp trung gian=[{string.Join(",", intermediates)}], lỗ=[{string.Join(",", leaves)}]).");
        }

        // DFS: lá vào leaves, nút có con vào intermediates. Không tính chính root.
        private static void CollectSubtree(int root, List<int>[] children, List<int> leaves, List<int> intermediates)
        {
            foreach (int c in children[root])
            {
                if (children[c].Count == 0) leaves.Add(c);
                else { intermediates.Add(c); CollectSubtree(c, children, leaves, intermediates); }
            }
        }

        private static bool BboxContains(Region outer, Region inner)
        {
            try
            {
                Extents3d eo = outer.GeometricExtents, ei = inner.GeometricExtents;
                double t = 1e-4;
                return ei.MinPoint.X >= eo.MinPoint.X - t && ei.MaxPoint.X <= eo.MaxPoint.X + t &&
                       ei.MinPoint.Y >= eo.MinPoint.Y - t && ei.MaxPoint.Y <= eo.MaxPoint.Y + t;
            }
            catch { return false; }
        }
    }
}
