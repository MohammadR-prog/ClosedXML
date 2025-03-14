using System;
using System.Collections;
using System.Collections.Generic;

namespace ClosedXML.Excel
{
    using System.Linq;

    internal class XLCells : XLStylizedBase, IXLCells, IXLStylized, IEnumerable<XLCell>
    {
        #region Fields

        private readonly List<XLRangeAddress> _rangeAddresses = new List<XLRangeAddress>();
        private readonly bool _usedCellsOnly;
        private readonly Func<IXLCell, Boolean> _predicate;
        private readonly XLCellsUsedOptions _options;
        private bool _styleInitialized = false;

        #endregion Fields

        #region Constructor

        public XLCells(bool usedCellsOnly, XLCellsUsedOptions options, Func<IXLCell, Boolean>? predicate = null)
            : base(XLStyle.Default.Value)
        {
            _usedCellsOnly = usedCellsOnly;
            _options = options;
            _predicate = predicate ?? (_ => true);
        }

        #endregion Constructor

        #region IEnumerable<XLCell> Members

        private IEnumerable<XLCell> GetAllCells()
        {
            var groupedAddresses = _rangeAddresses.GroupBy(addr => addr.Worksheet);
            foreach (var worksheetGroup in groupedAddresses)
            {
                var ws = worksheetGroup.Key!;
                var sheetPoints = worksheetGroup.SelectMany(addr => GetAllCellsInRange(addr))
                    .Distinct();
                foreach (var sheetPoint in sheetPoints)
                {
                    var c = ws.Cell(sheetPoint.Row, sheetPoint.Column);
                    if (_predicate(c))
                        yield return c;
                }
            }
        }

        private IEnumerable<XLSheetPoint> GetAllCellsInRange(IXLRangeAddress rangeAddress)
        {
            if (!rangeAddress.IsValid)
                yield break;

            var normalizedAddress = ((XLRangeAddress)rangeAddress).Normalize();
            var minRow = normalizedAddress.FirstAddress.RowNumber;
            var maxRow = normalizedAddress.LastAddress.RowNumber;
            var minColumn = normalizedAddress.FirstAddress.ColumnNumber;
            var maxColumn = normalizedAddress.LastAddress.ColumnNumber;

            for (var ro = minRow; ro <= maxRow; ro++)
            {
                for (var co = minColumn; co <= maxColumn; co++)
                {
                    yield return new XLSheetPoint(ro, co);
                }
            }
        }

        private IEnumerable<XLCell> GetUsedCells()
        {
            var visitedCells = new HashSet<XLAddress>();
            var groupedAddresses = _rangeAddresses.GroupBy(addr => addr.Worksheet);
            foreach (var worksheetGroup in groupedAddresses)
            {
                var ws = worksheetGroup.Key!;

                var usedCellsCandidates = GetUsedCellsCandidates(ws);

                var cells = worksheetGroup.SelectMany(addr => GetUsedCellsInRange(addr, ws, usedCellsCandidates))
                    .OrderBy(cell => cell.Address.RowNumber)
                    .ThenBy(cell => cell.Address.ColumnNumber);

                visitedCells.Clear();
                foreach (var cell in cells)
                {
                    if (visitedCells.Add(cell.Address))
                    {
                        yield return cell;
                    }
                }
            }
        }

        private IEnumerable<XLCell> GetUsedCellsInRange(XLRangeAddress rangeAddress, XLWorksheet worksheet, IEnumerable<XLSheetPoint> usedCellsCandidates)
        {
            if (!rangeAddress.IsValid)
                yield break;
            var normalizedAddress = rangeAddress.Normalize();
            var minRow = normalizedAddress.FirstAddress.RowNumber;
            var maxRow = normalizedAddress.LastAddress.RowNumber;
            var minColumn = normalizedAddress.FirstAddress.ColumnNumber;
            var maxColumn = normalizedAddress.LastAddress.ColumnNumber;

            var cellRange = worksheet.Internals.CellsCollection
                .GetCells(minRow, minColumn, maxRow, maxColumn, _predicate);

            foreach (var cell in cellRange)
            {
                if (!cell.IsEmpty(_options) && _predicate(cell))
                    yield return cell;
            }

            foreach (var sheetPoint in usedCellsCandidates)
            {
                if (sheetPoint.Row.Between(minRow, maxRow) &&
                    sheetPoint.Column.Between(minColumn, maxColumn))
                {
                    var cell = worksheet.Cell(sheetPoint.Row, sheetPoint.Column);

                    if (_predicate(cell))
                        yield return cell;
                }
            }
        }

        private IEnumerable<XLSheetPoint> GetUsedCellsCandidates(XLWorksheet worksheet)
        {
            var candidates = Enumerable.Empty<XLSheetPoint>();

            if (_options == XLCellsUsedOptions.AllContents)
            {
                return candidates;
            }

            if (_options.HasFlag(XLCellsUsedOptions.MergedRanges))
                candidates = candidates.Union(
                    worksheet.Internals.MergedRanges.SelectMany(r => GetAllCellsInRange(r.RangeAddress)));

            if (_options.HasFlag(XLCellsUsedOptions.ConditionalFormats))
                candidates = candidates.Union(
                    worksheet.ConditionalFormats.SelectMany(cf => cf.Ranges.SelectMany(r => GetAllCellsInRange(r.RangeAddress))));

            if (_options.HasFlag(XLCellsUsedOptions.DataValidation))
                candidates = candidates.Union(
                        worksheet.DataValidations.SelectMany(dv => dv.Ranges.SelectMany(r => GetAllCellsInRange(r.RangeAddress))));

            return candidates.Distinct();
        }

        public IEnumerator<XLCell> GetEnumerator()
        {
            return GetCells().GetEnumerator();
        }

        private IEnumerable<XLCell> GetCells()
        {
            return _usedCellsOnly ? GetUsedCells() : GetAllCells();
        }

        #endregion IEnumerable<XLCell> Members

        #region IXLCells Members

        IEnumerator<IXLCell> IEnumerable<IXLCell>.GetEnumerator()
        {
            return GetCells().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public XLCellValue Value
        {
            set { this.ForEach<XLCell>(c => c.Value = value); }
        }

        public IXLCells Clear(XLClearOptions clearOptions = XLClearOptions.All)
        {
            this.ForEach<XLCell>(c => c.Clear(clearOptions));
            return this;
        }

        public void DeleteComments()
        {
            this.ForEach<XLCell>(c => c.DeleteComment());
        }

        public void DeleteSparklines()
        {
            this.ForEach<XLCell>(c => c.DeleteSparkline());
        }

        public String FormulaA1
        {
            set { this.ForEach<XLCell>(c => c.FormulaA1 = value); }
        }

        public String FormulaR1C1
        {
            set { this.ForEach<XLCell>(c => c.FormulaR1C1 = value); }
        }

        #endregion IXLCells Members

        #region IXLStylized Members

        public override IEnumerable<IXLStyle> Styles
        {
            get
            {
                yield return Style;
                foreach (XLCell c in this)
                    yield return c.Style;
            }
        }

        protected override IEnumerable<XLStylizedBase> Children
        {
            get
            {
                foreach (XLCell c in this)
                    yield return c;
            }
        }

        public override IXLRanges RangesUsed
        {
            get
            {
                var retVal = new XLRanges();
                this.ForEach<XLCell>(c => retVal.Add(c.AsRange()));
                return retVal;
            }
        }

        #endregion IXLStylized Members

        public void Add(XLRangeAddress rangeAddress)
        {
            _rangeAddresses.Add(rangeAddress);

            if (_styleInitialized)
                return;

            var worksheetStyle = rangeAddress.Worksheet?.Style;
            if (worksheetStyle == null)
                return;

            InnerStyle = worksheetStyle;
            _styleInitialized = true;
        }

        public void Add(XLCell cell)
        {
            Add(new XLRangeAddress(cell.Address, cell.Address));
        }

        public void Select()
        {
            foreach (var cell in this)
                cell.Select();
        }
    }
}
