#nullable disable

using ClosedXML.Excel.ContentManagers;
using ClosedXML.Excel.Exceptions;
using ClosedXML.Extensions;
using ClosedXML.Utils;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Break = DocumentFormat.OpenXml.Spreadsheet.Break;
using Column = DocumentFormat.OpenXml.Spreadsheet.Column;
using Columns = DocumentFormat.OpenXml.Spreadsheet.Columns;
using Drawing = DocumentFormat.OpenXml.Spreadsheet.Drawing;
using Hyperlink = DocumentFormat.OpenXml.Spreadsheet.Hyperlink;
using OfficeExcel = DocumentFormat.OpenXml.Office.Excel;
using X14 = DocumentFormat.OpenXml.Office2010.Excel;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using System.Reflection;
using System.Xml;
using static ClosedXML.Excel.XLWorkbook;
using static ClosedXML.Excel.IO.OpenXmlConst;

namespace ClosedXML.Excel.IO
{
    internal class WorksheetPartWriter
    {
        internal static void GenerateWorksheetPartContent(
            bool partIsEmpty,
            WorksheetPart worksheetPart,
            XLWorksheet xlWorksheet,
            SaveOptions options,
            SaveContext context)
        {
            var worksheetDom = GetWorksheetDom(partIsEmpty, worksheetPart, xlWorksheet, options, context);
            StreamToPart(worksheetDom, worksheetPart, xlWorksheet, context, options);
        }

        private static Worksheet GetWorksheetDom(
            bool partIsEmpty,
            WorksheetPart worksheetPart,
            XLWorksheet xlWorksheet,
            SaveOptions options,
            SaveContext context)
        {
            if (options.ConsolidateConditionalFormatRanges)
            {
                ((XLConditionalFormats)xlWorksheet.ConditionalFormats).Consolidate();
            }

            #region Worksheet

            Worksheet worksheet;
            if (!partIsEmpty)
            {
                // Accessing the worksheet through worksheetPart.Worksheet creates an attached DOM
                // worksheet that is tracked and later saved automatically to the part.
                // Using the reader, we get a detached DOM.
                // The OpenXmlReader.Create method only reads xml declaration, but doesn't read content.
                using var reader = OpenXmlReader.Create(worksheetPart);
                if (!reader.Read())
                {
                    throw new ArgumentException("Worksheet part should contain worksheet xml, but is empty.");
                }

                worksheet = (Worksheet)reader.LoadCurrentElement();
            }
            else
            {
                worksheet = new Worksheet();
            }

            if (
                !worksheet.NamespaceDeclarations.Contains(new KeyValuePair<String, String>("r",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships")))
            {
                worksheet.AddNamespaceDeclaration("r",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            }

            #endregion Worksheet

            var cm = new XLWorksheetContentManager(worksheet);

            #region SheetProperties

            if (worksheet.SheetProperties == null)
                worksheet.SheetProperties = new SheetProperties();

            worksheet.SheetProperties.TabColor = xlWorksheet.TabColor.HasValue
                ? new TabColor().FromClosedXMLColor<TabColor>(xlWorksheet.TabColor)
                : null;

            cm.SetElement(XLWorksheetContents.SheetProperties, worksheet.SheetProperties);

            if (worksheet.SheetProperties.OutlineProperties == null)
                worksheet.SheetProperties.OutlineProperties = new OutlineProperties();

            worksheet.SheetProperties.OutlineProperties.SummaryBelow =
                (xlWorksheet.Outline.SummaryVLocation ==
                 XLOutlineSummaryVLocation.Bottom);
            worksheet.SheetProperties.OutlineProperties.SummaryRight =
                (xlWorksheet.Outline.SummaryHLocation ==
                 XLOutlineSummaryHLocation.Right);

            if (worksheet.SheetProperties.PageSetupProperties == null
                && (xlWorksheet.PageSetup.PagesTall > 0 || xlWorksheet.PageSetup.PagesWide > 0))
                worksheet.SheetProperties.PageSetupProperties = new PageSetupProperties { FitToPage = true };

            #endregion SheetProperties

            var maxColumn = 0;

            var sheetDimensionReference = "A1";
            if (xlWorksheet.Internals.CellsCollection.Count > 0)
            {
                maxColumn = xlWorksheet.Internals.CellsCollection.MaxColumnUsed;
                var maxRow = xlWorksheet.Internals.CellsCollection.MaxRowUsed;
                sheetDimensionReference = "A1:" + XLHelper.GetColumnLetterFromNumber(maxColumn) +
                                          maxRow.ToInvariantString();
            }

            #region SheetViews

            if (worksheet.SheetDimension == null)
                worksheet.SheetDimension = new SheetDimension { Reference = sheetDimensionReference };

            cm.SetElement(XLWorksheetContents.SheetDimension, worksheet.SheetDimension);

            if (worksheet.SheetViews == null)
                worksheet.SheetViews = new SheetViews();

            cm.SetElement(XLWorksheetContents.SheetViews, worksheet.SheetViews);

            var sheetView = (SheetView)worksheet.SheetViews.FirstOrDefault();
            if (sheetView == null)
            {
                sheetView = new SheetView { WorkbookViewId = 0U };
                worksheet.SheetViews.AppendChild(sheetView);
            }

            var svcm = new XLSheetViewContentManager(sheetView);

            if (xlWorksheet.TabSelected)
                sheetView.TabSelected = true;
            else
                sheetView.TabSelected = null;

            if (xlWorksheet.RightToLeft)
                sheetView.RightToLeft = true;
            else
                sheetView.RightToLeft = null;

            if (xlWorksheet.ShowFormulas)
                sheetView.ShowFormulas = true;
            else
                sheetView.ShowFormulas = null;

            if (xlWorksheet.ShowGridLines)
                sheetView.ShowGridLines = null;
            else
                sheetView.ShowGridLines = false;

            if (xlWorksheet.ShowOutlineSymbols)
                sheetView.ShowOutlineSymbols = null;
            else
                sheetView.ShowOutlineSymbols = false;

            if (xlWorksheet.ShowRowColHeaders)
                sheetView.ShowRowColHeaders = null;
            else
                sheetView.ShowRowColHeaders = false;

            if (xlWorksheet.ShowRuler)
                sheetView.ShowRuler = null;
            else
                sheetView.ShowRuler = false;

            if (xlWorksheet.ShowWhiteSpace)
                sheetView.ShowWhiteSpace = null;
            else
                sheetView.ShowWhiteSpace = false;

            if (xlWorksheet.ShowZeros)
                sheetView.ShowZeros = null;
            else
                sheetView.ShowZeros = false;

            if (xlWorksheet.RightToLeft)
                sheetView.RightToLeft = true;
            else
                sheetView.RightToLeft = null;

            if (xlWorksheet.SheetView.View == XLSheetViewOptions.Normal)
                sheetView.View = null;
            else
                sheetView.View = xlWorksheet.SheetView.View.ToOpenXml();

            var pane = sheetView.Elements<Pane>().FirstOrDefault();
            if (pane == null)
            {
                pane = new Pane();
                sheetView.InsertAt(pane, 0);
            }

            svcm.SetElement(XLSheetViewContents.Pane, pane);

            pane.State = PaneStateValues.FrozenSplit;
            int hSplit = xlWorksheet.SheetView.SplitColumn;
            int ySplit = xlWorksheet.SheetView.SplitRow;

            pane.HorizontalSplit = hSplit;
            pane.VerticalSplit = ySplit;

            pane.ActivePane = (ySplit == 0 ? PaneValues.TopRight : 0)
                              | (hSplit == 0 ? PaneValues.BottomLeft : 0);

            pane.TopLeftCell = XLHelper.GetColumnLetterFromNumber(xlWorksheet.SheetView.SplitColumn + 1)
                               + (xlWorksheet.SheetView.SplitRow + 1);

            if (hSplit == 0 && ySplit == 0)
            {
                // We don't have a pane. Just a regular sheet.
                pane = null;
                sheetView.RemoveAllChildren<Pane>();
                svcm.SetElement(XLSheetViewContents.Pane, null);
            }

            // Do sheet view. Whether it's for a regular sheet or for the bottom-right pane
            if (!xlWorksheet.SheetView.TopLeftCellAddress.IsValid
                || xlWorksheet.SheetView.TopLeftCellAddress == new XLAddress(1, 1, fixedRow: false, fixedColumn: false))
                sheetView.TopLeftCell = null;
            else
                sheetView.TopLeftCell = xlWorksheet.SheetView.TopLeftCellAddress.ToString();

            if (xlWorksheet.SelectedRanges.Any() || xlWorksheet.ActiveCell != null)
            {
                sheetView.RemoveAllChildren<Selection>();
                svcm.SetElement(XLSheetViewContents.Selection, null);

                var firstSelection = xlWorksheet.SelectedRanges.FirstOrDefault();

                Action<Selection> populateSelection = (Selection selection) =>
                {
                    if (xlWorksheet.ActiveCell != null)
                        selection.ActiveCell = xlWorksheet.ActiveCell.Address.ToStringRelative(false);
                    else if (firstSelection != null)
                        selection.ActiveCell = firstSelection.RangeAddress.FirstAddress.ToStringRelative(false);

                    var seqRef = new List<String> { selection.ActiveCell.Value };
                    seqRef.AddRange(xlWorksheet.SelectedRanges
                        .Select(range =>
                        {
                            if (range.RangeAddress.FirstAddress.Equals(range.RangeAddress.LastAddress))
                                return range.RangeAddress.FirstAddress.ToStringRelative(false);
                            else
                                return range.RangeAddress.ToStringRelative(false);
                        })
                    );

                    selection.SequenceOfReferences = new ListValue<StringValue> { InnerText = String.Join(" ", seqRef.Distinct().ToArray()) };

                    sheetView.InsertAfter(selection, svcm.GetPreviousElementFor(XLSheetViewContents.Selection));
                    svcm.SetElement(XLSheetViewContents.Selection, selection);
                };

                // If a pane exists, we need to set the active pane too
                // Yes, this might lead to 2 Selection elements!
                if (pane != null)
                {
                    populateSelection(new Selection()
                    {
                        Pane = pane.ActivePane
                    });
                }
                populateSelection(new Selection());
            }

            if (xlWorksheet.SheetView.ZoomScale == 100)
                sheetView.ZoomScale = null;
            else
                sheetView.ZoomScale = (UInt32)Math.Max(10, Math.Min(400, xlWorksheet.SheetView.ZoomScale));

            if (xlWorksheet.SheetView.ZoomScaleNormal == 100)
                sheetView.ZoomScaleNormal = null;
            else
                sheetView.ZoomScaleNormal = (UInt32)Math.Max(10, Math.Min(400, xlWorksheet.SheetView.ZoomScaleNormal));

            if (xlWorksheet.SheetView.ZoomScalePageLayoutView == 100)
                sheetView.ZoomScalePageLayoutView = null;
            else
                sheetView.ZoomScalePageLayoutView = (UInt32)Math.Max(10, Math.Min(400, xlWorksheet.SheetView.ZoomScalePageLayoutView));

            if (xlWorksheet.SheetView.ZoomScaleSheetLayoutView == 100)
                sheetView.ZoomScaleSheetLayoutView = null;
            else
                sheetView.ZoomScaleSheetLayoutView = (UInt32)Math.Max(10, Math.Min(400, xlWorksheet.SheetView.ZoomScaleSheetLayoutView));

            #endregion SheetViews

            var maxOutlineColumn = 0;
            if (xlWorksheet.ColumnCount() > 0)
                maxOutlineColumn = xlWorksheet.GetMaxColumnOutline();

            var maxOutlineRow = 0;
            if (xlWorksheet.RowCount() > 0)
                maxOutlineRow = xlWorksheet.GetMaxRowOutline();

            #region SheetFormatProperties

            if (worksheet.SheetFormatProperties == null)
                worksheet.SheetFormatProperties = new SheetFormatProperties();

            cm.SetElement(XLWorksheetContents.SheetFormatProperties,
                worksheet.SheetFormatProperties);

            worksheet.SheetFormatProperties.DefaultRowHeight = xlWorksheet.RowHeight.SaveRound();

            if (xlWorksheet.RowHeightChanged)
                worksheet.SheetFormatProperties.CustomHeight = true;
            else
                worksheet.SheetFormatProperties.CustomHeight = null;

            var worksheetColumnWidth = GetColumnWidth(xlWorksheet.ColumnWidth).SaveRound();
            if (xlWorksheet.ColumnWidthChanged)
                worksheet.SheetFormatProperties.DefaultColumnWidth = worksheetColumnWidth;
            else
                worksheet.SheetFormatProperties.DefaultColumnWidth = null;

            if (maxOutlineColumn > 0)
                worksheet.SheetFormatProperties.OutlineLevelColumn = (byte)maxOutlineColumn;
            else
                worksheet.SheetFormatProperties.OutlineLevelColumn = null;

            if (maxOutlineRow > 0)
                worksheet.SheetFormatProperties.OutlineLevelRow = (byte)maxOutlineRow;
            else
                worksheet.SheetFormatProperties.OutlineLevelRow = null;

            #endregion SheetFormatProperties

            #region Columns

            var worksheetStyleId = context.SharedStyles[xlWorksheet.StyleValue].StyleId;
            if (xlWorksheet.Internals.CellsCollection.Count == 0 &&
                xlWorksheet.Internals.ColumnsCollection.Count == 0
                && worksheetStyleId == 0)
                worksheet.RemoveAllChildren<Columns>();
            else
            {
                if (!worksheet.Elements<Columns>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.Columns);
                    worksheet.InsertAfter(new Columns(), previousElement);
                }

                var columns = worksheet.Elements<Columns>().First();
                cm.SetElement(XLWorksheetContents.Columns, columns);

                var sheetColumnsByMin = columns.Elements<Column>().ToDictionary(c => c.Min.Value, c => c);
                //Dictionary<UInt32, Column> sheetColumnsByMax = columns.Elements<Column>().ToDictionary(c => c.Max.Value, c => c);

                Int32 minInColumnsCollection;
                Int32 maxInColumnsCollection;
                if (xlWorksheet.Internals.ColumnsCollection.Count > 0)
                {
                    minInColumnsCollection = xlWorksheet.Internals.ColumnsCollection.Keys.Min();
                    maxInColumnsCollection = xlWorksheet.Internals.ColumnsCollection.Keys.Max();
                }
                else
                {
                    minInColumnsCollection = 1;
                    maxInColumnsCollection = 0;
                }

                if (minInColumnsCollection > 1)
                {
                    UInt32Value min = 1;
                    UInt32Value max = (UInt32)(minInColumnsCollection - 1);

                    for (var co = min; co <= max; co++)
                    {
                        var column = new Column
                        {
                            Min = co,
                            Max = co,
                            Style = worksheetStyleId,
                            Width = worksheetColumnWidth,
                            CustomWidth = true
                        };

                        UpdateColumn(column, columns, sheetColumnsByMin); //, sheetColumnsByMax);
                    }
                }

                for (var co = minInColumnsCollection; co <= maxInColumnsCollection; co++)
                {
                    UInt32 styleId;
                    Double columnWidth;
                    var isHidden = false;
                    var collapsed = false;
                    var outlineLevel = 0;
                    if (xlWorksheet.Internals.ColumnsCollection.TryGetValue(co, out XLColumn col))
                    {
                        styleId = context.SharedStyles[col.StyleValue].StyleId;
                        columnWidth = GetColumnWidth(col.Width).SaveRound();
                        isHidden = col.IsHidden;
                        collapsed = col.Collapsed;
                        outlineLevel = col.OutlineLevel;
                    }
                    else
                    {
                        styleId = context.SharedStyles[xlWorksheet.StyleValue].StyleId;
                        columnWidth = worksheetColumnWidth;
                    }

                    var column = new Column
                    {
                        Min = (UInt32)co,
                        Max = (UInt32)co,
                        Style = styleId,
                        Width = columnWidth,
                        CustomWidth = true
                    };

                    if (isHidden)
                        column.Hidden = true;
                    if (collapsed)
                        column.Collapsed = true;
                    if (outlineLevel > 0)
                        column.OutlineLevel = (byte)outlineLevel;

                    UpdateColumn(column, columns, sheetColumnsByMin); //, sheetColumnsByMax);
                }

                var collection = maxInColumnsCollection;
                foreach (
                    var col in
                        columns.Elements<Column>().Where(c => c.Min > (UInt32)(collection)).OrderBy(
                            c => c.Min.Value))
                {
                    col.Style = worksheetStyleId;
                    col.Width = worksheetColumnWidth;
                    col.CustomWidth = true;

                    if ((Int32)col.Max.Value > maxInColumnsCollection)
                        maxInColumnsCollection = (Int32)col.Max.Value;
                }

                if (maxInColumnsCollection < XLHelper.MaxColumnNumber && worksheetStyleId != 0)
                {
                    var column = new Column
                    {
                        Min = (UInt32)(maxInColumnsCollection + 1),
                        Max = (UInt32)(XLHelper.MaxColumnNumber),
                        Style = worksheetStyleId,
                        Width = worksheetColumnWidth,
                        CustomWidth = true
                    };
                    columns.AppendChild(column);
                }

                CollapseColumns(columns, sheetColumnsByMin);

                if (!columns.Any())
                {
                    worksheet.RemoveAllChildren<Columns>();
                    cm.SetElement(XLWorksheetContents.Columns, null);
                }
            }

            #endregion Columns

            #region SheetData

            if (!worksheet.Elements<SheetData>().Any())
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.SheetData);
                worksheet.InsertAfter(new SheetData(), previousElement);
            }

            var sheetData = worksheet.Elements<SheetData>().First();
            cm.SetElement(XLWorksheetContents.SheetData, sheetData);

            // Sheet data is not updated in the Worksheet DOM here, because it is later being streamed directly to the file
            // without an intermediate DOM representation. This is done to save memory, which is especially problematic
            // for large sheets.

            #endregion SheetData

            #region SheetProtection

            if (xlWorksheet.Protection.IsProtected)
            {
                if (!worksheet.Elements<SheetProtection>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.SheetProtection);
                    worksheet.InsertAfter(new SheetProtection(), previousElement);
                }

                var sheetProtection = worksheet.Elements<SheetProtection>().First();
                cm.SetElement(XLWorksheetContents.SheetProtection, sheetProtection);

                var protection = xlWorksheet.Protection;
                sheetProtection.Sheet = OpenXmlHelper.GetBooleanValue(protection.IsProtected, false);

                sheetProtection.Password = null;
                sheetProtection.AlgorithmName = null;
                sheetProtection.HashValue = null;
                sheetProtection.SpinCount = null;
                sheetProtection.SaltValue = null;

                if (protection.Algorithm == XLProtectionAlgorithm.Algorithm.SimpleHash)
                {
                    if (!String.IsNullOrWhiteSpace(protection.PasswordHash))
                        sheetProtection.Password = protection.PasswordHash;
                }
                else
                {
                    sheetProtection.AlgorithmName = DescribedEnumParser<XLProtectionAlgorithm.Algorithm>.ToDescription(protection.Algorithm);
                    sheetProtection.HashValue = protection.PasswordHash;
                    sheetProtection.SpinCount = protection.SpinCount;
                    sheetProtection.SaltValue = protection.Base64EncodedSalt;
                }

                // default value of "1"
                sheetProtection.FormatCells = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatCells), true);
                sheetProtection.FormatColumns = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatColumns), true);
                sheetProtection.FormatRows = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatRows), true);
                sheetProtection.InsertColumns = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.InsertColumns), true);
                sheetProtection.InsertRows = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.InsertRows), true);
                sheetProtection.InsertHyperlinks = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.InsertHyperlinks), true);
                sheetProtection.DeleteColumns = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.DeleteColumns), true);
                sheetProtection.DeleteRows = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.DeleteRows), true);
                sheetProtection.Sort = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.Sort), true);
                sheetProtection.AutoFilter = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.AutoFilter), true);
                sheetProtection.PivotTables = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.PivotTables), true);
                sheetProtection.Scenarios = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.EditScenarios), true);

                // default value of "0"
                sheetProtection.Objects = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.EditObjects), false);
                sheetProtection.SelectLockedCells = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.SelectLockedCells), false);
                sheetProtection.SelectUnlockedCells = OpenXmlHelper.GetBooleanValue(!protection.AllowedElements.HasFlag(XLSheetProtectionElements.SelectUnlockedCells), false);
            }
            else
            {
                worksheet.RemoveAllChildren<SheetProtection>();
                cm.SetElement(XLWorksheetContents.SheetProtection, null);
            }

            #endregion SheetProtection

            #region AutoFilter

            worksheet.RemoveAllChildren<AutoFilter>();
            if (xlWorksheet.AutoFilter.IsEnabled)
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.AutoFilter);
                worksheet.InsertAfter(new AutoFilter(), previousElement);

                var autoFilter = worksheet.Elements<AutoFilter>().First();
                cm.SetElement(XLWorksheetContents.AutoFilter, autoFilter);

                PopulateAutoFilter(xlWorksheet.AutoFilter, autoFilter);
            }
            else
            {
                cm.SetElement(XLWorksheetContents.AutoFilter, null);
            }

            #endregion AutoFilter

            #region MergeCells

            if ((xlWorksheet).Internals.MergedRanges.Any())
            {
                if (!worksheet.Elements<MergeCells>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.MergeCells);
                    worksheet.InsertAfter(new MergeCells(), previousElement);
                }

                var mergeCells = worksheet.Elements<MergeCells>().First();
                cm.SetElement(XLWorksheetContents.MergeCells, mergeCells);
                mergeCells.RemoveAllChildren<MergeCell>();

                foreach (var mergeCell in (xlWorksheet).Internals.MergedRanges.Select(
                    m => m.RangeAddress.FirstAddress.ToString() + ":" + m.RangeAddress.LastAddress.ToString()).Select(
                        merged => new MergeCell { Reference = merged }))
                    mergeCells.AppendChild(mergeCell);

                mergeCells.Count = (UInt32)mergeCells.Count();
            }
            else
            {
                worksheet.RemoveAllChildren<MergeCells>();
                cm.SetElement(XLWorksheetContents.MergeCells, null);
            }

            #endregion MergeCells

            #region Conditional Formatting

            if (!xlWorksheet.ConditionalFormats.Any())
            {
                worksheet.RemoveAllChildren<ConditionalFormatting>();
                cm.SetElement(XLWorksheetContents.ConditionalFormatting, null);
            }
            else
            {
                worksheet.RemoveAllChildren<ConditionalFormatting>();
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.ConditionalFormatting);

                var conditionalFormats = xlWorksheet.ConditionalFormats.ToList(); // Required for IndexOf method

                foreach (var cfGroup in conditionalFormats
                    .GroupBy(
                        c => string.Join(" ", c.Ranges.Select(r => r.RangeAddress.ToStringRelative(false))),
                        c => c,
                        (key, g) => new { RangeId = key, CfList = g.ToList() }
                    )
                    )
                {
                    var conditionalFormatting = new ConditionalFormatting
                    {
                        SequenceOfReferences =
                            new ListValue<StringValue> { InnerText = cfGroup.RangeId }
                    };
                    foreach (var cf in cfGroup.CfList)
                    {
                        var priority = conditionalFormats.IndexOf(cf) + 1;
                        conditionalFormatting.Append(XLCFConverters.Convert(cf, priority, context));
                    }
                    worksheet.InsertAfter(conditionalFormatting, previousElement);
                    previousElement = conditionalFormatting;
                    cm.SetElement(XLWorksheetContents.ConditionalFormatting, conditionalFormatting);
                }
            }

            var exlst = from c in xlWorksheet.ConditionalFormats where c.ConditionalFormatType == XLConditionalFormatType.DataBar && typeof(IXLConditionalFormat).IsAssignableFrom(c.GetType()) select c;
            if (exlst != null && exlst.Any())
            {
                if (!worksheet.Elements<WorksheetExtensionList>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.WorksheetExtensionList);
                    worksheet.InsertAfter<WorksheetExtensionList>(new WorksheetExtensionList(), previousElement);
                }

                WorksheetExtensionList worksheetExtensionList = worksheet.Elements<WorksheetExtensionList>().First();
                cm.SetElement(XLWorksheetContents.WorksheetExtensionList, worksheetExtensionList);

                var conditionalFormattings = worksheetExtensionList.Descendants<DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormattings>().SingleOrDefault();
                if (conditionalFormattings == null || !conditionalFormattings.Any())
                {
                    WorksheetExtension worksheetExtension1 = new WorksheetExtension { Uri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}" };
                    worksheetExtension1.AddNamespaceDeclaration("x14", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main");
                    worksheetExtensionList.Append(worksheetExtension1);

                    conditionalFormattings = new DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormattings();
                    worksheetExtension1.Append(conditionalFormattings);
                }

                foreach (var cfGroup in exlst
                    .GroupBy(
                        c => string.Join(" ", c.Ranges.Select(r => r.RangeAddress.ToStringRelative(false))),
                        c => c,
                        (key, g) => new { RangeId = key, CfList = g.ToList<IXLConditionalFormat>() }
                        )
                    )
                {
                    foreach (var xlConditionalFormat in cfGroup.CfList.Cast<XLConditionalFormat>())
                    {
                        var conditionalFormattingRule = conditionalFormattings.Descendants<DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormattingRule>()
                                .SingleOrDefault(r => r.Id == xlConditionalFormat.Id.WrapInBraces());
                        if (conditionalFormattingRule != null)
                        {
                            var conditionalFormat = conditionalFormattingRule.Ancestors<DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormatting>().SingleOrDefault();
                            conditionalFormattings.RemoveChild<DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormatting>(conditionalFormat);
                        }

                        var conditionalFormatting = new DocumentFormat.OpenXml.Office2010.Excel.ConditionalFormatting();
                        conditionalFormatting.AddNamespaceDeclaration("xm", "http://schemas.microsoft.com/office/excel/2006/main");
                        conditionalFormatting.Append(XLCFConvertersExtension.Convert(xlConditionalFormat, context));
                        var referenceSequence = new DocumentFormat.OpenXml.Office.Excel.ReferenceSequence { Text = cfGroup.RangeId };
                        conditionalFormatting.Append(referenceSequence);

                        conditionalFormattings.Append(conditionalFormatting);
                    }
                }
            }

            #endregion Conditional Formatting

            #region Sparklines

            const string sparklineGroupsExtensionUri = "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}";

            if (!xlWorksheet.SparklineGroups.Any())
            {
                var worksheetExtensionList = worksheet.Elements<WorksheetExtensionList>().FirstOrDefault();
                var worksheetExtension = worksheetExtensionList?.Elements<WorksheetExtension>()
                    .FirstOrDefault(ext => string.Equals(ext.Uri, sparklineGroupsExtensionUri, StringComparison.InvariantCultureIgnoreCase));

                worksheetExtension?.RemoveAllChildren<X14.SparklineGroups>();

                if (worksheetExtensionList != null)
                {
                    if (worksheetExtension != null && !worksheetExtension.HasChildren)
                    {
                        worksheetExtensionList.RemoveChild(worksheetExtension);
                    }

                    if (!worksheetExtensionList.HasChildren)
                    {
                        worksheet.RemoveChild(worksheetExtensionList);
                        cm.SetElement(XLWorksheetContents.WorksheetExtensionList, null);
                    }
                }
            }
            else
            {
                if (!worksheet.Elements<WorksheetExtensionList>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.WorksheetExtensionList);
                    worksheet.InsertAfter(new WorksheetExtensionList(), previousElement);
                }

                var worksheetExtensionList = worksheet.Elements<WorksheetExtensionList>().First();
                cm.SetElement(XLWorksheetContents.WorksheetExtensionList, worksheetExtensionList);

                var sparklineGroups = worksheetExtensionList.Descendants<X14.SparklineGroups>().SingleOrDefault();

                if (sparklineGroups == null || !sparklineGroups.Any())
                {
                    var worksheetExtension1 = new WorksheetExtension() { Uri = sparklineGroupsExtensionUri };
                    worksheetExtension1.AddNamespaceDeclaration("x14", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main");
                    worksheetExtensionList.Append(worksheetExtension1);

                    sparklineGroups = new X14.SparklineGroups();
                    sparklineGroups.AddNamespaceDeclaration("xm", "http://schemas.microsoft.com/office/excel/2006/main");
                    worksheetExtension1.Append(sparklineGroups);
                }
                else
                {
                    sparklineGroups.RemoveAllChildren();
                }

                foreach (var xlSparklineGroup in xlWorksheet.SparklineGroups)
                {
                    // Do not create an empty Sparkline group
                    if (!xlSparklineGroup.Any())
                        continue;

                    var sparklineGroup = new X14.SparklineGroup();
                    sparklineGroup.SetAttribute(new OpenXmlAttribute("xr2", "uid", "http://schemas.microsoft.com/office/spreadsheetml/2015/revision2", "{A98FF5F8-AE60-43B5-8001-AD89004F45D3}"));

                    sparklineGroup.FirstMarkerColor = new X14.FirstMarkerColor().FromClosedXMLColor<X14.FirstMarkerColor>(xlSparklineGroup.Style.FirstMarkerColor);
                    sparklineGroup.LastMarkerColor = new X14.LastMarkerColor().FromClosedXMLColor<X14.LastMarkerColor>(xlSparklineGroup.Style.LastMarkerColor);
                    sparklineGroup.HighMarkerColor = new X14.HighMarkerColor().FromClosedXMLColor<X14.HighMarkerColor>(xlSparklineGroup.Style.HighMarkerColor);
                    sparklineGroup.LowMarkerColor = new X14.LowMarkerColor().FromClosedXMLColor<X14.LowMarkerColor>(xlSparklineGroup.Style.LowMarkerColor);
                    sparklineGroup.SeriesColor = new X14.SeriesColor().FromClosedXMLColor<X14.SeriesColor>(xlSparklineGroup.Style.SeriesColor);
                    sparklineGroup.NegativeColor = new X14.NegativeColor().FromClosedXMLColor<X14.NegativeColor>(xlSparklineGroup.Style.NegativeColor);
                    sparklineGroup.MarkersColor = new X14.MarkersColor().FromClosedXMLColor<X14.MarkersColor>(xlSparklineGroup.Style.MarkersColor);

                    sparklineGroup.High = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.HighPoint);
                    sparklineGroup.Low = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.LowPoint);
                    sparklineGroup.First = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.FirstPoint);
                    sparklineGroup.Last = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.LastPoint);
                    sparklineGroup.Negative = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.NegativePoints);
                    sparklineGroup.Markers = xlSparklineGroup.ShowMarkers.HasFlag(XLSparklineMarkers.Markers);

                    sparklineGroup.DisplayHidden = xlSparklineGroup.DisplayHidden;
                    sparklineGroup.LineWeight = xlSparklineGroup.LineWeight;
                    sparklineGroup.Type = xlSparklineGroup.Type.ToOpenXml();
                    sparklineGroup.DisplayEmptyCellsAs = xlSparklineGroup.DisplayEmptyCellsAs.ToOpenXml();

                    sparklineGroup.AxisColor = new X14.AxisColor() { Rgb = xlSparklineGroup.HorizontalAxis.Color.Color.ToHex() };
                    sparklineGroup.DisplayXAxis = xlSparklineGroup.HorizontalAxis.IsVisible;
                    sparklineGroup.RightToLeft = xlSparklineGroup.HorizontalAxis.RightToLeft;
                    sparklineGroup.DateAxis = xlSparklineGroup.HorizontalAxis.DateAxis;
                    if (xlSparklineGroup.HorizontalAxis.DateAxis)
                        sparklineGroup.Formula = new OfficeExcel.Formula(
                            xlSparklineGroup.DateRange.RangeAddress.ToString(XLReferenceStyle.A1, true));

                    sparklineGroup.MinAxisType = xlSparklineGroup.VerticalAxis.MinAxisType.ToOpenXml();
                    if (xlSparklineGroup.VerticalAxis.MinAxisType == XLSparklineAxisMinMax.Custom)
                        sparklineGroup.ManualMin = xlSparklineGroup.VerticalAxis.ManualMin;

                    sparklineGroup.MaxAxisType = xlSparklineGroup.VerticalAxis.MaxAxisType.ToOpenXml();
                    if (xlSparklineGroup.VerticalAxis.MaxAxisType == XLSparklineAxisMinMax.Custom)
                        sparklineGroup.ManualMax = xlSparklineGroup.VerticalAxis.ManualMax;

                    var sparklines = new X14.Sparklines(xlSparklineGroup
                        .Select(xlSparkline => new X14.Sparkline
                        {
                            Formula = new OfficeExcel.Formula(xlSparkline.SourceData.RangeAddress.ToString(XLReferenceStyle.A1, true)),
                            ReferenceSequence =
                                    new OfficeExcel.ReferenceSequence(xlSparkline.Location.Address.ToString())
                        })
                        );

                    sparklineGroup.Append(sparklines);
                    sparklineGroups.Append(sparklineGroup);
                }

                // if all Sparkline groups had no Sparklines, remove the entire SparklineGroup element
                if (sparklineGroups.ChildElements.Count == 0)
                {
                    sparklineGroups.Remove();
                }
            }

            #endregion Sparklines

            #region DataValidations

            if (!xlWorksheet.DataValidations.Any(d => d.IsDirty()))
            {
                worksheet.RemoveAllChildren<DataValidations>();
                cm.SetElement(XLWorksheetContents.DataValidations, null);
            }
            else
            {
                if (!worksheet.Elements<DataValidations>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.DataValidations);
                    worksheet.InsertAfter(new DataValidations(), previousElement);
                }

                var dataValidations = worksheet.Elements<DataValidations>().First();
                cm.SetElement(XLWorksheetContents.DataValidations, dataValidations);
                dataValidations.RemoveAllChildren<DataValidation>();

                if (options.ConsolidateDataValidationRanges)
                {
                    xlWorksheet.DataValidations.Consolidate();
                }

                foreach (var dv in xlWorksheet.DataValidations)
                {
                    var sequence = dv.Ranges.Aggregate(String.Empty, (current, r) => current + (r.RangeAddress + " "));

                    if (sequence.Length > 0)
                        sequence = sequence.Substring(0, sequence.Length - 1);

                    var dataValidation = new DataValidation
                    {
                        AllowBlank = dv.IgnoreBlanks,
                        Formula1 = new Formula1(dv.MinValue),
                        Formula2 = new Formula2(dv.MaxValue),
                        Type = dv.AllowedValues.ToOpenXml(),
                        ShowErrorMessage = dv.ShowErrorMessage,
                        Prompt = dv.InputMessage,
                        PromptTitle = dv.InputTitle,
                        ErrorTitle = dv.ErrorTitle,
                        Error = dv.ErrorMessage,
                        ShowDropDown = !dv.InCellDropdown,
                        ShowInputMessage = dv.ShowInputMessage,
                        ErrorStyle = dv.ErrorStyle.ToOpenXml(),
                        Operator = dv.Operator.ToOpenXml(),
                        SequenceOfReferences =
                            new ListValue<StringValue> { InnerText = sequence }
                    };

                    dataValidations.AppendChild(dataValidation);
                }
                dataValidations.Count = (UInt32)xlWorksheet.DataValidations.Count();
            }

            #endregion DataValidations

            #region Hyperlinks

            var relToRemove = worksheetPart.HyperlinkRelationships.ToList();
            relToRemove.ForEach(worksheetPart.DeleteReferenceRelationship);
            if (!xlWorksheet.Hyperlinks.Any())
            {
                worksheet.RemoveAllChildren<Hyperlinks>();
                cm.SetElement(XLWorksheetContents.Hyperlinks, null);
            }
            else
            {
                if (!worksheet.Elements<Hyperlinks>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.Hyperlinks);
                    worksheet.InsertAfter(new Hyperlinks(), previousElement);
                }

                var hyperlinks = worksheet.Elements<Hyperlinks>().First();
                cm.SetElement(XLWorksheetContents.Hyperlinks, hyperlinks);
                hyperlinks.RemoveAllChildren<Hyperlink>();
                foreach (var hl in xlWorksheet.Hyperlinks)
                {
                    Hyperlink hyperlink;
                    if (hl.IsExternal)
                    {
                        var rId = context.RelIdGenerator.GetNext(XLWorkbook.RelType.Workbook);
                        hyperlink = new Hyperlink { Reference = hl.Cell.Address.ToString(), Id = rId };
                        worksheetPart.AddHyperlinkRelationship(hl.ExternalAddress, true, rId);
                    }
                    else
                    {
                        hyperlink = new Hyperlink
                        {
                            Reference = hl.Cell.Address.ToString(),
                            Location = hl.InternalAddress,
                            Display = hl.Cell.GetFormattedString()
                        };
                    }
                    if (!String.IsNullOrWhiteSpace(hl.Tooltip))
                        hyperlink.Tooltip = hl.Tooltip;
                    hyperlinks.AppendChild(hyperlink);
                }
            }

            #endregion Hyperlinks

            #region PrintOptions

            if (!worksheet.Elements<PrintOptions>().Any())
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.PrintOptions);
                worksheet.InsertAfter(new PrintOptions(), previousElement);
            }

            var printOptions = worksheet.Elements<PrintOptions>().First();
            cm.SetElement(XLWorksheetContents.PrintOptions, printOptions);

            printOptions.HorizontalCentered = xlWorksheet.PageSetup.CenterHorizontally;
            printOptions.VerticalCentered = xlWorksheet.PageSetup.CenterVertically;
            printOptions.Headings = xlWorksheet.PageSetup.ShowRowAndColumnHeadings;
            printOptions.GridLines = xlWorksheet.PageSetup.ShowGridlines;

            #endregion PrintOptions

            #region PageMargins

            if (!worksheet.Elements<PageMargins>().Any())
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.PageMargins);
                worksheet.InsertAfter(new PageMargins(), previousElement);
            }

            var pageMargins = worksheet.Elements<PageMargins>().First();
            cm.SetElement(XLWorksheetContents.PageMargins, pageMargins);
            pageMargins.Left = xlWorksheet.PageSetup.Margins.Left;
            pageMargins.Right = xlWorksheet.PageSetup.Margins.Right;
            pageMargins.Top = xlWorksheet.PageSetup.Margins.Top;
            pageMargins.Bottom = xlWorksheet.PageSetup.Margins.Bottom;
            pageMargins.Header = xlWorksheet.PageSetup.Margins.Header;
            pageMargins.Footer = xlWorksheet.PageSetup.Margins.Footer;

            #endregion PageMargins

            #region PageSetup

            if (!worksheet.Elements<PageSetup>().Any())
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.PageSetup);
                worksheet.InsertAfter(new PageSetup(), previousElement);
            }

            var pageSetup = worksheet.Elements<PageSetup>().First();
            cm.SetElement(XLWorksheetContents.PageSetup, pageSetup);

            pageSetup.Orientation = xlWorksheet.PageSetup.PageOrientation.ToOpenXml();
            pageSetup.PaperSize = (UInt32)xlWorksheet.PageSetup.PaperSize;
            pageSetup.BlackAndWhite = xlWorksheet.PageSetup.BlackAndWhite;
            pageSetup.Draft = xlWorksheet.PageSetup.DraftQuality;
            pageSetup.PageOrder = xlWorksheet.PageSetup.PageOrder.ToOpenXml();
            pageSetup.CellComments = xlWorksheet.PageSetup.ShowComments.ToOpenXml();
            pageSetup.Errors = xlWorksheet.PageSetup.PrintErrorValue.ToOpenXml();

            if (xlWorksheet.PageSetup.FirstPageNumber.HasValue)
            {
                pageSetup.FirstPageNumber = UInt32Value.FromUInt32(xlWorksheet.PageSetup.FirstPageNumber.Value);
                pageSetup.UseFirstPageNumber = true;
            }
            else
            {
                pageSetup.FirstPageNumber = null;
                pageSetup.UseFirstPageNumber = null;
            }

            if (xlWorksheet.PageSetup.HorizontalDpi > 0)
                pageSetup.HorizontalDpi = (UInt32)xlWorksheet.PageSetup.HorizontalDpi;
            else
                pageSetup.HorizontalDpi = null;

            if (xlWorksheet.PageSetup.VerticalDpi > 0)
                pageSetup.VerticalDpi = (UInt32)xlWorksheet.PageSetup.VerticalDpi;
            else
                pageSetup.VerticalDpi = null;

            if (xlWorksheet.PageSetup.Scale > 0)
            {
                pageSetup.Scale = (UInt32)xlWorksheet.PageSetup.Scale;
                pageSetup.FitToWidth = null;
                pageSetup.FitToHeight = null;
            }
            else
            {
                pageSetup.Scale = null;

                if (xlWorksheet.PageSetup.PagesWide >= 0 && xlWorksheet.PageSetup.PagesWide != 1)
                    pageSetup.FitToWidth = (UInt32)xlWorksheet.PageSetup.PagesWide;

                if (xlWorksheet.PageSetup.PagesTall >= 0 && xlWorksheet.PageSetup.PagesTall != 1)
                    pageSetup.FitToHeight = (UInt32)xlWorksheet.PageSetup.PagesTall;
            }

            // For some reason some Excel files already contains pageSetup.Copies = 0
            // The validation fails for this
            // Let's remove the attribute of that's the case.
            if ((pageSetup?.Copies ?? 0) <= 0)
                pageSetup.Copies = null;

            #endregion PageSetup

            #region HeaderFooter

            var headerFooter = worksheet.Elements<HeaderFooter>().FirstOrDefault();
            if (headerFooter == null)
                headerFooter = new HeaderFooter();
            else
                worksheet.RemoveAllChildren<HeaderFooter>();

            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.HeaderFooter);
                worksheet.InsertAfter(headerFooter, previousElement);
                cm.SetElement(XLWorksheetContents.HeaderFooter, headerFooter);
            }
            if (((XLHeaderFooter)xlWorksheet.PageSetup.Header).Changed
                || ((XLHeaderFooter)xlWorksheet.PageSetup.Footer).Changed)
            {
                headerFooter.RemoveAllChildren();

                headerFooter.ScaleWithDoc = xlWorksheet.PageSetup.ScaleHFWithDocument;
                headerFooter.AlignWithMargins = xlWorksheet.PageSetup.AlignHFWithMargins;
                headerFooter.DifferentFirst = xlWorksheet.PageSetup.DifferentFirstPageOnHF;
                headerFooter.DifferentOddEven = xlWorksheet.PageSetup.DifferentOddEvenPagesOnHF;

                var oddHeader = new OddHeader(xlWorksheet.PageSetup.Header.GetText(XLHFOccurrence.OddPages));
                headerFooter.AppendChild(oddHeader);
                var oddFooter = new OddFooter(xlWorksheet.PageSetup.Footer.GetText(XLHFOccurrence.OddPages));
                headerFooter.AppendChild(oddFooter);

                var evenHeader = new EvenHeader(xlWorksheet.PageSetup.Header.GetText(XLHFOccurrence.EvenPages));
                headerFooter.AppendChild(evenHeader);
                var evenFooter = new EvenFooter(xlWorksheet.PageSetup.Footer.GetText(XLHFOccurrence.EvenPages));
                headerFooter.AppendChild(evenFooter);

                var firstHeader = new FirstHeader(xlWorksheet.PageSetup.Header.GetText(XLHFOccurrence.FirstPage));
                headerFooter.AppendChild(firstHeader);
                var firstFooter = new FirstFooter(xlWorksheet.PageSetup.Footer.GetText(XLHFOccurrence.FirstPage));
                headerFooter.AppendChild(firstFooter);
            }

            #endregion HeaderFooter

            #region RowBreaks

            var rowBreakCount = xlWorksheet.PageSetup.RowBreaks.Count;
            if (rowBreakCount > 0)
            {
                if (!worksheet.Elements<RowBreaks>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.RowBreaks);
                    worksheet.InsertAfter(new RowBreaks(), previousElement);
                }

                var rowBreaks = worksheet.Elements<RowBreaks>().First();

                var existingBreaks = rowBreaks.ChildElements.OfType<Break>();
                var rowBreaksToDelete = existingBreaks
                    .Where(rb => !rb.Id.HasValue ||
                                 !xlWorksheet.PageSetup.RowBreaks.Contains((int)rb.Id.Value))
                    .ToList();

                foreach (var rb in rowBreaksToDelete)
                {
                    rowBreaks.RemoveChild(rb);
                }

                var rowBreaksToAdd = xlWorksheet.PageSetup.RowBreaks
                    .Where(xlRb => !existingBreaks.Any(rb => rb.Id.HasValue && rb.Id.Value == xlRb));

                rowBreaks.Count = (UInt32)rowBreakCount;
                rowBreaks.ManualBreakCount = (UInt32)rowBreakCount;
                var lastRowNum = (UInt32)xlWorksheet.RangeAddress.LastAddress.RowNumber;
                foreach (var break1 in rowBreaksToAdd.Select(rb => new Break
                {
                    Id = (UInt32)rb,
                    Max = lastRowNum,
                    ManualPageBreak = true
                }))
                    rowBreaks.AppendChild(break1);
                cm.SetElement(XLWorksheetContents.RowBreaks, rowBreaks);
            }
            else
            {
                worksheet.RemoveAllChildren<RowBreaks>();
                cm.SetElement(XLWorksheetContents.RowBreaks, null);
            }

            #endregion RowBreaks

            #region ColumnBreaks

            var columnBreakCount = xlWorksheet.PageSetup.ColumnBreaks.Count;
            if (columnBreakCount > 0)
            {
                if (!worksheet.Elements<ColumnBreaks>().Any())
                {
                    var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.ColumnBreaks);
                    worksheet.InsertAfter(new ColumnBreaks(), previousElement);
                }

                var columnBreaks = worksheet.Elements<ColumnBreaks>().First();

                var existingBreaks = columnBreaks.ChildElements.OfType<Break>();
                var columnBreaksToDelete = existingBreaks
                    .Where(cb => !cb.Id.HasValue ||
                                 !xlWorksheet.PageSetup.ColumnBreaks.Contains((int)cb.Id.Value))
                    .ToList();

                foreach (var rb in columnBreaksToDelete)
                {
                    columnBreaks.RemoveChild(rb);
                }

                var columnBreaksToAdd = xlWorksheet.PageSetup.ColumnBreaks
                    .Where(xlCb => !existingBreaks.Any(cb => cb.Id.HasValue && cb.Id.Value == xlCb));

                columnBreaks.Count = (UInt32)columnBreakCount;
                columnBreaks.ManualBreakCount = (UInt32)columnBreakCount;
                var maxColumnNumber = (UInt32)xlWorksheet.RangeAddress.LastAddress.ColumnNumber;
                foreach (var break1 in columnBreaksToAdd.Select(cb => new Break
                {
                    Id = (UInt32)cb,
                    Max = maxColumnNumber,
                    ManualPageBreak = true
                }))
                    columnBreaks.AppendChild(break1);
                cm.SetElement(XLWorksheetContents.ColumnBreaks, columnBreaks);
            }
            else
            {
                worksheet.RemoveAllChildren<ColumnBreaks>();
                cm.SetElement(XLWorksheetContents.ColumnBreaks, null);
            }

            #endregion ColumnBreaks

            #region Tables

            PopulateTablePartReferences((XLTables)xlWorksheet.Tables, worksheet, cm);

            #endregion Tables

            #region Drawings

            if (worksheetPart.DrawingsPart != null)
            {
                var xlPictures = xlWorksheet.Pictures as Drawings.XLPictures;
                foreach (var removedPicture in xlPictures.Deleted)
                {
                    worksheetPart.DrawingsPart.DeletePart(removedPicture);
                }
                xlPictures.Deleted.Clear();
            }

            foreach (var pic in xlWorksheet.Pictures)
            {
                AddPictureAnchor(worksheetPart, pic, context);
            }

            if (xlWorksheet.Pictures.Any())
                RebaseNonVisualDrawingPropertiesIds(worksheetPart);

            var tableParts = worksheet.Elements<TableParts>().First();
            if (xlWorksheet.Pictures.Any() && !worksheet.OfType<Drawing>().Any())
            {
                var worksheetDrawing = new Drawing { Id = worksheetPart.GetIdOfPart(worksheetPart.DrawingsPart) };
                worksheetDrawing.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                worksheet.InsertBefore(worksheetDrawing, tableParts);
                cm.SetElement(XLWorksheetContents.Drawing, worksheet.Elements<Drawing>().First());
            }

            // Instead of saving a file with an empty Drawings.xml file, rather remove the .xml file
            if (!xlWorksheet.Pictures.Any() && worksheetPart.DrawingsPart != null
                && !worksheetPart.DrawingsPart.Parts.Any())
            {
                var id = worksheetPart.GetIdOfPart(worksheetPart.DrawingsPart);
                worksheetPart.Worksheet.RemoveChild(worksheetPart.Worksheet.OfType<Drawing>().FirstOrDefault(p => p.Id == id));
                worksheetPart.DeletePart(worksheetPart.DrawingsPart);
                cm.SetElement(XLWorksheetContents.Drawing, null);
            }

            #endregion Drawings

            #region LegacyDrawing

            // Does worksheet have any comments (stored in legacy VML drawing)
            if (!String.IsNullOrEmpty(xlWorksheet.LegacyDrawingId))
            {
                worksheet.RemoveAllChildren<LegacyDrawing>();
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.LegacyDrawing);
                worksheet.InsertAfter(new LegacyDrawing { Id = xlWorksheet.LegacyDrawingId },
                    previousElement);

                cm.SetElement(XLWorksheetContents.LegacyDrawing, worksheet.Elements<LegacyDrawing>().First());
            }
            else
            {
                worksheet.RemoveAllChildren<LegacyDrawing>();
                cm.SetElement(XLWorksheetContents.LegacyDrawing, null);
            }

            #endregion LegacyDrawing

            #region LegacyDrawingHeaderFooter

            //LegacyDrawingHeaderFooter legacyHeaderFooter = worksheetPart.Worksheet.Elements<LegacyDrawingHeaderFooter>().FirstOrDefault();
            //if (legacyHeaderFooter != null)
            //{
            //    worksheetPart.Worksheet.RemoveAllChildren<LegacyDrawingHeaderFooter>();
            //    {
            //            var previousElement = cm.GetPreviousElementFor(XLWSContentManager.XLWSContents.LegacyDrawingHeaderFooter);
            //            worksheetPart.Worksheet.InsertAfter(new LegacyDrawingHeaderFooter { Id = xlWorksheet.LegacyDrawingId },
            //                                                previousElement);
            //    }
            //}

            #endregion LegacyDrawingHeaderFooter

            return worksheet;
        }

        private static void WriteCellValue(XmlWriter w, XLCell xlCell, SaveContext context)
        {
            var dataType = xlCell.DataType;
            if (dataType == XLDataType.Blank)
                return;

            if (dataType == XLDataType.Text)
            {
                if (xlCell.HasFormula)
                {
                    WriteStringValue(w, xlCell.GetText());
                }
                else
                {
                    if (xlCell.StyleValue.IncludeQuotePrefix || xlCell.GetText().Length != 0)
                    {
                        if (xlCell.ShareString)
                        {
                            w.WriteStartElement("v", Main2006SsNs);
                            w.WriteValue(xlCell.SharedStringId);
                            w.WriteEndElement();
                        }
                        else
                        {
                            w.WriteStartElement("is", Main2006SsNs);
                            if (xlCell.HasRichText)
                            {
                                TextSerializer.WriteRichTextElements(w, xlCell, context);
                            }
                            else
                            {
                                w.WriteStartElement("t", Main2006SsNs);
                                var text = xlCell.GetText();
                                if (text.PreserveSpaces())
                                    w.WritePreserveSpaceAttr();

                                w.WriteString(text);
                                w.WriteEndElement();
                            }

                            w.WriteEndElement(); // is
                        }
                    }
                }
            }
            else if (dataType == XLDataType.TimeSpan)
            {
                WriteNumberValue(w, xlCell.Value.GetUnifiedNumber());
            }
            else if (dataType == XLDataType.Number)
            {
                WriteNumberValue(w, xlCell.Value.GetNumber());
            }
            else if (dataType == XLDataType.DateTime)
            {
                // OpenXML SDK validator requires a specific format, in addition to the spec, but can reads many more
                var date = xlCell.GetDateTime();
                if (xlCell.Worksheet.Workbook.Use1904DateSystem)
                    date = date.AddDays(-1462);

                WriteNumberValue(w, date.ToSerialDateTime());
            }
            else if (dataType == XLDataType.Boolean)
            {
                WriteStringValue(w, xlCell.GetBoolean() ? TrueValue : FalseValue);
            }
            else if (dataType == XLDataType.Error)
            {
                WriteStringValue(w, xlCell.Value.GetError().ToDisplayString());
            }
            else
            {
                throw new InvalidOperationException();
            }

            static void WriteStringValue(XmlWriter w, String text)
            {
                w.WriteStartElement("v", Main2006SsNs);
                w.WriteString(text);
                w.WriteEndElement();
            }

            static void WriteNumberValue(XmlWriter w, Double value)
            {
                w.WriteStartElement("v", Main2006SsNs);
                w.WriteNumberValue(value);
                w.WriteEndElement();
            }
        }

        internal static void PopulateAutoFilter(XLAutoFilter xlAutoFilter, AutoFilter autoFilter)
        {
            var filterRange = xlAutoFilter.Range;
            autoFilter.Reference = filterRange.RangeAddress.ToString();

            foreach (var kp in xlAutoFilter.Filters)
            {
                var filterColumn = new FilterColumn { ColumnId = (UInt32)kp.Key - 1 };
                var xlFilterColumn = xlAutoFilter.Column(kp.Key);

                switch (xlFilterColumn.FilterType)
                {
                    case XLFilterType.Custom:
                        var customFilters = new CustomFilters();
                        foreach (var filter in kp.Value)
                        {
                            var customFilter = new CustomFilter { Val = filter.Value.ObjectToInvariantString() };

                            if (filter.Operator != XLFilterOperator.Equal)
                                customFilter.Operator = filter.Operator.ToOpenXml();

                            if (filter.Connector == XLConnector.And)
                                customFilters.And = true;

                            customFilters.Append(customFilter);
                        }
                        filterColumn.Append(customFilters);
                        break;

                    case XLFilterType.TopBottom:

                        var top101 = new Top10 { Val = (double)xlFilterColumn.TopBottomValue };
                        if (xlFilterColumn.TopBottomType == XLTopBottomType.Percent)
                            top101.Percent = true;
                        if (xlFilterColumn.TopBottomPart == XLTopBottomPart.Bottom)
                            top101.Top = false;

                        filterColumn.Append(top101);
                        break;

                    case XLFilterType.Dynamic:

                        var dynamicFilter = new DynamicFilter
                        { Type = xlFilterColumn.DynamicType.ToOpenXml(), Val = xlFilterColumn.DynamicValue };
                        filterColumn.Append(dynamicFilter);
                        break;

                    case XLFilterType.DateTimeGrouping:
                        var dateTimeGroupFilters = new Filters();
                        foreach (var filter in kp.Value)
                        {
                            if (filter.Value is DateTime)
                            {
                                var d = (DateTime)filter.Value;
                                var dgi = new DateGroupItem
                                {
                                    Year = (UInt16)d.Year,
                                    DateTimeGrouping = filter.DateTimeGrouping.ToOpenXml()
                                };

                                if (filter.DateTimeGrouping >= XLDateTimeGrouping.Month) dgi.Month = (UInt16)d.Month;
                                if (filter.DateTimeGrouping >= XLDateTimeGrouping.Day) dgi.Day = (UInt16)d.Day;
                                if (filter.DateTimeGrouping >= XLDateTimeGrouping.Hour) dgi.Hour = (UInt16)d.Hour;
                                if (filter.DateTimeGrouping >= XLDateTimeGrouping.Minute) dgi.Minute = (UInt16)d.Minute;
                                if (filter.DateTimeGrouping >= XLDateTimeGrouping.Second) dgi.Second = (UInt16)d.Second;

                                dateTimeGroupFilters.Append(dgi);
                            }
                        }
                        filterColumn.Append(dateTimeGroupFilters);
                        break;

                    default:
                        var filters = new Filters();
                        foreach (var filter in kp.Value)
                        {
                            filters.Append(new Filter { Val = filter.Value.ObjectToInvariantString() });
                        }

                        filterColumn.Append(filters);
                        break;
                }
                autoFilter.Append(filterColumn);
            }

            if (xlAutoFilter.Sorted)
            {
                string reference = null;

                if (filterRange.FirstCell().Address.RowNumber < filterRange.LastCell().Address.RowNumber)
                    reference = filterRange.Range(filterRange.FirstCell().CellBelow(), filterRange.LastCell()).RangeAddress.ToString();
                else
                    reference = filterRange.RangeAddress.ToString();

                var sortState = new SortState
                {
                    Reference = reference
                };

                var sortCondition = new SortCondition
                {
                    Reference =
                        filterRange.Range(1, xlAutoFilter.SortColumn, filterRange.RowCount(),
                            xlAutoFilter.SortColumn).RangeAddress.ToString()
                };
                if (xlAutoFilter.SortOrder == XLSortOrder.Descending)
                    sortCondition.Descending = true;

                sortState.Append(sortCondition);
                autoFilter.Append(sortState);
            }
        }

        private static void CollapseColumns(Columns columns, Dictionary<uint, Column> sheetColumns)
        {
            UInt32 lastMin = 1;
            var count = sheetColumns.Count;
            var arr = sheetColumns.OrderBy(kp => kp.Key).ToArray();
            // sheetColumns[kp.Key + 1]
            //Int32 i = 0;
            //foreach (KeyValuePair<uint, Column> kp in arr
            //    //.Where(kp => !(kp.Key < count && ColumnsAreEqual(kp.Value, )))
            //    )
            for (var i = 0; i < count; i++)
            {
                var kp = arr[i];
                if (i + 1 != count && ColumnsAreEqual(kp.Value, arr[i + 1].Value)) continue;

                var newColumn = (Column)kp.Value.CloneNode(true);
                newColumn.Min = lastMin;
                var newColumnMax = newColumn.Max.Value;
                var columnsToRemove =
                    columns.Elements<Column>().Where(co => co.Min >= lastMin && co.Max <= newColumnMax).
                        Select(co => co).ToList();
                columnsToRemove.ForEach(c => columns.RemoveChild(c));

                columns.AppendChild(newColumn);
                lastMin = kp.Key + 1;
                //i++;
            }
        }

        private static double GetColumnWidth(double columnWidth)
        {
            return Math.Min(255.0, Math.Max(0.0, columnWidth + XLConstants.ColumnWidthOffset));
        }

        private static void UpdateColumn(Column column, Columns columns, Dictionary<uint, Column> sheetColumnsByMin)
        {
            if (!sheetColumnsByMin.TryGetValue(column.Min.Value, out Column newColumn))
            {
                newColumn = (Column)column.CloneNode(true);
                columns.AppendChild(newColumn);
                sheetColumnsByMin.Add(column.Min.Value, newColumn);
            }
            else
            {
                var existingColumn = sheetColumnsByMin[column.Min.Value];
                newColumn = (Column)existingColumn.CloneNode(true);
                newColumn.Min = column.Min;
                newColumn.Max = column.Max;
                newColumn.Style = column.Style;
                newColumn.Width = column.Width.SaveRound();
                newColumn.CustomWidth = column.CustomWidth;

                if (column.Hidden != null)
                    newColumn.Hidden = true;
                else
                    newColumn.Hidden = null;

                if (column.Collapsed != null)
                    newColumn.Collapsed = true;
                else
                    newColumn.Collapsed = null;

                if (column.OutlineLevel != null && column.OutlineLevel > 0)
                    newColumn.OutlineLevel = (byte)column.OutlineLevel;
                else
                    newColumn.OutlineLevel = null;

                sheetColumnsByMin.Remove(column.Min.Value);
                if (existingColumn.Min + 1 > existingColumn.Max)
                {
                    //existingColumn.Min = existingColumn.Min + 1;
                    //columns.InsertBefore(existingColumn, newColumn);
                    //existingColumn.Remove();
                    columns.RemoveChild(existingColumn);
                    columns.AppendChild(newColumn);
                    sheetColumnsByMin.Add(newColumn.Min.Value, newColumn);
                }
                else
                {
                    //columns.InsertBefore(existingColumn, newColumn);
                    columns.AppendChild(newColumn);
                    sheetColumnsByMin.Add(newColumn.Min.Value, newColumn);
                    existingColumn.Min = existingColumn.Min + 1;
                    sheetColumnsByMin.Add(existingColumn.Min.Value, existingColumn);
                }
            }
        }

        private static bool ColumnsAreEqual(Column left, Column right)
        {
            return
                ((left.Style == null && right.Style == null)
                 || (left.Style != null && right.Style != null && left.Style.Value == right.Style.Value))
                && ((left.Width == null && right.Width == null)
                    || (left.Width != null && right.Width != null && (Math.Abs(left.Width.Value - right.Width.Value) < XLHelper.Epsilon)))
                && ((left.Hidden == null && right.Hidden == null)
                    || (left.Hidden != null && right.Hidden != null && left.Hidden.Value == right.Hidden.Value))
                && ((left.Collapsed == null && right.Collapsed == null)
                    ||
                    (left.Collapsed != null && right.Collapsed != null && left.Collapsed.Value == right.Collapsed.Value))
                && ((left.OutlineLevel == null && right.OutlineLevel == null)
                    ||
                    (left.OutlineLevel != null && right.OutlineLevel != null &&
                     left.OutlineLevel.Value == right.OutlineLevel.Value));
        }

        // http://polymathprogrammer.com/2009/10/22/english-metric-units-and-open-xml/
        // http://archive.oreilly.com/pub/post/what_is_an_emu.html
        // https://en.wikipedia.org/wiki/Office_Open_XML_file_formats#DrawingML
        private static Int64 ConvertToEnglishMetricUnits(Int32 pixels, Double resolution)
        {
            return Convert.ToInt64(914400L * pixels / resolution);
        }

        private static void AddPictureAnchor(WorksheetPart worksheetPart, Drawings.IXLPicture picture, SaveContext context)
        {
            var pic = picture as Drawings.XLPicture;
            var drawingsPart = worksheetPart.DrawingsPart ??
                               worksheetPart.AddNewPart<DrawingsPart>(context.RelIdGenerator.GetNext(RelType.Workbook));

            if (drawingsPart.WorksheetDrawing == null)
                drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();

            var worksheetDrawing = drawingsPart.WorksheetDrawing;

            // Add namespaces
            if (!worksheetDrawing.NamespaceDeclarations.Any(nd => nd.Value.Equals("http://schemas.openxmlformats.org/drawingml/2006/main")))
                worksheetDrawing.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

            if (!worksheetDrawing.NamespaceDeclarations.Any(nd => nd.Value.Equals("http://schemas.openxmlformats.org/officeDocument/2006/relationships")))
                worksheetDrawing.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            /////////

            // Overwrite actual image binary data
            ImagePart imagePart;
            if (drawingsPart.HasPartWithId(pic.RelId))
                imagePart = drawingsPart.GetPartById(pic.RelId) as ImagePart;
            else
            {
                pic.RelId = context.RelIdGenerator.GetNext(RelType.Workbook);
                imagePart = drawingsPart.AddImagePart(pic.Format.ToOpenXml(), pic.RelId);
            }

            using (var stream = new MemoryStream())
            {
                pic.ImageStream.Position = 0;
                pic.ImageStream.CopyTo(stream);
                stream.Seek(0, SeekOrigin.Begin);
                imagePart.FeedData(stream);
            }
            /////////

            // Clear current anchors
            var existingAnchor = GetAnchorFromImageId(drawingsPart, pic.RelId);

            var wb = pic.Worksheet.Workbook;
            var extentsCx = ConvertToEnglishMetricUnits(pic.Width, wb.DpiX);
            var extentsCy = ConvertToEnglishMetricUnits(pic.Height, wb.DpiY);

            var nvps = worksheetDrawing.Descendants<Xdr.NonVisualDrawingProperties>();
            var nvpId = nvps.Any() ?
                (UInt32Value)worksheetDrawing.Descendants<Xdr.NonVisualDrawingProperties>().Max(p => p.Id.Value) + 1 :
                1U;

            Xdr.FromMarker fMark;
            Xdr.ToMarker tMark;
            switch (pic.Placement)
            {
                case Drawings.XLPicturePlacement.FreeFloating:
                    var absoluteAnchor = new Xdr.AbsoluteAnchor(
                        new Xdr.Position
                        {
                            X = ConvertToEnglishMetricUnits(pic.Left, wb.DpiX),
                            Y = ConvertToEnglishMetricUnits(pic.Top, wb.DpiY)
                        },
                        new Xdr.Extent
                        {
                            Cx = extentsCx,
                            Cy = extentsCy
                        },
                        new Xdr.Picture(
                            new Xdr.NonVisualPictureProperties(
                                    new Xdr.NonVisualDrawingProperties { Id = nvpId, Name = pic.Name },
                                    new Xdr.NonVisualPictureDrawingProperties(new PictureLocks { NoChangeAspect = true })
                            ),
                            new Xdr.BlipFill(
                                new Blip { Embed = drawingsPart.GetIdOfPart(imagePart), CompressionState = BlipCompressionValues.Print },
                                new Stretch(new FillRectangle())
                            ),
                            new Xdr.ShapeProperties(
                                new Transform2D(
                                    new Offset { X = 0, Y = 0 },
                                    new Extents { Cx = extentsCx, Cy = extentsCy }
                                ),
                                new PresetGeometry { Preset = ShapeTypeValues.Rectangle }
                            )
                        ),
                        new Xdr.ClientData()
                    );

                    AttachAnchor(absoluteAnchor, existingAnchor);
                    break;

                case Drawings.XLPicturePlacement.MoveAndSize:
                    var moveAndSizeFromMarker = pic.Markers[Drawings.XLMarkerPosition.TopLeft];
                    if (moveAndSizeFromMarker == null) moveAndSizeFromMarker = new Drawings.XLMarker(picture.Worksheet.Cell("A1"));
                    fMark = new Xdr.FromMarker
                    {
                        ColumnId = new Xdr.ColumnId((moveAndSizeFromMarker.ColumnNumber - 1).ToInvariantString()),
                        RowId = new Xdr.RowId((moveAndSizeFromMarker.RowNumber - 1).ToInvariantString()),
                        ColumnOffset = new Xdr.ColumnOffset(ConvertToEnglishMetricUnits(moveAndSizeFromMarker.Offset.X, wb.DpiX).ToInvariantString()),
                        RowOffset = new Xdr.RowOffset(ConvertToEnglishMetricUnits(moveAndSizeFromMarker.Offset.Y, wb.DpiY).ToInvariantString())
                    };

                    var moveAndSizeToMarker = pic.Markers[Drawings.XLMarkerPosition.BottomRight];
                    if (moveAndSizeToMarker == null) moveAndSizeToMarker = new Drawings.XLMarker(picture.Worksheet.Cell("A1"), new SixLabors.ImageSharp.Point(picture.Width, picture.Height));
                    tMark = new Xdr.ToMarker
                    {
                        ColumnId = new Xdr.ColumnId((moveAndSizeToMarker.ColumnNumber - 1).ToInvariantString()),
                        RowId = new Xdr.RowId((moveAndSizeToMarker.RowNumber - 1).ToInvariantString()),
                        ColumnOffset = new Xdr.ColumnOffset(ConvertToEnglishMetricUnits(moveAndSizeToMarker.Offset.X, wb.DpiX).ToInvariantString()),
                        RowOffset = new Xdr.RowOffset(ConvertToEnglishMetricUnits(moveAndSizeToMarker.Offset.Y, wb.DpiY).ToInvariantString())
                    };

                    var twoCellAnchor = new Xdr.TwoCellAnchor(
                        fMark,
                        tMark,
                        new Xdr.Picture(
                            new Xdr.NonVisualPictureProperties(
                                new Xdr.NonVisualDrawingProperties { Id = nvpId, Name = pic.Name },
                                new Xdr.NonVisualPictureDrawingProperties(new PictureLocks { NoChangeAspect = true })
                            ),
                            new Xdr.BlipFill(
                                new Blip { Embed = drawingsPart.GetIdOfPart(imagePart), CompressionState = BlipCompressionValues.Print },
                                new Stretch(new FillRectangle())
                            ),
                            new Xdr.ShapeProperties(
                                new Transform2D(
                                    new Offset { X = 0, Y = 0 },
                                    new Extents { Cx = extentsCx, Cy = extentsCy }
                                ),
                                new PresetGeometry { Preset = ShapeTypeValues.Rectangle }
                            )
                        ),
                        new Xdr.ClientData()
                    );

                    AttachAnchor(twoCellAnchor, existingAnchor);
                    break;

                case Drawings.XLPicturePlacement.Move:
                    var moveFromMarker = pic.Markers[Drawings.XLMarkerPosition.TopLeft];
                    if (moveFromMarker == null) moveFromMarker = new Drawings.XLMarker(picture.Worksheet.Cell("A1"));
                    fMark = new Xdr.FromMarker
                    {
                        ColumnId = new Xdr.ColumnId((moveFromMarker.ColumnNumber - 1).ToInvariantString()),
                        RowId = new Xdr.RowId((moveFromMarker.RowNumber - 1).ToInvariantString()),
                        ColumnOffset = new Xdr.ColumnOffset(ConvertToEnglishMetricUnits(moveFromMarker.Offset.X, wb.DpiX).ToInvariantString()),
                        RowOffset = new Xdr.RowOffset(ConvertToEnglishMetricUnits(moveFromMarker.Offset.Y, wb.DpiY).ToInvariantString())
                    };

                    var oneCellAnchor = new Xdr.OneCellAnchor(
                        fMark,
                        new Xdr.Extent
                        {
                            Cx = extentsCx,
                            Cy = extentsCy
                        },
                        new Xdr.Picture(
                            new Xdr.NonVisualPictureProperties(
                                new Xdr.NonVisualDrawingProperties { Id = nvpId, Name = pic.Name },
                                new Xdr.NonVisualPictureDrawingProperties(new PictureLocks { NoChangeAspect = true })
                            ),
                            new Xdr.BlipFill(
                                new Blip { Embed = drawingsPart.GetIdOfPart(imagePart), CompressionState = BlipCompressionValues.Print },
                                new Stretch(new FillRectangle())
                            ),
                            new Xdr.ShapeProperties(
                                new Transform2D(
                                    new Offset { X = 0, Y = 0 },
                                    new Extents { Cx = extentsCx, Cy = extentsCy }
                                ),
                                new PresetGeometry { Preset = ShapeTypeValues.Rectangle }
                            )
                        ),
                        new Xdr.ClientData()
                    );

                    AttachAnchor(oneCellAnchor, existingAnchor);
                    break;
            }

            void AttachAnchor(OpenXmlElement pictureAnchor, OpenXmlElement existingAnchor)
            {
                if (existingAnchor is not null)
                {
                    worksheetDrawing.ReplaceChild(pictureAnchor, existingAnchor);
                }
                else
                {
                    worksheetDrawing.Append(pictureAnchor);
                }
            }
        }

        private static void RebaseNonVisualDrawingPropertiesIds(WorksheetPart worksheetPart)
        {
            var worksheetDrawing = worksheetPart.DrawingsPart.WorksheetDrawing;

            var toRebase = worksheetDrawing.Descendants<Xdr.NonVisualDrawingProperties>()
                .ToList();

            toRebase.ForEach(nvdpr => nvdpr.Id = Convert.ToUInt32(toRebase.IndexOf(nvdpr) + 1));
        }

        private static void PopulateTablePartReferences(XLTables xlTables, Worksheet worksheet, XLWorksheetContentManager cm)
        {
            var emptyTable = xlTables.FirstOrDefault(t => t.DataRange == null);
            if (emptyTable != null)
                throw new EmptyTableException($"Table '{emptyTable.Name}' should have at least 1 row.");

            TableParts tableParts;
            if (worksheet.Elements<TableParts>().Any())
            {
                tableParts = worksheet.Elements<TableParts>().First();
            }
            else
            {
                var previousElement = cm.GetPreviousElementFor(XLWorksheetContents.TableParts);
                tableParts = new TableParts();
                worksheet.InsertAfter(tableParts, previousElement);
            }
            cm.SetElement(XLWorksheetContents.TableParts, tableParts);

            xlTables.Deleted.Clear();
            tableParts.RemoveAllChildren();
            foreach (var xlTable in xlTables.Cast<XLTable>())
            {
                tableParts.AppendChild(new TablePart { Id = xlTable.RelId });
            }

            tableParts.Count = (UInt32)xlTables.Count();
        }

        /// <summary>
        /// Stream detached worksheet DOM to the worksheet part stream.
        /// Replaces the content of the part.
        /// </summary>
        private static void StreamToPart(Worksheet worksheet, WorksheetPart worksheetPart, XLWorksheet xlWorksheet, SaveContext context, SaveOptions options)
        {
            // Worksheet part might have some data, but the writer truncates everything upon creation.
            using var writer = OpenXmlWriter.Create(worksheetPart);
            using var reader = OpenXmlReader.Create(worksheet);

            writer.WriteStartDocument(true);

            while (reader.Read())
            {
                if (reader.ElementType == typeof(SheetData))
                {
                    StreamSheetData(writer, xlWorksheet, context, options);

                    // Skip whole SheetData elements from original file, already written
                    reader.Skip();
                }

                if (reader.IsStartElement)
                {
                    writer.WriteStartElement(reader);
                    var canContainText = typeof(OpenXmlLeafTextElement).IsAssignableFrom(reader.ElementType);
                    if (canContainText)
                    {
                        var text = reader.GetText();
                        if (text.Length > 0)
                        {
                            writer.WriteString(text);
                        }
                    }
                }
                else if (reader.IsEndElement)
                {
                    writer.WriteEndElement();
                }
            }
            writer.Close();
        }

        private static void StreamSheetData(OpenXmlWriter writer, XLWorksheet xlWorksheet, SaveContext context, SaveOptions options)
        {
            // Steal through reflection for now, whole OpenXmlPartWriter will be replaced by XmlWriter soon. OpenXmlPartWriter has basically
            // no inner state, unless it is in a string leaf node. By writing SheetData through XmlWriter only, we bypass all that.
            var xmlWriterFieldInfo = typeof(OpenXmlPartWriter).GetField("_xmlWriter", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var untypedXmlWriter = xmlWriterFieldInfo.GetValue(writer);
            var xml = (XmlWriter)untypedXmlWriter;

            var maxColumn = GetMaxColumn(xlWorksheet);

            xml.WriteStartElement("sheetData", Main2006SsNs);

            var tableTotalCells = new HashSet<IXLAddress>(
                xlWorksheet.Tables
                .Where(table => table.ShowTotalsRow)
                .SelectMany(table =>
                    table.TotalsRow().CellsUsed())
                .Select(cell => cell.Address));

            // Must be enough to hold span and rowNumber as strings
            var distinctRows = xlWorksheet.Internals.CellsCollection.RowsCollection.Keys.Union(xlWorksheet.Internals.RowsCollection.Keys);
            foreach (var rowNumber in distinctRows.OrderBy(r => r))
            {
                xlWorksheet.Internals.RowsCollection.TryGetValue(rowNumber, out XLRow xlRow);

                var cellRef = new char[10];
                var cellWritten = false;
                if (xlWorksheet.Internals.CellsCollection.RowsCollection.TryGetValue(rowNumber, out Dictionary<int, XLCell> cells))
                {
                    foreach (var xlCell in cells.Values.OrderBy(c => c.Address.ColumnNumber))
                    {
                        var styleId = context.SharedStyles[xlCell.StyleValue].StyleId;

                        // For saving cells to file, ignore conditional formatting, data validation rules and merged
                        // ranges. They just bloat the file
                        var isEmpty = xlCell.IsEmpty(XLCellsUsedOptions.All
                                                     & ~XLCellsUsedOptions.ConditionalFormats
                                                     & ~XLCellsUsedOptions.DataValidation
                                                     & ~XLCellsUsedOptions.MergedRanges);

                        if (isEmpty)
                            continue;

                        if (!cellWritten)
                        {
                            WriteStartRow(xml, xlRow, rowNumber, maxColumn, context);
                            cellWritten = true;
                        }

                        Span<Char> cellRefSpan = cellRef;
                        var cellRefLen = xlCell.SheetPoint.Format(cellRefSpan);

                        if (xlCell.HasFormula)
                        {
                            String dataType = null;
                            if (options.EvaluateFormulasBeforeSaving)
                            {
                                try
                                {
                                    xlCell.Evaluate(false);
                                    dataType = FormulaDataType[(int)xlCell.DataType];
                                }
                                catch
                                {
                                    // Do nothing, cell will be left blank. Unimplemented features or functions would stop trying to save a file.
                                }
                            }

                            WriteStartCell(xml, xlCell, cellRef, cellRefLen, dataType, styleId);

                            var xlFormula = xlCell.Formula;
                            if (xlFormula.Type == FormulaType.DataTable)
                            {
                                // Data table doesn't write actual text of formula, that is referenced by context
                                xml.WriteStartElement("f", Main2006SsNs);
                                xml.WriteAttributeString("t", "dataTable");
                                xml.WriteAttributeString("ref", xlFormula.Range.ToString());

                                var is2D = xlFormula.Is2DDataTable;
                                if (is2D)
                                    xml.WriteAttributeString("dt2D", TrueValue);

                                var isDataRowTable = xlFormula.IsRowDataTable;
                                if (isDataRowTable)
                                    xml.WriteAttributeString("dtr", TrueValue);

                                xml.WriteAttributeString("r1", xlFormula.Input1.ToString());
                                var input1Deleted = xlFormula.Input1Deleted;
                                if (input1Deleted)
                                    xml.WriteAttributeString("del1", TrueValue);

                                if (is2D)
                                    xml.WriteAttributeString("r2", xlFormula.Input2.ToString());

                                var input2Deleted = xlFormula.Input2Deleted;
                                if (input2Deleted)
                                    xml.WriteAttributeString("del2", TrueValue);

                                // Excel doesn't recalculate table formula on load or on click of a button or any kind of forced recalculation.
                                // It is necessary to mark some precedent formula dirty (e.g. edit cell formula and enter in Excel).
                                // By setting the CalculateCell, we ensure that Excel will calculate values of data table formula on load and
                                // user will see correct values.
                                xml.WriteAttributeString("ca", TrueValue);

                                xml.WriteEndElement(); // f
                            }
                            else if (xlCell.HasArrayFormula)
                            {
                                var isMasterCell = xlCell.Formula.Range.FirstPoint == xlCell.SheetPoint;
                                if (isMasterCell)
                                {
                                    xml.WriteStartElement("f", Main2006SsNs);
                                    xml.WriteAttributeString("t", "array");
                                    xml.WriteAttributeString("ref", xlCell.FormulaReference.ToStringRelative());
                                    xml.WriteString(xlCell.FormulaA1);
                                    xml.WriteEndElement(); // f
                                }
                            }
                            else
                            {
                                xml.WriteStartElement("f", Main2006SsNs);
                                xml.WriteString(xlCell.FormulaA1);
                                xml.WriteEndElement(); // f
                            }

                            if (options.EvaluateFormulasBeforeSaving && xlCell.CachedValue.Type != XLDataType.Blank && !xlCell.NeedsRecalculation)
                            {
                                WriteCellValue(xml, xlCell, context);
                            }
                        }
                        else if (tableTotalCells.Contains(xlCell.Address))
                        {
                            var table = xlWorksheet.Tables.First(t => t.AsRange().Contains(xlCell));
                            var field = table.Fields.First(f => f.Column.ColumnNumber() == xlCell.Address.ColumnNumber) as XLTableField;

                            // If this is a cell in the totals row that contains a label (xor with function), write label
                            // Only label can be written. Total functions are basically formulas that use structured
                            // references and SR are not yet supported, so not yet possible to calculate total values.
                            if (!String.IsNullOrWhiteSpace(field.TotalsRowLabel))
                            {
                                WriteStartCell(xml, xlCell, cellRef, cellRefLen, "s", styleId);

                                xml.WriteStartElement("v", Main2006SsNs);
                                xml.WriteValue(xlCell.SharedStringId);
                                xml.WriteEndElement();
                            }
                        }
                        else
                        {
                            // Cell contains only a value
                            var dataType = GetCellValueType(xlCell);
                            WriteStartCell(xml, xlCell, cellRef, cellRefLen, dataType, styleId);

                            WriteCellValue(xml, xlCell, context);
                        }

                        xml.WriteEndElement(); // cell
                    }
                }

                if (cellWritten)
                {
                    xml.WriteEndElement(); // row
                }
                else if (xlRow is not null)
                {
                    var rowHasCustomProperties =
                        xlRow.HeightChanged ||
                        xlRow.IsHidden ||
                        xlRow.StyleValue != xlWorksheet.StyleValue ||
                        xlRow.Collapsed ||
                        xlRow.OutlineLevel > 0;

                    if (rowHasCustomProperties)
                    {
                        WriteStartRow(xml, xlRow, rowNumber, maxColumn, context);
                        xml.WriteEndElement(); // row
                    }
                }
            }

            xml.WriteEndElement(); // SheetData

            static void WriteStartRow(XmlWriter w, XLRow xlRow, int rowNumber, int maxColumn, SaveContext context)
            {
                w.WriteStartElement("row", Main2006SsNs);

                w.WriteStartAttribute("r");
                w.WriteValue(rowNumber);
                w.WriteEndAttribute();

                if (maxColumn > 0)
                {
                    w.WriteStartAttribute("spans");
                    w.WriteString("1:");
                    w.WriteValue(maxColumn);
                    w.WriteEndAttribute();
                }

                if (xlRow is null)
                    return;

                if (xlRow.HeightChanged)
                {
                    var height = xlRow.Height.SaveRound();
                    w.WriteStartAttribute("ht");
                    w.WriteNumberValue(height);
                    w.WriteEndAttribute();

                    // Note that dyDescent automatically implies custom height
                    w.WriteAttributeString("customHeight", TrueValue);
                }

                if (xlRow.IsHidden)
                {
                    w.WriteAttributeString("hidden", TrueValue);
                }

                if (xlRow.StyleValue != xlRow.Worksheet.StyleValue)
                {
                    var styleIndex = context.SharedStyles[xlRow.StyleValue].StyleId;
                    w.WriteAttribute("s", styleIndex);
                    w.WriteAttributeString("customFormat", TrueValue);
                }

                if (xlRow.Collapsed)
                {
                    w.WriteAttributeString("collapsed", TrueValue);
                }

                if (xlRow.OutlineLevel > 0)
                {
                    w.WriteAttribute("outlineLevel", xlRow.OutlineLevel);
                }

                if (xlRow.ShowPhonetic)
                {
                    w.WriteAttributeString("ph", TrueValue);
                }

                if (xlRow.DyDescent is not null)
                {
                    w.WriteAttribute("dyDescent", X14Ac2009SsNs, xlRow.DyDescent.Value);
                }

                // thickBot and thickTop attributes are not written, because Excel seems to determine adjustments
                // from cell borders on its own and it would be rather costly to check each cell in each row.
                // If row was adjusted when cell had it's border modified, then it would be fine to write
                // the thickBot/thickBot attributes.
            }

            static void WriteStartCell(XmlWriter w, XLCell xlCell, Char[] reference, int referenceLength, String dataType, UInt32 styleId)
            {
                w.WriteStartElement("c", Main2006SsNs);

                w.WriteStartAttribute("r");
                w.WriteRaw(reference, 0, referenceLength);
                w.WriteEndAttribute();

                // TODO: if (styleId != 0) Test files have style even for 0, fix later
                w.WriteAttribute("s", styleId);

                if (dataType is not null)
                    w.WriteAttributeString("t", dataType);

                if (xlCell.ShowPhonetic)
                    w.WriteAttributeString("ph", TrueValue);

                if (xlCell.CellMetaIndex is not null)
                    w.WriteAttribute("cm", xlCell.CellMetaIndex.Value);

                if (xlCell.ValueMetaIndex is not null)
                    w.WriteAttribute("vm", xlCell.ValueMetaIndex.Value);
            }
        }

        /// <summary>
        /// An array to convert data type for a formula cell. Key is <see cref="XLDataType"/>.
        /// It saves some performance through direct indexation instead of switch.
        /// </summary>
        private static readonly String[] FormulaDataType =
        {
            null, // blank
            "b", // boolean
            null, // number, default value, no need to save type
            "str", // text, formula can only save this type, no inline or shared string
            "e", // error
            null, // datetime, saved as serialized date-time
            null // timespan, saved as serialized date-time
        };

        /// <summary>
        /// An array to convert data type for a cell that only contains a value. Key is <see cref="XLDataType"/>.
        /// It saves some performance through direct indexation instead of switch.
        /// </summary>
        private static readonly String[] ValueDataType =
        {
            null, // blank
            "b", // boolean
            null, // number, default value, no need to save type
            "s", // text, the default is a shared string, but there also can be inline string depending on ShareString property
            "e", // error
            null, // datetime, saved as serialized date-time
            null // timespan, saved as serialized date-time
        };

        private static String GetCellValueType(XLCell xlCell)
        {
            var dataType = xlCell.DataType;
            if (dataType == XLDataType.Text && !xlCell.ShareString)
                return "inlineStr";
            return ValueDataType[(int)dataType];
        }

        private static Int32 GetMaxColumn(XLWorksheet xlWorksheet)
        {
            var maxColumn = 0;

            if (xlWorksheet.Internals.CellsCollection.Count > 0)
            {
                maxColumn = xlWorksheet.Internals.CellsCollection.MaxColumnUsed;
            }

            if (xlWorksheet.Internals.ColumnsCollection.Count > 0)
            {
                var maxColCollection = xlWorksheet.Internals.ColumnsCollection.Keys.Max();
                if (maxColCollection > maxColumn)
                    maxColumn = maxColCollection;
            }

            return maxColumn;
        }
    }
}
