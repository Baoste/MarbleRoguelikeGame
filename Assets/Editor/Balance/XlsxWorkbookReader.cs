using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MarblesECS.Editor
{
    internal static class XlsxWorkbookReader
    {
        internal static Dictionary<string, List<string[]>> Read(string path, bool skipFormulas = false)
        {
            using (var archive = new XlsxArchive(path))
            {
                var shared = XlsxCells.SharedStrings(archive.Read("xl/sharedStrings.xml", true));
                var relationships = archive.Read("xl/_rels/workbook.xml.rels").Root.Elements()
                    .ToDictionary(x => (string)x.Attribute("Id"));
                var sheets = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
                XNamespace relation = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                foreach (var sheet in archive.Read("xl/workbook.xml").Descendants(XlsxCells.Main + "sheet"))
                {
                    string name = (string)sheet.Attribute("name");
                    string id = (string)sheet.Attribute(relation + "id");
                    if (name == "README") continue;
                    if (sheets.Count >= 16 || string.IsNullOrEmpty(name) || sheets.ContainsKey(name))
                        throw new InvalidDataException("Invalid or duplicate worksheet name.");
                    if (id == null || !relationships.TryGetValue(id, out var link) || (string)link.Attribute("TargetMode") == "External")
                        throw new InvalidDataException("Invalid worksheet relationship: " + name);
                    string part = Resolve((string)link.Attribute("Target"));
                    sheets.Add(name, XlsxCells.Rows(archive.Read(part), shared, name, skipFormulas));
                }
                return sheets;
            }
        }

        private static string Resolve(string target)
        {
            if (string.IsNullOrEmpty(target) || target.Contains("\\") || target.Contains(":"))
                throw new InvalidDataException("Invalid worksheet target.");
            var root = new Uri("https://xlsx.local/xl/workbook.xml");
            var resolved = new Uri(root, target);
            string part = Uri.UnescapeDataString(resolved.AbsolutePath).TrimStart('/');
            if (resolved.Host != root.Host || !part.StartsWith("xl/worksheets/", StringComparison.Ordinal))
                throw new InvalidDataException("Worksheet target is outside xl/worksheets.");
            return part;
        }
    }
}
