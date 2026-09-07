// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>The parts a new .xlsx needs before it holds anything.</summary>
internal static class ExcelDefaultParts
{
    internal static XDocument Workbook()
    {
        var root = new XElement(Ns.S + "workbook",
            new XAttribute("xmlns", Ns.S.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XElement(Ns.S + "bookViews",
                new XElement(Ns.S + "workbookView",
                    new XAttribute("activeTab", 0))),
            new XElement(Ns.S + "sheets"),
            // fullCalcOnLoad makes Excel evaluate every formula when the file opens, which is what
            // a generated workbook needs: its formulas have no cached results yet, and without
            // this the cells show as blank until the user forces a recalculation.
            new XElement(Ns.S + "calcPr",
                new XAttribute("calcId", 191029),
                new XAttribute("fullCalcOnLoad", 1)));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>
    /// The smallest valid worksheet part.
    /// </summary>
    /// <remarks>
    /// Written as bytes rather than as an <see cref="XDocument"/> because a sheet is replaced
    /// wholesale on every save; parsing this only to discard it would be pure waste.
    /// </remarks>
    internal static byte[] EmptySheet()
    {
        const string Xml =
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1"/><sheetViews><sheetView workbookViewId="0"/></sheetViews><sheetFormatPr defaultRowHeight="15"/><sheetData/><pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/></worksheet>
            """;

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(Xml);
    }
}
