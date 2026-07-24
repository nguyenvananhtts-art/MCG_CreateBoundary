using Autodesk.AutoCAD.Runtime;
using MCG_CreateBoundary.UI;
using MCG_CreateBoundary.Services;

[assembly: CommandClass(typeof(MCG_CreateBoundary.Commands.BoundaryCommands))]
namespace MCG_CreateBoundary.Commands
{
    public class BoundaryCommands
    {
        // Lệnh duy nhất để hiện Palette
        [CommandMethod("MCG_CreateBoundary")]
        public void ShowPalette() => PaletteConnector.ShowPalette();

        // Lệnh nội bộ để Button gọi
        [CommandMethod("MCG_INTERNAL_CREATE", CommandFlags.Modal)]
        public void InternalCreate()
        {
            var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            SplitBoundary.ExecuteCreate(ed, PaletteConnector.MyVM);
        }
    }
}