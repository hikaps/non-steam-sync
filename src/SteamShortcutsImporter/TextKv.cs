using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SteamShortcutsImporter;

internal static class TextKv
{
    public static Dictionary<string, object> Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var reader = new StringReader(text);
        var tokens = new Tokenizer(reader);
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            var t = tokens.Peek();
            if (t == null) break;
            var key = tokens.ReadString();
            var next = tokens.Peek();
            if (next == "{")
            {
                tokens.Read(); // consume {
                var obj = ReadObject(tokens);
                root[key] = obj;
            }
            else
            {
                var val = tokens.ReadString();
                root[key] = val;
            }
        }
        return root;
    }

    private static Dictionary<string, object> ReadObject(Tokenizer tokens)
    {
        var obj = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            var t = tokens.Peek();
            if (t == null)
            {
                break;
            }
            if (t == "}")
            {
                tokens.Read(); // consume }
                break;
            }
            var key = tokens.ReadString();
            var next = tokens.Peek();
            if (next == "{")
            {
                tokens.Read(); // consume {
                var child = ReadObject(tokens);
                obj[key] = child;
            }
            else
            {
                var val = tokens.ReadString();
                obj[key] = val;
            }
        }
        return obj;
    }

    public static void Write(string path, Dictionary<string, object> root)
    {
        var sb = new StringBuilder(4096);
        WritePairs(sb, root, 0);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WritePairs(StringBuilder sb, Dictionary<string, object> obj, int indent)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is Dictionary<string, object> child)
            {
                Indent(sb, indent).Append('"').Append(kv.Key).Append("\"\n");
                Indent(sb, indent).Append("{\n");
                WritePairs(sb, child, indent + 1);
                Indent(sb, indent).Append("}\n");
            }
            else
            {
                var val = kv.Value?.ToString() ?? string.Empty;
                var esc = val.Replace("\"", "\\\"");
                Indent(sb, indent).Append('"').Append(kv.Key).Append('"').Append('\t').Append('"').Append(esc).Append('"').Append('\n');
            }
        }
    }

    private static StringBuilder Indent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++) sb.Append('\t');
        return sb;
    }

    private sealed class Tokenizer
    {
        private readonly StringReader _reader;
        private string _buffered;

        public Tokenizer(StringReader reader)
        {
            _reader = reader;
        }

        public string Peek()
        {
            if (_buffered != null) return _buffered;
            _buffered = ReadInternal();
            return _buffered;
        }

        public string Read()
        {
            var t = Peek();
            _buffered = null;
            return t;
        }

        public string ReadString()
        {
            var t = Read();
            if (t == "{" || t == "}")
            {
                throw new InvalidDataException("Expected string token but found brace.");
            }
            return t;
        }

        private string ReadInternal()
        {
            SkipWhitespaceAndComments();
            int c = _reader.Peek();
            if (c == -1) return null;
            if ((char)c == '{' || (char)c == '}')
            {
                _reader.Read();
                return ((char)c).ToString();
            }
            if ((char)c == '"')
            {
                return ReadQuotedString();
            }
            // bare strings (rare), read until whitespace or brace
            var sb = new StringBuilder();
            while (true)
            {
                c = _reader.Peek();
                if (c == -1) break;
                char ch = (char)c;
                if (char.IsWhiteSpace(ch) || ch == '{' || ch == '}') break;
                sb.Append(ch);
                _reader.Read();
            }
            return sb.ToString();
        }

        private string ReadQuotedString()
        {
            // assumes next char is '"'
            _reader.Read(); // consume opening quote
            var sb = new StringBuilder();
            while (true)
            {
                int c = _reader.Read();
                if (c == -1) break;
                char ch = (char)c;
                if (ch == '"') break;
                if (ch == '\\')
                {
                    int n = _reader.Read();
                    if (n == -1) break;
                    sb.Append((char)n);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private void SkipWhitespaceAndComments()
        {
            while (true)
            {
                int c = _reader.Peek();
                if (c == -1) return;
                char ch = (char)c;
                if (char.IsWhiteSpace(ch))
                {
                    _reader.Read();
                    continue;
                }
                // // comment
                if (ch == '/')
                {
                    _reader.Read();
                    int c2 = _reader.Peek();
                    if (c2 == '/')
                    {
                        // line comment
                        while (true)
                        {
                            c2 = _reader.Read();
                            if (c2 == -1 || c2 == '\n') break;
                        }
                        continue;
                    }
                    else
                    {
                        // not a comment, push back second char by creating a buffer
                        // but our simple tokenizer doesn't support unread, so just treat '/' as part of token
                        // place back into stream by prepending
                        _reader.Read();
                    }
                }
                break;
            }
        }
    }
}
