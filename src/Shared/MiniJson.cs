using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GalgameMetadata.Shared
{
    /// <summary>
    /// Minimal dependency-free JSON reader. Parses into a tree of JsonValue nodes.
    /// Kept deliberately small: the Playnite plugins must not require any third party DLL.
    /// </summary>
    public sealed class JsonValue
    {
        public enum JsonKind { Null, Bool, Number, String, Array, Object }

        public JsonKind NodeType { get; private set; }
        public bool BoolValue { get; private set; }
        public double NumberValue { get; private set; }
        public string StringValue { get; private set; }
        public List<JsonValue> Array { get; private set; }
        public Dictionary<string, JsonValue> Object { get; private set; }

        public static readonly JsonValue Null = new JsonValue { NodeType = JsonKind.Null };

        private JsonValue() { }

        public bool IsNull { get { return NodeType == JsonKind.Null; } }
        public bool IsObject { get { return NodeType == JsonKind.Object; } }
        public bool IsArray { get { return NodeType == JsonKind.Array; } }
        public bool IsString { get { return NodeType == JsonKind.String; } }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue v;
                if (NodeType == JsonKind.Object && Object != null && key != null && Object.TryGetValue(key, out v)) return v;
                return Null;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (NodeType == JsonKind.Array && Array != null && index >= 0 && index < Array.Count) return Array[index];
                return Null;
            }
        }

        public int Count
        {
            get
            {
                if (NodeType == JsonKind.Array) return Array == null ? 0 : Array.Count;
                if (NodeType == JsonKind.Object) return Object == null ? 0 : Object.Count;
                return 0;
            }
        }

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (NodeType == JsonKind.Array && Array != null) return Array;
                return new JsonValue[0];
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Properties
        {
            get
            {
                if (NodeType == JsonKind.Object && Object != null) return Object;
                return new KeyValuePair<string, JsonValue>[0];
            }
        }

        /// <summary>String form of a scalar; null for arrays/objects/null.</summary>
        public string AsString
        {
            get
            {
                switch (NodeType)
                {
                    case JsonKind.String: return StringValue;
                    case JsonKind.Number: return NumberValue.ToString(CultureInfo.InvariantCulture);
                    case JsonKind.Bool: return BoolValue ? "true" : "false";
                    default: return null;
                }
            }
        }

        public string AsTrimmedString
        {
            get
            {
                var s = AsString;
                if (s == null) return null;
                s = s.Trim();
                return s.Length == 0 ? null : s;
            }
        }

        public double? AsNumber
        {
            get
            {
                if (NodeType == JsonKind.Number) return NumberValue;
                if (NodeType == JsonKind.String)
                {
                    double d;
                    if (double.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                }
                return null;
            }
        }

        public int? AsInt
        {
            get
            {
                var d = AsNumber;
                if (!d.HasValue) return null;
                return (int)Math.Round(d.Value);
            }
        }

        public bool? AsBool
        {
            get
            {
                if (NodeType == JsonKind.Bool) return BoolValue;
                if (NodeType == JsonKind.Number) return NumberValue != 0;
                if (NodeType == JsonKind.String)
                {
                    var s = StringValue.Trim().ToLowerInvariant();
                    if (s == "true" || s == "1" || s == "yes") return true;
                    if (s == "false" || s == "0" || s == "no") return false;
                }
                return null;
            }
        }

        public static JsonValue Parse(string text)
        {
            if (text == null) return Null;
            int pos = 0;
            // Skip UTF-8 BOM and whitespace.
            if (text.Length > 0 && text[0] == '\uFEFF') pos = 1;
            var value = ParseValue(text, ref pos);
            return value ?? Null;
        }

        public static bool TryParse(string text, out JsonValue value)
        {
            try
            {
                value = Parse(text);
                return value != null;
            }
            catch
            {
                value = Null;
                return false;
            }
        }

        private static JsonValue ParseValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) return Null;
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject(s, ref pos);
                case '[': return ParseArray(s, ref pos);
                case '"': return String(ParseString(s, ref pos));
                case 't':
                    Expect(s, ref pos, "true");
                    return new JsonValue { NodeType = JsonKind.Bool, BoolValue = true };
                case 'f':
                    Expect(s, ref pos, "false");
                    return new JsonValue { NodeType = JsonKind.Bool, BoolValue = false };
                case 'n':
                    Expect(s, ref pos, "null");
                    return Null;
                default:
                    return ParseNumber(s, ref pos);
            }
        }

        private static JsonValue ParseObject(string s, ref int pos)
        {
            var obj = new JsonValue { NodeType = JsonKind.Object, Object = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase) };
            pos++; // {
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == '}') { pos++; break; }
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] != '"') { pos++; continue; }
                var key = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == ':') pos++;
                var val = ParseValue(s, ref pos);
                obj.Object[key] = val ?? Null;
            }
            return obj;
        }

        private static JsonValue ParseArray(string s, ref int pos)
        {
            var arr = new JsonValue { NodeType = JsonKind.Array, Array = new List<JsonValue>() };
            pos++; // [
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == ']') { pos++; break; }
                if (s[pos] == ',') { pos++; continue; }
                var val = ParseValue(s, ref pos);
                arr.Array.Add(val ?? Null);
            }
            return arr;
        }

        private static string ParseString(string s, ref int pos)
        {
            var sb = new StringBuilder();
            pos++; // opening quote
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= s.Length) break;
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 3 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                pos += 4;
                            }
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static JsonValue ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0) pos++;
            if (pos == start) { pos++; return Null; } // unexpected char, skip it
            double d;
            if (double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return new JsonValue { NodeType = JsonKind.Number, NumberValue = d };
            return Null;
        }

        private static void Expect(string s, ref int pos, string literal)
        {
            if (pos + literal.Length <= s.Length && string.CompareOrdinal(s, pos, literal, 0, literal.Length) == 0) pos += literal.Length;
            else pos++;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
                else break;
            }
        }

        private static JsonValue String(string v)
        {
            return new JsonValue { NodeType = JsonKind.String, StringValue = v ?? string.Empty };
        }
    }

    /// <summary>Dotted-path helpers over JsonValue, tolerant of missing data.</summary>
    public static class J
    {
        public static JsonValue Get(JsonValue root, string path)
        {
            if (root == null) return JsonValue.Null;
            if (string.IsNullOrEmpty(path)) return root;
            var node = root;
            foreach (var part in path.Split('.'))
            {
                if (part.Length == 0) continue;
                int idx;
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)) node = node[idx];
                else node = node[part];
                if (node == null) return JsonValue.Null;
            }
            return node;
        }

        public static string Str(JsonValue root, string path)
        {
            return Get(root, path).AsTrimmedString;
        }

        public static string Str(JsonValue node)
        {
            return node == null ? null : node.AsTrimmedString;
        }

        public static int? Int(JsonValue root, string path)
        {
            return Get(root, path).AsInt;
        }

        public static double? Num(JsonValue root, string path)
        {
            return Get(root, path).AsNumber;
        }

        public static bool? Bool(JsonValue root, string path)
        {
            return Get(root, path).AsBool;
        }

        public static IEnumerable<JsonValue> Arr(JsonValue root, string path)
        {
            return Get(root, path).Items;
        }

        public static bool Has(JsonValue root, string path)
        {
            var v = Get(root, path);
            return v != null && !v.IsNull;
        }

        /// <summary>Returns the first non-empty string among the given dotted paths.</summary>
        public static string FirstStr(JsonValue root, params string[] paths)
        {
            foreach (var p in paths)
            {
                var v = Str(root, p);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        public static List<string> StrList(JsonValue root, string path)
        {
            var list = new List<string>();
            foreach (var item in Arr(root, path))
            {
                var s = item.AsTrimmedString;
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }

        /// <summary>Collects a string field from every object inside the array at <paramref name="path"/>.</summary>
        public static List<string> StrListOf(JsonValue root, string path, string field)
        {
            var list = new List<string>();
            foreach (var item in Arr(root, path))
            {
                var s = item[field].AsTrimmedString;
                if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
            }
            return list;
        }
    }
}
