using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace MCG_CreateBoundary.Services
{
    public static class PlateDrawingHelper
    {
        public const string RegAppName = "MCG_PLATE_DATA";
        public const string TargetLayer = "Mechanical-AM_5";
        public const string BaseCogLayer = "Mechanical-AM_8";
        public const string CogBlockName = "COG Block";
        public const string BaseCogBlockName = "BaseCOG_Block";

        public static void EnsureLayerExists(Transaction tr, Database db, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord { Name = layerName, Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex) };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        public static void CreateMText(Transaction tr, BlockTableRecord btr, Point3d loc, string txt, double height)
        {
            EnsureLayerExists(tr, tr.GetObject(btr.OwnerId, OpenMode.ForRead).Database, TargetLayer, 4);
            MText mt = new MText { Contents = txt, Location = loc, TextHeight = height, Attachment = AttachmentPoint.MiddleCenter, Layer = TargetLayer, ColorIndex = 2 };
            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
        }

        // ĐÃ CẬP NHẬT: LineSpacingFactor = 0.8
        public static void CreateMTextWithArea(Transaction tr, BlockTableRecord btr, Point3d loc, string name, double areaM2)
        {
            EnsureLayerExists(tr, tr.GetObject(btr.OwnerId, OpenMode.ForRead).Database, TargetLayer, 4);
            string txtFormat = "\\pxqc;{\\H750;\\L" + name + "\\l}\\P{\\H600;" + areaM2.ToString("F2") + " m\\U+00B2}";

            MText mt = new MText
            {
                Contents = txtFormat,
                Location = loc,
                TextHeight = 750,
                Attachment = AttachmentPoint.BottomCenter,
                Layer = TargetLayer,
                ColorIndex = 2,
                LineSpacingFactor = 0.8 // Tinh chỉnh theo yêu cầu của bạn
            };
            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
        }

        public static void InsertCogBlock(Transaction tr, Database db, BlockTableRecord btr, Point3d loc, double scale)
        {
            EnsureLayerExists(tr, db, TargetLayer, 4);
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(CogBlockName))
            {
                bt.UpgradeOpen(); BlockTableRecord newBtr = new BlockTableRecord { Name = CogBlockName };

                Circle outerCircle = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.5);
                newBtr.AppendEntity(outerCircle);

                Hatch h1 = new Hatch(); newBtr.AppendEntity(h1); h1.SetDatabaseDefaults(); h1.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                Point2dCollection pts1 = new Point2dCollection { new Point2d(0, 0), new Point2d(0.5, 0), new Point2d(0, 0.5), new Point2d(0, 0) };
                DoubleCollection blg1 = new DoubleCollection { 0.0, Math.Tan(Math.PI / 8.0), 0.0, 0.0 };
                h1.AppendLoop(HatchLoopTypes.Default, pts1, blg1); h1.EvaluateHatch(true);

                Hatch h2 = new Hatch(); newBtr.AppendEntity(h2); h2.SetDatabaseDefaults(); h2.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                Point2dCollection pts2 = new Point2dCollection { new Point2d(0, 0), new Point2d(-0.5, 0), new Point2d(0, -0.5), new Point2d(0, 0) };
                DoubleCollection blg2 = new DoubleCollection { 0.0, Math.Tan(Math.PI / 8.0), 0.0, 0.0 };
                h2.AppendLoop(HatchLoopTypes.Default, pts2, blg2); h2.EvaluateHatch(true);

                bt.Add(newBtr); tr.AddNewlyCreatedDBObject(newBtr, true);
            }
            BlockReference bref = new BlockReference(loc, bt[CogBlockName]) { ScaleFactors = new Scale3d(scale), Layer = TargetLayer };
            btr.AppendEntity(bref); tr.AddNewlyCreatedDBObject(bref, true);
        }

        public static void InsertBaseCogBlock(Transaction tr, Database db, BlockTableRecord btr, Point3d loc, double scale)
        {
            EnsureLayerExists(tr, db, BaseCogLayer, 1);
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BaseCogBlockName))
            {
                bt.UpgradeOpen(); BlockTableRecord newBtr = new BlockTableRecord { Name = BaseCogBlockName };
                Circle c = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.3) { ColorIndex = 1 };
                Line lx = new Line(Point3d.Origin, new Point3d(1.2, 0, 0)) { ColorIndex = 1 };
                Polyline px = new Polyline(); px.AddVertexAt(0, new Point2d(1.0, 0.15), 0, 0, 0); px.AddVertexAt(1, new Point2d(1.2, 0), 0, 0, 0); px.AddVertexAt(2, new Point2d(1.0, -0.15), 0, 0, 0); px.ColorIndex = 1;
                Line ly = new Line(Point3d.Origin, new Point3d(0, 1.2, 0)) { ColorIndex = 1 };
                Polyline py = new Polyline(); py.AddVertexAt(0, new Point2d(0.15, 1.0), 0, 0, 0); py.AddVertexAt(1, new Point2d(0, 1.2), 0, 0, 0); py.AddVertexAt(2, new Point2d(-0.15, 1.0), 0, 0, 0); py.ColorIndex = 1;
                newBtr.AppendEntity(c); newBtr.AppendEntity(lx); newBtr.AppendEntity(px); newBtr.AppendEntity(ly); newBtr.AppendEntity(py);
                bt.Add(newBtr); tr.AddNewlyCreatedDBObject(newBtr, true);
            }
            BlockReference bref = new BlockReference(loc, bt[BaseCogBlockName]) { ScaleFactors = new Scale3d(scale), Layer = BaseCogLayer };
            btr.AppendEntity(bref); tr.AddNewlyCreatedDBObject(bref, true);
        }

        public static void AddXData(Polyline pl, int no, string name, Point3d basePoint, string category, bool isInsertCog, Transaction tr, Database db)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppName)) { rat.UpgradeOpen(); RegAppTableRecord regRecord = new RegAppTableRecord { Name = RegAppName }; rat.Add(regRecord); tr.AddNewlyCreatedDBObject(regRecord, true); }

            ResultBuffer rb = new ResultBuffer();
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName));
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger32, no));
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, name));

            if (isInsertCog)
            {
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, basePoint.X));
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, basePoint.Y));
            }

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, category));
            pl.XData = rb;
        }

        public static bool HasOurXData(Entity ent)
        {
            if (ent == null || ent.XData == null) return false;
            return ent.XData.AsArray().Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value.ToString() == RegAppName);
        }
    }
}