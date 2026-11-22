using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MarblesECS.Editor
{
    internal static class XlsxCells
    {
        internal static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        internal static string[] SharedStrings(XDocument document)
        {
            if (document == null) return new string[0];
            return document.Descendants(Main + "si").Select(Text).ToArray();
        }

        internal static List<string[]> Rows(XDocument document, string[] strings, string sheet)
        {
            var result = new List<string[]>();
            foreach (var row in document.Descendants(Main + "sheetData").Elements(Main + "row"))
            {
                if (result.Count >= 4096) throw new InvalidDataException(sheet + ": too many rows.");
                var cells = new SortedDictionary<int, string>();
                foreach (var cell in row.Elements(Main + "c"))
                {
                    int column = Column((string)cell.Attribute("r"));
                    if (cells.ContainsKey(column)) throw new InvalidDataException(sheet + ": duplicate cell.");
                    cells.Add(column, Value(cell, strings, sheet));
                }
                if (cells.Count == 0 || cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                var values = new string[cells.Keys.Last() + 1];
                foreach (var cell in cells) values[cell.Key] = cell.Value;
                result.Add(values);
            }
            return result;
        }

        private static string Value(XElement cell, string[] strings, string sheet)
        {
            if (cell.Element(Main + "f") != null)
                throw new InvalidDataException(sheet + "!" + cell.Attribute("r") + ": formulas are not supported; paste values.");
            string type = (string)cell.Attribute("t");
            string value = (string)cell.Element(Main + "v") ?? "";
            if (type == "inlineStr") return Text(cell.Element(Main + "is"));
            if (type == "e") throw new InvalidDataException(sheet + ": Excel error " + value);
            if (type != "s") return value;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= strings.Length)
                throw new InvalidDataException(sheet + ": invalid shared string index.");
            return strings[index];
        }

        private static string Text(XElement element)
        {
            return element == null ? "" : string.Concat(element.Descendants(Main + "t").Select(x => x.Value));
        }

        private static int Column(string reference)
        {
            if (string.IsNullOrEmpty(reference)) throw new InvalidDataException("Cell address is missing.");
            int column = 0;
            foreach (char c in reference)
            {
                if (c < 'A' || c > 'Z') break;
                column = column * 26 + c - 'A' + 1;
                if (column > 128) throw new InvalidDataException("Balance sheets support at most 128 columns.");
            }
            if (column == 0) throw new InvalidDataException("Invalid cell address: " + reference);
            return column - 1;
        }
    }
}
