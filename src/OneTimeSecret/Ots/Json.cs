using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// Minimal JSON reader. No third-party dependency ships in this plugin, and
    /// DataContractJsonSerializer leaves missing fields silently null, which is exactly
    /// the failure mode we do not want when reading a share response.
    /// </summary>
    public static class Json
    {
        /// <summary>Parses into Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null.</summary>
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("Empty response body.");

            int i = 0;
            SkipWhitespace(text, ref i);
            if (i >= text.Length) throw new JsonException("Empty response body.");

            object value = ParseValue(text, ref i);

            SkipWhitespace(text, ref i);
            if (i < text.Length)
                throw new JsonException("Unexpected trailing content in JSON at position " + i + ".");

            return value;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var obj = Parse(text) as Dictionary<string, object>;
            if (obj == null) throw new JsonException("Expected a JSON object at the top level.");
            return obj;
        }

        /// <summary>Reads a required string field. Throws if absent, null, or not a string.</summary>
        public static string GetString(Dictionary<string, object> obj, string key)
        {
            if (obj == null) throw new ArgumentNullException("obj");

            object value;
            if (!obj.TryGetValue(key, out value) || value == null)
                throw new JsonException("Response is missing the expected field '" + key + "'.");

            var s = value as string;
            if (s == null)
                throw new JsonException("Response field '" + key + "' is not a string.");

            return s;
        }

        /// <summary>Reads an optional string field. Returns null when absent, null, or not a string.</summary>
        public static string GetStringOrNull(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;

            object value;
            if (!obj.TryGetValue(key, out value)) return null;
            return value as string;
        }

        /// <summary>Reads an optional nested object. Returns null when absent or not an object.</summary>
        public static Dictionary<string, object> GetObjectOrNull(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;

            object value;
            if (!obj.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }

        /// <summary>Reads an optional numeric field. Returns null when absent or not a number.</summary>
        public static int? GetIntOrNull(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;

            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return null;

            if (value is double)
            {
                double d = (double)value;
                if (d < int.MinValue || d > int.MaxValue) return null;
                return (int)d;
            }

            // Some deployments return numbers as strings; accept that rather than losing the value.
            var s = value as string;
            int parsed;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return null;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new JsonException("Unexpected end of JSON.");

            char c = s[i];
            if (c == '{') return ParseObjectValue(s, ref i);
            if (c == '[') return ParseArrayValue(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (Literal(s, ref i, "true")) return true;
            if (Literal(s, ref i, "false")) return false;
            if (Literal(s, ref i, "null")) return null;
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObjectValue(string s, ref int i)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}') { i++; return result; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new JsonException("Expected a property name in JSON at position " + i + ".");

                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new JsonException("Expected ':' in JSON at position " + i + ".");
                i++;

                result[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonException("Unexpected end of JSON object.");

                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }

                throw new JsonException("Expected ',' or '}' in JSON at position " + i + ".");
            }
        }

        private static List<object> ParseArrayValue(string s, ref int i)
        {
            var result = new List<object>();
            i++; // '['
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']') { i++; return result; }

            while (true)
            {
                result.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonException("Unexpected end of JSON array.");

                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }

                throw new JsonException("Expected ',' or ']' in JSON at position " + i + ".");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();

            while (true)
            {
                if (i >= s.Length) throw new JsonException("Unterminated string in JSON.");

                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }

                if (c != '\\') { sb.Append(c); i++; continue; }

                i++;
                if (i >= s.Length) throw new JsonException("Unterminated escape in JSON.");

                char esc = s[i];
                i++;
                switch (esc)
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
                        if (i + 4 > s.Length) throw new JsonException("Truncated \\u escape in JSON.");
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            throw new JsonException("Invalid \\u escape in JSON.");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new JsonException("Unsupported escape '\\" + esc + "' in JSON.");
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' ||
                   s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;

            if (i == start) throw new JsonException("Expected a JSON value at position " + start + ".");

            double value;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
                throw new JsonException("Invalid number in JSON at position " + start + ".");

            return value;
        }

        private static bool Literal(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length) return false;
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0) return false;
            i += literal.Length;
            return true;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
