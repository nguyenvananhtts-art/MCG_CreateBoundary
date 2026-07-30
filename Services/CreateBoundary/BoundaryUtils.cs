using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCG_CreateBoundary.ViewModels;

namespace MCG_CreateBoundary.Services
{
    public static class BoundaryUtils
    {
        public static List<Curve> PurifyInputCurves(List<Curve> curves) { return curves.Where(c => { try { return c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam) > 1e-3; } catch { return false; } }).ToList(); }
        public static Polyline PurifyPolyline(Polyline pl) { if (pl == null || pl.NumberOfVertices < 2) return null; Polyline cleanPl = new Polyline(); int idx = 0; double tol = 1e-3; Point2d lastPt = pl.GetPoint2dAt(0); cleanPl.AddVertexAt(idx++, lastPt, pl.GetBulgeAt(0), 0, 0); for (int i = 1; i < pl.NumberOfVertices; i++) { Point2d pt = pl.GetPoint2dAt(i); if (pt.GetDistanceTo(lastPt) > tol) { cleanPl.AddVertexAt(idx++, pt, pl.GetBulgeAt(i), 0, 0); lastPt = pt; } } if (cleanPl.Closed && cleanPl.NumberOfVertices > 2) { if (cleanPl.GetPoint2dAt(cleanPl.NumberOfVertices - 1).GetDistanceTo(cleanPl.GetPoint2dAt(0)) < tol) cleanPl.RemoveVertexAt(cleanPl.NumberOfVertices - 1); } cleanPl.Closed = pl.Closed; return cleanPl; }

        public static DBObjectCollection ShatterCurves(DBObjectCollection curves)
        {
            DBObjectCollection result = new DBObjectCollection(); List<Curve> cList = curves.Cast<Curve>().ToList(); List<Extents3d> extList = new List<Extents3d>();
            foreach (Curve c in cList) { try { extList.Add(c.GeometricExtents); } catch { extList.Add(new Extents3d(Point3d.Origin, Point3d.Origin)); } }
            for (int i = 0; i < cList.Count; i++)
            {
                Curve c1 = cList[i]; Extents3d ext1 = extList[i]; Point3dCollection pts = new Point3dCollection();
                for (int j = 0; j < cList.Count; j++)
                {
                    if (i == j) continue; Extents3d ext2 = extList[j];
                    if (ext1.MaxPoint.X < ext2.MinPoint.X - 1e-3 || ext1.MinPoint.X > ext2.MaxPoint.X + 1e-3 || ext1.MaxPoint.Y < ext2.MinPoint.Y - 1e-3 || ext1.MinPoint.Y > ext2.MaxPoint.Y + 1e-3) continue;
                    c1.IntersectWith(cList[j], Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                }
                List<double> pList = new List<double>(); foreach (Point3d p in pts) { try { pList.Add(c1.GetParameterAtPoint(p)); } catch { } }
                var distinctParams = pList.OrderBy(x => x).ToList(); DoubleCollection dParams = new DoubleCollection(); double lastP = -1.0;
                foreach (double p in distinctParams) { if (Math.Abs(p - lastP) > 1e-5 && p > c1.StartParam + 1e-5 && p < c1.EndParam - 1e-5) { dParams.Add(p); lastP = p; } }
                if (dParams.Count > 0) { try { var pieces = c1.GetSplitCurves(dParams); foreach (DBObject p in pieces) { if (p is Curve pc && pc.GetDistanceAtParameter(pc.EndParam) > 1e-4) result.Add(p); else p.Dispose(); } } catch { if (c1.GetDistanceAtParameter(c1.EndParam) > 1e-4) result.Add(c1); } } else { if (c1.GetDistanceAtParameter(c1.EndParam) > 1e-4) result.Add(c1); }
            }
            return result;
        }

        public static DBObjectCollection CleanNetwork(DBObjectCollection shatteredCurves) { List<Curve> curves = shatteredCurves.Cast<Curve>().ToList(); bool removedAny = true; double tol = 1e-4; while (removedAny) { removedAny = false; for (int i = curves.Count - 1; i >= 0; i--) { Curve c = curves[i]; bool startConnected = false; bool endConnected = false; if (c.Closed || c.StartPoint.DistanceTo(c.EndPoint) < tol) continue; for (int j = 0; j < curves.Count; j++) { if (i == j) continue; Curve other = curves[j]; if (!startConnected && (c.StartPoint.DistanceTo(other.StartPoint) < tol || c.StartPoint.DistanceTo(other.EndPoint) < tol)) startConnected = true; if (!endConnected && (c.EndPoint.DistanceTo(other.StartPoint) < tol || c.EndPoint.DistanceTo(other.EndPoint) < tol)) endConnected = true; if (startConnected && endConnected) break; } if (!startConnected || !endConnected) { curves.RemoveAt(i); removedAny = true; } } } DBObjectCollection cleanResult = new DBObjectCollection(); foreach (var c in curves) cleanResult.Add(c); return cleanResult; }
        public static List<Region> FilterInnerMost(List<Region> sortedRegs) { List<Region> result = new List<Region>(); for (int i = 0; i < sortedRegs.Count; i++) { bool isParent = false; for (int j = 0; j < i; j++) { if (IsPointInRegion(sortedRegs[i], GetCentroidFromRegion(sortedRegs[j]))) { isParent = true; break; } } if (!isParent) result.Add(sortedRegs[i]); } return result; }

        public static Polyline ConvertRegionToPolyline(Region reg, Editor ed, string name) { DBObjectCollection ex = new DBObjectCollection(); reg.Explode(ex); List<Curve> segs = ex.Cast<Curve>().ToList(); if (segs.Count == 0) return null; Polyline pl = new Polyline(); Plane plane = new Plane(Point3d.Origin, Vector3d.ZAxis); Curve first = segs[0]; Point3d next = first.EndPoint; pl.AddVertexAt(0, first.StartPoint.Convert2d(plane), GetBulge(first, false), 0, 0); pl.AddVertexAt(1, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(0); int vIdx = 2; while (segs.Count > 0) { bool found = false; for (int i = 0; i < segs.Count; i++) { if (segs[i].StartPoint.DistanceTo(next) < 1e-3) { pl.SetBulgeAt(vIdx - 1, GetBulge(segs[i], false)); next = segs[i].EndPoint; pl.AddVertexAt(vIdx++, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(i); found = true; break; } else if (segs[i].EndPoint.DistanceTo(next) < 1e-3) { pl.SetBulgeAt(vIdx - 1, GetBulge(segs[i], true)); next = segs[i].StartPoint; pl.AddVertexAt(vIdx++, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(i); found = true; break; } } if (!found) break; } pl.Closed = true; return PurifyPolyline(pl); }
        public static double GetBulge(Curve cur, bool inv) { if (cur is Arc arc) { double d = arc.EndAngle - arc.StartAngle; if (d < 0) d += 2 * Math.PI; return Math.Tan(d / 4.0) * (inv ? -1.0 : 1.0); } return 0.0; }
        public static Point3d GetCentroidFromRegion(Region reg) { Point3d o = Point3d.Origin; Vector3d x = Vector3d.XAxis; Vector3d y = Vector3d.YAxis; var prop = reg.AreaProperties(ref o, ref x, ref y); Point3d localCentroid = new Point3d(prop.Centroid.X, prop.Centroid.Y, 0.0); Plane plane = new Plane(Point3d.Origin, reg.Normal); return localCentroid.TransformBy(Matrix3d.PlaneToWorld(plane)); }
        public static Point3d GetCentroidFromPolyline(Polyline pl) { try { DBObjectCollection col = new DBObjectCollection(); col.Add(pl); using (var rs = Region.CreateFromCurves(col)) { if (rs != null && rs.Count > 0) return GetCentroidFromRegion((Region)rs[0]); } } catch { } return Point3d.Origin; }
        public static bool IsPointInRegion(Region reg, Point3d pt) { Extents3d ext = reg.GeometricExtents; if (pt.X < ext.MinPoint.X - 1e-3 || pt.X > ext.MaxPoint.X + 1e-3 || pt.Y < ext.MinPoint.Y - 1e-3 || pt.Y > ext.MaxPoint.Y + 1e-3) return false; try { using (Circle cir = new Circle(pt, Vector3d.ZAxis, 0.1)) { DBObjectCollection col = new DBObjectCollection(); col.Add(cir); using (DBObjectCollection tinyRegs = Region.CreateFromCurves(col)) { if (tinyRegs != null && tinyRegs.Count > 0) { using (Region tinyReg = (Region)tinyRegs[0]) using (Region cloneReg = (Region)reg.Clone()) { cloneReg.BooleanOperation(BooleanOperationType.BoolIntersect, tinyReg); return cloneReg.Area > 0; } } } } } catch { } return false; }

        public static void GetLocalIds(Transaction tr, List<ObjectId> allIds, Point3d pt, out List<ObjectId> localIds, out Extents3d localBox)
        {
            double maxD = 50000.0; // Phóng xa 50 mét
            double dR = maxD, dL = maxD, dT = maxD, dB = maxD;

            using (Line rayR = new Line(pt, new Point3d(pt.X + maxD, pt.Y, pt.Z)))
            using (Line rayL = new Line(pt, new Point3d(pt.X - maxD, pt.Y, pt.Z)))
            using (Line rayT = new Line(pt, new Point3d(pt.X, pt.Y + maxD, pt.Z)))
            using (Line rayB = new Line(pt, new Point3d(pt.X, pt.Y - maxD, pt.Z)))
            {
                Point3dCollection pts = new Point3dCollection();
                foreach (ObjectId id in allIds)
                {
                    Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (c == null) continue;

                    pts.Clear(); c.IntersectWith(rayR, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                    foreach (Point3d p in pts) { double d = p.DistanceTo(pt); if (d < dR) dR = d; }

                    pts.Clear(); c.IntersectWith(rayL, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                    foreach (Point3d p in pts) { double d = p.DistanceTo(pt); if (d < dL) dL = d; }

                    pts.Clear(); c.IntersectWith(rayT, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                    foreach (Point3d p in pts) { double d = p.DistanceTo(pt); if (d < dT) dT = d; }

                    pts.Clear(); c.IntersectWith(rayB, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                    foreach (Point3d p in pts) { double d = p.DistanceTo(pt); if (d < dB) dB = d; }
                }
            }

            if (dR == maxD) dR = 5000; if (dL == maxD) dL = 5000;
            if (dT == maxD) dT = 5000; if (dB == maxD) dB = 5000;

            double marginX = (dR + dL) * 0.2; if (marginX < 1000) marginX = 1000;
            double marginY = (dT + dB) * 0.2; if (marginY < 1000) marginY = 1000;

            Point3d minPt = new Point3d(pt.X - dL - marginX, pt.Y - dB - marginY, 0);
            Point3d maxPt = new Point3d(pt.X + dR + marginX, pt.Y + dT + marginY, 0);
            localBox = new Extents3d(minPt, maxPt);

            localIds = new List<ObjectId>();
            foreach (ObjectId id in allIds)
            {
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c != null)
                {
                    try
                    {
                        Extents3d ext = c.GeometricExtents;
                        if (ext.MaxPoint.X < minPt.X || ext.MinPoint.X > maxPt.X || ext.MaxPoint.Y < minPt.Y || ext.MinPoint.Y > maxPt.Y) continue;
                        localIds.Add(id);
                    }
                    catch { }
                }
            }
        }

        public static List<Point3d> FindLocalGaps(List<Curve> localCurves, Point3d clickPt, Extents3d localBox)
        {
            List<Point3d> danglings = new List<Point3d>();
            double tol = 1e-3;

            foreach (Curve c1 in localCurves)
            {
                if (c1.Closed) continue;
                Point3d[] endpoints = { c1.StartPoint, c1.EndPoint };

                foreach (Point3d pt in endpoints)
                {
                    if (pt.X < localBox.MinPoint.X || pt.X > localBox.MaxPoint.X || pt.Y < localBox.MinPoint.Y || pt.Y > localBox.MaxPoint.Y) continue;

                    bool connected = false;
                    foreach (Curve c2 in localCurves)
                    {
                        if (c1 == c2) continue;
                        try { if (c2.GetClosestPointTo(pt, false).DistanceTo(pt) <= tol) { connected = true; break; } } catch { }
                    }
                    if (!connected) danglings.Add(pt);
                }
            }

            var unique = new List<Point3d>();
            foreach (var p in danglings) { if (!unique.Any(u => u.DistanceTo(p) < tol)) unique.Add(p); }
            return unique.OrderBy(p => p.DistanceTo(clickPt)).ToList();
        }

        // =========================================================================
        // HÀM LÕI MỚI: TẠO PLATE + ĐỤC LỖ TỰ ĐỘNG + VẼ VIỀN ĐỎ
        // =========================================================================
        public static Polyline ProcessRegionToPlate(Region reg, List<Region> holeCandidates, Transaction tr, BlockTableRecord btr, Database db, string name, int no, Point3d basePoint, List<Tuple<Point3d, string>> catBlocks, BoundaryViewModel vm, Editor ed)
        {
            double areaM2 = reg.Area / 1000000.0;
            Point3d cog = GetCentroidFromRegion(reg);

            // LOGIC "ĐẢO TRONG ĐẢO" VÀ TRỪ LỖ
            // Luôn tự động trừ lỗ nếu RegionClassifier đã xác định đây là lỗ thật,
            // không còn phụ thuộc checkbox vm.IsSubtractHole (đúng nghĩa "Automatically").
            if (holeCandidates != null)
            {
                List<Region> actualHoles = new List<Region>();
                foreach (Region candidate in holeCandidates)
                {
                    if (candidate == null || candidate.IsDisposed || candidate == reg) continue;
                    // Lọc để tránh trừ chính nó (Trùng Area)
                    if (Math.Abs(candidate.Area - reg.Area) < 1e-4) continue;

                    // NẾU TRỌNG TÂM CỦA ỨNG VIÊN NẰM TRONG MIỀN CHÍNH -> NÓ LÀ CÁI LỖ!
                    if (IsPointInRegion(reg, GetCentroidFromRegion(candidate)))
                    {
                        actualHoles.Add(candidate);
                    }
                }

                if (actualHoles.Count > 0)
                {
                    using (Region booleanReg = reg.Clone() as Region)
                    {
                        foreach (Region h in actualHoles)
                        {
                            using (Region hClone = h.Clone() as Region)
                            {
                                try { booleanReg.BooleanOperation(BooleanOperationType.BoolSubtract, hClone); } catch { }
                            }

                            // IN ĐƯỜNG POLYLINE LỖ MÀU ĐỎ (Permanent Visual Feedback)
                            Polyline hPl = ConvertRegionToPolyline(h, ed, "HOLE");
                            if (hPl != null)
                            {
                                PlateDrawingHelper.EnsureLayerExists(tr, db, PlateDrawingHelper.TargetLayer, 4);
                                hPl.SetDatabaseDefaults();
                                hPl.Layer = PlateDrawingHelper.TargetLayer; // Cùng Layer với Plate
                                hPl.ColorIndex = 1; // Màu Đỏ
                                btr.AppendEntity(hPl);
                                tr.AddNewlyCreatedDBObject(hPl, true);
                            }
                        }

                        // Lấy Net Area và True COG sau khi đã trừ khối
                        areaM2 = booleanReg.Area / 1000000.0;
                        cog = GetCentroidFromRegion(booleanReg);
                    }
                }
            }

            // VẼ ĐƯỜNG POLYLINE VIỀN NGOÀI CÙNG (PLATE GỐC)
            Polyline pl = ConvertRegionToPolyline(reg, ed, name);
            if (pl != null)
            {
                PlateDrawingHelper.EnsureLayerExists(tr, db, PlateDrawingHelper.TargetLayer, 4);
                pl.SetDatabaseDefaults(); pl.Layer = PlateDrawingHelper.TargetLayer; pl.ColorIndex = 256; // ByLayer
                btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);

                string category = "-";
                if (areaM2 > 20.0) { foreach (var cat in catBlocks) { if (IsPointInRegion(reg, cat.Item1)) { category = cat.Item2; break; } } }

                // Lưu dữ liệu Area MỚI và COG MỚI vào Data
                PlateDrawingHelper.AddXData(pl, no, name, basePoint, category, vm.IsInsertCog, tr, db);

                double baseScale = reg.GeometricExtents.MinPoint.DistanceTo(reg.GeometricExtents.MaxPoint) * 0.04;
                double cogScale = baseScale * 0.5;
                bool isGiantPlate = (areaM2 > 20.0) && vm.IsInsertCog && vm.IsCreateText;

                if (isGiantPlate)
                {
                    cogScale = 500.0; Point3d textLoc = new Point3d(cog.X, cog.Y + 350.0, cog.Z);
                    PlateDrawingHelper.CreateMTextWithArea(tr, btr, textLoc, name, areaM2);
                    PlateDrawingHelper.InsertCogBlock(tr, db, btr, cog, cogScale);
                }
                else
                {
                    double yOffset = (0.5 * cogScale) + (baseScale * 0.5) + (baseScale * 0.2);
                    Point3d textLoc = vm.IsInsertCog ? new Point3d(cog.X, cog.Y + yOffset, cog.Z) : cog;
                    if (vm.IsCreateText) PlateDrawingHelper.CreateMText(tr, btr, textLoc, name, baseScale);
                    if (vm.IsInsertCog) PlateDrawingHelper.InsertCogBlock(tr, db, btr, cog, cogScale);
                }
            }
            return pl;
        }
    }
}