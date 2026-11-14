using System.Globalization;
using System.Xml.Linq;
using VPrint.Models;

namespace VPrint.Services;

/// <summary>Parses successful Windows PrintService/Operational event 307.</summary>
public static class PrintEventParser
{
    public static PrintJob? TryParse(string xml, long recordId, DateTimeOffset occurredAt)
    {
        try
        {
            var document = XDocument.Parse(xml);
            var payload = document.Descendants().FirstOrDefault(n => n.Name.LocalName == "DocumentPrinted");
            if (payload is null) return null;

            string Value(int number) => payload.Elements().FirstOrDefault(n =>
                n.Name.LocalName.Equals($"Param{number}", StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            var printerName = Value(5);
            if (!printerName.StartsWith(PrinterManager.PrinterPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            // A record's timestamp is part of its key because event log IDs may be recycled after clearing.
            var id = $"{recordId}:{occurredAt.UtcTicks}";
            return new PrintJob(
                id,
                printerName,
                string.IsNullOrWhiteSpace(Value(2)) ? "Untitled document" : Value(2),
                string.IsNullOrWhiteSpace(Value(3)) ? "Unknown" : Value(3),
                ParseInt(Value(1)), ParseInt(Value(8)), ParseLong(Value(7)), occurredAt);
        }
        catch (System.Xml.XmlException) { return null; }
    }

    private static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result) : 0;

    private static long ParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result) : 0;
}
