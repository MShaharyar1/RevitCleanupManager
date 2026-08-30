using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Excel
{
    /// <summary>A single row read back from an imported Excel file, ready to apply.</summary>
    public class ImportedUpdateRow
    {
        public ElementId Id { get; set; }
        public string ParameterName { get; set; }
        public string NewValue { get; set; }
    }

    /// <summary>
    /// Excel export/import for both QA/QC issue fixing and general bulk parameter editing
    /// (Diroots Param/Family/Sheet Manager style). Both flows produce the same shape --
    /// one row per (element, parameter) pair with a "New Value" column -- so one importer
    /// (and one ParameterUpdateExecutor) handles applying either kind of edit.
    ///
    /// Element Id is written as a hidden-ish utility column ("_ElementId", Int64 value)
    /// so re-import can match rows back to model elements even after the user reorders
    /// or filters rows in Excel. Do not rename or delete that column.
    /// </summary>
    public class ExcelRoundTripService
    {
        private const string IdColumnHeader = "_ElementId";

        public void ExportQaIssues(List<QaIssue> issues, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("QA-QC Issues");

            string[] headers = { IdColumnHeader, "Category", "Element Name", "Issue Type", "Parameter", "Current Value", "Severity", "Rule", "New Value" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var i in issues)
            {
                ws.Cell(r, 1).Value = i.Id.IntegerValue;
                ws.Cell(r, 2).Value = i.RevitCategory;
                ws.Cell(r, 3).Value = i.ElementName;
                ws.Cell(r, 4).Value = i.IssueType.ToString();
                ws.Cell(r, 5).Value = i.ParameterName;
                ws.Cell(r, 6).Value = i.CurrentValue;
                ws.Cell(r, 7).Value = i.Severity.ToString();
                ws.Cell(r, 8).Value = i.RuleDescription;
                ws.Cell(r, 9).Value = i.ProposedValue; // pre-filled so the user can just tweak and re-import
                r++;
            }

            ws.Column(1).Hide();
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        public void ExportParameterGrid(List<ParameterGridRow> rows, List<string> parameterNames, string filePath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Parameters");

            var headers = new List<string> { IdColumnHeader, "Category", "Family", "Type", "Name" };
            headers.AddRange(parameterNames);
            for (int c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var row in rows)
            {
                ws.Cell(r, 1).Value = row.Id.IntegerValue;
                ws.Cell(r, 2).Value = row.RevitCategory;
                ws.Cell(r, 3).Value = row.FamilyName;
                ws.Cell(r, 4).Value = row.TypeName;
                ws.Cell(r, 5).Value = row.ElementName;
                for (int c = 0; c < parameterNames.Count; c++)
                    ws.Cell(r, 6 + c).Value = row.Values.TryGetValue(parameterNames[c], out var v) ? v : "";
                r++;
            }

            ws.Column(1).Hide();
            ws.Columns().AdjustToContents();
            wb.SaveAs(filePath);
        }

        /// <summary>
        /// Reads back a QA-issue-style export (fixed "New Value" as the last column) and
        /// returns one update row per non-empty New Value cell.
        /// </summary>
        public List<ImportedUpdateRow> ImportQaFixes(string filePath)
        {
            var result = new List<ImportedUpdateRow>();
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheet(1);
            var headerRow = ws.Row(1);
            int idCol = FindColumn(headerRow, IdColumnHeader);
            int paramCol = FindColumn(headerRow, "Parameter");
            int newValCol = FindColumn(headerRow, "New Value");
            if (idCol == 0 || paramCol == 0 || newValCol == 0) return result;

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var idCell = row.Cell(idCol);
                if (idCell.IsEmpty()) continue;
                var newVal = row.Cell(newValCol).GetString();
                if (string.IsNullOrWhiteSpace(newVal)) continue; // untouched rows are skipped

                result.Add(new ImportedUpdateRow
                {
                    Id = new ElementId(int.Parse(idCell.GetString(), CultureInfo.InvariantCulture)),
                    ParameterName = row.Cell(paramCol).GetString(),
                    NewValue = newVal
                });
            }
            return result;
        }

        /// <summary>
        /// Reads back a bulk parameter grid export. Any non-empty cell in a parameter
        /// column produces an update row for that (element, parameter). Cells left exactly
        /// as exported also count (harmless no-op set) -- to intentionally clear a value,
        /// type the literal text <blank> in the cell.
        /// </summary>
        public List<ImportedUpdateRow> ImportParameterGrid(string filePath)
        {
            var result = new List<ImportedUpdateRow>();
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheet(1);
            var headerRow = ws.Row(1);
            int idCol = FindColumn(headerRow, IdColumnHeader);
            if (idCol == 0) return result;

            var paramCols = new List<(int Col, string Name)>();
            foreach (var cell in headerRow.CellsUsed())
            {
                var header = cell.GetString();
                if (header == IdColumnHeader || header == "Category" || header == "Family" || header == "Type" || header == "Name") continue;
                paramCols.Add((cell.Address.ColumnNumber, header));
            }

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var idCell = row.Cell(idCol);
                if (idCell.IsEmpty()) continue;
                var id = new ElementId(int.Parse(idCell.GetString(), CultureInfo.InvariantCulture));

                foreach (var (col, name) in paramCols)
                {
                    var val = row.Cell(col).GetString();
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    result.Add(new ImportedUpdateRow { Id = id, ParameterName = name, NewValue = val == "<blank>" ? "" : val });
                }
            }
            return result;
        }

        private static int FindColumn(IXLRow headerRow, string name)
        {
            foreach (var cell in headerRow.CellsUsed())
                if (cell.GetString() == name) return cell.Address.ColumnNumber;
            return 0;
        }
    }
}
