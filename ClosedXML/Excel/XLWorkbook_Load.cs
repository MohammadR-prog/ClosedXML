#nullable disable

using ClosedXML.Extensions;
using ClosedXML.Utils;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ap = DocumentFormat.OpenXml.ExtendedProperties;
using Formula = DocumentFormat.OpenXml.Spreadsheet.Formula;
using Op = DocumentFormat.OpenXml.CustomProperties;
using X14 = DocumentFormat.OpenXml.Office2010.Excel;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ClosedXML.Excel
{
    using Ap;
    using Drawings;
    using Op;
    using SixLabors.ImageSharp;

    public partial class XLWorkbook
    {
        private readonly Dictionary<String, Color> _colorList = new Dictionary<string, Color>();

        private void Load(String file)
        {
            LoadSheets(file);
        }

        private void Load(Stream stream)
        {
            LoadSheets(stream);
        }

        private void LoadSheets(String fileName)
        {
            using (var dSpreadsheet = SpreadsheetDocument.Open(fileName, false))
                LoadSpreadsheetDocument(dSpreadsheet);
        }

        private void LoadSheets(Stream stream)
        {
            using (var dSpreadsheet = SpreadsheetDocument.Open(stream, false))
                LoadSpreadsheetDocument(dSpreadsheet);
        }

        private void LoadSheetsFromTemplate(String fileName)
        {
            using (var dSpreadsheet = SpreadsheetDocument.CreateFromTemplate(fileName))
                LoadSpreadsheetDocument(dSpreadsheet);

            // If we load a workbook as a template, we have to treat it as a "new" workbook.
            // The original file will NOT be copied into place before changes are applied
            // Hence all loaded RelIds have to be cleared
            ResetAllRelIds();
        }

        private void ResetAllRelIds()
        {
            foreach (var ws in Worksheets.Cast<XLWorksheet>())
            {
                ws.SheetId = 0;
                ws.RelId = null;

                foreach (var pt in ws.PivotTables.Cast<XLPivotTable>())
                {
                    pt.WorkbookCacheRelId = null;
                    pt.CacheDefinitionRelId = null;
                    pt.RelId = null;
                }

                foreach (var picture in ws.Pictures.Cast<XLPicture>())
                    picture.RelId = null;

                foreach (var table in ws.Tables.Cast<XLTable>())
                    table.RelId = null;
            }
        }

        private void LoadSpreadsheetDocument(SpreadsheetDocument dSpreadsheet)
        {
            ShapeIdManager = new XLIdManager();
            SetProperties(dSpreadsheet);

            SharedStringItem[] sharedStrings = null;
            if (dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().Any())
            {
                var shareStringPart = dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().First();
                sharedStrings = shareStringPart.SharedStringTable.Elements<SharedStringItem>().ToArray();
            }

            if (dSpreadsheet.CustomFilePropertiesPart != null)
            {
                foreach (var m in dSpreadsheet.CustomFilePropertiesPart.Properties.Elements<CustomDocumentProperty>())
                {
                    String name = m.Name?.Value;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (m.VTLPWSTR != null)
                        CustomProperties.Add(name, m.VTLPWSTR.Text);
                    else if (m.VTFileTime != null)
                    {
                        CustomProperties.Add(name,
                                             DateTime.ParseExact(m.VTFileTime.Text, "yyyy'-'MM'-'dd'T'HH':'mm':'ssK",
                                                                 CultureInfo.InvariantCulture));
                    }
                    else if (m.VTDouble != null)
                        CustomProperties.Add(name, Double.Parse(m.VTDouble.Text, CultureInfo.InvariantCulture));
                    else if (m.VTBool != null)
                        CustomProperties.Add(name, m.VTBool.Text == "true");
                }
            }

            var wbProps = dSpreadsheet.WorkbookPart.Workbook.WorkbookProperties;
            if (wbProps != null)
                Use1904DateSystem = OpenXmlHelper.GetBooleanValueAsBool(wbProps.Date1904, false);

            var wbFilesharing = dSpreadsheet.WorkbookPart.Workbook.FileSharing;
            if (wbFilesharing != null)
            {
                FileSharing.ReadOnlyRecommended = OpenXmlHelper.GetBooleanValueAsBool(wbFilesharing.ReadOnlyRecommended, false);
                FileSharing.UserName = wbFilesharing.UserName?.Value;
            }

            LoadWorkbookProtection(dSpreadsheet.WorkbookPart.Workbook.WorkbookProtection, this);

            var calculationProperties = dSpreadsheet.WorkbookPart.Workbook.CalculationProperties;
            if (calculationProperties != null)
            {
                var calculateMode = calculationProperties.CalculationMode;
                if (calculateMode != null)
                    CalculateMode = calculateMode.Value.ToClosedXml();

                var calculationOnSave = calculationProperties.CalculationOnSave;
                if (calculationOnSave != null)
                    CalculationOnSave = calculationOnSave.Value;

                var forceFullCalculation = calculationProperties.ForceFullCalculation;
                if (forceFullCalculation != null)
                    ForceFullCalculation = forceFullCalculation.Value;

                var fullCalculationOnLoad = calculationProperties.FullCalculationOnLoad;
                if (fullCalculationOnLoad != null)
                    FullCalculationOnLoad = fullCalculationOnLoad.Value;

                var fullPrecision = calculationProperties.FullPrecision;
                if (fullPrecision != null)
                    FullPrecision = fullPrecision.Value;

                var referenceMode = calculationProperties.ReferenceMode;
                if (referenceMode != null)
                    ReferenceStyle = referenceMode.Value.ToClosedXml();
            }

            var efp = dSpreadsheet.ExtendedFilePropertiesPart;
            if (efp != null && efp.Properties != null)
            {
                if (efp.Properties.Elements<Company>().Any())
                    Properties.Company = efp.Properties.GetFirstChild<Company>().Text;

                if (efp.Properties.Elements<Manager>().Any())
                    Properties.Manager = efp.Properties.GetFirstChild<Manager>().Text;
            }

            Stylesheet s = dSpreadsheet.WorkbookPart.WorkbookStylesPart?.Stylesheet;
            NumberingFormats numberingFormats = s?.NumberingFormats;
            Fills fills = s?.Fills;
            Borders borders = s?.Borders;
            Fonts fonts = s?.Fonts;
            Int32 dfCount = 0;
            Dictionary<Int32, DifferentialFormat> differentialFormats;
            if (s != null && s.DifferentialFormats != null)
                differentialFormats = s.DifferentialFormats.Elements<DifferentialFormat>().ToDictionary(k => dfCount++);
            else
                differentialFormats = new Dictionary<Int32, DifferentialFormat>();

            // If the loaded workbook has a changed "Normal" style, it might affect the default width of a column.
            var normalStyle = s?.CellStyles?.Elements<CellStyle>().FirstOrDefault(x => x.BuiltinId is not null && x.BuiltinId.Value == 0);
            if (normalStyle != null)
            {
                var normalStyleKey = ((XLStyle)Style).Key;
                LoadStyle(ref normalStyleKey, (Int32)normalStyle.FormatId.Value, s, fills, borders, fonts, numberingFormats);
                Style = new XLStyle(null, normalStyleKey);
                ColumnWidth = CalculateColumnWidth(8, Style.Font, this);
            }

            var sheets = dSpreadsheet.WorkbookPart.Workbook.Sheets;
            Int32 position = 0;
            foreach (var dSheet in sheets.OfType<Sheet>())
            {
                position++;
                var sharedFormulasR1C1 = new Dictionary<UInt32, String>();

                var worksheetPart = dSpreadsheet.WorkbookPart.GetPartById(dSheet.Id) as WorksheetPart;

                if (worksheetPart == null)
                {
                    UnsupportedSheets.Add(new UnsupportedSheet { SheetId = dSheet.SheetId.Value, Position = position });
                    continue;
                }

                var sheetName = dSheet.Name;

                var ws = (XLWorksheet)WorksheetsInternal.Add(sheetName, position);
                ws.RelId = dSheet.Id;
                ws.SheetId = (Int32)dSheet.SheetId.Value;

                if (dSheet.State != null)
                    ws.Visibility = dSheet.State.Value.ToClosedXml();

                ApplyStyle(ws, 0, s, fills, borders, fonts, numberingFormats);

                var styleList = new Dictionary<int, IXLStyle>();// {{0, ws.Style}};
                PageSetupProperties pageSetupProperties = null;

                lastRow = 0;

                using (var reader = OpenXmlReader.Create(worksheetPart))
                {
                    Type[] ignoredElements = new Type[]
                    {
                        typeof(CustomSheetViews) // Custom sheet views contain its own auto filter data, and more, which should be ignored for now
                    };

                    while (reader.Read())
                    {
                        while (ignoredElements.Contains(reader.ElementType))
                            reader.ReadNextSibling();

                        if (reader.ElementType == typeof(SheetFormatProperties))
                        {
                            var sheetFormatProperties = (SheetFormatProperties)reader.LoadCurrentElement();
                            if (sheetFormatProperties != null)
                            {
                                if (sheetFormatProperties.DefaultRowHeight != null)
                                    ws.RowHeight = sheetFormatProperties.DefaultRowHeight;

                                ws.RowHeightChanged = (sheetFormatProperties.CustomHeight != null &&
                                                       sheetFormatProperties.CustomHeight.Value);

                                if (sheetFormatProperties.DefaultColumnWidth != null)
                                    ws.ColumnWidth = XLHelper.ConvertWidthToNoC(sheetFormatProperties.DefaultColumnWidth.Value, ws.Style.Font, this);
                                else if (sheetFormatProperties.BaseColumnWidth != null)
                                    ws.ColumnWidth = CalculateColumnWidth(sheetFormatProperties.BaseColumnWidth.Value, ws.Style.Font, this);
                            }
                        }
                        else if (reader.ElementType == typeof(SheetViews))
                            LoadSheetViews((SheetViews)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(MergeCells))
                        {
                            var mergedCells = (MergeCells)reader.LoadCurrentElement();
                            if (mergedCells != null)
                            {
                                foreach (MergeCell mergeCell in mergedCells.Elements<MergeCell>())
                                    ws.Range(mergeCell.Reference).Merge(false);
                            }
                        }
                        else if (reader.ElementType == typeof(Columns))
                            LoadColumns(s, numberingFormats, fills, borders, fonts, ws,
                                        (Columns)reader.LoadCurrentElement());
                        else if (reader.ElementType == typeof(Row))
                        {
                            LoadRows(s, numberingFormats, fills, borders, fonts, ws, sharedStrings, sharedFormulasR1C1,
                                     styleList, (Row)reader.LoadCurrentElement());
                        }
                        else if (reader.ElementType == typeof(AutoFilter))
                            LoadAutoFilter((AutoFilter)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(SheetProtection))
                            LoadSheetProtection((SheetProtection)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(DataValidations))
                            LoadDataValidations((DataValidations)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(ConditionalFormatting))
                            LoadConditionalFormatting((ConditionalFormatting)reader.LoadCurrentElement(), ws, differentialFormats);
                        else if (reader.ElementType == typeof(Hyperlinks))
                            LoadHyperlinks((Hyperlinks)reader.LoadCurrentElement(), worksheetPart, ws);
                        else if (reader.ElementType == typeof(PrintOptions))
                            LoadPrintOptions((PrintOptions)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(PageMargins))
                            LoadPageMargins((PageMargins)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(PageSetup))
                            LoadPageSetup((PageSetup)reader.LoadCurrentElement(), ws, pageSetupProperties);
                        else if (reader.ElementType == typeof(HeaderFooter))
                            LoadHeaderFooter((HeaderFooter)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(SheetProperties))
                            LoadSheetProperties((SheetProperties)reader.LoadCurrentElement(), ws, out pageSetupProperties);
                        else if (reader.ElementType == typeof(RowBreaks))
                            LoadRowBreaks((RowBreaks)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(ColumnBreaks))
                            LoadColumnBreaks((ColumnBreaks)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(WorksheetExtensionList))
                            LoadExtensions((WorksheetExtensionList)reader.LoadCurrentElement(), ws);
                        else if (reader.ElementType == typeof(LegacyDrawing))
                            ws.LegacyDrawingId = (reader.LoadCurrentElement() as LegacyDrawing).Id.Value;
                    }
                    reader.Close();
                }

                (ws.ConditionalFormats as XLConditionalFormats).ReorderAccordingToOriginalPriority();

                #region LoadTables

                foreach (var tableDefinitionPart in worksheetPart.TableDefinitionParts)
                {
                    var relId = worksheetPart.GetIdOfPart(tableDefinitionPart);
                    var dTable = tableDefinitionPart.Table;

                    String reference = dTable.Reference.Value;
                    String tableName = dTable.Name ?? dTable.DisplayName ?? string.Empty;
                    if (String.IsNullOrWhiteSpace(tableName))
                        throw new InvalidDataException("The table name is missing.");

                    var xlTable = ws.Range(reference).CreateTable(tableName, false) as XLTable;
                    xlTable.RelId = relId;

                    if (dTable.HeaderRowCount != null && dTable.HeaderRowCount == 0)
                    {
                        xlTable._showHeaderRow = false;
                        //foreach (var tableColumn in dTable.TableColumns.Cast<TableColumn>())
                        xlTable.AddFields(dTable.TableColumns.Cast<TableColumn>().Select(t => GetTableColumnName(t.Name.Value)));
                    }
                    else
                    {
                        xlTable.InitializeAutoFilter();
                    }

                    if (dTable.TotalsRowCount != null && dTable.TotalsRowCount.Value > 0)
                        ((XLTable)xlTable)._showTotalsRow = true;

                    if (dTable.TableStyleInfo != null)
                    {
                        if (dTable.TableStyleInfo.ShowFirstColumn != null)
                            xlTable.EmphasizeFirstColumn = dTable.TableStyleInfo.ShowFirstColumn.Value;
                        if (dTable.TableStyleInfo.ShowLastColumn != null)
                            xlTable.EmphasizeLastColumn = dTable.TableStyleInfo.ShowLastColumn.Value;
                        if (dTable.TableStyleInfo.ShowRowStripes != null)
                            xlTable.ShowRowStripes = dTable.TableStyleInfo.ShowRowStripes.Value;
                        if (dTable.TableStyleInfo.ShowColumnStripes != null)
                            xlTable.ShowColumnStripes = dTable.TableStyleInfo.ShowColumnStripes.Value;
                        if (dTable.TableStyleInfo.Name != null)
                        {
                            var theme = XLTableTheme.FromName(dTable.TableStyleInfo.Name.Value);
                            if (theme != null)
                                xlTable.Theme = theme;
                            else
                                xlTable.Theme = new XLTableTheme(dTable.TableStyleInfo.Name.Value);
                        }
                        else
                            xlTable.Theme = XLTableTheme.None;
                    }

                    if (dTable.AutoFilter != null)
                    {
                        xlTable.ShowAutoFilter = true;
                        LoadAutoFilterColumns(dTable.AutoFilter, xlTable.AutoFilter);
                    }
                    else
                        xlTable.ShowAutoFilter = false;

                    if (xlTable.ShowTotalsRow)
                    {
                        foreach (var tableColumn in dTable.TableColumns.Cast<TableColumn>())
                        {
                            var tableColumnName = GetTableColumnName(tableColumn.Name.Value);
                            if (tableColumn.TotalsRowFunction != null)
                                xlTable.Field(tableColumnName).TotalsRowFunction =
                                    tableColumn.TotalsRowFunction.Value.ToClosedXml();

                            if (tableColumn.TotalsRowFormula != null)
                                xlTable.Field(tableColumnName).TotalsRowFormulaA1 =
                                    tableColumn.TotalsRowFormula.Text;

                            if (tableColumn.TotalsRowLabel != null)
                                xlTable.Field(tableColumnName).TotalsRowLabel = tableColumn.TotalsRowLabel.Value;
                        }
                        if (xlTable.AutoFilter != null)
                            xlTable.AutoFilter.Range = xlTable.Worksheet.Range(
                                                    xlTable.RangeAddress.FirstAddress.RowNumber, xlTable.RangeAddress.FirstAddress.ColumnNumber,
                                                    xlTable.RangeAddress.LastAddress.RowNumber - 1, xlTable.RangeAddress.LastAddress.ColumnNumber);
                    }
                    else if (xlTable.AutoFilter != null)
                        xlTable.AutoFilter.Range = xlTable.Worksheet.Range(xlTable.RangeAddress);
                }

                #endregion LoadTables

                LoadDrawings(worksheetPart, ws);

                #region LoadComments

                if (worksheetPart.WorksheetCommentsPart != null)
                {
                    var root = worksheetPart.WorksheetCommentsPart.Comments;
                    var authors = root.GetFirstChild<Authors>().ChildElements.OfType<Author>().ToList();
                    var comments = root.GetFirstChild<CommentList>().ChildElements.OfType<Comment>().ToList();

                    // **** MAYBE FUTURE SHAPE SIZE SUPPORT
                    var shapes = GetCommentShapes(worksheetPart);

                    for (var i = 0; i < comments.Count; i++)
                    {
                        var c = comments[i];

                        XElement shape = null;
                        if (i < shapes.Count)
                            shape = shapes[i];

                        // find cell by reference
                        var cell = ws.Cell(c.Reference);

                        var shapeIdString = shape?.Attribute("id")?.Value;
                        if (shapeIdString?.StartsWith("_x0000_s") ?? false)
                            shapeIdString = shapeIdString.Substring(8);

                        int? shapeId = int.TryParse(shapeIdString, out int sid) ? (int?)sid : null;
                        var xlComment = cell.CreateComment(shapeId);

                        xlComment.Author = authors[(int)c.AuthorId.Value].InnerText;
                        ShapeIdManager.Add(xlComment.ShapeId);

                        var runs = c.GetFirstChild<CommentText>().Elements<Run>();
                        foreach (var run in runs)
                        {
                            var runProperties = run.RunProperties;
                            String text = run.Text.InnerText.FixNewLines();
                            var rt = xlComment.AddText(text);
                            LoadFont(runProperties, rt);
                        }

                        if (shape != null)
                        {
                            LoadShapeProperties(xlComment, shape);

                            var clientData = shape.Elements().First(e => e.Name.LocalName == "ClientData");
                            LoadClientData(xlComment, clientData);

                            var textBox = shape.Elements().First(e => e.Name.LocalName == "textbox");
                            LoadTextBox(xlComment, textBox);

                            var alt = shape.Attribute("alt");
                            if (alt != null) xlComment.Style.Web.SetAlternateText(alt.Value);

                            LoadColorsAndLines(xlComment, shape);

                            //var insetmode = (string)shape.Attributes().First(a=> a.Name.LocalName == "insetmode");
                            //xlComment.Style.Margins.Automatic = insetmode != null && insetmode.Equals("auto");
                        }
                    }
                }

                #endregion LoadComments
            }

            var workbook = dSpreadsheet.WorkbookPart.Workbook;

            var bookViews = workbook.BookViews;
            if (bookViews != null && bookViews.FirstOrDefault() is WorkbookView workbookView)
            {
                if (workbookView.ActiveTab == null || !workbookView.ActiveTab.HasValue)
                {
                    Worksheets.First().SetTabActive().Unhide();
                }
                else
                {
                    UnsupportedSheet unsupportedSheet =
                        UnsupportedSheets.FirstOrDefault(us => us.Position == (Int32)(workbookView.ActiveTab.Value + 1));
                    if (unsupportedSheet != null)
                        unsupportedSheet.IsActive = true;
                    else
                    {
                        Worksheet((Int32)(workbookView.ActiveTab.Value + 1)).SetTabActive();
                    }
                }
            }
            LoadDefinedNames(workbook);

            #region Pivot tables

            // Delay loading of pivot tables until all sheets have been loaded
            foreach (var dSheet in sheets.OfType<Sheet>())
            {
                var worksheetPart = dSpreadsheet.WorkbookPart.GetPartById(dSheet.Id) as WorksheetPart;

                if (worksheetPart != null)
                {
                    var ws = (XLWorksheet)WorksheetsInternal.Worksheet(dSheet.Name);

                    foreach (var pivotTablePart in worksheetPart.PivotTableParts)
                    {
                        var pivotTableCacheDefinitionPart = pivotTablePart.PivotTableCacheDefinitionPart;
                        var pivotTableDefinition = pivotTablePart.PivotTableDefinition;

                        var target = ws.FirstCell();
                        if (pivotTableDefinition?.Location?.Reference?.HasValue ?? false)
                        {
                            ws.Range(pivotTableDefinition.Location.Reference.Value).Clear(XLClearOptions.All);
                            target = ws.Range(pivotTableDefinition.Location.Reference.Value).FirstCell();
                        }

                        IXLRange source = null;
                        XLPivotTableSourceType sourceType = XLPivotTableSourceType.Range;
                        if (pivotTableCacheDefinitionPart?.PivotCacheDefinition?.CacheSource?.WorksheetSource != null)
                        {
                            // TODO: Implement other sources besides worksheetSource
                            // But for now assume names and references point directly to a range
                            var wss = pivotTableCacheDefinitionPart.PivotCacheDefinition.CacheSource.WorksheetSource;

                            if (!String.IsNullOrEmpty(wss.Id))
                            {
                                var externalRelationship = pivotTableCacheDefinitionPart.ExternalRelationships.FirstOrDefault(er => er.Id.Equals(wss.Id));
                                if (externalRelationship?.IsExternal ?? false)
                                {
                                    // We don't support external sources
                                    continue;
                                }
                            }

                            if (wss.Name != null)
                            {
                                var table = ws
                                    .Workbook
                                    .Worksheets
                                    .SelectMany(ws1 => ws1.Tables)
                                    .FirstOrDefault(t => t.Name.Equals(wss.Name.Value));

                                if (table != null)
                                {
                                    sourceType = XLPivotTableSourceType.Table;
                                    source = table;
                                }
                                else
                                {
                                    sourceType = XLPivotTableSourceType.Range;
                                    source = this.Range(wss.Name.Value);
                                }
                            }
                            else
                            {
                                sourceType = XLPivotTableSourceType.Range;

                                IXLWorksheet sourceSheet;
                                if (wss.Sheet == null)
                                    sourceSheet = ws;
                                else if (WorksheetsInternal.TryGetWorksheet(wss.Sheet.Value, out sourceSheet))
                                    source = this.Range(sourceSheet.Range(wss.Reference.Value).RangeAddress.ToStringRelative(includeSheet: true));
                            }

                            if (source == null)
                                continue;
                        }

                        if (target != null && source != null)
                        {
                            XLPivotTable pt;
                            switch (sourceType)
                            {
                                case XLPivotTableSourceType.Range:
                                    pt = ws.PivotTables.Add(pivotTableDefinition.Name, target, source) as XLPivotTable;
                                    break;

                                case XLPivotTableSourceType.Table:
                                    pt = ws.PivotTables.Add(pivotTableDefinition.Name, target, source as XLTable) as XLPivotTable;
                                    break;

                                default:
                                    throw new NotSupportedException($"Pivot table source type {sourceType} is not supported.");
                            }

                            if (!String.IsNullOrWhiteSpace(StringValue.ToString(pivotTableDefinition?.ColumnHeaderCaption ?? String.Empty)))
                                pt.SetColumnHeaderCaption(StringValue.ToString(pivotTableDefinition.ColumnHeaderCaption));

                            if (!String.IsNullOrWhiteSpace(StringValue.ToString(pivotTableDefinition?.RowHeaderCaption ?? String.Empty)))
                                pt.SetRowHeaderCaption(StringValue.ToString(pivotTableDefinition.RowHeaderCaption));

                            pt.RelId = worksheetPart.GetIdOfPart(pivotTablePart);
                            pt.CacheDefinitionRelId = pivotTablePart.GetIdOfPart(pivotTableCacheDefinitionPart);
                            pt.WorkbookCacheRelId = dSpreadsheet.WorkbookPart.GetIdOfPart(pivotTableCacheDefinitionPart);

                            if (pivotTableDefinition.MergeItem != null) pt.MergeAndCenterWithLabels = pivotTableDefinition.MergeItem.Value;
                            if (pivotTableDefinition.Indent != null) pt.RowLabelIndent = (int)pivotTableDefinition.Indent.Value;
                            if (pivotTableDefinition.PageOverThenDown != null) pt.FilterAreaOrder = pivotTableDefinition.PageOverThenDown.Value ? XLFilterAreaOrder.OverThenDown : XLFilterAreaOrder.DownThenOver;
                            if (pivotTableDefinition.PageWrap != null) pt.FilterFieldsPageWrap = (int)pivotTableDefinition.PageWrap.Value;
                            if (pivotTableDefinition.UseAutoFormatting != null) pt.AutofitColumns = pivotTableDefinition.UseAutoFormatting.Value;
                            if (pivotTableDefinition.PreserveFormatting != null) pt.PreserveCellFormatting = pivotTableDefinition.PreserveFormatting.Value;
                            if (pivotTableDefinition.RowGrandTotals != null) pt.ShowGrandTotalsRows = pivotTableDefinition.RowGrandTotals.Value;
                            if (pivotTableDefinition.ColumnGrandTotals != null) pt.ShowGrandTotalsColumns = pivotTableDefinition.ColumnGrandTotals.Value;
                            if (pivotTableDefinition.SubtotalHiddenItems != null) pt.FilteredItemsInSubtotals = pivotTableDefinition.SubtotalHiddenItems.Value;
                            if (pivotTableDefinition.MultipleFieldFilters != null) pt.AllowMultipleFilters = pivotTableDefinition.MultipleFieldFilters.Value;
                            if (pivotTableDefinition.CustomListSort != null) pt.UseCustomListsForSorting = pivotTableDefinition.CustomListSort.Value;
                            if (pivotTableDefinition.ShowDrill != null) pt.ShowExpandCollapseButtons = pivotTableDefinition.ShowDrill.Value;
                            if (pivotTableDefinition.ShowDataTips != null) pt.ShowContextualTooltips = pivotTableDefinition.ShowDataTips.Value;
                            if (pivotTableDefinition.ShowMemberPropertyTips != null) pt.ShowPropertiesInTooltips = pivotTableDefinition.ShowMemberPropertyTips.Value;
                            if (pivotTableDefinition.ShowHeaders != null) pt.DisplayCaptionsAndDropdowns = pivotTableDefinition.ShowHeaders.Value;
                            if (pivotTableDefinition.GridDropZones != null) pt.ClassicPivotTableLayout = pivotTableDefinition.GridDropZones.Value;
                            if (pivotTableDefinition.ShowEmptyRow != null) pt.ShowEmptyItemsOnRows = pivotTableDefinition.ShowEmptyRow.Value;
                            if (pivotTableDefinition.ShowEmptyColumn != null) pt.ShowEmptyItemsOnColumns = pivotTableDefinition.ShowEmptyColumn.Value;
                            if (pivotTableDefinition.ShowItems != null) pt.DisplayItemLabels = pivotTableDefinition.ShowItems.Value;
                            if (pivotTableDefinition.FieldListSortAscending != null) pt.SortFieldsAtoZ = pivotTableDefinition.FieldListSortAscending.Value;
                            if (pivotTableDefinition.PrintDrill != null) pt.PrintExpandCollapsedButtons = pivotTableDefinition.PrintDrill.Value;
                            if (pivotTableDefinition.ItemPrintTitles != null) pt.RepeatRowLabels = pivotTableDefinition.ItemPrintTitles.Value;
                            if (pivotTableDefinition.FieldPrintTitles != null) pt.PrintTitles = pivotTableDefinition.FieldPrintTitles.Value;
                            if (pivotTableDefinition.EnableDrill != null) pt.EnableShowDetails = pivotTableDefinition.EnableDrill.Value;
                            if (pivotTableCacheDefinitionPart.PivotCacheDefinition.SaveData != null) pt.SaveSourceData = pivotTableCacheDefinitionPart.PivotCacheDefinition.SaveData.Value;

                            if (pivotTableCacheDefinitionPart.PivotCacheDefinition.MissingItemsLimit != null)
                            {
                                if (pivotTableCacheDefinitionPart.PivotCacheDefinition.MissingItemsLimit == 0U)
                                    pt.ItemsToRetainPerField = XLItemsToRetain.None;
                                else if (pivotTableCacheDefinitionPart.PivotCacheDefinition.MissingItemsLimit == XLHelper.MaxRowNumber)
                                    pt.ItemsToRetainPerField = XLItemsToRetain.Max;
                            }

                            if (pivotTableDefinition.ShowMissing != null && pivotTableDefinition.MissingCaption != null)
                                pt.EmptyCellReplacement = pivotTableDefinition.MissingCaption.Value;

                            if (pivotTableDefinition.ShowError != null && pivotTableDefinition.ErrorCaption != null)
                                pt.ErrorValueReplacement = pivotTableDefinition.ErrorCaption.Value;

                            var pivotTableDefinitionExtensionList = pivotTableDefinition.GetFirstChild<PivotTableDefinitionExtensionList>();
                            var pivotTableDefinitionExtension = pivotTableDefinitionExtensionList?.GetFirstChild<PivotTableDefinitionExtension>();
                            var pivotTableDefinition2 = pivotTableDefinitionExtension?.GetFirstChild<DocumentFormat.OpenXml.Office2010.Excel.PivotTableDefinition>();
                            if (pivotTableDefinition2 != null)
                            {
                                if (pivotTableDefinition2.EnableEdit != null) pt.EnableCellEditing = pivotTableDefinition2.EnableEdit.Value;
                                if (pivotTableDefinition2.HideValuesRow != null) pt.ShowValuesRow = !pivotTableDefinition2.HideValuesRow.Value;
                            }

                            var pivotTableStyle = pivotTableDefinition.GetFirstChild<PivotTableStyle>();
                            if (pivotTableStyle != null)
                            {
                                if (pivotTableStyle.Name != null)
                                    pt.Theme = (XLPivotTableTheme)Enum.Parse(typeof(XLPivotTableTheme), pivotTableStyle.Name);
                                else
                                    pt.Theme = XLPivotTableTheme.None;

                                pt.ShowRowHeaders = OpenXmlHelper.GetBooleanValueAsBool(pivotTableStyle.ShowRowHeaders, false);
                                pt.ShowColumnHeaders = OpenXmlHelper.GetBooleanValueAsBool(pivotTableStyle.ShowColumnHeaders, false);
                                pt.ShowRowStripes = OpenXmlHelper.GetBooleanValueAsBool(pivotTableStyle.ShowRowStripes, false);
                                pt.ShowColumnStripes = OpenXmlHelper.GetBooleanValueAsBool(pivotTableStyle.ShowColumnStripes, false);
                            }

                            // Subtotal configuration
                            if (pivotTableDefinition.PivotFields.Cast<PivotField>().All(pf => (pf.DefaultSubtotal == null || pf.DefaultSubtotal.Value)
                                                                                              && (pf.SubtotalTop == null || pf.SubtotalTop == true)))
                                pt.SetSubtotals(XLPivotSubtotals.AtTop);
                            else if (pivotTableDefinition.PivotFields.Cast<PivotField>().All(pf => (pf.DefaultSubtotal == null || pf.DefaultSubtotal.Value)
                                                                                                   && (pf.SubtotalTop != null && pf.SubtotalTop.Value == false)))
                                pt.SetSubtotals(XLPivotSubtotals.AtBottom);
                            else
                                pt.SetSubtotals(XLPivotSubtotals.DoNotShow);

                            // Row labels
                            if (pivotTableDefinition.RowFields != null)
                            {
                                foreach (var rf in pivotTableDefinition.RowFields.Cast<Field>())
                                {
                                    if (rf.Index < pivotTableDefinition.PivotFields.Count)
                                    {
                                        IXLPivotField pivotField = null;
                                        if (rf.Index.Value == -2)
                                            pivotField = pt.RowLabels.Add(XLConstants.PivotTable.ValuesSentinalLabel);
                                        else
                                        {
                                            var pf = pivotTableDefinition.PivotFields.ElementAt(rf.Index.Value) as PivotField;
                                            if (pf == null)
                                                continue;

                                            var cacheField = pivotTableCacheDefinitionPart.PivotCacheDefinition.CacheFields.ElementAt(rf.Index.Value) as CacheField;
                                            if (pt.SourceRangeFieldsAvailable.Contains(cacheField.Name?.Value))
                                                pivotField = pf.Name != null
                                                    ? pt.RowLabels.Add(cacheField.Name, pf.Name.Value)
                                                    : pt.RowLabels.Add(cacheField.Name.Value);
                                            else
                                                continue;

                                            if (pivotField != null)
                                            {
                                                LoadFieldOptions(pf, pivotField);
                                                LoadSubtotals(pf, pivotField);

                                                if (pf.SortType != null)
                                                {
                                                    pivotField.SetSort((XLPivotSortType)pf.SortType.Value);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // Column labels
                            if (pivotTableDefinition.ColumnFields != null)
                            {
                                foreach (var cf in pivotTableDefinition.ColumnFields.Cast<Field>())
                                {
                                    IXLPivotField pivotField = null;
                                    if (cf.Index.Value == -2)
                                        pivotField = pt.ColumnLabels.Add(XLConstants.PivotTable.ValuesSentinalLabel);
                                    else if (cf.Index < pivotTableDefinition.PivotFields.Count)
                                    {
                                        var pf = pivotTableDefinition.PivotFields.ElementAt(cf.Index.Value) as PivotField;
                                        if (pf == null)
                                            continue;

                                        var cacheField = pivotTableCacheDefinitionPart.PivotCacheDefinition.CacheFields.ElementAt(cf.Index.Value) as CacheField;
                                        if (pt.SourceRangeFieldsAvailable.Contains(cacheField.Name?.Value))
                                            pivotField = pf.Name != null
                                                ? pt.ColumnLabels.Add(cacheField.Name, pf.Name.Value)
                                                : pt.ColumnLabels.Add(cacheField.Name.Value);
                                        else
                                            continue;

                                        if (pivotField != null)
                                        {
                                            LoadFieldOptions(pf, pivotField);
                                            LoadSubtotals(pf, pivotField);

                                            if (pf.SortType != null)
                                            {
                                                pivotField.SetSort((XLPivotSortType)pf.SortType.Value);
                                            }
                                        }
                                    }
                                }
                            }

                            // Values
                            if (pivotTableDefinition.DataFields != null)
                            {
                                foreach (var df in pivotTableDefinition.DataFields.Cast<DataField>())
                                {
                                    IXLPivotValue pivotValue = null;
                                    if ((int)df.Field.Value == -2)
                                        pivotValue = pt.Values.Add(XLConstants.PivotTable.ValuesSentinalLabel);
                                    else if (df.Field.Value < pivotTableDefinition.PivotFields.Count)
                                    {
                                        var pf = pivotTableDefinition.PivotFields.ElementAt((int)df.Field.Value) as PivotField;
                                        if (pf == null)
                                            continue;

                                        var cacheField = pivotTableCacheDefinitionPart.PivotCacheDefinition.CacheFields.ElementAt((int)df.Field.Value) as CacheField;

                                        if (pf.Name != null)
                                            pivotValue = pt.Values.Add(pf.Name.Value, df.Name.Value);
                                        else if (cacheField.Name != null && pt.SourceRangeFieldsAvailable.Contains<String>(cacheField.Name))
                                            pivotValue = pt.Values.Add(cacheField.Name.Value, df.Name.Value);
                                        else
                                            continue;

                                        if (df.NumberFormatId != null) pivotValue.NumberFormat.SetNumberFormatId((int)df.NumberFormatId.Value);
                                        if (df.Subtotal != null) pivotValue = pivotValue.SetSummaryFormula(df.Subtotal.Value.ToClosedXml());
                                        if (df.ShowDataAs != null)
                                        {
                                            var calculation = df.ShowDataAs.Value.ToClosedXml();
                                            pivotValue = pivotValue.SetCalculation(calculation);
                                        }

                                        if (df.BaseField?.Value != null)
                                        {
                                            var col = pt.SourceRange.Column(df.BaseField.Value + 1);

                                            var items = col.CellsUsed()
                                                        .Select(c => c.Value)
                                                        .Skip(1) // Skip header column
                                                        .Distinct().ToList();

                                            pivotValue.BaseField = col.FirstCell().GetValue<string>();

                                            if (df.BaseItem?.Value != null)
                                            {
                                                var bi = (int)df.BaseItem.Value;
                                                if (bi.Between(0, items.Count - 1))
                                                    pivotValue.BaseItem = items[(int)df.BaseItem.Value];
                                            }
                                        }
                                    }
                                }
                            }

                            // Filters
                            if (pivotTableDefinition.PageFields != null)
                            {
                                foreach (var pageField in pivotTableDefinition.PageFields.Cast<PageField>())
                                {
                                    var pf = pivotTableDefinition.PivotFields.ElementAt(pageField.Field.Value) as PivotField;
                                    if (pf == null)
                                        continue;

                                    var cacheField = pivotTableCacheDefinitionPart.PivotCacheDefinition.CacheFields.ElementAt(pageField.Field.Value) as CacheField;

                                    if (!pt.SourceRangeFieldsAvailable.Contains(cacheField.Name?.Value))
                                        continue;

                                    var filterName = pf.Name?.Value ?? cacheField.Name?.Value;

                                    IXLPivotField rf;
                                    if (pageField.Name?.Value != null)
                                        rf = pt.ReportFilters.Add(filterName, pageField.Name.Value);
                                    else
                                        rf = pt.ReportFilters.Add(filterName);

                                    var openXmlItems = new List<Item>();
                                    if ((pageField.Item?.HasValue ?? false)
                                        && pf.Items.Any() && cacheField.SharedItems.Any())
                                    {
                                        if (!(pf.Items.ElementAt(Convert.ToInt32(pageField.Item.Value)) is Item item))
                                            continue;

                                        openXmlItems.Add(item);
                                    }
                                    else if (OpenXmlHelper.GetBooleanValueAsBool(pf.MultipleItemSelectionAllowed, false))
                                    {
                                        openXmlItems.AddRange(pf.Items.Cast<Item>());
                                    }

                                    foreach (var item in openXmlItems)
                                    {
                                        if (!OpenXmlHelper.GetBooleanValueAsBool(item.Hidden, false)
                                            && (item.Index?.HasValue ?? false))
                                        {
                                            var sharedItem = cacheField.SharedItems.ElementAt(Convert.ToInt32((uint)item.Index));
                                            // https://msdn.microsoft.com/en-us/library/documentformat.openxml.spreadsheet.shareditems.aspx
                                            switch (sharedItem)
                                            {
                                                case NumberItem numberItem:
                                                    rf.AddSelectedValue(Convert.ToDouble(numberItem.Val.Value));
                                                    break;

                                                case DateTimeItem dateTimeItem:
                                                    rf.AddSelectedValue(Convert.ToDateTime(dateTimeItem.Val.Value));
                                                    break;

                                                case BooleanItem booleanItem:
                                                    rf.AddSelectedValue(Convert.ToBoolean(booleanItem.Val.Value));
                                                    break;

                                                case StringItem stringItem:
                                                    rf.AddSelectedValue(stringItem.Val.Value);
                                                    break;

                                                case MissingItem missingItem:
                                                case ErrorItem errorItem:
                                                    // Ignore missing and error items
                                                    break;

                                                default:
                                                    throw new NotImplementedException();
                                            }
                                        }
                                    }
                                }

                                pt.TargetCell = pt.TargetCell.CellAbove(pt.ReportFilters.Count() + 1);
                            }

                            LoadPivotStyleFormats(pt, pivotTableDefinition, pivotTableCacheDefinitionPart.PivotCacheDefinition, differentialFormats);
                        }
                    }
                }
            }

            #endregion Pivot tables
        }

        /// <summary>
        /// Calculate expected column width as a number displayed in the column in Excel from
        /// number of characters that should fit into the width and a font.
        /// </summary>
        internal static double CalculateColumnWidth(double charWidth, IXLFont font, XLWorkbook workbook)
        {
            // Convert width as a number of characters and translate it into a given number of pixels.
            int mdw = workbook.GraphicEngine.GetMaxDigitWidth(font, workbook.DpiX).RoundToInt();
            int defaultColWidthPx = XLHelper.NoCToPixels(charWidth, mdw).RoundToInt();

            // Excel then rounds this number up to the nearest multiple of 8 pixels, so that
            // scrolling across columns and rows is faster.
            int roundUpToMultiple = defaultColWidthPx + (8 - defaultColWidthPx % 8);

            // and last convert the width in pixels to width displayed in Excel. Shouldn't round the number, because
            // it causes inconsistency with conversion to other units, but other places in ClosedXML do = keep for now.
            double defaultColumnWidth = XLHelper.PixelToNoC(roundUpToMultiple, mdw).Round(2);
            return defaultColumnWidth;
        }

        private void LoadPivotStyleFormats(XLPivotTable pt, PivotTableDefinition ptd, PivotCacheDefinition pcd, Dictionary<Int32, DifferentialFormat> differentialFormats)
        {
            if (ptd.Formats == null)
                return;

            foreach (var format in ptd.Formats.OfType<Format>())
            {
                var pivotArea = format.PivotArea;
                if (pivotArea == null)
                    continue;

                var type = pivotArea.Type ?? PivotAreaValues.Normal;
                var dataOnly = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.DataOnly, true);
                var labelOnly = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.LabelOnly, false);

                if (dataOnly && labelOnly)
                    throw new InvalidOperationException("Cannot have dataOnly and labelOnly both set to true.");

                XLPivotStyleFormat styleFormat;

                if (pivotArea.Field == null && !(pivotArea.PivotAreaReferences?.OfType<PivotAreaReference>()?.Any() ?? false))
                {
                    // If the pivot field is null and doesn't have children (references), we assume this format is a grand total
                    // Example:
                    // <x:pivotArea type="normal" dataOnly="0" grandRow="1" axis="axisRow" fieldPosition="0" />

                    var appliesTo = XLPivotStyleFormatElement.All;
                    if (dataOnly)
                        appliesTo = XLPivotStyleFormatElement.Data;
                    else if (labelOnly)
                        appliesTo = XLPivotStyleFormatElement.Label;

                    var isRow = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.GrandRow, false);
                    var isColumn = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.GrandColumn, false);

                    // Either of the two should be true, else this is an unsupported format
                    if (!isRow && !isColumn)
                        continue;
                    //throw new NotImplementedException();

                    if (isRow)
                        styleFormat = pt.StyleFormats.RowGrandTotalFormats.ForElement(appliesTo) as XLPivotStyleFormat;
                    else
                        styleFormat = pt.StyleFormats.ColumnGrandTotalFormats.ForElement(appliesTo) as XLPivotStyleFormat;
                }
                else
                {
                    Int32 fieldIndex;
                    Boolean defaultSubtotal = false;

                    if (pivotArea.Field != null)
                        fieldIndex = (Int32)pivotArea.Field;
                    else if (pivotArea.PivotAreaReferences?.OfType<PivotAreaReference>()?.Any() ?? false)
                    {
                        // The field we want does NOT have any <x v="..."/>  children
                        var r = pivotArea.PivotAreaReferences.OfType<PivotAreaReference>().FirstOrDefault(r1 => !r1.Any());
                        if (r == null)
                            continue;

                        fieldIndex = Convert.ToInt32((UInt32)r.Field);
                        defaultSubtotal = OpenXmlHelper.GetBooleanValueAsBool(r.DefaultSubtotal, false);
                    }
                    else
                        throw new NotImplementedException();

                    XLPivotField field = null;
                    if (fieldIndex == -2)
                    {
                        var axis = pivotArea.Axis.Value;
                        if (axis == PivotTableAxisValues.AxisRow)
                            field = (XLPivotField)pt.RowLabels.Single(f => f.SourceName == "{{Values}}");
                        else if (axis == PivotTableAxisValues.AxisColumn)
                            field = (XLPivotField)pt.ColumnLabels.Single(f => f.SourceName == "{{Values}}");
                        else
                            continue;
                    }
                    else
                    {
                        var fieldName = pt.SourceRangeFieldsAvailable.ElementAt(fieldIndex);
                        field = (XLPivotField)pt.ImplementedFields.SingleOrDefault(f => f.SourceName.Equals(fieldName));

                        if (field is null)
                            continue;
                    }

                    if (defaultSubtotal)
                    {
                        // Subtotal format
                        // Example:
                        // <x:pivotArea type="normal" fieldPosition="0">
                        //     <x:references count="1">
                        //         <x:reference field="0" defaultSubtotal="1" />
                        //     </x:references>
                        // </x:pivotArea>

                        styleFormat = field.StyleFormats.Subtotal as XLPivotStyleFormat;
                    }
                    else if (type == PivotAreaValues.Button)
                    {
                        // Header format
                        // Example:
                        // <x:pivotArea field="4" type="button" outline="0" axis="axisCol" fieldPosition="0" />
                        styleFormat = field.StyleFormats.Header as XLPivotStyleFormat;
                    }
                    else if (labelOnly)
                    {
                        // Label format
                        // Example:
                        // <x:pivotArea type="normal" dataOnly="0" labelOnly="1" fieldPosition="0">
                        //   <x:references count="1">
                        //     <x:reference field="4" />
                        //   </x:references>
                        // </x:pivotArea>
                        styleFormat = field.StyleFormats.Label as XLPivotStyleFormat;
                    }
                    else
                    {
                        // Assume DataValues format
                        // Example:
                        // <x:pivotArea type="normal" fieldPosition="0">
                        //     <x:references count="3">
                        //         <x:reference field="0" />
                        //         <x:reference field="4">
                        //             <x:x v="1" />
                        //         </x:reference>
                        //         <x:reference field="4294967294">
                        //             <x:x v="0" />
                        //         </x:reference>
                        //     </x:references>
                        //</x:pivotArea>
                        styleFormat = field.StyleFormats.DataValuesFormat as XLPivotStyleFormat;

                        foreach (var reference in pivotArea.PivotAreaReferences.OfType<PivotAreaReference>())
                        {
                            fieldIndex = unchecked((int)reference.Field.Value);
                            if (field.Offset == fieldIndex)
                                continue; // already handled

                            var fieldItem = reference.OfType<FieldItem>().First();
                            var fieldItemValue = (int)fieldItem.Val.Value;

                            if (fieldIndex == -2)
                            {
                                styleFormat = (styleFormat as XLPivotValueStyleFormat)
                                    .ForValueField(pt.Values.ElementAt(fieldItemValue))
                                    as XLPivotValueStyleFormat;
                            }
                            else
                            {
                                var additionalFieldName = pt.SourceRangeFieldsAvailable.ElementAt(fieldIndex);
                                var additionalField = pt.ImplementedFields
                                    .Single(f => f.SourceName == additionalFieldName);

                                var cacheField = pcd.CacheFields.OfType<CacheField>()
                                    .FirstOrDefault(cf => cf.Name == additionalFieldName);

                                Predicate<XLCellValue> predicate = null;
                                if ((cacheField?.SharedItems?.Any() ?? false)
                                    && fieldItemValue < cacheField.SharedItems.Count)
                                {
                                    // Shared items can only be strings, not other types
                                    var value = cacheField.SharedItems.OfType<StringItem>().ElementAt(fieldItemValue).Val?.Value;
                                    predicate = o => o.Type == XLDataType.Text && o.GetText() == value;
                                }

                                styleFormat = (styleFormat as XLPivotValueStyleFormat)
                                    .AndWith(additionalField, predicate)
                                    as XLPivotValueStyleFormat;
                            }
                        }
                    }

                    styleFormat.AreaType = type.Value.ToClosedXml();
                    styleFormat.Outline = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.Outline, true);
                    styleFormat.CollapsedLevelsAreSubtotals = OpenXmlHelper.GetBooleanValueAsBool(pivotArea.CollapsedLevelsAreSubtotals, false);
                }

                IXLStyle style = XLStyle.Default;
                if (format.FormatId != null)
                {
                    var df = differentialFormats[(Int32)format.FormatId.Value];
                    LoadFont(df.Font, style.Font);
                    LoadFill(df.Fill, style.Fill, differentialFillFormat: true);
                    LoadBorder(df.Border, style.Border);
                    LoadNumberFormat(df.NumberingFormat, style.NumberFormat);
                }

                styleFormat.Style = style;
            }
        }

        private static void LoadFieldOptions(PivotField pf, IXLPivotField pivotField)
        {
            if (pf.SubtotalCaption != null) pivotField.SubtotalCaption = pf.SubtotalCaption;
            if (pf.IncludeNewItemsInFilter != null) pivotField.IncludeNewItemsInFilter = pf.IncludeNewItemsInFilter.Value;
            if (pf.Outline != null) pivotField.Outline = pf.Outline.Value;
            if (pf.Compact != null) pivotField.Compact = pf.Compact.Value;
            if (pf.InsertBlankRow != null) pivotField.InsertBlankLines = pf.InsertBlankRow.Value;
            pivotField.ShowBlankItems = OpenXmlHelper.GetBooleanValueAsBool(pf.ShowAll, true);
            if (pf.InsertPageBreak != null) pivotField.InsertPageBreaks = pf.InsertPageBreak.Value;
            if (pf.SubtotalTop != null) pivotField.SubtotalsAtTop = pf.SubtotalTop.Value;
            if (pf.AllDrilled != null) pivotField.Collapsed = !pf.AllDrilled.Value;

            var pivotFieldExtensionList = pf.GetFirstChild<PivotFieldExtensionList>();
            var pivotFieldExtension = pivotFieldExtensionList?.GetFirstChild<PivotFieldExtension>();
            var field2010 = pivotFieldExtension?.GetFirstChild<DocumentFormat.OpenXml.Office2010.Excel.PivotField>();
            if (field2010?.FillDownLabels != null) pivotField.RepeatItemLabels = field2010.FillDownLabels.Value;
        }

        private static void LoadSubtotals(PivotField pf, IXLPivotField pivotField)
        {
            if (pf.AverageSubTotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Average);
            if (pf.CountASubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Count);
            if (pf.CountSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.CountNumbers);
            if (pf.MaxSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Maximum);
            if (pf.MinSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Minimum);
            if (pf.ApplyStandardDeviationPInSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.PopulationStandardDeviation);
            if (pf.ApplyVariancePInSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.PopulationVariance);
            if (pf.ApplyProductInSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Product);
            if (pf.ApplyStandardDeviationInSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.StandardDeviation);
            if (pf.SumSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Sum);
            if (pf.ApplyVarianceInSubtotal != null)
                pivotField.AddSubtotal(XLSubtotalFunction.Variance);

            if (pf.Items?.Any() ?? false)
            {
                var items = pf.Items.OfType<Item>().Where(i => i.Index != null && i.Index.HasValue);
                if (!items.Any(i => i.HideDetails == null || BooleanValue.ToBoolean(i.HideDetails)))
                    pivotField.SetCollapsed();
            }
        }

        private void LoadDrawings(WorksheetPart wsPart, XLWorksheet ws)
        {
            if (wsPart.DrawingsPart != null)
            {
                var drawingsPart = wsPart.DrawingsPart;

                foreach (var anchor in drawingsPart.WorksheetDrawing.ChildElements)
                {
                    var imgId = GetImageRelIdFromAnchor(anchor);

                    //If imgId is null, we're probably dealing with a TextBox (or another shape) instead of a picture
                    if (imgId == null) continue;

                    var imagePart = drawingsPart.GetPartById(imgId);
                    using (var stream = imagePart.GetStream())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var vsdp = GetPropertiesFromAnchor(anchor);

                        var picture = ws.AddPicture(ms, vsdp.Name, Convert.ToInt32(vsdp.Id.Value)) as XLPicture;
                        picture.RelId = imgId;

                        Xdr.ShapeProperties spPr = anchor.Descendants<Xdr.ShapeProperties>().First();
                        picture.Placement = XLPicturePlacement.FreeFloating;

                        if (spPr?.Transform2D?.Extents?.Cx.HasValue ?? false)
                            picture.Width = ConvertFromEnglishMetricUnits(spPr.Transform2D.Extents.Cx, ws.Workbook.DpiX);

                        if (spPr?.Transform2D?.Extents?.Cy.HasValue ?? false)
                            picture.Height = ConvertFromEnglishMetricUnits(spPr.Transform2D.Extents.Cy, ws.Workbook.DpiY);

                        if (anchor is Xdr.AbsoluteAnchor)
                        {
                            var absoluteAnchor = anchor as Xdr.AbsoluteAnchor;
                            picture.MoveTo(
                                ConvertFromEnglishMetricUnits(absoluteAnchor.Position.X.Value, ws.Workbook.DpiX),
                                ConvertFromEnglishMetricUnits(absoluteAnchor.Position.Y.Value, ws.Workbook.DpiY)
                            );
                        }
                        else if (anchor is Xdr.OneCellAnchor)
                        {
                            var oneCellAnchor = anchor as Xdr.OneCellAnchor;
                            var from = LoadMarker(ws, oneCellAnchor.FromMarker);
                            picture.MoveTo(from.Cell, from.Offset);
                        }
                        else if (anchor is Xdr.TwoCellAnchor)
                        {
                            var twoCellAnchor = anchor as Xdr.TwoCellAnchor;
                            var from = LoadMarker(ws, twoCellAnchor.FromMarker);
                            var to = LoadMarker(ws, twoCellAnchor.ToMarker);

                            if (twoCellAnchor.EditAs == null || !twoCellAnchor.EditAs.HasValue || twoCellAnchor.EditAs.Value == Xdr.EditAsValues.TwoCell)
                            {
                                picture.MoveTo(from.Cell, from.Offset, to.Cell, to.Offset);
                            }
                            else if (twoCellAnchor.EditAs.Value == Xdr.EditAsValues.Absolute)
                            {
                                var shapeProperties = twoCellAnchor.Descendants<Xdr.ShapeProperties>().FirstOrDefault();
                                if (shapeProperties != null)
                                {
                                    picture.MoveTo(
                                        ConvertFromEnglishMetricUnits(spPr.Transform2D.Offset.X, ws.Workbook.DpiX),
                                        ConvertFromEnglishMetricUnits(spPr.Transform2D.Offset.Y, ws.Workbook.DpiY)
                                    );
                                }
                            }
                            else if (twoCellAnchor.EditAs.Value == Xdr.EditAsValues.OneCell)
                            {
                                picture.MoveTo(from.Cell, from.Offset);
                            }
                        }
                    }
                }
            }
        }

        private static Int32 ConvertFromEnglishMetricUnits(long emu, double resolution)
        {
            return Convert.ToInt32(emu * resolution / 914400);
        }

        private static XLMarker LoadMarker(XLWorksheet ws, Xdr.MarkerType marker)
        {
            var row = Math.Min(XLHelper.MaxRowNumber, Math.Max(1, Convert.ToInt32(marker.RowId.InnerText) + 1));
            var column = Math.Min(XLHelper.MaxColumnNumber, Math.Max(1, Convert.ToInt32(marker.ColumnId.InnerText) + 1));
            return new XLMarker(
                ws.Cell(row, column),
                new Point(
                    ConvertFromEnglishMetricUnits(Convert.ToInt32(marker.ColumnOffset.InnerText), ws.Workbook.DpiX),
                    ConvertFromEnglishMetricUnits(Convert.ToInt32(marker.RowOffset.InnerText), ws.Workbook.DpiY)
                )
            );
        }

        #region Comment Helpers

        private static IList<XElement> GetCommentShapes(WorksheetPart worksheetPart)
        {
            // Cannot get this to return Vml.Shape elements
            foreach (var vmlPart in worksheetPart.VmlDrawingParts)
            {
                using (var stream = vmlPart.GetStream(FileMode.Open))
                {
                    var xdoc = XDocumentExtensions.Load(stream);
                    if (xdoc == null)
                        continue;

                    var root = xdoc.Root.Element("xml") ?? xdoc.Root;

                    if (root == null)
                        continue;

                    var shapes = root
                        .Elements(XName.Get("shape", "urn:schemas-microsoft-com:vml"))
                        .Where(e => new[]
                        {
                            "#" + XLConstants.Comment.ShapeTypeId ,
                            "#" + XLConstants.Comment.AlternateShapeTypeId
                        }.Contains(e.Attribute("type")?.Value))
                        .ToList();

                    if (shapes != null)
                        return shapes;
                }
            }

            throw new ArgumentException("Could not load comments file");
        }

        #endregion Comment Helpers

        private String GetTableColumnName(string name)
        {
            return name.Replace("_x000a_", Environment.NewLine).Replace("_x005f_x000a_", "_x000a_");
        }

        private void LoadColorsAndLines<T>(IXLDrawing<T> drawing, XElement shape)
        {
            var strokeColor = shape.Attribute("strokecolor");
            if (strokeColor != null) drawing.Style.ColorsAndLines.LineColor = XLColor.FromVmlColor(strokeColor.Value);

            var strokeWeight = shape.Attribute("strokeweight");
            if (strokeWeight != null && TryGetPtValue(strokeWeight.Value, out var lineWeight))
                drawing.Style.ColorsAndLines.LineWeight = lineWeight;

            var fillColor = shape.Attribute("fillcolor");
            if (fillColor != null) drawing.Style.ColorsAndLines.FillColor = XLColor.FromVmlColor(fillColor.Value);

            var fill = shape.Elements().FirstOrDefault(e => e.Name.LocalName == "fill");
            if (fill != null)
            {
                var opacity = fill.Attribute("opacity");
                if (opacity != null)
                {
                    String opacityVal = opacity.Value;
                    if (opacityVal.EndsWith("f"))
                        drawing.Style.ColorsAndLines.FillTransparency =
                            Double.Parse(opacityVal.Substring(0, opacityVal.Length - 1), CultureInfo.InvariantCulture) / 65536.0;
                    else
                        drawing.Style.ColorsAndLines.FillTransparency = Double.Parse(opacityVal, CultureInfo.InvariantCulture);
                }
            }

            var stroke = shape.Elements().FirstOrDefault(e => e.Name.LocalName == "stroke");
            if (stroke != null)
            {
                var opacity = stroke.Attribute("opacity");
                if (opacity != null)
                {
                    String opacityVal = opacity.Value;
                    if (opacityVal.EndsWith("f"))
                        drawing.Style.ColorsAndLines.LineTransparency =
                            Double.Parse(opacityVal.Substring(0, opacityVal.Length - 1), CultureInfo.InvariantCulture) / 65536.0;
                    else
                        drawing.Style.ColorsAndLines.LineTransparency = Double.Parse(opacityVal, CultureInfo.InvariantCulture);
                }

                var dashStyle = stroke.Attribute("dashstyle");
                if (dashStyle != null)
                {
                    String dashStyleVal = dashStyle.Value.ToLower();
                    if (dashStyleVal == "1 1" || dashStyleVal == "shortdot")
                    {
                        var endCap = stroke.Attribute("endcap");
                        if (endCap != null && endCap.Value == "round")
                            drawing.Style.ColorsAndLines.LineDash = XLDashStyle.RoundDot;
                        else
                            drawing.Style.ColorsAndLines.LineDash = XLDashStyle.SquareDot;
                    }
                    else
                    {
                        switch (dashStyleVal)
                        {
                            case "dash": drawing.Style.ColorsAndLines.LineDash = XLDashStyle.Dash; break;
                            case "dashdot": drawing.Style.ColorsAndLines.LineDash = XLDashStyle.DashDot; break;
                            case "longdash": drawing.Style.ColorsAndLines.LineDash = XLDashStyle.LongDash; break;
                            case "longdashdot": drawing.Style.ColorsAndLines.LineDash = XLDashStyle.LongDashDot; break;
                            case "longdashdotdot": drawing.Style.ColorsAndLines.LineDash = XLDashStyle.LongDashDotDot; break;
                        }
                    }
                }

                var lineStyle = stroke.Attribute("linestyle");
                if (lineStyle != null)
                {
                    String lineStyleVal = lineStyle.Value.ToLower();
                    switch (lineStyleVal)
                    {
                        case "single": drawing.Style.ColorsAndLines.LineStyle = XLLineStyle.Single; break;
                        case "thickbetweenthin": drawing.Style.ColorsAndLines.LineStyle = XLLineStyle.ThickBetweenThin; break;
                        case "thickthin": drawing.Style.ColorsAndLines.LineStyle = XLLineStyle.ThickThin; break;
                        case "thinthick": drawing.Style.ColorsAndLines.LineStyle = XLLineStyle.ThinThick; break;
                        case "thinthin": drawing.Style.ColorsAndLines.LineStyle = XLLineStyle.ThinThin; break;
                    }
                }
            }
        }

        private void LoadTextBox<T>(IXLDrawing<T> xlDrawing, XElement textBox)
        {
            var attStyle = textBox.Attribute("style");
            if (attStyle != null) LoadTextBoxStyle<T>(xlDrawing, attStyle);

            var attInset = textBox.Attribute("inset");
            if (attInset != null) LoadTextBoxInset<T>(xlDrawing, attInset);
        }

        private void LoadTextBoxInset<T>(IXLDrawing<T> xlDrawing, XAttribute attInset)
        {
            var split = attInset.Value.Split(',');
            xlDrawing.Style.Margins.Left = GetInsetValue(split[0]);
            xlDrawing.Style.Margins.Top = GetInsetValue(split[1]);
            xlDrawing.Style.Margins.Right = GetInsetValue(split[2]);
            xlDrawing.Style.Margins.Bottom = GetInsetValue(split[3]);
        }

        private double GetInsetValue(string value)
        {
            String v = value.Trim();
            if (v.EndsWith("pt"))
                return Double.Parse(v.Substring(0, v.Length - 2), CultureInfo.InvariantCulture) / 72.0;
            else
                return Double.Parse(v.Substring(0, v.Length - 2), CultureInfo.InvariantCulture);
        }

        private static void LoadTextBoxStyle<T>(IXLDrawing<T> xlDrawing, XAttribute attStyle)
        {
            var style = attStyle.Value;
            var attributes = style.Split(';');
            foreach (String pair in attributes)
            {
                var split = pair.Split(':');
                if (split.Length != 2) continue;

                var attribute = split[0].Trim().ToLower();
                var value = split[1].Trim();
                Boolean isVertical = false;
                switch (attribute)
                {
                    case "mso-fit-shape-to-text": xlDrawing.Style.Size.SetAutomaticSize(value.Equals("t")); break;
                    case "mso-layout-flow-alt":
                        if (value.Equals("bottom-to-top")) xlDrawing.Style.Alignment.SetOrientation(XLDrawingTextOrientation.BottomToTop);
                        else if (value.Equals("top-to-bottom")) xlDrawing.Style.Alignment.SetOrientation(XLDrawingTextOrientation.Vertical);
                        break;

                    case "layout-flow": isVertical = value.Equals("vertical"); break;
                    case "mso-direction-alt": if (value == "auto") xlDrawing.Style.Alignment.Direction = XLDrawingTextDirection.Context; break;
                    case "direction": if (value == "RTL") xlDrawing.Style.Alignment.Direction = XLDrawingTextDirection.RightToLeft; break;
                }
                if (isVertical && xlDrawing.Style.Alignment.Orientation == XLDrawingTextOrientation.LeftToRight)
                    xlDrawing.Style.Alignment.Orientation = XLDrawingTextOrientation.TopToBottom;
            }
        }

        private void LoadClientData<T>(IXLDrawing<T> drawing, XElement clientData)
        {
            var anchor = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "Anchor");
            if (anchor != null) LoadClientDataAnchor<T>(drawing, anchor);

            LoadDrawingPositioning<T>(drawing, clientData);
            LoadDrawingProtection<T>(drawing, clientData);

            var visible = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "Visible");
            drawing.Visible = visible != null &&
                              (string.IsNullOrEmpty(visible.Value) ||
                               visible.Value.StartsWith("t", StringComparison.OrdinalIgnoreCase));

            LoadDrawingHAlignment<T>(drawing, clientData);
            LoadDrawingVAlignment<T>(drawing, clientData);
        }

        private void LoadDrawingHAlignment<T>(IXLDrawing<T> drawing, XElement clientData)
        {
            var textHAlign = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "TextHAlign");
            if (textHAlign != null)
                drawing.Style.Alignment.Horizontal = (XLDrawingHorizontalAlignment)Enum.Parse(typeof(XLDrawingHorizontalAlignment), textHAlign.Value.ToProper());
        }

        private void LoadDrawingVAlignment<T>(IXLDrawing<T> drawing, XElement clientData)
        {
            var textVAlign = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "TextVAlign");
            if (textVAlign != null)
                drawing.Style.Alignment.Vertical = (XLDrawingVerticalAlignment)Enum.Parse(typeof(XLDrawingVerticalAlignment), textVAlign.Value.ToProper());
        }

        private void LoadDrawingProtection<T>(IXLDrawing<T> drawing, XElement clientData)
        {
            var lockedElement = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "Locked");
            var lockTextElement = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "LockText");
            Boolean locked = lockedElement != null && lockedElement.Value.ToLower() == "true";
            Boolean lockText = lockTextElement != null && lockTextElement.Value.ToLower() == "true";
            drawing.Style.Protection.Locked = locked;
            drawing.Style.Protection.LockText = lockText;
        }

        private static void LoadDrawingPositioning<T>(IXLDrawing<T> drawing, XElement clientData)
        {
            var moveWithCellsElement = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "MoveWithCells");
            var sizeWithCellsElement = clientData.Elements().FirstOrDefault(e => e.Name.LocalName == "SizeWithCells");
            Boolean moveWithCells = !(moveWithCellsElement != null && moveWithCellsElement.Value.ToLower() == "true");
            Boolean sizeWithCells = !(sizeWithCellsElement != null && sizeWithCellsElement.Value.ToLower() == "true");
            if (moveWithCells && !sizeWithCells)
                drawing.Style.Properties.Positioning = XLDrawingAnchor.MoveWithCells;
            else if (moveWithCells && sizeWithCells)
                drawing.Style.Properties.Positioning = XLDrawingAnchor.MoveAndSizeWithCells;
            else
                drawing.Style.Properties.Positioning = XLDrawingAnchor.Absolute;
        }

        private static void LoadClientDataAnchor<T>(IXLDrawing<T> drawing, XElement anchor)
        {
            var location = anchor.Value.Split(',');
            drawing.Position.Column = int.Parse(location[0]) + 1;
            drawing.Position.ColumnOffset = Double.Parse(location[1], CultureInfo.InvariantCulture) / 7.5;
            drawing.Position.Row = int.Parse(location[2]) + 1;
            drawing.Position.RowOffset = Double.Parse(location[3], CultureInfo.InvariantCulture);
        }

        private void LoadShapeProperties<T>(IXLDrawing<T> xlDrawing, XElement shape)
        {
            if (shape.Attribute("style") == null)
                return;

            foreach (var attributePair in shape.Attribute("style").Value.Split(';'))
            {
                var split = attributePair.Split(':');
                if (split.Length != 2) continue;

                var attribute = split[0].Trim().ToLower();
                var value = split[1].Trim();

                switch (attribute)
                {
                    case "visibility": xlDrawing.Visible = string.Equals("visible", value, StringComparison.OrdinalIgnoreCase); break;
                    case "width":
                        if (TryGetPtValue(value, out var ptWidth))
                        {
                            xlDrawing.Style.Size.Width = ptWidth / 7.5;
                        }
                        break;

                    case "height":
                        if (TryGetPtValue(value, out var ptHeight))
                        {
                            xlDrawing.Style.Size.Height = ptHeight;
                        }
                        break;

                    case "z-index":
                        if (Int32.TryParse(value, out var zOrder))
                        {
                            xlDrawing.ZOrder = zOrder;
                        }
                        break;
                }
            }
        }

        private readonly Dictionary<string, double> knownUnits = new Dictionary<string, double>
        {
            {"pt", 1.0},
            {"in", 72.0},
            {"mm", 72.0/25.4}
        };

        private bool TryGetPtValue(string value, out double result)
        {
            var knownUnit = knownUnits.FirstOrDefault(ku => value.Contains(ku.Key));

            if (knownUnit.Key == null)
                return Double.TryParse(value, out result);

            value = value.Replace(knownUnit.Key, String.Empty);

            if (Double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                result *= knownUnit.Value;
                return true;
            }

            result = 0d;
            return false;
        }

        private void LoadDefinedNames(Workbook workbook)
        {
            if (workbook.DefinedNames == null) return;

            foreach (var definedName in workbook.DefinedNames.OfType<DefinedName>())
            {
                var name = definedName.Name;
                var visible = true;
                if (definedName.Hidden != null) visible = !BooleanValue.ToBoolean(definedName.Hidden);

                var localSheetId = -1;
                if (definedName.LocalSheetId?.HasValue ?? false) localSheetId = Convert.ToInt32(definedName.LocalSheetId.Value);

                if (name == "_xlnm.Print_Area")
                {
                    var fixedNames = validateDefinedNames(definedName.Text.Split(','));
                    foreach (string area in fixedNames)
                    {
                        if (area.Contains("["))
                        {
                            var ws = Worksheets.FirstOrDefault(w => (w as XLWorksheet).SheetId == (localSheetId + 1));
                            if (ws != null)
                            {
                                ws.PageSetup.PrintAreas.Add(area);
                            }
                        }
                        else
                        {
                            ParseReference(area, out String sheetName, out String sheetArea);
                            if (!(sheetArea.Equals("#REF") || sheetArea.EndsWith("#REF!") || sheetArea.Length == 0 || sheetName.Length == 0))
                                WorksheetsInternal.Worksheet(sheetName).PageSetup.PrintAreas.Add(sheetArea);
                        }
                    }
                }
                else if (name == "_xlnm.Print_Titles")
                {
                    LoadPrintTitles(definedName);
                }
                else
                {
                    string text = definedName.Text;

                    var comment = definedName.Comment;
                    if (localSheetId == -1)
                    {
                        if (NamedRanges.All(nr => nr.Name != name))
                            (NamedRanges as XLNamedRanges).Add(name, text, comment, validateName: false, validateRangeAddress: false).Visible = visible;
                    }
                    else
                    {
                        if (Worksheet(localSheetId + 1).NamedRanges.All(nr => nr.Name != name))
                            (Worksheet(localSheetId + 1).NamedRanges as XLNamedRanges).Add(name, text, comment, validateName: false, validateRangeAddress: false).Visible = visible;
                    }
                }
            }
        }

        private static Regex definedNameRegex = new Regex(@"\A'.*'!.*\z", RegexOptions.Compiled);

        private IEnumerable<String> validateDefinedNames(IEnumerable<String> definedNames)
        {
            var sb = new StringBuilder();
            foreach (string testName in definedNames)
            {
                if (sb.Length > 0)
                    sb.Append(',');

                sb.Append(testName);

                Match matchedValidPattern = definedNameRegex.Match(sb.ToString());
                if (matchedValidPattern.Success)
                {
                    yield return sb.ToString();
                    sb = new StringBuilder();
                }
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }

        private void LoadPrintTitles(DefinedName definedName)
        {
            var areas = validateDefinedNames(definedName.Text.Split(','));
            foreach (var item in areas)
            {
                if (this.Range(item) != null)
                    SetColumnsOrRowsToRepeat(item);
            }
        }

        private void SetColumnsOrRowsToRepeat(string area)
        {
            ParseReference(area, out String sheetName, out String sheetArea);
            sheetArea = sheetArea.Replace("$", "");

            if (sheetArea.Equals("#REF")) return;
            if (IsColReference(sheetArea))
                WorksheetsInternal.Worksheet(sheetName).PageSetup.SetColumnsToRepeatAtLeft(sheetArea);
            if (IsRowReference(sheetArea))
                WorksheetsInternal.Worksheet(sheetName).PageSetup.SetRowsToRepeatAtTop(sheetArea);
        }

        // either $A:$X => true or $1:$99 => false
        private static bool IsColReference(string sheetArea)
        {
            return sheetArea.All(c => c == ':' || char.IsLetter(c));
        }

        private static bool IsRowReference(string sheetArea)
        {
            return sheetArea.All(c => c == ':' || char.IsNumber(c));
        }

        private static void ParseReference(string item, out string sheetName, out string sheetArea)
        {
            var sections = item.Trim().Split('!');
            if (sections.Count() == 1)
            {
                sheetName = string.Empty;
                sheetArea = item;
            }
            else
            {
                sheetName = string.Join("!", sections.Take(sections.Length - 1)).UnescapeSheetName();
                sheetArea = sections[sections.Length - 1];
            }
        }

        private Int32 lastColumnNumber;

        private void LoadCells(SharedStringItem[] sharedStrings, Stylesheet s, NumberingFormats numberingFormats,
                               Fills fills, Borders borders, Fonts fonts, Dictionary<uint, string> sharedFormulasR1C1,
                               XLWorksheet ws, Dictionary<Int32, IXLStyle> styleList, Cell cell, Int32 rowIndex)
        {
            Int32 styleIndex = cell.StyleIndex != null ? Int32.Parse(cell.StyleIndex.InnerText) : 0;

            XLAddress cellAddress;
            if (cell.CellReference == null)
            {
                cellAddress = new XLAddress(ws, rowIndex, ++lastColumnNumber, false, false);
            }
            else
            {
                cellAddress = XLAddress.Create(ws, cell.CellReference.Value);
                lastColumnNumber = cellAddress.ColumnNumber;
            }

            var xlCell = ws.Cell(in cellAddress);
            var dataType = cell.DataType?.Value ?? CellValues.Number;

            if (styleList.TryGetValue(styleIndex, out IXLStyle style))
            {
                xlCell.InnerStyle = style;
            }
            else
            {
                ApplyStyle(xlCell, styleIndex, s, fills, borders, fonts, numberingFormats);
            }

            if (cell.ShowPhonetic is not null && cell.ShowPhonetic.Value)
                xlCell.ShowPhonetic = true;

            if (cell.CellMetaIndex is not null)
                xlCell.CellMetaIndex = cell.CellMetaIndex.Value;

            if (cell.ValueMetaIndex is not null)
                xlCell.ValueMetaIndex = cell.ValueMetaIndex.Value;

            var cellFormula = cell.CellFormula;
            if (cellFormula is not null)
            {
                // bx attribute of cell formula is not ever used, per MS-OI29500 2.1.620
                var formulaType = cellFormula.FormulaType?.Value ?? CellFormulaValues.Normal;
                if (formulaType == CellFormulaValues.Normal)
                {
                    xlCell.Formula = XLCellFormula.NormalA1(cellFormula.Text);
                }
                else if (formulaType == CellFormulaValues.Array && cellFormula.Reference is not null) // Child cells of an array may have array type, but not ref, that is reserved for master cell
                {
                    var aca = cellFormula.AlwaysCalculateArray?.Value ?? false;

                    var range = XLSheetRange.Parse(cellFormula.Reference);
                    var arrayFormula = XLCellFormula.Array(cellFormula.Text, range, aca);

                    // Because cells are read from top-to-bottom, from left-to-right, none of child cells have
                    // a formula yet. Also, Excel doesn't allow change of array data, only through parent formula.
                    for (var col = range.FirstPoint.Column; col <= range.LastPoint.Column; ++col)
                    {
                        for (var row = range.FirstPoint.Row; row <= range.LastPoint.Row; ++row)
                        {
                            ws.Cell(row, col).Formula = arrayFormula;
                        }
                    }
                }
                else if (formulaType == CellFormulaValues.Shared && cellFormula.SharedIndex is not null)
                {
                    // Shared formulas are rather limited in use and parsing, even by Excel
                    // https://stackoverflow.com/questions/54654993. Therefore we accept them,
                    // but don't output them. Shared formula is created, when user in Excel
                    // takes a supported formula and drags it to more cells.
                    var sharedIndex = cellFormula.SharedIndex.Value;
                    if (!sharedFormulasR1C1.TryGetValue(sharedIndex, out var sharedR1C1Formula))
                    {
                        // Spec: The first formula in a group of shared formulas is saved
                        // in the f element.This is considered the 'master' formula cell.
                        var formula = XLCellFormula.NormalA1(cellFormula.Text);
                        xlCell.Formula = formula;

                        // The key reason why Excel hates shared formulas is likely relative addressing and the messy situation it creates
                        var xlCellSheetPoint = new XLSheetPoint(cellAddress.RowNumber, cellAddress.ColumnNumber);
                        var formulaR1C1 = formula.GetFormulaR1C1(xlCellSheetPoint);
                        sharedFormulasR1C1.Add(cellFormula.SharedIndex.Value, formulaR1C1);
                    }
                    else
                    {
                        // Spec: The formula expression for a cell that is specified to be part of a shared formula
                        // (and is not the master) shall be ignored, and the master formula shall override.
                        xlCell.FormulaR1C1 = sharedR1C1Formula;
                    }
                }
                else if (formulaType == CellFormulaValues.DataTable && cellFormula.Reference is not null)
                {
                    var range = XLSheetRange.Parse(cellFormula.Reference);
                    var is2D = cellFormula.DataTable2D?.Value ?? false;
                    var input1Deleted = cellFormula.Input1Deleted?.Value ?? false;
                    var input1 = XLSheetPoint.Parse(cellFormula.R1);
                    if (is2D)
                    {
                        // Input 2 is only used for 2D tables
                        var input2Deleted = cellFormula.Input2Deleted?.Value ?? false;
                        var input2 = XLSheetPoint.Parse(cellFormula.R2);
                        xlCell.Formula = XLCellFormula.DataTable2D(range, input1, input1Deleted, input2, input2Deleted);
                    }
                    else
                    {
                        var isRowDataTable = cellFormula.DataTableRow?.Value ?? false;
                        xlCell.Formula = XLCellFormula.DataTable1D(range, input1, input1Deleted, isRowDataTable);
                    }
                }

                // If the cell doesn't contain value, we should invalidate it, otherwise rely on the stored value.
                // The value is likely more reliable. It should be set when cellFormula.CalculateCell is set or
                // when value is missing.
                if (cell.CellValue?.Text is null)
                {
                    xlCell.InvalidateFormula();
                }
            }
            // Unified code to load value. Value can be empty and only type specified (e.g. when formula doesn't save values)
            // String type is only for formulas, while shared string/inline string/date is only for pure cell values.
            var cellValue = cell.CellValue;
            if (dataType == CellValues.Number)
            {
                // XLCell is by default blank, so no need to set it.
                if (cellValue is not null && cellValue.TryGetDouble(out var number))
                {
                    var numberDataType = GetNumberDataType(xlCell.StyleValue.NumberFormat);
                    var cellNumber = numberDataType switch
                    {
                        XLDataType.DateTime => XLCellValue.FromSerialDateTime(number),
                        XLDataType.TimeSpan => XLCellValue.FromSerialTimeSpan(number),
                        _ => number // Normal number
                    };
                    xlCell.SetOnlyValue(cellNumber);
                }
            }
            else if (dataType == CellValues.SharedString)
            {
                if (cellValue is not null
                    && Int32.TryParse(cellValue.Text, XLHelper.NumberStyle, XLHelper.ParseCulture, out Int32 sharedStringId)
                    && sharedStringId >= 0 && sharedStringId < sharedStrings.Length)
                {
                    xlCell.SharedStringId = sharedStringId;
                    var sharedString = sharedStrings[sharedStringId];

                    SetCellText(xlCell, sharedString);
                }
                else
                    xlCell.SetOnlyValue(String.Empty);
            }
            else if (dataType == CellValues.String) // A plain string that is a result of a formula calculation
            {
                xlCell.SetOnlyValue(cellValue?.Text ?? String.Empty);
            }
            else if (dataType == CellValues.Boolean)
            {
                if (cellValue is not null)
                {
                    var isTrue = string.Equals(cellValue.Text, "1", StringComparison.Ordinal) ||
                                 string.Equals(cellValue.Text, "TRUE", StringComparison.OrdinalIgnoreCase);
                    xlCell.SetOnlyValue(isTrue);
                }
            }
            else if (dataType == CellValues.InlineString)
            {
                xlCell.ShareString = false;
                if (cell.InlineString != null)
                {
                    if (cell.InlineString.Text != null)
                        xlCell.SetOnlyValue(cell.InlineString.Text.Text.FixNewLines());
                    else
                        SetCellText(xlCell, cell.InlineString);
                }
                else
                    xlCell.SetOnlyValue(String.Empty);
            }
            else if (dataType == CellValues.Error)
            {
                if (cellValue is not null && XLErrorParser.TryParseError(cellValue.InnerText, out var error))
                    xlCell.SetOnlyValue(error);
            }
            else if (dataType == CellValues.Date)
            {
                // Technically, cell can contain date as ISO8601 string, but not rarely used due
                // to inconsistencies between ISO and serial date time representation.
                if (cellValue is not null)
                {
                    var date = DateTime.ParseExact(cellValue.Text, DateCellFormats,
                        XLHelper.ParseCulture,
                        DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite);
                    xlCell.SetOnlyValue(date);
                }
            }

            if (Use1904DateSystem && xlCell.DataType == XLDataType.DateTime)
            {
                // Internally ClosedXML stores cells as standard 1900-based style
                // so if a workbook is in 1904-format, we do that adjustment here and when saving.
                xlCell.SetOnlyValue(xlCell.GetDateTime().AddDays(1462));
            }

            if (!styleList.ContainsKey(styleIndex))
                styleList.Add(styleIndex, xlCell.Style);
        }

        private static readonly string[] DateCellFormats =
        {
            "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff", // Format accepted by OpenXML SDK
            "yyyy-MM-ddTHH:mm", "yyyy-MM-dd" // Formats accepted by Excel.
        };

        /// <summary>
        /// Parses the cell value for normal or rich text
        /// Input element should either be a shared string or inline string
        /// </summary>
        /// <param name="xlCell">The cell.</param>
        /// <param name="element">The element (either a shared string or inline string)</param>
        private void SetCellText(XLCell xlCell, RstType element)
        {
            var runs = element.Elements<Run>();
            var hasRuns = false;
            foreach (Run run in runs)
            {
                hasRuns = true;
                var runProperties = run.RunProperties;
                String text = run.Text.InnerText.FixNewLines();

                if (runProperties == null)
                    xlCell.GetRichText().AddText(text, xlCell.Style.Font);
                else
                {
                    var rt = xlCell.GetRichText().AddText(text);
                    var fontScheme = runProperties.Elements<FontScheme>().FirstOrDefault();
                    if (fontScheme != null && fontScheme.Val is not null)
                        rt.SetFontScheme(fontScheme.Val.Value.ToClosedXml());

                    LoadFont(runProperties, rt);
                }
            }

            if (!hasRuns)
                xlCell.SetOnlyValue(XmlEncoder.DecodeString(element.Text.InnerText));

            // Load phonetic properties
            var phoneticProperties = element.Elements<PhoneticProperties>();
            var pp = phoneticProperties.FirstOrDefault();
            if (pp != null)
            {
                if (pp.Alignment != null)
                    xlCell.GetRichText().Phonetics.Alignment = pp.Alignment.Value.ToClosedXml();
                if (pp.Type != null)
                    xlCell.GetRichText().Phonetics.Type = pp.Type.Value.ToClosedXml();

                LoadFont(pp, xlCell.GetRichText().Phonetics);
            }

            // Load phonetic runs
            var phoneticRuns = element.Elements<PhoneticRun>();
            foreach (PhoneticRun pr in phoneticRuns)
            {
                xlCell.GetRichText().Phonetics.Add(pr.Text.InnerText.FixNewLines(), (Int32)pr.BaseTextStartIndex.Value,
                                              (Int32)pr.EndingBaseIndex.Value);
            }
        }

        private void LoadNumberFormat(NumberingFormat nfSource, IXLNumberFormat nf)
        {
            if (nfSource == null) return;

            if (nfSource.NumberFormatId != null && nfSource.NumberFormatId.Value < XLConstants.NumberOfBuiltInStyles)
                nf.NumberFormatId = (Int32)nfSource.NumberFormatId.Value;
            else if (nfSource.FormatCode != null)
                nf.Format = nfSource.FormatCode.Value;
        }

        private void LoadBorder(Border borderSource, IXLBorder border)
        {
            if (borderSource == null) return;

            LoadBorderValues(borderSource.DiagonalBorder, border.SetDiagonalBorder, border.SetDiagonalBorderColor);

            if (borderSource.DiagonalUp != null)
                border.DiagonalUp = borderSource.DiagonalUp.Value;
            if (borderSource.DiagonalDown != null)
                border.DiagonalDown = borderSource.DiagonalDown.Value;

            LoadBorderValues(borderSource.LeftBorder, border.SetLeftBorder, border.SetLeftBorderColor);
            LoadBorderValues(borderSource.RightBorder, border.SetRightBorder, border.SetRightBorderColor);
            LoadBorderValues(borderSource.TopBorder, border.SetTopBorder, border.SetTopBorderColor);
            LoadBorderValues(borderSource.BottomBorder, border.SetBottomBorder, border.SetBottomBorderColor);
        }

        private void LoadBorderValues(BorderPropertiesType source, Func<XLBorderStyleValues, IXLStyle> setBorder, Func<XLColor, IXLStyle> setColor)
        {
            if (source != null)
            {
                if (source.Style != null)
                    setBorder(source.Style.Value.ToClosedXml());
                if (source.Color != null)
                    setColor(source.Color.ToClosedXMLColor(_colorList));
            }
        }

        // Differential fills store the patterns differently than other fills
        // Actually differential fills make more sense. bg is bg and fg is fg
        // 'Other' fills store the bg color in the fg field when pattern type is solid
        private void LoadFill(Fill openXMLFill, IXLFill closedXMLFill, Boolean differentialFillFormat)
        {
            if (openXMLFill == null || openXMLFill.PatternFill == null) return;

            if (openXMLFill.PatternFill.PatternType != null)
                closedXMLFill.PatternType = openXMLFill.PatternFill.PatternType.Value.ToClosedXml();
            else
                closedXMLFill.PatternType = XLFillPatternValues.Solid;

            switch (closedXMLFill.PatternType)
            {
                case XLFillPatternValues.None:
                    break;

                case XLFillPatternValues.Solid:
                    if (differentialFillFormat)
                    {
                        if (openXMLFill.PatternFill.BackgroundColor != null)
                            closedXMLFill.BackgroundColor = openXMLFill.PatternFill.BackgroundColor.ToClosedXMLColor(_colorList);
                        else
                            closedXMLFill.BackgroundColor = XLColor.FromIndex(64);
                    }
                    else
                    {
                        // yes, source is foreground!
                        if (openXMLFill.PatternFill.ForegroundColor != null)
                            closedXMLFill.BackgroundColor = openXMLFill.PatternFill.ForegroundColor.ToClosedXMLColor(_colorList);
                        else
                            closedXMLFill.BackgroundColor = XLColor.FromIndex(64);
                    }
                    break;

                default:
                    if (openXMLFill.PatternFill.ForegroundColor != null)
                        closedXMLFill.PatternColor = openXMLFill.PatternFill.ForegroundColor.ToClosedXMLColor(_colorList);

                    if (openXMLFill.PatternFill.BackgroundColor != null)
                        closedXMLFill.BackgroundColor = openXMLFill.PatternFill.BackgroundColor.ToClosedXMLColor(_colorList);
                    else
                        closedXMLFill.BackgroundColor = XLColor.FromIndex(64);
                    break;
            }
        }

        private void LoadFont(OpenXmlElement fontSource, IXLFontBase fontBase)
        {
            if (fontSource == null) return;

            fontBase.Bold = GetBoolean(fontSource.Elements<Bold>().FirstOrDefault());
            var fontColor = fontSource.Elements<DocumentFormat.OpenXml.Spreadsheet.Color>().FirstOrDefault();
            if (fontColor != null)
                fontBase.FontColor = fontColor.ToClosedXMLColor(_colorList);

            var fontFamilyNumbering =
                fontSource.Elements<DocumentFormat.OpenXml.Spreadsheet.FontFamily>().FirstOrDefault();
            if (fontFamilyNumbering != null && fontFamilyNumbering.Val != null)
                fontBase.FontFamilyNumbering =
                    (XLFontFamilyNumberingValues)Int32.Parse(fontFamilyNumbering.Val.ToString());
            var runFont = fontSource.Elements<RunFont>().FirstOrDefault();
            if (runFont != null)
            {
                if (runFont.Val != null)
                    fontBase.FontName = runFont.Val;
            }
            var fontSize = fontSource.Elements<FontSize>().FirstOrDefault();
            if (fontSize != null)
            {
                if ((fontSize).Val != null)
                    fontBase.FontSize = (fontSize).Val;
            }

            fontBase.Italic = GetBoolean(fontSource.Elements<Italic>().FirstOrDefault());
            fontBase.Shadow = GetBoolean(fontSource.Elements<Shadow>().FirstOrDefault());
            fontBase.Strikethrough = GetBoolean(fontSource.Elements<Strike>().FirstOrDefault());

            var underline = fontSource.Elements<Underline>().FirstOrDefault();
            if (underline != null)
            {
                fontBase.Underline = underline.Val != null ? underline.Val.Value.ToClosedXml() : XLFontUnderlineValues.Single;
            }

            var verticalTextAlignment = fontSource.Elements<VerticalTextAlignment>().FirstOrDefault();

            if (verticalTextAlignment == null) return;

            fontBase.VerticalAlignment = verticalTextAlignment.Val != null ? verticalTextAlignment.Val.Value.ToClosedXml() : XLFontVerticalTextAlignmentValues.Baseline;
        }

        private Int32 lastRow;

        private void LoadRows(Stylesheet s, NumberingFormats numberingFormats, Fills fills, Borders borders, Fonts fonts,
                              XLWorksheet ws, SharedStringItem[] sharedStrings,
                              Dictionary<uint, string> sharedFormulasR1C1, Dictionary<Int32, IXLStyle> styleList,
                              Row row)
        {
            Int32 rowIndex = row.RowIndex == null ? ++lastRow : (Int32)row.RowIndex.Value;
            var xlRow = ws.Row(rowIndex, false);

            if (row.Height != null)
                xlRow.Height = row.Height;
            else
            {
                xlRow.Loading = true;
                xlRow.Height = ws.RowHeight;
                xlRow.Loading = false;
            }

            if (row.DyDescent != null)
                xlRow.DyDescent = row.DyDescent.Value;

            if (row.Hidden != null && row.Hidden)
                xlRow.Hide();

            if (row.Collapsed != null && row.Collapsed)
                xlRow.Collapsed = true;

            if (row.OutlineLevel != null && row.OutlineLevel > 0)
                xlRow.OutlineLevel = row.OutlineLevel;

            if (row.ShowPhonetic != null && row.ShowPhonetic.Value)
                xlRow.ShowPhonetic = true;

            if (row.CustomFormat != null)
            {
                Int32 styleIndex = row.StyleIndex != null ? Int32.Parse(row.StyleIndex.InnerText) : -1;
                if (styleIndex >= 0)
                {
                    ApplyStyle(xlRow, styleIndex, s, fills, borders, fonts, numberingFormats);
                }
                else
                {
                    xlRow.Style = ws.Style;
                }
            }

            lastColumnNumber = 0;
            foreach (Cell cell in row.Elements<Cell>())
                LoadCells(sharedStrings, s, numberingFormats, fills, borders, fonts, sharedFormulasR1C1, ws, styleList,
                          cell, rowIndex);
        }

        private void LoadColumns(Stylesheet s, NumberingFormats numberingFormats, Fills fills, Borders borders,
                                 Fonts fonts, XLWorksheet ws, Columns columns)
        {
            if (columns == null) return;

            var wsDefaultColumn =
                columns.Elements<Column>().FirstOrDefault(c => c.Max == XLHelper.MaxColumnNumber);

            if (wsDefaultColumn != null && wsDefaultColumn.Width != null)
                ws.ColumnWidth = wsDefaultColumn.Width - XLConstants.ColumnWidthOffset;

            Int32 styleIndexDefault = wsDefaultColumn != null && wsDefaultColumn.Style != null
                                          ? Int32.Parse(wsDefaultColumn.Style.InnerText)
                                          : -1;
            if (styleIndexDefault >= 0)
                ApplyStyle(ws, styleIndexDefault, s, fills, borders, fonts, numberingFormats);

            foreach (Column col in columns.Elements<Column>())
            {
                //IXLStylized toApply;
                if (col.Max == XLHelper.MaxColumnNumber) continue;

                var xlColumns = (XLColumns)ws.Columns(col.Min, col.Max);
                if (col.Width != null)
                {
                    Double width = col.Width - XLConstants.ColumnWidthOffset;
                    //if (width < 0) width = 0;
                    xlColumns.Width = width;
                }
                else
                    xlColumns.Width = ws.ColumnWidth;

                if (col.Hidden != null && col.Hidden)
                    xlColumns.Hide();

                if (col.Collapsed != null && col.Collapsed)
                    xlColumns.CollapseOnly();

                if (col.OutlineLevel != null)
                {
                    var outlineLevel = col.OutlineLevel;
                    xlColumns.ForEach(c => c.OutlineLevel = outlineLevel);
                }

                Int32 styleIndex = col.Style != null ? Int32.Parse(col.Style.InnerText) : -1;
                if (styleIndex >= 0)
                {
                    ApplyStyle(xlColumns, styleIndex, s, fills, borders, fonts, numberingFormats);
                }
                else
                {
                    xlColumns.Style = ws.Style;
                }
            }
        }


        private static XLDataType GetNumberDataType(XLNumberFormatValue numberFormat)
        {
            var numberFormatId = numberFormat.NumberFormatId;
            if (numberFormatId == 46U)
                return XLDataType.TimeSpan;

            if ((numberFormatId >= 14 && numberFormatId <= 22) ||
                     (numberFormatId >= 45 && numberFormatId <= 47))
                return XLDataType.DateTime;

            if (!String.IsNullOrWhiteSpace(numberFormat.Format))
            {
                var dataType = GetDataTypeFromFormat(numberFormat.Format);
                return dataType ?? XLDataType.Number;
            }

            return XLDataType.Number;
        }

        private static XLDataType? GetDataTypeFromFormat(String format)
        {
            int length = format.Length;
            String f = format.ToLower();
            for (Int32 i = 0; i < length; i++)
            {
                Char c = f[i];
                if (c == '"')
                    i = f.IndexOf('"', i + 1);
                else if (c == '0' || c == '#' || c == '?')
                    return XLDataType.Number;
                else if (c == 'y' || c == 'm' || c == 'd' || c == 'h' || c == 's')
                    return XLDataType.DateTime;
            }
            return null;
        }

        private static void LoadAutoFilter(AutoFilter af, XLWorksheet ws)
        {
            if (af != null)
            {
                ws.Range(af.Reference.Value).SetAutoFilter();
                var autoFilter = ws.AutoFilter;
                LoadAutoFilterSort(af, ws, autoFilter);
                LoadAutoFilterColumns(af, autoFilter);
            }
        }

        private static void LoadAutoFilterColumns(AutoFilter af, XLAutoFilter autoFilter)
        {
            foreach (var filterColumn in af.Elements<FilterColumn>())
            {
                Int32 column = (int)filterColumn.ColumnId.Value + 1;
                if (filterColumn.CustomFilters != null)
                {
                    var filterList = new List<XLFilter>();
                    autoFilter.Column(column).FilterType = XLFilterType.Custom;
                    autoFilter.Filters.Add(column, filterList);
                    XLConnector connector = filterColumn.CustomFilters.And != null && filterColumn.CustomFilters.And.Value ? XLConnector.And : XLConnector.Or;

                    Boolean isText = false;
                    foreach (var filter in filterColumn.CustomFilters.OfType<CustomFilter>())
                    {
                        String val = filter.Val.Value;
                        if (!Double.TryParse(val, out Double dTest))
                        {
                            isText = true;
                            break;
                        }
                    }

                    foreach (var filter in filterColumn.CustomFilters.OfType<CustomFilter>())
                    {
                        var xlFilter = new XLFilter { Connector = connector };
                        if (filter.Operator != null)
                            xlFilter.Operator = filter.Operator.Value.ToClosedXml();
                        else
                            xlFilter.Operator = XLFilterOperator.Equal;

                        if (isText)
                        {
                            // TODO: Treat text BETWEEN functions better
                            if (filter.Val.Value.StartsWith("*") && filter.Val.Value.EndsWith("*"))
                            {
                                var value = filter.Val.Value.Substring(1, filter.Val.Value.Length - 2);
                                xlFilter.Value = filter.Val.Value;
                                xlFilter.Condition = xlFilter.Operator == XLFilterOperator.NotEqual
                                    ? s => !XLFilterColumn.ContainsFunction(value, s)
                                    : s => XLFilterColumn.ContainsFunction(value, s);
                            }
                            else if (filter.Val.Value.StartsWith("*"))
                            {
                                var value = filter.Val.Value.Substring(1);
                                xlFilter.Value = filter.Val.Value;
                                xlFilter.Condition = xlFilter.Operator == XLFilterOperator.NotEqual
                                    ? s => !XLFilterColumn.EndsWithFunction(value, s)
                                    : s => XLFilterColumn.EndsWithFunction(value, s);
                            }
                            else if (filter.Val.Value.EndsWith("*"))
                            {
                                var value = filter.Val.Value.Substring(0, filter.Val.Value.Length - 1);
                                xlFilter.Value = filter.Val.Value;
                                xlFilter.Condition = xlFilter.Operator == XLFilterOperator.NotEqual
                                    ? s => !XLFilterColumn.BeginsWithFunction(value, s)
                                    : s => XLFilterColumn.BeginsWithFunction(value, s);
                            }
                            else
                                xlFilter.Value = filter.Val.Value;
                        }
                        else
                            xlFilter.Value = Double.Parse(filter.Val.Value, CultureInfo.InvariantCulture);

                        // Unhandled instances - we should actually improve this
                        if (xlFilter.Condition == null)
                        {
                            Func<Object, Boolean> condition = null;
                            switch (xlFilter.Operator)
                            {
                                case XLFilterOperator.Equal:
                                    if (isText)
                                        condition = o => o.ToString().Equals(xlFilter.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                                    else
                                        condition = o => (o as IComparable).CompareTo(xlFilter.Value) == 0;
                                    break;

                                case XLFilterOperator.EqualOrGreaterThan: condition = o => (o as IComparable).CompareTo(xlFilter.Value) >= 0; break;
                                case XLFilterOperator.EqualOrLessThan: condition = o => (o as IComparable).CompareTo(xlFilter.Value) <= 0; break;
                                case XLFilterOperator.GreaterThan: condition = o => (o as IComparable).CompareTo(xlFilter.Value) > 0; break;
                                case XLFilterOperator.LessThan: condition = o => (o as IComparable).CompareTo(xlFilter.Value) < 0; break;
                                case XLFilterOperator.NotEqual:
                                    if (isText)
                                        condition = o => !o.ToString().Equals(xlFilter.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                                    else
                                        condition = o => (o as IComparable).CompareTo(xlFilter.Value) != 0;
                                    break;
                            }

                            xlFilter.Condition = condition;
                        }

                        filterList.Add(xlFilter);
                    }
                }
                else if (filterColumn.Filters != null)
                {
                    if (filterColumn.Filters.Elements().All(element => element is Filter))
                        autoFilter.Column(column).FilterType = XLFilterType.Regular;
                    else if (filterColumn.Filters.Elements().All(element => element is DateGroupItem))
                        autoFilter.Column(column).FilterType = XLFilterType.DateTimeGrouping;
                    else
                        throw new NotSupportedException(String.Format("Mixing regular filters and date group filters in a single autofilter column is not supported. Column {0} of {1}", column, autoFilter.Range.ToString()));

                    var filterList = new List<XLFilter>();

                    autoFilter.Filters.Add((int)filterColumn.ColumnId.Value + 1, filterList);

                    Boolean isText = false;
                    foreach (var filter in filterColumn.Filters.OfType<Filter>())
                    {
                        String val = filter.Val.Value;
                        if (!Double.TryParse(val, NumberStyles.Any, null, out Double dTest))
                        {
                            isText = true;
                            break;
                        }
                    }

                    foreach (var filter in filterColumn.Filters.OfType<Filter>())
                    {
                        var xlFilter = new XLFilter { Connector = XLConnector.Or, Operator = XLFilterOperator.Equal };

                        Func<Object, Boolean> condition;
                        if (isText)
                        {
                            xlFilter.Value = filter.Val.Value;
                            condition = o => o.ToString().Equals(xlFilter.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            xlFilter.Value = Double.Parse(filter.Val.Value, NumberStyles.Any);
                            condition = o => (o as IComparable).CompareTo(xlFilter.Value) == 0;
                        }

                        xlFilter.Condition = condition;
                        filterList.Add(xlFilter);
                    }

                    foreach (var dateGroupItem in filterColumn.Filters.OfType<DateGroupItem>())
                    {
                        bool valid = true;

                        if (!(dateGroupItem.DateTimeGrouping?.HasValue ?? false))
                            continue;

                        var xlDateGroupFilter = new XLFilter
                        {
                            Connector = XLConnector.Or,
                            Operator = XLFilterOperator.Equal,
                            DateTimeGrouping = dateGroupItem.DateTimeGrouping?.Value.ToClosedXml() ?? XLDateTimeGrouping.Year
                        };

                        int year = 1900;
                        int month = 1;
                        int day = 1;
                        int hour = 0;
                        int minute = 0;
                        int second = 0;

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Year)
                        {
                            if (dateGroupItem?.Year?.HasValue ?? false)
                                year = (int)dateGroupItem.Year?.Value;
                            else
                                valid &= false;
                        }

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Month)
                        {
                            if (dateGroupItem?.Month?.HasValue ?? false)
                                month = (int)dateGroupItem.Month?.Value;
                            else
                                valid &= false;
                        }

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Day)
                        {
                            if (dateGroupItem?.Day?.HasValue ?? false)
                                day = (int)dateGroupItem.Day?.Value;
                            else
                                valid &= false;
                        }

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Hour)
                        {
                            if (dateGroupItem?.Hour?.HasValue ?? false)
                                hour = (int)dateGroupItem.Hour?.Value;
                            else
                                valid &= false;
                        }

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Minute)
                        {
                            if (dateGroupItem?.Minute?.HasValue ?? false)
                                minute = (int)dateGroupItem.Minute?.Value;
                            else
                                valid &= false;
                        }

                        if (xlDateGroupFilter.DateTimeGrouping >= XLDateTimeGrouping.Second)
                        {
                            if (dateGroupItem?.Second?.HasValue ?? false)
                                second = (int)dateGroupItem.Second?.Value;
                            else
                                valid &= false;
                        }

                        var date = new DateTime(year, month, day, hour, minute, second);
                        xlDateGroupFilter.Value = date;

                        xlDateGroupFilter.Condition = date2 => XLDateTimeGroupFilteredColumn.IsMatch(date, (DateTime)date2, xlDateGroupFilter.DateTimeGrouping);

                        if (valid)
                            filterList.Add(xlDateGroupFilter);
                    }
                }
                else if (filterColumn.Top10 != null)
                {
                    var xlFilterColumn = autoFilter.Column(column);
                    autoFilter.Filters.Add(column, null);
                    xlFilterColumn.FilterType = XLFilterType.TopBottom;
                    if (filterColumn.Top10.Percent != null && filterColumn.Top10.Percent.Value)
                        xlFilterColumn.TopBottomType = XLTopBottomType.Percent;
                    else
                        xlFilterColumn.TopBottomType = XLTopBottomType.Items;

                    if (filterColumn.Top10.Top != null && !filterColumn.Top10.Top.Value)
                        xlFilterColumn.TopBottomPart = XLTopBottomPart.Bottom;
                    else
                        xlFilterColumn.TopBottomPart = XLTopBottomPart.Top;

                    xlFilterColumn.TopBottomValue = (int)filterColumn.Top10.Val.Value;
                }
                else if (filterColumn.DynamicFilter != null)
                {
                    autoFilter.Filters.Add(column, null);
                    var xlFilterColumn = autoFilter.Column(column);
                    xlFilterColumn.FilterType = XLFilterType.Dynamic;
                    if (filterColumn.DynamicFilter.Type != null)
                        xlFilterColumn.DynamicType = filterColumn.DynamicFilter.Type.Value.ToClosedXml();
                    else
                        xlFilterColumn.DynamicType = XLFilterDynamicType.AboveAverage;

                    xlFilterColumn.DynamicValue = filterColumn.DynamicFilter.Val.Value;
                }
            }
        }

        private static void LoadAutoFilterSort(AutoFilter af, XLWorksheet ws, IXLAutoFilter autoFilter)
        {
            var sort = af.Elements<SortState>().FirstOrDefault();
            if (sort != null)
            {
                var condition = sort.Elements<SortCondition>().FirstOrDefault();
                if (condition != null)
                {
                    Int32 column = ws.Range(condition.Reference.Value).FirstCell().Address.ColumnNumber - autoFilter.Range.FirstCell().Address.ColumnNumber + 1;
                    autoFilter.SortColumn = column;
                    autoFilter.Sorted = true;
                    autoFilter.SortOrder = condition.Descending != null && condition.Descending.Value ? XLSortOrder.Descending : XLSortOrder.Ascending;
                }
            }
        }

        private static void LoadSheetProtection(SheetProtection sp, XLWorksheet ws)
        {
            if (sp == null) return;

            ws.Protection.IsProtected = OpenXmlHelper.GetBooleanValueAsBool(sp.Sheet, false);

            var algorithmName = sp.AlgorithmName?.Value ?? string.Empty;
            if (String.IsNullOrEmpty(algorithmName))
            {
                ws.Protection.PasswordHash = sp.Password?.Value ?? string.Empty;
                ws.Protection.Base64EncodedSalt = string.Empty;
            }
            else if (DescribedEnumParser<XLProtectionAlgorithm.Algorithm>.IsValidDescription(algorithmName))
            {
                ws.Protection.Algorithm = DescribedEnumParser<XLProtectionAlgorithm.Algorithm>.FromDescription(algorithmName);
                ws.Protection.PasswordHash = sp.HashValue?.Value ?? string.Empty;
                ws.Protection.SpinCount = sp.SpinCount?.Value ?? 0;
                ws.Protection.Base64EncodedSalt = sp.SaltValue?.Value ?? string.Empty;
            }

            ws.Protection.AllowElement(XLSheetProtectionElements.FormatCells, !OpenXmlHelper.GetBooleanValueAsBool(sp.FormatCells, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.FormatColumns, !OpenXmlHelper.GetBooleanValueAsBool(sp.FormatColumns, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.FormatRows, !OpenXmlHelper.GetBooleanValueAsBool(sp.FormatRows, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.InsertColumns, !OpenXmlHelper.GetBooleanValueAsBool(sp.InsertColumns, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.InsertHyperlinks, !OpenXmlHelper.GetBooleanValueAsBool(sp.InsertHyperlinks, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.InsertRows, !OpenXmlHelper.GetBooleanValueAsBool(sp.InsertRows, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.DeleteColumns, !OpenXmlHelper.GetBooleanValueAsBool(sp.DeleteColumns, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.DeleteRows, !OpenXmlHelper.GetBooleanValueAsBool(sp.DeleteRows, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.AutoFilter, !OpenXmlHelper.GetBooleanValueAsBool(sp.AutoFilter, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.PivotTables, !OpenXmlHelper.GetBooleanValueAsBool(sp.PivotTables, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.Sort, !OpenXmlHelper.GetBooleanValueAsBool(sp.Sort, true));
            ws.Protection.AllowElement(XLSheetProtectionElements.EditScenarios, !OpenXmlHelper.GetBooleanValueAsBool(sp.Scenarios, true));

            ws.Protection.AllowElement(XLSheetProtectionElements.EditObjects, !OpenXmlHelper.GetBooleanValueAsBool(sp.Objects, false));
            ws.Protection.AllowElement(XLSheetProtectionElements.SelectLockedCells, !OpenXmlHelper.GetBooleanValueAsBool(sp.SelectLockedCells, false));
            ws.Protection.AllowElement(XLSheetProtectionElements.SelectUnlockedCells, !OpenXmlHelper.GetBooleanValueAsBool(sp.SelectUnlockedCells, false));
        }

        private static void LoadDataValidations(DataValidations dataValidations, XLWorksheet ws)
        {
            if (dataValidations == null) return;

            foreach (DataValidation dvs in dataValidations.Elements<DataValidation>())
            {
                String txt = dvs.SequenceOfReferences.InnerText;
                if (String.IsNullOrWhiteSpace(txt)) continue;
                foreach (var rangeAddress in txt.Split(' '))
                {
                    var dvt = new XLDataValidation(ws.Range(rangeAddress));
                    ws.DataValidations.Add(dvt, skipIntersectionsCheck: true);
                    if (dvs.AllowBlank != null) dvt.IgnoreBlanks = dvs.AllowBlank;
                    if (dvs.ShowDropDown != null) dvt.InCellDropdown = !dvs.ShowDropDown.Value;
                    if (dvs.ShowErrorMessage != null) dvt.ShowErrorMessage = dvs.ShowErrorMessage;
                    if (dvs.ShowInputMessage != null) dvt.ShowInputMessage = dvs.ShowInputMessage;
                    if (dvs.PromptTitle != null) dvt.InputTitle = dvs.PromptTitle;
                    if (dvs.Prompt != null) dvt.InputMessage = dvs.Prompt;
                    if (dvs.ErrorTitle != null) dvt.ErrorTitle = dvs.ErrorTitle;
                    if (dvs.Error != null) dvt.ErrorMessage = dvs.Error;
                    if (dvs.ErrorStyle != null) dvt.ErrorStyle = dvs.ErrorStyle.Value.ToClosedXml();
                    if (dvs.Type != null) dvt.AllowedValues = dvs.Type.Value.ToClosedXml();
                    if (dvs.Operator != null) dvt.Operator = dvs.Operator.Value.ToClosedXml();
                    if (dvs.Formula1 != null) dvt.MinValue = dvs.Formula1.Text;
                    if (dvs.Formula2 != null) dvt.MaxValue = dvs.Formula2.Text;
                }
            }
        }

        /// <summary>
        /// Loads the conditional formatting.
        /// </summary>
        // https://msdn.microsoft.com/en-us/library/documentformat.openxml.spreadsheet.conditionalformattingrule%28v=office.15%29.aspx?f=255&MSPPError=-2147217396
        private void LoadConditionalFormatting(ConditionalFormatting conditionalFormatting, XLWorksheet ws,
            Dictionary<Int32, DifferentialFormat> differentialFormats)
        {
            if (conditionalFormatting == null) return;

            foreach (var fr in conditionalFormatting.Elements<ConditionalFormattingRule>())
            {
                var ranges = conditionalFormatting.SequenceOfReferences.Items
                    .Select(sor => ws.Range(sor.Value));
                var conditionalFormat = new XLConditionalFormat(ranges);

                conditionalFormat.StopIfTrue = OpenXmlHelper.GetBooleanValueAsBool(fr.StopIfTrue, false);

                if (fr.FormatId != null)
                {
                    LoadFont(differentialFormats[(Int32)fr.FormatId.Value].Font, conditionalFormat.Style.Font);
                    LoadFill(differentialFormats[(Int32)fr.FormatId.Value].Fill, conditionalFormat.Style.Fill,
                        differentialFillFormat: true);
                    LoadBorder(differentialFormats[(Int32)fr.FormatId.Value].Border, conditionalFormat.Style.Border);
                    LoadNumberFormat(differentialFormats[(Int32)fr.FormatId.Value].NumberingFormat,
                        conditionalFormat.Style.NumberFormat);
                }

                // The conditional formatting type is compulsory. If it doesn't exist, skip the entire rule.
                if (fr.Type == null) continue;
                conditionalFormat.ConditionalFormatType = fr.Type.Value.ToClosedXml();
                conditionalFormat.OriginalPriority = fr.Priority?.Value ?? Int32.MaxValue;

                if (conditionalFormat.ConditionalFormatType == XLConditionalFormatType.CellIs && fr.Operator != null)
                    conditionalFormat.Operator = fr.Operator.Value.ToClosedXml();

                if (!String.IsNullOrWhiteSpace(fr.Text))
                    conditionalFormat.Values.Add(GetFormula(fr.Text.Value));

                if (conditionalFormat.ConditionalFormatType == XLConditionalFormatType.Top10)
                {
                    if (fr.Percent != null)
                        conditionalFormat.Percent = fr.Percent.Value;
                    if (fr.Bottom != null)
                        conditionalFormat.Bottom = fr.Bottom.Value;
                    if (fr.Rank != null)
                        conditionalFormat.Values.Add(GetFormula(fr.Rank.Value.ToString()));
                }
                else if (conditionalFormat.ConditionalFormatType == XLConditionalFormatType.TimePeriod)
                {
                    if (fr.TimePeriod != null)
                        conditionalFormat.TimePeriod = fr.TimePeriod.Value.ToClosedXml();
                    else
                        conditionalFormat.TimePeriod = XLTimePeriod.Yesterday;
                }

                if (fr.Elements<ColorScale>().Any())
                {
                    var colorScale = fr.Elements<ColorScale>().First();
                    ExtractConditionalFormatValueObjects(conditionalFormat, colorScale);
                }
                else if (fr.Elements<DataBar>().Any())
                {
                    var dataBar = fr.Elements<DataBar>().First();
                    if (dataBar.ShowValue != null)
                        conditionalFormat.ShowBarOnly = !dataBar.ShowValue.Value;

                    var id = fr.Descendants<DocumentFormat.OpenXml.Office2010.Excel.Id>().FirstOrDefault();
                    if (id != null && id.Text != null && !String.IsNullOrWhiteSpace(id.Text))
                        conditionalFormat.Id = new Guid(id.Text.Substring(1, id.Text.Length - 2));

                    ExtractConditionalFormatValueObjects(conditionalFormat, dataBar);
                }
                else if (fr.Elements<IconSet>().Any())
                {
                    var iconSet = fr.Elements<IconSet>().First();
                    if (iconSet.ShowValue != null)
                        conditionalFormat.ShowIconOnly = !iconSet.ShowValue.Value;
                    if (iconSet.Reverse != null)
                        conditionalFormat.ReverseIconOrder = iconSet.Reverse.Value;

                    if (iconSet.IconSetValue != null)
                        conditionalFormat.IconSetStyle = iconSet.IconSetValue.Value.ToClosedXml();
                    else
                        conditionalFormat.IconSetStyle = XLIconSetStyle.ThreeTrafficLights1;

                    ExtractConditionalFormatValueObjects(conditionalFormat, iconSet);
                }
                else
                {
                    foreach (var formula in fr.Elements<Formula>())
                    {
                        if (formula.Text != null
                            && (conditionalFormat.ConditionalFormatType == XLConditionalFormatType.CellIs
                                || conditionalFormat.ConditionalFormatType == XLConditionalFormatType.Expression))
                        {
                            conditionalFormat.Values.Add(GetFormula(formula.Text));
                        }
                    }
                }

                ws.ConditionalFormats.Add(conditionalFormat);
            }
        }

        private void LoadExtensions(WorksheetExtensionList extensions, XLWorksheet ws)
        {
            if (extensions == null)
            {
                return;
            }

            foreach (var conditionalFormattingRule in extensions
                .Descendants<DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormattingRule>()
                .Where(cf =>
                    cf.Type != null
                    && cf.Type.HasValue
                    && cf.Type.Value == ConditionalFormatValues.DataBar))
            {
                var xlConditionalFormat = ws.ConditionalFormats
                    .Cast<XLConditionalFormat>()
                    .SingleOrDefault(cf => cf.Id.WrapInBraces() == conditionalFormattingRule.Id);
                if (xlConditionalFormat != null)
                {
                    var negativeFillColor = conditionalFormattingRule.Descendants<DocumentFormat.OpenXml.Office2010.Excel.NegativeFillColor>().SingleOrDefault();
                    xlConditionalFormat.Colors.Add(negativeFillColor.ToClosedXMLColor(_colorList));
                }
            }

            foreach (var slg in extensions
                .Descendants<X14.SparklineGroups>()
                .SelectMany(sparklineGroups => sparklineGroups.Descendants<X14.SparklineGroup>()))
            {
                var xlSparklineGroup = (ws.SparklineGroups as XLSparklineGroups).Add();

                if (slg.Formula != null)
                    xlSparklineGroup.DateRange = Range(slg.Formula.Text);

                var xlSparklineStyle = xlSparklineGroup.Style;
                if (slg.FirstMarkerColor != null) xlSparklineStyle.FirstMarkerColor = slg.FirstMarkerColor.ToClosedXMLColor();
                if (slg.LastMarkerColor != null) xlSparklineStyle.LastMarkerColor = slg.LastMarkerColor.ToClosedXMLColor();
                if (slg.HighMarkerColor != null) xlSparklineStyle.HighMarkerColor = slg.HighMarkerColor.ToClosedXMLColor();
                if (slg.LowMarkerColor != null) xlSparklineStyle.LowMarkerColor = slg.LowMarkerColor.ToClosedXMLColor();
                if (slg.SeriesColor != null) xlSparklineStyle.SeriesColor = slg.SeriesColor.ToClosedXMLColor();
                if (slg.NegativeColor != null) xlSparklineStyle.NegativeColor = slg.NegativeColor.ToClosedXMLColor();
                if (slg.MarkersColor != null) xlSparklineStyle.MarkersColor = slg.MarkersColor.ToClosedXMLColor();
                xlSparklineGroup.Style = xlSparklineStyle;

                if (slg.DisplayHidden != null) xlSparklineGroup.DisplayHidden = slg.DisplayHidden;
                if (slg.LineWeight != null) xlSparklineGroup.LineWeight = slg.LineWeight;
                if (slg.Type != null) xlSparklineGroup.Type = slg.Type.Value.ToClosedXml();
                if (slg.DisplayEmptyCellsAs != null) xlSparklineGroup.DisplayEmptyCellsAs = slg.DisplayEmptyCellsAs.Value.ToClosedXml();

                xlSparklineGroup.ShowMarkers = XLSparklineMarkers.None;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.Markers, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.Markers;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.High, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.HighPoint;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.Low, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.LowPoint;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.First, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.FirstPoint;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.Last, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.LastPoint;
                if (OpenXmlHelper.GetBooleanValueAsBool(slg.Negative, false)) xlSparklineGroup.ShowMarkers |= XLSparklineMarkers.NegativePoints;

                if (slg.AxisColor != null) xlSparklineGroup.HorizontalAxis.Color = XLColor.FromHtml(slg.AxisColor.Rgb.Value);
                if (slg.DisplayXAxis != null) xlSparklineGroup.HorizontalAxis.IsVisible = slg.DisplayXAxis;
                if (slg.RightToLeft != null) xlSparklineGroup.HorizontalAxis.RightToLeft = slg.RightToLeft;

                if (slg.ManualMax != null) xlSparklineGroup.VerticalAxis.ManualMax = slg.ManualMax;
                if (slg.ManualMin != null) xlSparklineGroup.VerticalAxis.ManualMin = slg.ManualMin;
                if (slg.MinAxisType != null) xlSparklineGroup.VerticalAxis.MinAxisType = slg.MinAxisType.Value.ToClosedXml();
                if (slg.MaxAxisType != null) xlSparklineGroup.VerticalAxis.MaxAxisType = slg.MaxAxisType.Value.ToClosedXml();

                slg.Descendants<X14.Sparklines>().SelectMany(sls => sls.Descendants<X14.Sparkline>())
                    .ForEach(sl => xlSparklineGroup.Add(sl.ReferenceSequence?.Text, sl.Formula?.Text));
            }
        }

        private static void LoadWorkbookProtection(WorkbookProtection wp, XLWorkbook wb)
        {
            if (wp == null) return;

            wb.Protection.IsProtected = true;

            var algorithmName = wp.WorkbookAlgorithmName?.Value ?? string.Empty;
            if (String.IsNullOrEmpty(algorithmName))
            {
                wb.Protection.PasswordHash = wp.WorkbookPassword?.Value ?? string.Empty;
                wb.Protection.Base64EncodedSalt = string.Empty;
            }
            else if (DescribedEnumParser<XLProtectionAlgorithm.Algorithm>.IsValidDescription(algorithmName))
            {
                wb.Protection.Algorithm = DescribedEnumParser<XLProtectionAlgorithm.Algorithm>.FromDescription(algorithmName);
                wb.Protection.PasswordHash = wp.WorkbookHashValue?.Value ?? string.Empty;
                wb.Protection.SpinCount = wp.WorkbookSpinCount?.Value ?? 0;
                wb.Protection.Base64EncodedSalt = wp.WorkbookSaltValue?.Value ?? string.Empty;
            }

            wb.Protection.AllowElement(XLWorkbookProtectionElements.Structure, !OpenXmlHelper.GetBooleanValueAsBool(wp.LockStructure, false));
            wb.Protection.AllowElement(XLWorkbookProtectionElements.Windows, !OpenXmlHelper.GetBooleanValueAsBool(wp.LockWindows, false));
        }

        private static XLFormula GetFormula(String value)
        {
            var formula = new XLFormula();
            formula._value = value;
            formula.IsFormula = !(value[0] == '"' && value.EndsWith("\""));
            return formula;
        }

        private void ExtractConditionalFormatValueObjects(XLConditionalFormat conditionalFormat, OpenXmlElement element)
        {
            foreach (var c in element.Elements<ConditionalFormatValueObject>())
            {
                if (c.Type != null)
                    conditionalFormat.ContentTypes.Add(c.Type.Value.ToClosedXml());
                if (c.Val != null)
                    conditionalFormat.Values.Add(new XLFormula { Value = c.Val.Value });
                else
                    conditionalFormat.Values.Add(null);

                if (c.GreaterThanOrEqual != null)
                    conditionalFormat.IconSetOperators.Add(c.GreaterThanOrEqual.Value ? XLCFIconSetOperator.EqualOrGreaterThan : XLCFIconSetOperator.GreaterThan);
                else
                    conditionalFormat.IconSetOperators.Add(XLCFIconSetOperator.EqualOrGreaterThan);
            }
            foreach (var c in element.Elements<DocumentFormat.OpenXml.Spreadsheet.Color>())
            {
                conditionalFormat.Colors.Add(c.ToClosedXMLColor(_colorList));
            }
        }

        private static void LoadHyperlinks(Hyperlinks hyperlinks, WorksheetPart worksheetPart, XLWorksheet ws)
        {
            var hyperlinkDictionary = new Dictionary<String, Uri>();
            if (worksheetPart.HyperlinkRelationships != null)
                hyperlinkDictionary = worksheetPart.HyperlinkRelationships.ToDictionary(hr => hr.Id, hr => hr.Uri);

            if (hyperlinks == null) return;

            foreach (Hyperlink hl in hyperlinks.Elements<Hyperlink>())
            {
                if (hl.Reference.Value.Equals("#REF")) continue;
                String tooltip = hl.Tooltip != null ? hl.Tooltip.Value : String.Empty;
                var xlRange = ws.Range(hl.Reference.Value);
                foreach (XLCell xlCell in xlRange.Cells())
                {
                    xlCell.SettingHyperlink = true;

                    if (hl.Id != null)
                        xlCell.SetHyperlink(new XLHyperlink(hyperlinkDictionary[hl.Id], tooltip));
                    else if (hl.Location != null)
                        xlCell.SetHyperlink(new XLHyperlink(hl.Location.Value, tooltip));
                    else
                        xlCell.SetHyperlink(new XLHyperlink(hl.Reference.Value, tooltip));

                    xlCell.SettingHyperlink = false;
                }
            }
        }

        private static void LoadColumnBreaks(ColumnBreaks columnBreaks, XLWorksheet ws)
        {
            if (columnBreaks == null) return;

            foreach (Break columnBreak in columnBreaks.Elements<Break>().Where(columnBreak => columnBreak.Id != null))
            {
                ws.PageSetup.ColumnBreaks.Add(Int32.Parse(columnBreak.Id.InnerText));
            }
        }

        private static void LoadRowBreaks(RowBreaks rowBreaks, XLWorksheet ws)
        {
            if (rowBreaks == null) return;

            foreach (Break rowBreak in rowBreaks.Elements<Break>())
                ws.PageSetup.RowBreaks.Add(Int32.Parse(rowBreak.Id.InnerText));
        }

        private void LoadSheetProperties(SheetProperties sheetProperty, XLWorksheet ws, out PageSetupProperties pageSetupProperties)
        {
            pageSetupProperties = null;
            if (sheetProperty == null) return;

            if (sheetProperty.TabColor != null)
                ws.TabColor = sheetProperty.TabColor.ToClosedXMLColor(_colorList);

            if (sheetProperty.OutlineProperties != null)
            {
                if (sheetProperty.OutlineProperties.SummaryBelow != null)
                {
                    ws.Outline.SummaryVLocation = sheetProperty.OutlineProperties.SummaryBelow
                                                      ? XLOutlineSummaryVLocation.Bottom
                                                      : XLOutlineSummaryVLocation.Top;
                }

                if (sheetProperty.OutlineProperties.SummaryRight != null)
                {
                    ws.Outline.SummaryHLocation = sheetProperty.OutlineProperties.SummaryRight
                                                      ? XLOutlineSummaryHLocation.Right
                                                      : XLOutlineSummaryHLocation.Left;
                }
            }

            if (sheetProperty.PageSetupProperties != null)
                pageSetupProperties = sheetProperty.PageSetupProperties;
        }

        private static void LoadHeaderFooter(HeaderFooter headerFooter, XLWorksheet ws)
        {
            if (headerFooter == null) return;

            if (headerFooter.AlignWithMargins != null)
                ws.PageSetup.AlignHFWithMargins = headerFooter.AlignWithMargins;
            if (headerFooter.ScaleWithDoc != null)
                ws.PageSetup.ScaleHFWithDocument = headerFooter.ScaleWithDoc;

            if (headerFooter.DifferentFirst != null)
                ws.PageSetup.DifferentFirstPageOnHF = headerFooter.DifferentFirst;
            if (headerFooter.DifferentOddEven != null)
                ws.PageSetup.DifferentOddEvenPagesOnHF = headerFooter.DifferentOddEven;

            // Footers
            var xlFooter = (XLHeaderFooter)ws.PageSetup.Footer;
            var evenFooter = headerFooter.EvenFooter;
            if (evenFooter != null)
                xlFooter.SetInnerText(XLHFOccurrence.EvenPages, evenFooter.Text);
            var oddFooter = headerFooter.OddFooter;
            if (oddFooter != null)
                xlFooter.SetInnerText(XLHFOccurrence.OddPages, oddFooter.Text);
            var firstFooter = headerFooter.FirstFooter;
            if (firstFooter != null)
                xlFooter.SetInnerText(XLHFOccurrence.FirstPage, firstFooter.Text);
            // Headers
            var xlHeader = (XLHeaderFooter)ws.PageSetup.Header;
            var evenHeader = headerFooter.EvenHeader;
            if (evenHeader != null)
                xlHeader.SetInnerText(XLHFOccurrence.EvenPages, evenHeader.Text);
            var oddHeader = headerFooter.OddHeader;
            if (oddHeader != null)
                xlHeader.SetInnerText(XLHFOccurrence.OddPages, oddHeader.Text);
            var firstHeader = headerFooter.FirstHeader;
            if (firstHeader != null)
                xlHeader.SetInnerText(XLHFOccurrence.FirstPage, firstHeader.Text);

            ((XLHeaderFooter)ws.PageSetup.Header).SetAsInitial();
            ((XLHeaderFooter)ws.PageSetup.Footer).SetAsInitial();
        }

        private static void LoadPageSetup(PageSetup pageSetup, XLWorksheet ws, PageSetupProperties pageSetupProperties)
        {
            if (pageSetup == null) return;

            if (pageSetup.PaperSize != null)
                ws.PageSetup.PaperSize = (XLPaperSize)Int32.Parse(pageSetup.PaperSize.InnerText);
            if (pageSetup.Scale != null)
                ws.PageSetup.Scale = Int32.Parse(pageSetup.Scale.InnerText);
            if (pageSetupProperties != null && pageSetupProperties.FitToPage != null && pageSetupProperties.FitToPage.Value)
            {
                if (pageSetup.FitToWidth == null)
                    ws.PageSetup.PagesWide = 1;
                else
                    ws.PageSetup.PagesWide = Int32.Parse(pageSetup.FitToWidth.InnerText);

                if (pageSetup.FitToHeight == null)
                    ws.PageSetup.PagesTall = 1;
                else
                    ws.PageSetup.PagesTall = Int32.Parse(pageSetup.FitToHeight.InnerText);
            }
            if (pageSetup.PageOrder != null)
                ws.PageSetup.PageOrder = pageSetup.PageOrder.Value.ToClosedXml();
            if (pageSetup.Orientation != null)
                ws.PageSetup.PageOrientation = pageSetup.Orientation.Value.ToClosedXml();
            if (pageSetup.BlackAndWhite != null)
                ws.PageSetup.BlackAndWhite = pageSetup.BlackAndWhite;
            if (pageSetup.Draft != null)
                ws.PageSetup.DraftQuality = pageSetup.Draft;
            if (pageSetup.CellComments != null)
                ws.PageSetup.ShowComments = pageSetup.CellComments.Value.ToClosedXml();
            if (pageSetup.Errors != null)
                ws.PageSetup.PrintErrorValue = pageSetup.Errors.Value.ToClosedXml();
            if (pageSetup.HorizontalDpi != null) ws.PageSetup.HorizontalDpi = (Int32)pageSetup.HorizontalDpi.Value;
            if (pageSetup.VerticalDpi != null) ws.PageSetup.VerticalDpi = (Int32)pageSetup.VerticalDpi.Value;
            if (pageSetup.FirstPageNumber?.HasValue ?? false)
                ws.PageSetup.FirstPageNumber = pageSetup.FirstPageNumber.Value;
        }

        private static void LoadPageMargins(PageMargins pageMargins, XLWorksheet ws)
        {
            if (pageMargins == null) return;

            if (pageMargins.Bottom != null)
                ws.PageSetup.Margins.Bottom = pageMargins.Bottom;
            if (pageMargins.Footer != null)
                ws.PageSetup.Margins.Footer = pageMargins.Footer;
            if (pageMargins.Header != null)
                ws.PageSetup.Margins.Header = pageMargins.Header;
            if (pageMargins.Left != null)
                ws.PageSetup.Margins.Left = pageMargins.Left;
            if (pageMargins.Right != null)
                ws.PageSetup.Margins.Right = pageMargins.Right;
            if (pageMargins.Top != null)
                ws.PageSetup.Margins.Top = pageMargins.Top;
        }

        private static void LoadPrintOptions(PrintOptions printOptions, XLWorksheet ws)
        {
            if (printOptions == null) return;

            if (printOptions.GridLines != null)
                ws.PageSetup.ShowGridlines = printOptions.GridLines;
            if (printOptions.HorizontalCentered != null)
                ws.PageSetup.CenterHorizontally = printOptions.HorizontalCentered;
            if (printOptions.VerticalCentered != null)
                ws.PageSetup.CenterVertically = printOptions.VerticalCentered;
            if (printOptions.Headings != null)
                ws.PageSetup.ShowRowAndColumnHeadings = printOptions.Headings;
        }

        private static void LoadSheetViews(SheetViews sheetViews, XLWorksheet ws)
        {
            if (sheetViews == null) return;

            var sheetView = sheetViews.Elements<SheetView>().FirstOrDefault();

            if (sheetView == null) return;

            if (sheetView.RightToLeft != null) ws.RightToLeft = sheetView.RightToLeft.Value;
            if (sheetView.ShowFormulas != null) ws.ShowFormulas = sheetView.ShowFormulas.Value;
            if (sheetView.ShowGridLines != null) ws.ShowGridLines = sheetView.ShowGridLines.Value;
            if (sheetView.ShowOutlineSymbols != null)
                ws.ShowOutlineSymbols = sheetView.ShowOutlineSymbols.Value;
            if (sheetView.ShowRowColHeaders != null) ws.ShowRowColHeaders = sheetView.ShowRowColHeaders.Value;
            if (sheetView.ShowRuler != null) ws.ShowRuler = sheetView.ShowRuler.Value;
            if (sheetView.ShowWhiteSpace != null) ws.ShowWhiteSpace = sheetView.ShowWhiteSpace.Value;
            if (sheetView.ShowZeros != null) ws.ShowZeros = sheetView.ShowZeros.Value;
            if (sheetView.TabSelected != null) ws.TabSelected = sheetView.TabSelected.Value;

            var selection = sheetView.Elements<Selection>().FirstOrDefault();
            if (selection != null)
            {
                if (selection.SequenceOfReferences != null)
                    ws.Ranges(selection.SequenceOfReferences.InnerText.Replace(" ", ",")).Select();

                if (selection.ActiveCell != null)
                    ws.Cell(selection.ActiveCell).SetActive();
            }

            if (sheetView.ZoomScale != null)
                ws.SheetView.ZoomScale = (int)UInt32Value.ToUInt32(sheetView.ZoomScale);
            if (sheetView.ZoomScaleNormal != null)
                ws.SheetView.ZoomScaleNormal = (int)UInt32Value.ToUInt32(sheetView.ZoomScaleNormal);
            if (sheetView.ZoomScalePageLayoutView != null)
                ws.SheetView.ZoomScalePageLayoutView = (int)UInt32Value.ToUInt32(sheetView.ZoomScalePageLayoutView);
            if (sheetView.ZoomScaleSheetLayoutView != null)
                ws.SheetView.ZoomScaleSheetLayoutView = (int)UInt32Value.ToUInt32(sheetView.ZoomScaleSheetLayoutView);

            var pane = sheetView.Elements<Pane>().FirstOrDefault();
            if (new[] { PaneStateValues.Frozen, PaneStateValues.FrozenSplit }.Contains(pane?.State?.Value ?? PaneStateValues.Split))
            {
                if (pane.HorizontalSplit != null)
                    ws.SheetView.SplitColumn = (Int32)pane.HorizontalSplit.Value;
                if (pane.VerticalSplit != null)
                    ws.SheetView.SplitRow = (Int32)pane.VerticalSplit.Value;
            }

            if (XLHelper.IsValidA1Address(sheetView.TopLeftCell))
                ws.SheetView.TopLeftCellAddress = ws.Cell(sheetView.TopLeftCell.Value).Address;
        }

        private void SetProperties(SpreadsheetDocument dSpreadsheet)
        {
            var p = dSpreadsheet.PackageProperties;
            Properties.Author = p.Creator;
            Properties.Category = p.Category;
            Properties.Comments = p.Description;
            if (p.Created != null)
                Properties.Created = p.Created.Value;
            if (p.Modified != null)
                Properties.Modified = p.Modified.Value;
            Properties.Keywords = p.Keywords;
            Properties.LastModifiedBy = p.LastModifiedBy;
            Properties.Status = p.ContentStatus;
            Properties.Subject = p.Subject;
            Properties.Title = p.Title;
        }

        private void ApplyStyle(IXLStylized xlStylized, Int32 styleIndex, Stylesheet s, Fills fills, Borders borders,
            Fonts fonts, NumberingFormats numberingFormats)
        {
            var xlStyleKey = XLStyle.Default.Key;
            LoadStyle(ref xlStyleKey, styleIndex, s, fills, borders, fonts, numberingFormats);

            // When loading columns we must propagate style to each column but not deeper. In other cases we do not propagate at all.
            if (xlStylized is IXLColumns columns)
            {
                columns.Cast<XLColumn>().ForEach(col => col.InnerStyle = new XLStyle(col, xlStyleKey));
            }
            else
            {
                xlStylized.InnerStyle = new XLStyle(xlStylized, xlStyleKey);
            }
        }

        private void LoadStyle(ref XLStyleKey xlStyle, Int32 styleIndex, Stylesheet s, Fills fills, Borders borders,
                                Fonts fonts, NumberingFormats numberingFormats)
        {
            if (s == null || s.CellFormats is null) return; //No Stylesheet, no Styles

            var cellFormat = (CellFormat)s.CellFormats.ElementAt(styleIndex);

            xlStyle.IncludeQuotePrefix = OpenXmlHelper.GetBooleanValueAsBool(cellFormat.QuotePrefix, false);

            if (cellFormat.ApplyProtection != null)
            {
                var protection = cellFormat.Protection;

                if (protection == null)
                    xlStyle.Protection = XLProtectionValue.Default.Key;
                else
                {
                    xlStyle.Protection = new XLProtectionKey
                    {
                        Hidden = protection.Hidden != null && protection.Hidden.HasValue &&
                                                              protection.Hidden.Value,
                        Locked = protection.Locked == null ||
                                (protection.Locked.HasValue && protection.Locked.Value)
                    };
                }
            }

            if (UInt32HasValue(cellFormat.FillId))
            {
                var fill = (Fill)fills.ElementAt((Int32)cellFormat.FillId.Value);
                if (fill.PatternFill != null)
                {
                    var xlFill = new XLFill();
                    LoadFill(fill, xlFill, differentialFillFormat: false);
                    xlStyle.Fill = xlFill.Key;
                }
            }

            var alignment = cellFormat.Alignment;
            if (alignment != null)
            {
                var xlAlignment = xlStyle.Alignment;
                if (alignment.Horizontal != null)
                    xlAlignment.Horizontal = alignment.Horizontal.Value.ToClosedXml();
                if (alignment.Indent != null && alignment.Indent != 0)
                    xlAlignment.Indent = Int32.Parse(alignment.Indent.ToString());
                if (alignment.JustifyLastLine != null)
                    xlAlignment.JustifyLastLine = alignment.JustifyLastLine;
                if (alignment.ReadingOrder != null)
                {
                    xlAlignment.ReadingOrder =
                        (XLAlignmentReadingOrderValues)Int32.Parse(alignment.ReadingOrder.ToString());
                }
                if (alignment.RelativeIndent != null)
                    xlAlignment.RelativeIndent = alignment.RelativeIndent;
                if (alignment.ShrinkToFit != null)
                    xlAlignment.ShrinkToFit = alignment.ShrinkToFit;
                if (alignment.TextRotation != null)
                    xlAlignment.TextRotation = OpenXmlHelper.GetClosedXmlTextRotation(alignment);
                if (alignment.Vertical != null)
                    xlAlignment.Vertical = alignment.Vertical.Value.ToClosedXml();
                if (alignment.WrapText != null)
                    xlAlignment.WrapText = alignment.WrapText;

                xlStyle.Alignment = xlAlignment;
            }

            if (UInt32HasValue(cellFormat.BorderId))
            {
                uint borderId = cellFormat.BorderId.Value;
                var border = (Border)borders.ElementAt((Int32)borderId);
                var xlBorder = xlStyle.Border;
                if (border != null)
                {
                    var bottomBorder = border.BottomBorder;
                    if (bottomBorder != null)
                    {
                        if (bottomBorder.Style != null)
                            xlBorder.BottomBorder = bottomBorder.Style.Value.ToClosedXml();

                        if (bottomBorder.Color != null)
                            xlBorder.BottomBorderColor = bottomBorder.Color.ToClosedXMLColor(_colorList).Key;
                    }
                    var topBorder = border.TopBorder;
                    if (topBorder != null)
                    {
                        if (topBorder.Style != null)
                            xlBorder.TopBorder = topBorder.Style.Value.ToClosedXml();
                        if (topBorder.Color != null)
                            xlBorder.TopBorderColor = topBorder.Color.ToClosedXMLColor(_colorList).Key;
                    }
                    var leftBorder = border.LeftBorder;
                    if (leftBorder != null)
                    {
                        if (leftBorder.Style != null)
                            xlBorder.LeftBorder = leftBorder.Style.Value.ToClosedXml();
                        if (leftBorder.Color != null)
                            xlBorder.LeftBorderColor = leftBorder.Color.ToClosedXMLColor(_colorList).Key;
                    }
                    var rightBorder = border.RightBorder;
                    if (rightBorder != null)
                    {
                        if (rightBorder.Style != null)
                            xlBorder.RightBorder = rightBorder.Style.Value.ToClosedXml();
                        if (rightBorder.Color != null)
                            xlBorder.RightBorderColor = rightBorder.Color.ToClosedXMLColor(_colorList).Key;
                    }
                    var diagonalBorder = border.DiagonalBorder;
                    if (diagonalBorder != null)
                    {
                        if (diagonalBorder.Style != null)
                            xlBorder.DiagonalBorder = diagonalBorder.Style.Value.ToClosedXml();
                        if (diagonalBorder.Color != null)
                            xlBorder.DiagonalBorderColor = diagonalBorder.Color.ToClosedXMLColor(_colorList).Key;
                        if (border.DiagonalDown != null)
                            xlBorder.DiagonalDown = border.DiagonalDown;
                        if (border.DiagonalUp != null)
                            xlBorder.DiagonalUp = border.DiagonalUp;
                    }

                    xlStyle.Border = xlBorder;
                }
            }

            if (UInt32HasValue(cellFormat.FontId))
            {
                var fontId = cellFormat.FontId;
                var font = (DocumentFormat.OpenXml.Spreadsheet.Font)fonts.ElementAt((Int32)fontId.Value);

                var xlFont = xlStyle.Font;
                if (font != null)
                {
                    xlFont.Bold = GetBoolean(font.Bold);

                    if (font.Color != null)
                        xlFont.FontColor = font.Color.ToClosedXMLColor(_colorList).Key;

                    if (font.FontFamilyNumbering != null && (font.FontFamilyNumbering).Val != null)
                    {
                        xlFont.FontFamilyNumbering =
                            (XLFontFamilyNumberingValues)Int32.Parse((font.FontFamilyNumbering).Val.ToString());
                    }
                    if (font.FontName != null)
                    {
                        if ((font.FontName).Val != null)
                            xlFont.FontName = (font.FontName).Val;
                    }
                    if (font.FontSize != null)
                    {
                        if ((font.FontSize).Val != null)
                            xlFont.FontSize = (font.FontSize).Val;
                    }

                    xlFont.Italic = GetBoolean(font.Italic);
                    xlFont.Shadow = GetBoolean(font.Shadow);
                    xlFont.Strikethrough = GetBoolean(font.Strike);

                    if (font.Underline != null)
                    {
                        xlFont.Underline = font.Underline.Val != null
                                            ? (font.Underline).Val.Value.ToClosedXml()
                                            : XLFontUnderlineValues.Single;
                    }

                    if (font.VerticalTextAlignment != null)
                    {
                        xlFont.VerticalAlignment = font.VerticalTextAlignment.Val != null
                                                    ? (font.VerticalTextAlignment).Val.Value.ToClosedXml()
                                                    : XLFontVerticalTextAlignmentValues.Baseline;
                    }

                    xlStyle.Font = xlFont;
                }
            }

            if (UInt32HasValue(cellFormat.NumberFormatId))
            {
                var numberFormatId = cellFormat.NumberFormatId;

                string formatCode = String.Empty;
                if (numberingFormats != null)
                {
                    var numberingFormat =
                        numberingFormats.FirstOrDefault(
                            nf =>
                            ((NumberingFormat)nf).NumberFormatId != null &&
                            ((NumberingFormat)nf).NumberFormatId.Value == numberFormatId) as NumberingFormat;

                    if (numberingFormat != null && numberingFormat.FormatCode != null)
                        formatCode = numberingFormat.FormatCode.Value;
                }

                var xlNumberFormat = xlStyle.NumberFormat;
                if (formatCode.Length > 0)
                {
                    xlNumberFormat.Format = formatCode;
                    xlNumberFormat.NumberFormatId = -1;
                }
                else
                    xlNumberFormat.NumberFormatId = (Int32)numberFormatId.Value;
                xlStyle.NumberFormat = xlNumberFormat;
            }
        }

        private static Boolean UInt32HasValue(UInt32Value value)
        {
            return value != null && value.HasValue;
        }

        private static Boolean GetBoolean(BooleanPropertyType property)
        {
            if (property != null)
            {
                if (property.Val != null)
                    return property.Val;
                return true;
            }

            return false;
        }
    }
}
