using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FunscriptThumbnail
{
    /// <summary>
    /// Minimal, tolerant JSON parser. Produces Dictionary&lt;string, object&gt;, List&lt;object&gt;,
    /// double, string, bool or null. Good enough for multi-MB funscripts without extra dependencies.
    /// </summary>
    internal sealed class MiniJson
    {
        private readonly string _s;
        private int _i;
        private int _depth;
        private const int MaxDepth = 64;   // runs inside explorer.exe: never risk a stack overflow

        private MiniJson(string s) { _s = s; }

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var p = new MiniJson(text);
            p.SkipWs();
            var v = p.ReadValue();
            return v;
        }

        private void SkipWs()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') { _i++; continue; }
                break;
            }
        }

        private Exception Err(string msg) { return new FormatException("JSON: " + msg + " at " + _i); }

        private object ReadValue()
        {
            if (_i >= _s.Length) throw Err("unexpected end");
            char c = _s[_i];
            switch (c)
            {
                case '{': if (++_depth > MaxDepth) throw Err("nesting too deep"); try { return ReadObject(); } finally { _depth--; }
                case '[': if (++_depth > MaxDepth) throw Err("nesting too deep"); try { return ReadArray(); } finally { _depth--; }
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                    throw Err("unexpected char '" + c + "'");
            }
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Err("expected " + word);
            _i += word.Length;
        }

        private Dictionary<string, object> ReadObject()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            _i++; SkipWs();
            if (_i < _s.Length && _s[_i] == '}') { _i++; return d; }
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length || _s[_i] != '"') throw Err("expected key");
                string key = ReadString();
                SkipWs();
                if (_i >= _s.Length || _s[_i] != ':') throw Err("expected ':'");
                _i++; SkipWs();
                d[key] = ReadValue();
                SkipWs();
                if (_i >= _s.Length) throw Err("unexpected end");
                if (_s[_i] == ',') { _i++; SkipWs(); if (_i < _s.Length && _s[_i] == '}') { _i++; return d; } continue; }
                if (_s[_i] == '}') { _i++; return d; }
                throw Err("expected ',' or '}'");
            }
        }

        private List<object> ReadArray()
        {
            var l = new List<object>();
            _i++; SkipWs();
            if (_i < _s.Length && _s[_i] == ']') { _i++; return l; }
            while (true)
            {
                SkipWs();
                l.Add(ReadValue());
                SkipWs();
                if (_i >= _s.Length) throw Err("unexpected end");
                if (_s[_i] == ',') { _i++; SkipWs(); if (_i < _s.Length && _s[_i] == ']') { _i++; return l; } continue; }
                if (_s[_i] == ']') { _i++; return l; }
                throw Err("expected ',' or ']'");
            }
        }

        private string ReadString()
        {
            _i++;
            int start = _i;
            // fast path: no escapes
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '"') { var r = _s.Substring(start, _i - start); _i++; return r; }
                if (c == '\\') break;
                _i++;
            }
            var sb = new StringBuilder(_s, start, _i - start, (_i - start) + 16);
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) break;
                char e = _s[_i++];
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
                        if (_i + 4 > _s.Length) throw Err("bad \\u escape");
                        sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw Err("unterminated string");
        }

        private object ReadNumber()
        {
            int start = _i;
            if (_s[_i] == '-') _i++;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { _i++; continue; }
                break;
            }
            double v;
            if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                throw Err("bad number");
            return v;
        }
    }
}
