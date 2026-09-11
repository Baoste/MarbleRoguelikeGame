using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace MarblesECS.Editor
{
    internal sealed class XlsxArchive : IDisposable
    {
        private readonly FileStream stream;
        private readonly ZipArchive archive;

        internal XlsxArchive(string path)
        {
            if (new FileInfo(path).Length > 10 * 1024 * 1024)
                throw new InvalidDataException("Balance workbook exceeds 10 MB.");
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try { archive = new ZipArchive(stream, ZipArchiveMode.Read); }
            catch { stream.Dispose(); throw; }
        }

        internal XDocument Read(string part, bool optional = false)
        {
            var entry = archive.GetEntry(part);
            if (entry == null)
            {
                if (optional) return null;
                throw new InvalidDataException("Missing workbook part: " + part);
            }
            if (entry.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Workbook XML part is too large: " + part);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            };
            using (var input = entry.Open())
            using (var reader = XmlReader.Create(input, settings)) return XDocument.Load(reader);
        }

        public void Dispose() { archive.Dispose(); stream.Dispose(); }
    }
}
