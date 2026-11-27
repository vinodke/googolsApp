using System.Text;
using Googols.Domain.Common;

namespace Googols.Application.Curriculum;

/// <summary>
/// Bulk import of a syllabus into one academic year. One row per chapter:
/// <c>grade,subject_code,subject_name,medium,chapter_no,chapter_title</c> (header row required).
/// Missing classes and subjects are created; existing ones are matched by grade / code + medium.
/// Everything imported starts as Draft.
/// </summary>
public static class CurriculumCsv
{
    public sealed record Row(int Line, int Grade, string SubjectCode, string SubjectName, string Medium, int ChapterNo, string ChapterTitle);

    public sealed record ParseResult(IReadOnlyList<Row> Rows, IReadOnlyList<string> Errors);

    public static readonly string[] Header = ["grade", "subject_code", "subject_name", "medium", "chapter_no", "chapter_title"];
    public const int MaxRows = 2000;

    public static ParseResult Parse(string csv)
    {
        var rows = new List<Row>();
        var errors = new List<string>();
        var lines = SplitRecords(csv.TrimStart('﻿'));
        if (lines.Count == 0)
        {
            return new ParseResult(rows, ["The file is empty."]);
        }
        var header = lines[0].Fields.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var index = Header.ToDictionary(h => h, header.IndexOf);
        var missing = index.Where(kv => kv.Value < 0).Select(kv => kv.Key).ToList();
        if (missing.Count > 0)
        {
            return new ParseResult(rows, [$"Missing column(s): {string.Join(", ", missing)}. The first row must be: {string.Join(",", Header)}"]);
        }
        if (lines.Count - 1 > MaxRows)
        {
            return new ParseResult(rows, [$"Too many rows ({lines.Count - 1}). Import at most {MaxRows} chapters at a time."]);
        }

        foreach (var (line, fields) in lines.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }
            string F(string name) => index[name] < fields.Count ? fields[index[name]].Trim() : "";
            var problems = new List<string>();
            if (!int.TryParse(F("grade"), out var grade) || grade is < 0 or > 12) problems.Add("grade must be 0–12");
            var code = F("subject_code").ToUpperInvariant();
            if (code.Length is 0 or > 40 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) problems.Add("subject_code must be letters, digits or '-'");
            var name = F("subject_name");
            if (name.Length is 0 or > 200) problems.Add("subject_name is required");
            var medium = F("medium").ToLowerInvariant();
            if (!LanguageCodes.IsSupported(medium)) problems.Add("medium must be en or ml");
            if (!int.TryParse(F("chapter_no"), out var no) || no is < 1 or > 999) problems.Add("chapter_no must be 1–999");
            var title = F("chapter_title");
            if (title.Length is 0 or > 300) problems.Add("chapter_title is required");
            if (problems.Count > 0)
            {
                errors.Add($"Line {line}: {string.Join("; ", problems)}.");
                continue;
            }
            rows.Add(new Row(line, grade, code, name, medium, no, title));
        }
        var dupes = rows.GroupBy(r => (r.Grade, r.SubjectCode, r.Medium, r.ChapterNo)).Where(g => g.Count() > 1);
        errors.AddRange(dupes.Select(d => $"Lines {string.Join(", ", d.Select(r => r.Line))}: the same chapter number appears twice for {d.Key.SubjectCode}."));
        return new ParseResult(rows, errors);
    }

    /// <summary>RFC 4180-style records: commas, double-quoted fields, "" escapes, CRLF or LF, newlines inside quotes.</summary>
    private static List<(int Line, List<string> Fields)> SplitRecords(string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var startLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString()); field.Clear();
                    records.Add((startLine, fields)); fields = []; line++; startLine = line;
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((startLine, fields));
        }
        return records;
    }
}
