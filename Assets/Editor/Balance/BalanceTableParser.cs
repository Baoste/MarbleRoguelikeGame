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
