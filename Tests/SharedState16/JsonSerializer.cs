using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace WildShiftSharedStateValidation.Json
{
    // A fixture-only serializer for anonymous result objects, primitive values
    // and arrays. Mono/net472 needs no external System.Text.Json assembly.
    public sealed class JsonSerializerOptions { public bool WriteIndented { get; set; } }

    public static class JsonSerializer
    {
        public static string Serialize(object value, JsonSerializerOptions options)
        {
            var output = new StringBuilder();
            Write(output, value, options != null && options.WriteIndented, 0);
            return output.ToString();
        }

        private static void Write(StringBuilder output, object value, bool indent, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("Fixture JSON exceeds its supported nesting depth.");
            if (value == null) { output.Append("null"); return; }
            if (value is string || value is char) { Quote(output, value.ToString()); return; }
            if (value is bool) { output.Append((bool)value ? "true" : "false"); return; }
            Type type = value.GetType();
            if (type.IsPrimitive || value is decimal)
            {
                string number = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (number == "NaN" || number == "Infinity" || number == "-Infinity")
                    throw new InvalidOperationException("Fixture JSON does not accept non-finite numbers.");
                output.Append(number); return;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null)
            {
                output.Append('[');
                bool any = false;
                foreach (object item in sequence)
                {
                    if (any) output.Append(',');
                    Line(output, indent, depth + 1);
                    Write(output, item, indent, depth + 1);
                    any = true;
                }
                if (any) Line(output, indent, depth);
                output.Append(']'); return;
            }
            output.Append('{');
            bool propertyWritten = false;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                if (propertyWritten) output.Append(',');
                Line(output, indent, depth + 1);
                Quote(output, property.Name); output.Append(indent ? ": " : ":");
                Write(output, property.GetValue(value, null), indent, depth + 1);
                propertyWritten = true;
            }
            if (propertyWritten) Line(output, indent, depth);
            output.Append('}');
        }

        private static void Line(StringBuilder output, bool indent, int depth)
        {
            if (indent) { output.Append('\n'); output.Append(' ', depth * 2); }
        }
        private static void Quote(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (c < 0x20 || Char.IsSurrogate(c)) output.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(c);
                        break;
                }
            }
            output.Append('"');
        }
    }
}
