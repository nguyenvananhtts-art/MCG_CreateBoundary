using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_CreateBoundary.Services
{
    public class Case3_EndpointBridging
    {
        private class PointData
        {
            public Point3d Point { get; set; }
            public Curve ParentCurve { get; set; }
            public bool IsPaired { get; set; } = false;
        }

        public static List<Region> GetRegions(List<Curve> curves, Editor ed)
        {
            List<Region> result = new List<Region>();
            double maxBridgeDist = 5000.0;
            double tol = 1e-3;

            List<PointData> allPoints = new List<PointData>();
            foreach (Curve c in curves)
            {
                allPoints.Add(new PointData { Point = c.StartPoint, ParentCurve = c });
                if (!c.Closed && c.StartPoint.DistanceTo(c.EndPoint) > tol)
                {
                    allPoints.Add(new PointData { Point = c.EndPoint, ParentCurve = c });
                }
            }

            List<PointData> orphans = new List<PointData>();
            for (int i = 0; i < allPoints.Count; i++)
            {
                bool isOrphan = true;
                for (int j = 0; j < allPoints.Count; j++)
                {
                    if (i == j) continue;
                    if (allPoints[i].Point.DistanceTo(allPoints[j].Point) < tol)
                    {
                        isOrphan = false;
                        break;
                    }
                }
                if (isOrphan) orphans.Add(allPoints[i]);
            }

            if (orphans.Count < 2) return result;

            List<Curve> bridges = new List<Curve>();

            foreach (var o1 in orphans)
            {
                if (o1.IsPaired) continue;

                PointData bestMatch = null;
                double minDist = double.MaxValue;

                foreach (var o2 in orphans)
                {
                    if (o2 == o1 || o2.IsPaired) continue;
                    if (o1.ParentCurve == o2.ParentCurve) continue;

                    double dist = o1.Point.DistanceTo(o2.Point);
                    if (dist < minDist && dist <= maxBridgeDist)
                    {
                        minDist = dist;
                        bestMatch = o2;
                    }
                }

                if (bestMatch != null)
                {
                    bridges.Add(new Line(o1.Point, bestMatch.Point));
                    o1.IsPaired = true;
                    bestMatch.IsPaired = true;
                }
            }

            if (bridges.Count == 0) return result;

            List<Curve> combinedCurves = new List<Curve>(curves);
            combinedCurves.AddRange(bridges);

            result = Case1_BasicSplit.GetRegions(combinedCurves, ed);

            if (result.Count == 0)
            {
                result = Case2_ExtensionSplitLine.GetRegions(combinedCurves, ed);
            }

            return result;
        }
    }
}