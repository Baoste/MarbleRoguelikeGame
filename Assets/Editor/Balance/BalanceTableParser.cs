using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MarblesECS.Editor
{
    internal static class BalanceTableParser
    {
        internal static T Singleton<T>(List<string[]> rows, string sheet) where T : new()
        {
            if (rows.Count == 0 || rows[0].Length < 2 || rows[0][0] != "Field" || rows[0][1] != "Value")
                throw new InvalidDataException(sheet + ": first columns must be Field and Value.");
            var fields = Fields<T>();
            var seen = new HashSet<string>();
            var result = new T();
            for (int index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                string key = row[0];
                if (key == null || !fields.TryGetValue(key, out var field) || !seen.Add(key))
                    throw new InvalidDataException(sheet + ": unknown or repeated field '" + key + "'.");
                string value = row.Length > 1 ? row[1] : null;
                field.SetValue(result, BalanceValueParser.Parse(value, field.FieldType, sheet + "." + key));
            }
            RequireAll(fields, seen, sheet);
            return result;
        }

        internal static T[] Table<T>(List<string[]> rows, string sheet) where T : new()
        {
            if (rows.Count < 2) throw new InvalidDataException(sheet + ": header and data are required.");
            var fields = Fields<T>();
            var seen = new HashSet<string>();
            var columns = new FieldInfo[rows[0].Length];
            for (int column = 0; column < columns.Length; column++)
            {
                string key = rows[0][column];
                if (key == null || !fields.TryGetValue(key, out columns[column]) || !seen.Add(key))
                    throw new InvalidDataException(sheet + ": unknown or repeated column '" + key + "'.");
            }
            RequireAll(fields, seen, sheet);
            var output = new T[rows.Count - 1];
            for (int index = 1; index < rows.Count; index++) output[index - 1] = ReadRow<T>(rows[index], columns, sheet + " row " + (index + 1));
            return output;
        }

        private static T ReadRow<T>(string[] row, FieldInfo[] columns, string address) where T : new()
        {
            if (row.Length > columns.Length && row.Skip(columns.Length).Any(x => !string.IsNullOrWhiteSpace(x)))
                throw new InvalidDataException(address + ": unexpected extra cell.");
            var result = new T();
            for (int index = 0; index < columns.Length; index++)
            {
                var field = columns[index];
                field.SetValue(result, BalanceValueParser.Parse(index < row.Length ? row[index] : null, field.FieldType, address + "." + field.Name));
            }
            return result;
        }

        private static Dictionary<string, FieldInfo> Fields<T>()
        {
            return typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => !Attribute.IsDefined(x, typeof(NonSerializedAttribute))).ToDictionary(x => x.Name);
        }

        private static void RequireAll(Dictionary<string, FieldInfo> fields, HashSet<string> seen, string sheet)
        {
            var missing = fields.Keys.Where(x => !seen.Contains(x) &&
                !Attribute.IsDefined(fields[x], typeof(OptionalBalanceFieldAttribute))).ToArray();
            if (missing.Length > 0) throw new InvalidDataException(sheet + ": missing fields " + string.Join(", ", missing));
        }
    }
}
