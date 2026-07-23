using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_CreateBoundary.Services
{
    public static class FlattenUtils
    {
        // ĐÃ NÂNG CẤP: Nhận thêm biến out Extents3d để tính khung bao chung 1 vòng lặp (Single Pass I/O)
        public static List<Curve> FlattenAndExtractCurves(IEnumerable<ObjectId> ids, Transaction tr, BlockTableRecord btr, Editor ed, out Extents3d globalExtents)
        {
            List<Curve> resultCurves = new List<Curve>();
            Plane flatPlane = new Plane(Point3d.Origin, Vector3d.ZAxis);
            double tol = 1e-4;
            int flattenCount = 0;

            globalExtents = new Extents3d();
            bool firstExt = true;

            foreach (ObjectId id in ids)
            {
                Curve cur = null;
                try { cur = tr.GetObject(id, OpenMode.ForWrite) as Curve; }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { if (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.OnLockedLayer) continue; }

                if (cur == null || cur.IsErased) continue;

                // 1. TÍNH KHUNG BAO TẠI CHỖ (Tiết kiệm I/O)
                try
                {
                    if (cur.Bounds.HasValue)
                    {
                        if (firstExt) { globalExtents = cur.Bounds.Value; firstExt = false; }
                        else { globalExtents.AddExtents(cur.Bounds.Value); }
                    }
                }
                catch { }

                try
                {
                    // 2. LAZY FLATTENING (Bỏ qua chiếu bóng nếu vốn dĩ là 2D chuẩn)
                    bool isStrictly2D = Math.Abs(cur.StartPoint.Z) < tol && Math.Abs(cur.EndPoint.Z) < tol;
                    if (isStrictly2D && cur.IsPlanar)
                    {
                        try
                        {
                            if (!cur.GetPlane().Normal.IsEqualTo(Vector3d.ZAxis, new Tolerance(tol, tol)))
                                isStrictly2D = false; // Pháp tuyến bị nghiêng
                        }
                        catch { isStrictly2D = false; }
                    }

                    Curve finalCurve = null;

                    if (isStrictly2D)
                    {
                        // TỐC ĐỘ ÁNH SÁNG: Bypass 90% đối tượng 2D xịn
                        finalCurve = cur.Clone() as Curve;
                    }
                    else
                    {
                        // XỬ LÝ 3D (Phẫu thuật bằng C++)
                        Curve projectedCurve = cur.GetProjectedCurve(flatPlane, Vector3d.ZAxis) as Curve;
                        if (projectedCurve == null || projectedCurve.GetDistanceAtParameter(projectedCurve.EndParam) < tol)
                        {
                            projectedCurve?.Dispose();
                            continue;
                        }

                        projectedCurve.LayerId = cur.LayerId;
                        projectedCurve.Color = cur.Color;
                        projectedCurve.LinetypeId = cur.LinetypeId;

                        if (projectedCurve is Ellipse ell)
                        {
                            Polyline smoothPl = TessellateEllipseToSmoothCurve(ell);
                            smoothPl.LayerId = cur.LayerId; smoothPl.Color = cur.Color; smoothPl.LinetypeId = cur.LinetypeId;
                            finalCurve = smoothPl;
                            ell.Dispose();
                        }
                        else finalCurve = projectedCurve;

                        btr.AppendEntity(finalCurve);
                        tr.AddNewlyCreatedDBObject(finalCurve, true);
                        cur.Erase();
                        flattenCount++;
                    }

                    // 4. Băm nhỏ Polyline vào Lõi Toán Học
                    if (finalCurve is Polyline || finalCurve is Polyline2d || finalCurve is Polyline3d)
                    {
                        DBObjectCollection ex = new DBObjectCollection();
                        finalCurve.Explode(ex);
                        foreach (DBObject obj in ex)
                        {
                            if (obj is Curve exCurve && exCurve.GetDistanceAtParameter(exCurve.EndParam) > tol)
                                resultCurves.Add(exCurve);
                        }
                        finalCurve.Dispose();
                    }
                    else resultCurves.Add(finalCurve);
                }
                catch (Exception) { /* Bỏ qua các đường bị lỗi hình học nặng */ }
            }

            if (flattenCount > 0) ed.WriteMessage($"\n[MCG] Đã dọn dẹp và san phẳng (Z=0) thành công {flattenCount} đối tượng 3D bị lỗi.");
            return resultCurves;
        }

        private static Polyline TessellateEllipseToSmoothCurve(Ellipse ell)
        {
            Polyline pl = new Polyline();
            double length = ell.GetDistanceAtParameter(ell.EndParam);
            int segments = Math.Max(150, (int)(length / 0.5));
            double paramStep = (ell.EndParam - ell.StartParam) / segments;
            for (int i = 0; i <= segments; i++)
            {
                double p = ell.StartParam + i * paramStep;
                if (p > ell.EndParam) p = ell.EndParam;
                Point3d pt = ell.GetPointAtParameter(p);
                pl.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
            }
            pl.Closed = ell.Closed; return pl;
        }
    }
}