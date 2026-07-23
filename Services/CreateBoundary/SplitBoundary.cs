using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.GraphicsInterface;
using MCG_CreateBoundary.Models;
using MCG_CreateBoundary.ViewModels;

using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace MCG_CreateBoundary.Services
{
    public class SplitBoundary
    {
        private static ObjectId _lastHighlightedId = ObjectId.Null;
        private static List<Entity> _transientGraphics = new List<Entity>();
        private static Entity _paletteTransient = null;

        public static void ClearHighlightState()
        {
            _lastHighlightedId = ObjectId.Null;
            if (_paletteTransient != null)
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(_paletteTransient, new IntegerCollection()); } catch { }
                if (!_paletteTransient.IsDisposed) _paletteTransient.Dispose();
                _paletteTransient = null;
            }
        }

        private static void AddGhostHighlight(Entity ent, short colorIndex)
        {
            if (ent == null) return;
            ent.ColorIndex = colorIndex;
            TransientManager.CurrentTransientManager.AddTransient(ent, TransientDrawingMode.Main, 128, new IntegerCollection());
            _transientGraphics.Add(ent);
        }

        private static void ClearGhostHighlights()
        {
            if (_transientGraphics.Count == 0) return;
            foreach (var ent in _transientGraphics)
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(ent, new IntegerCollection()); } catch { }
                if (!ent.IsDisposed) ent.Dispose();
            }
            _transientGraphics.Clear();
        }

        private static bool IsLayerLocked(ObjectId layerId, Transaction tr, Dictionary<ObjectId, bool> cache)
        {
            if (cache.TryGetValue(layerId, out bool isLocked)) return isLocked;
            LayerTableRecord ltr = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
            isLocked = ltr != null && ltr.IsLocked; cache[layerId] = isLocked; return isLocked;
        }

        private const string ErrorLayerName = "MCG_ERROR_GAPS";

        private static void ClearPhysicalErrors(Transaction tr, Database db)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null && ent.Layer == ErrorLayerName) { ent.UpgradeOpen(); ent.Erase(); }
            }
        }

        private static void DrawPhysicalErrorCircle(Transaction tr, Database db, Point3d pt)
        {
            CreateBoundary.EnsureLayerExists(tr, db, ErrorLayerName, 1);
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            double r = 300.0;
            Circle c = new Circle(pt, Vector3d.ZAxis, r) { Layer = ErrorLayerName, ColorIndex = 1 };
            btr.AppendEntity(c); tr.AddNewlyCreatedDBObject(c, true);
        }

        public static void ExecuteCreate(Editor ed, BoundaryViewModel vm)
        {
            if (vm == null) return;
            ed.WriteMessage("\n[MCG] --- TIẾN TRÌNH TẠO PLATE ---");
            if (vm.IsMethodSplitLines) RunMethodSplitLines(ed, vm);
            else RunMethodPickPoint(ed, vm);
        }

        public static List<Tuple<Point3d, string>> ScanForCategoryBlocks(Transaction tr, BlockTableRecord btr)
        {
            var list = new List<Tuple<Point3d, string>>();
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName == "INSERT")
                {
                    BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br != null)
                    {
                        string bName = br.Name;
                        if (br.IsDynamicBlock) { BlockTableRecord dbr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord; bName = dbr.Name; }
                        if (bName.StartsWith("Cat ", StringComparison.OrdinalIgnoreCase)) list.Add(new Tuple<Point3d, string>(br.Position, bName.Substring(4).Trim()));
                    }
                }
            }
            return list;
        }

        // =========================================================================
        // OPTION 1: SPLIT LINES (Có trừ lỗ)
        // =========================================================================
        private static void RunMethodSplitLines(Editor ed, BoundaryViewModel vm)
        {
            Document doc = ed.Document; Database db = doc.Database; PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\n[Option 1] Quét chọn khung bao và các đường chia:" }; PromptSelectionResult psr = ed.GetSelection(pso); if (psr.Status != PromptStatus.OK) return;
            Point3d basePt = Point3d.Origin; bool hasBasePt = false;
            if (vm.IsInsertCog) { PromptPointOptions ppoBase = new PromptPointOptions("\n[MCG] Pick điểm gốc tọa độ CNC:"); ppoBase.AllowNone = true; PromptPointResult pprBase = ed.GetPoint(ppoBase); if (pprBase.Status == PromptStatus.Cancel) return; if (pprBase.Status == PromptStatus.OK) basePt = pprBase.Value; hasBasePt = true; }
            ObjectId[] ids = psr.Value.GetObjectIds();
            using (DocumentLock loc = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var catBlocks = ScanForCategoryBlocks(tr, btr);
                Extents3d globalExt; List<Curve> allCurves = FlattenUtils.FlattenAndExtractCurves(ids.ToList(), tr, btr, ed, out globalExt);
                double globalScale = globalExt.MaxPoint.DistanceTo(globalExt.MinPoint) * 0.05;
                if (vm.IsInsertCog && hasBasePt) CreateBoundary.InsertBaseCogBlock(tr, db, btr, basePt, globalScale);

                // CHẠY LÕI TỰ VÁ
                List<Region> regs = Case1_BasicSplit.GetRegions(allCurves, ed);
                if (regs.Count == 0) regs = Case2_ExtensionSplitLine.GetRegions(allCurves, ed);
                if (regs.Count == 0) regs = Case3_EndpointBridging.GetRegions(allCurves, ed);

                // PHÂN LOẠI "TẤM PLATE" VÀ "LỖ KHOÉT" (CHỐNG TẠO PLATE RÁC TỪ LỖ)
                List<Region> validOuterPlates = new List<Region>();
                List<Region> allHoles = new List<Region>();

                if (vm.IsSubtractHole)
                {
                    foreach (Region r1 in regs)
                    {
                        foreach (Region r2 in regs)
                        {
                            if (r1 == r2) continue;
                            if (Math.Abs(r1.Area - r2.Area) < 1e-4) continue;
                            if (BoundaryUtils.IsPointInRegion(r1, BoundaryUtils.GetCentroidFromRegion(r2)))
                            {
                                if (!allHoles.Contains(r2)) allHoles.Add(r2); // r2 là cái Lỗ!
                            }
                        }
                    }
                }

                foreach (Region r in regs)
                {
                    if (vm.IsSubtractHole && allHoles.Contains(r)) continue; // Lỗ thì không xuất thành Tấm Thép
                    validOuterPlates.Add(r);
                }

                int count = 0; int startNo = GetLastNumber(db) + 1;
                foreach (Region r in validOuterPlates)
                {
                    BoundaryUtils.ProcessRegionToPlate(r, allHoles, tr, btr, db, $"PL-{startNo + count}", startNo + count, basePt, catBlocks, vm, ed);
                    count++;
                }

                foreach (Region r in regs) { if (r != null && !r.IsDisposed) r.Dispose(); }

                if (vm.IsDeleteOriginal && count > 0) { foreach (var id in ids) { Entity e = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (e != null) e.Erase(); } }
                tr.Commit();
            }
            ed.UpdateScreen(); UI.PaletteConnector.SyncData();
        }

        // =========================================================================
        // OPTION 2: LƯỜI BIẾNG TUYỆT ĐỐI & ĐỤC LỖ
        // =========================================================================
        private static void RunMethodPickPoint(Editor ed, BoundaryViewModel vm)
        {
            Document doc = ed.Document; Database db = doc.Database; int plateCount = 0; int startNo = GetLastNumber(db) + 1;
            ed.WriteMessage("\n[MCG] KÍCH HOẠT OPTION 2: Chế độ tối ưu không gian cục bộ.");

            PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\n[BƯỚC 1] Quét chọn khu vực làm việc (Bao gồm cả các lỗ):" };
            PromptSelectionResult psr = ed.GetSelection(pso); if (psr.Status != PromptStatus.OK) return;

            Point3d basePt = Point3d.Origin; bool hasBasePt = false;
            if (vm.IsInsertCog)
            {
                PromptPointOptions ppoBase = new PromptPointOptions("\n[MCG] Pick điểm gốc tọa độ CNC:"); ppoBase.AllowNone = true;
                PromptPointResult pprBase = ed.GetPoint(ppoBase); if (pprBase.Status == PromptStatus.Cancel) return;
                if (pprBase.Status == PromptStatus.OK) basePt = pprBase.Value; hasBasePt = true;
            }

            List<ObjectId> selectedIds = psr.Value.GetObjectIds().ToList();
            List<Tuple<Point3d, string>> catBlocks = new List<Tuple<Point3d, string>>();
            Extents3d globalRawExtents = new Extents3d();

            ClearGhostHighlights();

            using (DocumentLock loc = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                catBlocks = ScanForCategoryBlocks(tr, btr);

                foreach (ObjectId id in selectedIds)
                {
                    Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (c != null)
                    {
                        try { globalRawExtents.AddExtents(c.GeometricExtents); } catch { }
                        AddGhostHighlight(c.Clone() as Curve, 6);
                    }
                }

                double globalScale = 1.0;
                try { double diag = globalRawExtents.MaxPoint.DistanceTo(globalRawExtents.MinPoint); if (diag > 0) globalScale = diag * 0.05; } catch { }
                if (vm.IsInsertCog && hasBasePt) CreateBoundary.InsertBaseCogBlock(tr, db, btr, basePt, globalScale);

                tr.Commit();
            }
            ed.UpdateScreen();

            object oldOsMode = Application.GetSystemVariable("OSMODE");
            try
            {
                Application.SetSystemVariable("OSMODE", 0);
                while (true)
                {
                    PromptPointOptions ppo = new PromptPointOptions("\nClick điểm tạo Plate (Esc để thoát):") { AllowNone = true };
                    PromptPointResult ppr = ed.GetPoint(ppo); if (ppr.Status != PromptStatus.OK) break;
                    Point3d pt = ppr.Value;

                    using (DocumentLock loc = doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        ClearPhysicalErrors(tr, db);

                        List<ObjectId> localIds; Extents3d localBox;
                        BoundaryUtils.GetLocalIds(tr, selectedIds, pt, out localIds, out localBox);

                        if (localIds.Count > 0)
                        {
                            Extents3d ignoredExt;
                            List<Curve> localFlattenedCurves = FlattenUtils.FlattenAndExtractCurves(localIds, tr, btr, ed, out ignoredExt);

                            // LÕI TOÁN HỌC (Sinh ra cả miền ngoài lẫn các lỗ bên trong)
                            List<Region> localRegs = Case1_BasicSplit.GetRegions(localFlattenedCurves, ed);
                            if (localRegs.Count == 0) localRegs = Case2_ExtensionSplitLine.GetRegions(localFlattenedCurves, ed);
                            if (localRegs.Count == 0) localRegs = Case3_EndpointBridging.GetRegions(localFlattenedCurves, ed);

                            Region clickedReg = null;
                            foreach (Region r in localRegs)
                            {
                                // Tìm đúng cái Plate chứa con trỏ chuột
                                if (BoundaryUtils.IsPointInRegion(r, pt)) { clickedReg = r; break; }
                            }

                            if (clickedReg != null)
                            {
                                string pName = $"PL-{startNo + plateCount}";
                                // Hàm ProcessRegionToPlate sẽ lo vụ đục lỗ nếu localRegs chứa các đảo bên trong clickedReg
                                Polyline newPl = BoundaryUtils.ProcessRegionToPlate(clickedReg, localRegs, tr, btr, db, pName, startNo + plateCount, basePt, catBlocks, vm, ed);
                                if (newPl != null) AddGhostHighlight(newPl.Clone() as Polyline, 3);
                                plateCount++;
                                ed.WriteMessage($"\n  -> Đã tạo {pName} thành công!");
                            }
                            else
                            {
                                ed.WriteMessage("\n[⚠] Không thể khép kín. Đang dò tìm khe hở...");
                                var gaps = BoundaryUtils.FindLocalGaps(localFlattenedCurves, pt, localBox);

                                foreach (var gap in gaps.Take(5)) DrawPhysicalErrorCircle(tr, db, gap);

                                if (gaps.Count > 0) ed.WriteMessage($"\n[MCG] Đã đóng dấu đỏ {Math.Min(gaps.Count, 5)} vị trí hở sát chuột. Hãy nối lại!");
                                else ed.WriteMessage("\n[MCG] Không tìm thấy lỗi cụ thể. Hãy kiểm tra lại các ranh giới.");
                            }

                            foreach (Region r in localRegs) { if (r != null && !r.IsDisposed) r.Dispose(); }
                        }
                        tr.Commit();
                    }
                    ed.UpdateScreen();
                }
            }
            finally
            {
                Application.SetSystemVariable("OSMODE", oldOsMode);
                ClearGhostHighlights();
            }

            if (vm.IsDeleteOriginal && plateCount > 0)
            {
                using (DocumentLock loc = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in selectedIds)
                    {
                        Entity e = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (e != null) e.Erase();
                    }
                    tr.Commit();
                }
            }
            ed.Regen(); UI.PaletteConnector.SyncData();
        }

        public static List<BoundaryData> ScanDocument(Database db)
        {
            var res = new List<BoundaryData>(); using (var tr = db.TransactionManager.StartTransaction()) { var btrId = SymbolUtilityServices.GetBlockModelSpaceId(db); var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead); foreach (ObjectId id in btr) { if (id.IsNull || id.IsErased || !id.IsValid) continue; var ent = tr.GetObject(id, OpenMode.ForRead) as Entity; if (ent is Polyline pl && CreateBoundary.HasOurXData(pl)) { var tvs = pl.XData.AsArray(); if (tvs.Length >= 3) { string xStr = "-"; string yStr = "-"; string catStr = "-"; int len = tvs.Length; if (len == 4) { catStr = tvs[3].Value.ToString(); } else if (len == 5) { double baseX = 0; double baseY = 0; double.TryParse(tvs[3].Value.ToString(), out baseX); double.TryParse(tvs[4].Value.ToString(), out baseY); Point3d c = BoundaryUtils.GetCentroidFromPolyline(pl); xStr = Math.Round(c.X - baseX, 2).ToString(); yStr = Math.Round(c.Y - baseY, 2).ToString(); } else if (len >= 6) { double baseX = 0; double baseY = 0; double.TryParse(tvs[3].Value.ToString(), out baseX); double.TryParse(tvs[4].Value.ToString(), out baseY); Point3d c = BoundaryUtils.GetCentroidFromPolyline(pl); xStr = Math.Round(c.X - baseX, 2).ToString(); yStr = Math.Round(c.Y - baseY, 2).ToString(); catStr = tvs[5].Value.ToString(); } res.Add(new BoundaryData { No = (int)tvs[1].Value, PlateName = tvs[2].Value.ToString(), Area = pl.Area, XCog = xStr, YCog = yStr, Category = catStr, Id = id }); } } } tr.Commit(); }
            return res;
        }

        public static int GetLastNumber(Database db) { var d = ScanDocument(db); return d.Count == 0 ? 0 : d.Max(p => p.No); }

        public static void HighlightPlateSafe(ObjectId id)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            try
            {
                using (DocumentLock loc = doc.LockDocument())
                {
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {

                        if (_paletteTransient != null)
                        {
                            try { TransientManager.CurrentTransientManager.EraseTransient(_paletteTransient, new IntegerCollection()); } catch { }
                            if (!_paletteTransient.IsDisposed) _paletteTransient.Dispose();
                            _paletteTransient = null;
                        }

                        if (_lastHighlightedId != ObjectId.Null && !_lastHighlightedId.IsErased && _lastHighlightedId.IsValid)
                        {
                            if (_lastHighlightedId.Database == doc.Database)
                            {
                                Entity oldEnt = tr.GetObject(_lastHighlightedId, OpenMode.ForRead) as Entity;
                                if (oldEnt != null) oldEnt.Unhighlight();
                            }
                        }

                        if (id.IsNull || id.IsErased || !id.IsValid || id.Database != doc.Database)
                        {
                            tr.Commit();
                            return;
                        }

                        Entity newEnt = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (newEnt != null)
                        {
                            newEnt.Highlight();
                            _lastHighlightedId = id;

                            _paletteTransient = newEnt.Clone() as Entity;
                            if (_paletteTransient != null)
                            {
                                _paletteTransient.ColorIndex = 6;

                                if (_paletteTransient is Polyline pl)
                                {
                                    try
                                    {
                                        double diag = pl.GeometricExtents.MaxPoint.DistanceTo(pl.GeometricExtents.MinPoint);
                                        pl.ConstantWidth = diag * 0.005;
                                    }
                                    catch { }
                                }
                                TransientManager.CurrentTransientManager.AddTransient(_paletteTransient, TransientDrawingMode.Main, 128, new IntegerCollection());
                            }
                        }
                        tr.Commit();
                    }
                }
                ed.UpdateScreen();
            }
            catch { }
        }
    }
}