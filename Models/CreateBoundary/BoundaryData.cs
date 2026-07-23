using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_CreateBoundary.Models
{
    public class BoundaryData
    {
        public int No { get; set; }
        public string PlateName { get; set; }
        public double Area { get; set; }
        public string XCog { get; set; }
        public string YCog { get; set; }
        public string Category { get; set; } // ĐÃ THÊM MỚI
        public ObjectId Id { get; set; }
    }
}