using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Services;

public sealed partial class WorkspacePatchApplier : IWorkspacePatchApplier
{
    public Fin<WorkspacePatchApplicationResult> Apply(string originalText, string patchText)
    {
        if (patchText is null)
        {
            return Fin.Fail<WorkspacePatchApplicationResult>(Error.New("Patch text is required."));
        }

        var originalLines = SplitLines(originalText ?? string.Empty, out var hadTrailingNewline, out var newline);
        var patchLines = SplitLines(patchText, out _, out _);

        var outputLines = new List<string>(originalLines.Count + 8);
        var sourceIndex = 0;
        var lineIndex = 0;
        var appliedHunks = 0;
        var addedLines = 0;
        var removedLines = 0;

        while (lineIndex < patchLines.Count)
        {
            var current = patchLines[lineIndex];
            if (current.StartsWith("diff --git ", StringComparison.Ordinal) ||
                current.StartsWith("index ", StringComparison.Ordinal) ||
                current.StartsWith("--- ", StringComparison.Ordinal) ||
                current.StartsWith("+++ ", StringComparison.Ordinal))
            {
                lineIndex++;
                continue;
            }

            if (!current.StartsWith("@@", StringComparison.Ordinal))
            {
                return Fin.Fail<WorkspacePatchApplicationResult>(Error.New($"Unsupported patch line: '{current}'."));
            }

            var hunk = ParseHunkHeader(current);
            if (hunk is not { } parsedHunk)
            {
                return Fin.Fail<WorkspacePatchApplicationResult>(Error.New($"Invalid hunk header: '{current}'."));
            }

            var targetIndex = parsedHunk.OldStart - 1;
            if (targetIndex < sourceIndex)
            {
                return Fin.Fail<WorkspacePatchApplicationResult>(Error.New("Patch hunks must be ordered and non-overlapping."));
            }

            while (sourceIndex < targetIndex)
            {
                outputLines.Add(originalLines[sourceIndex++]);
            }

            lineIndex++;
            while (lineIndex < patchLines.Count && !patchLines[lineIndex].StartsWith("@@", StringComparison.Ordinal))
            {
                var patchLine = patchLines[lineIndex];
                if (patchLine.Length == 0)
                {
                    return Fin.Fail<WorkspacePatchApplicationResult>(Error.New("Malformed patch line."));
                }

                var marker = patchLine[0];
                if (marker == '\\')
                {
                    lineIndex++;
                    continue;
                }

                var patchContent = patchLine.Length > 1 ? patchLine[1..] : string.Empty;
                switch (marker)
                {
                    case ' ':
                        if (sourceIndex >= originalLines.Count || !string.Equals(originalLines[sourceIndex], patchContent, StringComparison.Ordinal))
                        {
                            return Fin.Fail<WorkspacePatchApplicationResult>(Error.New("Patch context did not match the target file."));
                        }

                        outputLines.Add(originalLines[sourceIndex]);
                        sourceIndex++;
                        break;
                    case '-':
                        if (sourceIndex >= originalLines.Count || !string.Equals(originalLines[sourceIndex], patchContent, StringComparison.Ordinal))
                        {
                            return Fin.Fail<WorkspacePatchApplicationResult>(Error.New("Patch removal did not match the target file."));
                        }

                        sourceIndex++;
                        removedLines++;
                        break;
                    case '+':
                        outputLines.Add(patchContent);
                        addedLines++;
                        break;
                    default:
                        return Fin.Fail<WorkspacePatchApplicationResult>(Error.New($"Unsupported patch marker '{marker}'."));
                }

                lineIndex++;
            }

            appliedHunks++;
        }

        while (sourceIndex < originalLines.Count)
        {
            outputLines.Add(originalLines[sourceIndex++]);
        }

        var content = JoinLines(outputLines, newline);
        if (hadTrailingNewline && outputLines.Count > 0)
        {
            content += newline;
        }

        return Fin.Succ(new WorkspacePatchApplicationResult
        {
            Content = content,
            AppliedHunks = appliedHunks,
            AddedLines = addedLines,
            RemovedLines = removedLines
        });
    }

    private static ParsedHunk? ParseHunkHeader(string header)
    {
        var match = HunkHeaderRegex().Match(header);
        if (!match.Success)
        {
            return null;
        }

        return new ParsedHunk(
            int.Parse(match.Groups["oldStart"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["oldCount"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["newStart"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["newCount"].Value, CultureInfo.InvariantCulture));
    }

    private static List<string> SplitLines(string text, out bool hadTrailingNewline, out string newline)
    {
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        hadTrailingNewline = text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n');

        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static string JoinLines(IReadOnlyList<string> lines, string newline)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(newline);
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private sealed record ParsedHunk(int OldStart, int OldCount, int NewStart, int NewCount);

    [GeneratedRegex(@"^@@\s*-(?<oldStart>\d+)(?:,(?<oldCount>\d+))?\s+\+(?<newStart>\d+)(?:,(?<newCount>\d+))?\s*@@")]
    private static partial Regex HunkHeaderRegex();
}
