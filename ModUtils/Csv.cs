using System;
using System.Collections.Generic;
using System.Text;

namespace ModUtils
{
    public static class Csv
    {
        private static readonly char[] MustQuoteChars = { '"', ',', '\r', '\n' };

        public static string Escape(string field)
        {
            if (field == null) return "";

            var mustQuote = field.Length == 0 ||
                            field.IndexOfAny(MustQuoteChars) != -1 ||
                            field.Length > 0 &&
                            (char.IsWhiteSpace(field[0]) ||
                             char.IsWhiteSpace(field[field.Length - 1]));

            return !mustQuote
                ? field
                : $"\"{field.Replace("\"", "\"\"")}\"";
        }

        public static List<List<string>> Parse(string csv)
        {
            return Parse(csv, false);
        }

        public static List<List<string>> Parse(string csv, bool trimUnquotedFields)
        {
            return new Parser(csv, 0, trimUnquotedFields).Parse();
        }

        public static List<string> ParseLine(string line)
        {
            return ParseLine(line, false);
        }

        public static List<string> ParseLine(string line, bool trimUnquotedFields)
        {
            return new Parser(line, 0, trimUnquotedFields).ParseLine();
        }

        public sealed class Parser
        {
            private readonly StringBuilder _fieldBuffer;
            private readonly List<string> _recordBuffer;
            private readonly string _source;
            private readonly bool _trimUnquotedFields;
            private bool _fieldQuoted;
            private bool _fieldStarted;
            private bool _inQuotes;
            private bool _lastTokenWasDelimiter;
            private int _offset;

            public Parser(string source, int offset = 0, bool trimUnquotedFields = false)
            {
                _source = source ?? "";
                _offset = offset;
                _trimUnquotedFields = trimUnquotedFields;
                _recordBuffer = new List<string>();
                _fieldBuffer = new StringBuilder();
            }

            public List<List<string>> Parse()
            {
                var result = new List<List<string>>();

                var fieldCount = -1;
                while (HasNext())
                {
                    var record = ParseLine();

                    var count = record.Count;
                    if (count == 0) continue;

                    if (fieldCount == -1)
                        fieldCount = count;
                    else if (fieldCount != count)
                        throw new Exception("Number of fields in a record is not uniform.");

                    result.Add(record);
                }

                return result;
            }

            public bool HasNext()
            {
                return _offset < _source.Length;
            }

            public List<string> ParseLine()
            {
                _recordBuffer.Clear();
                _fieldBuffer.Clear();
                _fieldQuoted = false;
                _fieldStarted = false;
                _inQuotes = false;
                _lastTokenWasDelimiter = false;

                var record = new List<string>();
                var recordHasContent = false;
                while (_offset < _source.Length)
                {
                    var c = _source[_offset++];
                    if (ParseChar(c, ref recordHasContent)) continue;

                    break;
                }

                if (recordHasContent || _fieldStarted || _lastTokenWasDelimiter)
                    FlushField();

                if (_recordBuffer.Count > 0)
                    record.AddRange(_recordBuffer);

                return record;
            }

            private bool ParseChar(char c, ref bool recordHasContent)
            {
                if (c == '"')
                {
                    recordHasContent = true;
                    if (_inQuotes)
                    {
                        if (_offset < _source.Length && _source[_offset] == '"')
                        {
                            _fieldBuffer.Append('"');
                            _offset++;
                        }
                        else
                        {
                            _inQuotes = false;
                        }
                    }
                    else if (!_fieldStarted)
                    {
                        _fieldQuoted = true;
                        _fieldStarted = true;
                        _inQuotes = true;
                    }
                    else
                    {
                        _fieldBuffer.Append(c);
                        _fieldStarted = true;
                    }

                    _lastTokenWasDelimiter = false;
                    return true;
                }

                if (c == ',' && !_inQuotes)
                {
                    recordHasContent = true;
                    FlushField();
                    _lastTokenWasDelimiter = true;
                    return true;
                }

                if ((c == '\r' || c == '\n') && !_inQuotes)
                {
                    if (c == '\r' && _offset < _source.Length && _source[_offset] == '\n')
                        _offset++;

                    return false;
                }

                recordHasContent = true;
                _fieldBuffer.Append(c);
                _fieldStarted = true;
                _lastTokenWasDelimiter = false;
                return true;
            }

            private void FlushField()
            {
                var field = _fieldBuffer.ToString();
                _recordBuffer.Add(_trimUnquotedFields && !_fieldQuoted ? field.Trim() : field);
                _fieldBuffer.Clear();
                _fieldQuoted = false;
                _fieldStarted = false;
            }
        }
    }
}
