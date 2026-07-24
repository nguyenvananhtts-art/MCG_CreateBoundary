using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using MCG_CreateBoundary.Models;
using MCG_CreateBoundary.UI;
using MCG_CreateBoundary.Services;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace MCG_CreateBoundary.Views
{
    public partial class BoundaryView : UserControl
    {
        public BoundaryView() { InitializeComponent(); }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.SendStringToExecute("MCG_INTERNAL_CREATE ", true, false, false);
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            SplitBoundary.ClearHighlightState(); PaletteConnector.SyncData();
            if (doc != null) doc.Editor.WriteMessage("\n[MCG] Đã quét lại bản vẽ, dọn dẹp các tấm bị xóa và đồng bộ số liệu mới nhất!");
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (e.AddedItems != null && e.AddedItems.Count > 0)
                {
                    if (e.AddedItems[0] is BoundaryData data) SplitBoundary.HighlightPlateSafe(data.Id);
                }
            }
            catch (Exception ex)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage($"\n[WPF] LỖI FATAL NGẦM: {ex.Message}");
            }
        }

        private void MenuItem_CopyData_Click(object sender, RoutedEventArgs e)
        {
            var vm = PaletteConnector.MyVM; if (vm == null || vm.Boundaries.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("No\tPlateName\tArea\tX_COG\tY_COG\tCategory");
            foreach (var item in vm.Boundaries)
            {
                sb.AppendLine($"{item.No}\t{item.PlateName}\t{item.Area:F2}\t{item.XCog}\t{item.YCog}\t{item.Category}");
            }
            Clipboard.SetText(sb.ToString()); MessageBox.Show("Data copied to Clipboard!", "MCG Tool");
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var vm = PaletteConnector.MyVM;
            if (vm == null || vm.Boundaries.Count == 0) { MessageBox.Show("Bảng dữ liệu đang trống, không có gì để xuất!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            List<BoundaryData> itemsToExport = new List<BoundaryData>();
            if (BoundaryDataGrid.SelectedItems.Count > 0)
            {
                foreach (var item in BoundaryDataGrid.SelectedItems) { if (item is BoundaryData data) itemsToExport.Add(data); }
            }
            else itemsToExport.AddRange(vm.Boundaries);

            SaveFileDialog sfd = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", Title = "Save Data to Excel", FileName = "Boundary_Data_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx" };
            if (sfd.ShowDialog() == true) ExportDataToExcelCOM(itemsToExport, sfd.FileName);
        }

        private void ExportDataToExcelCOM(List<BoundaryData> dataList, string filePath)
        {
            Excel.Application xlApp = null; Excel.Workbook xlWorkBook = null; Excel.Worksheet xlWorkSheet = null; Excel.Range fullDataRange = null;
            try
            {
                xlApp = new Excel.Application();
                if (xlApp == null) { MessageBox.Show("Máy tính chưa cài đặt Microsoft Excel!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); return; }

                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                xlApp.DisplayAlerts = false; xlWorkBook = xlApp.Workbooks.Add();
                xlWorkSheet = (Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1); xlWorkSheet.Name = "Plate Data";

                int rowCount = dataList.Count; int colCount = 6; // ĐÃ SỬA THÀNH 6 CỘT
                object[,] dataArray = new object[rowCount + 1, colCount];

                dataArray[0, 0] = "No"; dataArray[0, 1] = "Plate Name"; dataArray[0, 2] = "Area (mm²)"; dataArray[0, 3] = "X_COG (mm)"; dataArray[0, 4] = "Y_COG (mm)"; dataArray[0, 5] = "Category";

                for (int i = 0; i < rowCount; i++)
                {
                    dataArray[i + 1, 0] = dataList[i].No; dataArray[i + 1, 1] = dataList[i].PlateName; dataArray[i + 1, 2] = dataList[i].Area;
                    dataArray[i + 1, 3] = dataList[i].XCog; dataArray[i + 1, 4] = dataList[i].YCog; dataArray[i + 1, 5] = dataList[i].Category;
                }

                string endCell = $"F{rowCount + 1}"; // ĐÃ SỬA THÀNH CỘT F
                fullDataRange = xlWorkSheet.Range["A1", endCell];
                fullDataRange.Value2 = dataArray;

                Excel.Range headerRange = xlWorkSheet.Range["A1", "F1"];
                headerRange.Font.Bold = true; headerRange.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightGray);
                fullDataRange.Borders.LineStyle = Excel.XlLineStyle.xlContinuous; fullDataRange.Columns.AutoFit();

                xlWorkBook.SaveAs(filePath, Excel.XlFileFormat.xlOpenXMLWorkbook, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Excel.XlSaveAsAccessMode.xlNoChange, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                MessageBox.Show("Xuất dữ liệu ra Excel thành công!", "MCG Tool", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Diagnostics.Process.Start(filePath);
                Marshal.ReleaseComObject(headerRange);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi khi xuất file Excel:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                if (fullDataRange != null) Marshal.ReleaseComObject(fullDataRange); if (xlWorkSheet != null) Marshal.ReleaseComObject(xlWorkSheet);
                if (xlWorkBook != null) { try { xlWorkBook.Close(false); } catch { } Marshal.ReleaseComObject(xlWorkBook); }
                if (xlApp != null) { try { xlApp.Quit(); } catch { } Marshal.ReleaseComObject(xlApp); }
                fullDataRange = null; xlWorkSheet = null; xlWorkBook = null; xlApp = null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers();
            }
        }
    }
}