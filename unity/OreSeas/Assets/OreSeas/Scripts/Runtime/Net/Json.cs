using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OreSeas.Net
{
    /// <summary>
    /// Tiny dependency-free JSON for the NUGSHIP protocol (JsonUtility can't do polymorphic "t" messages or int[] in dicts).
    /// Parse -> Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null. Serialize accepts the same
    /// plus int/long/float, IDictionary, IEnumerable. Big integers (lamports) stay strings, as the server sends them.
    /// </summary>
    public static class Json
    {
        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0; var v = Value(s, ref i); return v;
        }

        public static Dictionary<string, object> ParseObject(string s) => Parse(s) as Dictionary<string, object>;

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("json: unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return Num(s, ref i);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>(); i++;
            Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("json: expected ':'");
                i++; d[k] = Value(s, ref i); Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("json: expected ',' or '}'");
            }
            throw new FormatException("json: unterminated object");
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>(); i++;
            Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; }
            while (i < s.Length)
            {
                l.Add(Value(s, ref i)); Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new FormatException("json: expected ',' or ']'");
            }
            throw new FormatException("json: unterminated array");
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json: expected string");
            var sb = new StringBuilder(); i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break; case '\\': sb.Append('\\'); break; case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break; case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json: unterminated string");
        }

        static object Num(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("json: bad token at " + st);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------------- writer ----------------
        public static string Serialize(object o) { var sb = new StringBuilder(128); Write(sb, o); return sb.ToString(); }

        static void Write(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s: WriteStr(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case ulong n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case uint n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                    {
                        sb.Append('{'); bool first = true;
                        foreach (DictionaryEntry e in dict) { if (!first) sb.Append(','); first = false; WriteStr(sb, e.Key.ToString()); sb.Append(':'); Write(sb, e.Value); }
                        sb.Append('}'); return;
                    }
                case IEnumerable en:
                    {
                        sb.Append('['); bool first = true;
                        foreach (var x in en) { if (!first) sb.Append(','); first = false; Write(sb, x); }
                        sb.Append(']'); return;
                    }
                default: WriteStr(sb, o.ToString()); return;
            }
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break; case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break; case '\r': sb.Append("\\r"); break; case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>Null-safe accessors over parsed JSON objects.</summary>
    public static class J
    {
        public static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        public static string Str(Dictionary<string, object> d, string k, string def = null) { var v = Get(d, k); return v == null ? def : v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double Num(Dictionary<string, object> d, string k, double def = 0)
        {
            var v = Get(d, k);
            if (v is double x) return x;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return y;
            return def;
        }
        public static int Int(Dictionary<string, object> d, string k, int def = 0) => (int)Math.Round(Num(d, k, def));
        public static long Long(Dictionary<string, object> d, string k, long def = 0) => (long)Math.Round(Num(d, k, def));
        public static ulong ULong(Dictionary<string, object> d, string k, ulong def = 0)
        {
            var v = Get(d, k);
            if (v is string s && ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) return u;
            if (v is double x && x >= 0) return (ulong)Math.Round(x);
            return def;
        }
        public static bool Bool(Dictionary<string, object> d, string k, bool def = false) { var v = Get(d, k); return v is bool b ? b : def; }
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) => Get(d, k) as Dictionary<string, object>;
        public static List<object> Arr(Dictionary<string, object> d, string k) => Get(d, k) as List<object>;
        public static List<int> Ints(Dictionary<string, object> d, string k)
        {
            var l = new List<int>(); var a = Arr(d, k); if (a == null) return l;
            foreach (var x in a) if (x is double n) l.Add((int)Math.Round(n));
            return l;
        }
        public static List<string> Strs(Dictionary<string, object> d, string k)
        {
            var l = new List<string>(); var a = Arr(d, k); if (a == null) return l;
            foreach (var x in a) if (x is string s) l.Add(s);
            return l;
        }
        public static IEnumerable<Dictionary<string, object>> Objs(Dictionary<string, object> d, string k)
        {
            var a = Arr(d, k); if (a == null) yield break;
            foreach (var x in a) if (x is Dictionary<string, object> o) yield return o;
        }
        /// <summary>Message builder: J.Msg("aim", "matchId", id, "squares", list).</summary>
        public static Dictionary<string, object> Msg(string t, params object[] kv)
        {
            var d = new Dictionary<string, object> { ["t"] = t };
            for (int i = 0; i + 1 < kv.Length; i += 2) if (kv[i + 1] != null) d[(string)kv[i]] = kv[i + 1];
            return d;
        }
    }
}
