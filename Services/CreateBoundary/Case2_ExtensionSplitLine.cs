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

        // ==============================================================================
        // CÁC HÀM XỬ LÝ TOPO BÊN DƯỚI GIỮ NGUYÊN 100%
        // ==============================================================================
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

        private static Point3d? FireRayWithMidpointCheck(Point3d origin, Vector3d dir, Point3d midPoint, Curve sourceCurve, List<Curve> others)
        {
            using (Ray ray = new Ray { BasePoint = origin, UnitDir = dir })
            {
                Point3d? bestHit = null;
                double minMidDistance = double.MaxValue;

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
                            double distToMid = hit.DistanceTo(midPoint);
                            if (distToMid < minMidDistance)
                            {
                                minMidDistance = distToMid;
                                bestHit = hit;
                            }
                        }
                    }
                }
                return bestHit;
            }
        }

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