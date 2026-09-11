using System;
using System.IO;
using System.Text;

namespace MarblesECS.Editor
{
    internal static class BalanceAssetWriter
    {
        internal static void Write(string path, string contents)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string token = Guid.NewGuid().ToString("N");
            string temporary = fullPath + "." + token + ".tmp";
            string backup = fullPath + "." + token + ".bak";
            bool committed = false;
            try
            {
                File.WriteAllText(temporary, contents, new UTF8Encoding(false));
                // Same-directory renames preserve the old file until publication is ready.
                // File.Replace may require ACL permissions unavailable in restricted Windows setups.
                if (File.Exists(fullPath)) File.Move(fullPath, backup);
                try { File.Move(temporary, fullPath); committed = true; }
                catch (Exception publishError)
                {
                    try
                    {
                        if (!File.Exists(fullPath) && File.Exists(backup)) File.Move(backup, fullPath);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new IOException("Cannot restore previous JSON; recover backup at " + backup,
                            new AggregateException(publishError, rollbackError));
                    }
                    throw;
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                // A backup remains recoverable if a filesystem error prevented rollback.
                if (committed && File.Exists(backup)) File.Delete(backup);
            }
        }
    }
}
