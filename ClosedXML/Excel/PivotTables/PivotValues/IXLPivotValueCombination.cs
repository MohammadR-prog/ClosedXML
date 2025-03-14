#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClosedXML.Excel
{
    public interface IXLPivotValueCombination
    {
        IXLPivotValue And(XLCellValue item);
        IXLPivotValue AndPrevious();
        IXLPivotValue AndNext();
    }
}
