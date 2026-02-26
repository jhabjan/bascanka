using static Enums;

namespace Bascanka.Core.Diff;

/// <summary>
/// Computes a line-level diff between two texts using LCS (longest common subsequence),
/// then optionally computes character-level diffs for modified line pairs.
/// Produces padded output suitable for side-by-side display.
/// </summary>
public static class DiffEngine
{
    private const int CharDiffLineThreshold = 1000;

    public static DiffResult Compare(string leftText, string rightText,
        string leftTitle = "", string rightTitle = "")
    {
        string[] leftLines = SplitLines(leftText);
        string[] rightLines = SplitLines(rightText);

        // Compute edit script via LCS.
        var ops = ComputeEditScript(leftLines, rightLines);

        // Merge adjacent Delete+Insert into Modified.
        var merged = MergeOperations(ops);

        // Build padded sides.
        var leftDiffLines = new List<DiffLine>();
        var rightDiffLines = new List<DiffLine>();
        var sectionStarts = new List<int>();

        int leftIdx = 0, rightIdx = 0;
        bool inDiffSection = false;

        foreach (var op in merged)
        {
            switch (op.Type)
            {
                case OpType.Equal:
                    inDiffSection = false;
                    for (int i = 0; i < op.Count; i++)
                    {
                        leftDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Equal,
                            Text = leftLines[leftIdx],
                            OriginalLineNumber = leftIdx,
                        });
                        rightDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Equal,
                            Text = rightLines[rightIdx],
                            OriginalLineNumber = rightIdx,
                        });
                        leftIdx++;
                        rightIdx++;
                    }
                    break;

                case OpType.Delete:
                    if (!inDiffSection)
                    {
                        sectionStarts.Add(leftDiffLines.Count);
                        inDiffSection = true;
                    }
                    for (int i = 0; i < op.Count; i++)
                    {
                        leftDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Removed,
                            Text = leftLines[leftIdx],
                            OriginalLineNumber = leftIdx,
                        });
                        rightDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Padding,
                            Text = string.Empty,
                            OriginalLineNumber = -1,
                        });
                        leftIdx++;
                    }
                    break;

                case OpType.Insert:
                    if (!inDiffSection)
                    {
                        sectionStarts.Add(leftDiffLines.Count);
                        inDiffSection = true;
                    }
                    for (int i = 0; i < op.Count; i++)
                    {
                        leftDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Padding,
                            Text = string.Empty,
                            OriginalLineNumber = -1,
                        });
                        rightDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Added,
                            Text = rightLines[rightIdx],
                            OriginalLineNumber = rightIdx,
                        });
                        rightIdx++;
                    }
                    break;

                case OpType.Modified:
                    if (!inDiffSection)
                    {
                        sectionStarts.Add(leftDiffLines.Count);
                        inDiffSection = true;
                    }
                    for (int i = 0; i < op.Count; i++)
                    {
                        string leftLine = leftLines[leftIdx];
                        string rightLine = rightLines[rightIdx];

                        List<CharDiffRange>? leftCharDiffs = null;
                        List<CharDiffRange>? rightCharDiffs = null;

                        if (leftLine.Length <= CharDiffLineThreshold &&
                            rightLine.Length <= CharDiffLineThreshold)
                        {
                            (leftCharDiffs, rightCharDiffs) = ComputeCharDiffs(leftLine, rightLine);
                        }

                        leftDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Modified,
                            Text = leftLine,
                            OriginalLineNumber = leftIdx,
                            CharDiffs = leftCharDiffs,
                        });
                        rightDiffLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Modified,
                            Text = rightLine,
                            OriginalLineNumber = rightIdx,
                            CharDiffs = rightCharDiffs,
                        });
                        leftIdx++;
                        rightIdx++;
                    }
                    break;
            }
        }

        // Build padded text.
        string leftPadded = string.Join("\n", leftDiffLines.Select(l => l.Text));
        string rightPadded = string.Join("\n", rightDiffLines.Select(l => l.Text));

        return new DiffResult
        {
            Left = new DiffSide
            {
                Title = leftTitle,
                PaddedText = leftPadded,
                Lines = [.. leftDiffLines],
            },
            Right = new DiffSide
            {
                Title = rightTitle,
                PaddedText = rightPadded,
                Lines = [.. rightDiffLines],
            },
            DiffSectionStarts = [.. sectionStarts],
            DiffCount = sectionStarts.Count,
        };
    }

    // ── Fast line-by-line comparison (O(N), no LCS) ─────────────────

    /// <summary>
    /// Compares two texts line-by-line assuming a 1:1 line correspondence
    /// (e.g. sed s/// which modifies lines in-place without adding/removing lines).
    /// Returns a <see cref="DiffLine"/> array aligned with <paramref name="rightText"/> lines.
    /// </summary>
    public static DiffLine[] CompareLineByLine(string leftText, string rightText)
    {
        string[] leftLines = SplitLines(leftText);
        string[] rightLines = SplitLines(rightText);

        int count = rightLines.Length;
        var result = new DiffLine[count];

        for (int i = 0; i < count; i++)
        {
            string original = i < leftLines.Length ? leftLines[i] : string.Empty;
            string transformed = rightLines[i];

            if (original == transformed)
            {
                result[i] = new DiffLine
                {
                    Type = DiffLineType.Equal,
                    Text = transformed,
                    OriginalLineNumber = i,
                };
            }
            else
            {
                result[i] = new DiffLine
                {
                    Type = i < leftLines.Length ? DiffLineType.Modified : DiffLineType.Added,
                    Text = transformed,
                    OriginalLineNumber = i,
                };
            }
        }

        return result;
    }

    // ── LCS-based edit script ────────────────────────────────────────

    private static List<EditOp> ComputeEditScript(string[] a, string[] b)
    {
        int n = a.Length;
        int m = b.Length;

        // Trim common prefix.
        int prefix = 0;
        while (prefix < n && prefix < m && a[prefix] == b[prefix])
            prefix++;

        // Trim common suffix.
        int suffix = 0;
        while (suffix < n - prefix && suffix < m - prefix &&
               a[n - 1 - suffix] == b[m - 1 - suffix])
            suffix++;

        int trimmedN = n - prefix - suffix;
        int trimmedM = m - prefix - suffix;

        var ops = new List<EditOp>();

        // Add prefix equals.
        for (int i = 0; i < prefix; i++)
            ops.Add(new EditOp(OpType.Equal));

        if (trimmedN == 0 && trimmedM == 0)
        {
            // No differences in the middle.
        }
        else if (trimmedN == 0)
        {
            // Only inserts.
            for (int i = 0; i < trimmedM; i++)
                ops.Add(new EditOp(OpType.Insert));
        }
        else if (trimmedM == 0)
        {
            // Only deletes.
            for (int i = 0; i < trimmedN; i++)
                ops.Add(new EditOp(OpType.Delete));
        }
        else
        {
            // LCS on the trimmed middle portion.
            // Use O(trimmedN * trimmedM) DP — fine for typical file sizes.
            // For very large files, we'd want a more memory-efficient approach,
            // but line counts are rarely in the millions.
            var dp = new int[trimmedN + 1][];
            for (int i = 0; i <= trimmedN; i++)
                dp[i] = new int[trimmedM + 1];

            for (int i = 1; i <= trimmedN; i++)
            {
                for (int j = 1; j <= trimmedM; j++)
                {
                    if (a[prefix + i - 1] == b[prefix + j - 1])
                        dp[i][j] = dp[i - 1][j - 1] + 1;
                    else
                        dp[i][j] = Math.Max(dp[i - 1][j], dp[i][j - 1]);
                }
            }

            // Backtrack to produce edit ops.
            var middleOps = new List<EditOp>();
            int ii = trimmedN, jj = trimmedM;
            while (ii > 0 || jj > 0)
            {
                if (ii > 0 && jj > 0 && a[prefix + ii - 1] == b[prefix + jj - 1])
                {
                    middleOps.Add(new EditOp(OpType.Equal));
                    ii--;
                    jj--;
                }
                else if (jj > 0 && (ii == 0 || dp[ii][jj - 1] >= dp[ii - 1][jj]))
                {
                    middleOps.Add(new EditOp(OpType.Insert));
                    jj--;
                }
                else
                {
                    middleOps.Add(new EditOp(OpType.Delete));
                    ii--;
                }
            }

            middleOps.Reverse();
            ops.AddRange(middleOps);
        }

        // Add suffix equals.
        for (int i = 0; i < suffix; i++)
            ops.Add(new EditOp(OpType.Equal));

        return ops;
    }

    // ── Merge pass ───────────────────────────────────────────────────

    private static List<MergedOp> MergeOperations(List<EditOp> ops)
    {
        var result = new List<MergedOp>();

        int i = 0;
        while (i < ops.Count)
        {
            if (ops[i].Type == OpType.Equal)
            {
                int count = 0;
                while (i < ops.Count && ops[i].Type == OpType.Equal)
                {
                    count++;
                    i++;
                }
                result.Add(new MergedOp(OpType.Equal, count));
            }
            else
            {
                // Collect consecutive Delete and Insert blocks.
                int deletes = 0, inserts = 0;
                while (i < ops.Count && (ops[i].Type == OpType.Delete || ops[i].Type == OpType.Insert))
                {
                    if (ops[i].Type == OpType.Delete)
                        deletes++;
                    else
                        inserts++;
                    i++;
                }

                // Merge overlapping deletes+inserts into Modified.
                int modified = Math.Min(deletes, inserts);
                int remainingDeletes = deletes - modified;
                int remainingInserts = inserts - modified;

                if (remainingDeletes > 0)
                    result.Add(new MergedOp(OpType.Delete, remainingDeletes));
                if (modified > 0)
                    result.Add(new MergedOp(OpType.Modified, modified));
                if (remainingInserts > 0)
                    result.Add(new MergedOp(OpType.Insert, remainingInserts));
            }
        }

        return result;
    }

    // ── Character-level LCS diff ─────────────────────────────────────

    private static (List<CharDiffRange> Left, List<CharDiffRange> Right)
        ComputeCharDiffs(string left, string right)
    {
        // Find common prefix and suffix to reduce LCS work.
        int prefixLen = 0;
        int minLen = Math.Min(left.Length, right.Length);
        while (prefixLen < minLen && left[prefixLen] == right[prefixLen])
            prefixLen++;

        int suffixLen = 0;
        while (suffixLen < minLen - prefixLen &&
               left[left.Length - 1 - suffixLen] == right[right.Length - 1 - suffixLen])
            suffixLen++;

        string leftMid = left.Substring(prefixLen, left.Length - prefixLen - suffixLen);
        string rightMid = right.Substring(prefixLen, right.Length - prefixLen - suffixLen);

        if (leftMid.Length == 0 && rightMid.Length == 0)
            return (new List<CharDiffRange>(), new List<CharDiffRange>());

        // If the middle sections are too large, highlight the whole middle as changed.
        if ((long)leftMid.Length * rightMid.Length > 4_000_000)
        {
            var leftRanges = new List<CharDiffRange>();
            var rightRanges = new List<CharDiffRange>();
            if (leftMid.Length > 0)
                leftRanges.Add(new CharDiffRange(prefixLen, leftMid.Length));
            if (rightMid.Length > 0)
                rightRanges.Add(new CharDiffRange(prefixLen, rightMid.Length));
            return (leftRanges, rightRanges);
        }

        // LCS on the middle portions.
        var (leftInLcs, rightInLcs) = ComputeLcsFlags(leftMid, rightMid);

        // Build diff ranges for each side (chars NOT in LCS are changed).
        var leftResult = BuildCharRanges(leftMid, leftInLcs, prefixLen);
        var rightResult = BuildCharRanges(rightMid, rightInLcs, prefixLen);

        return (leftResult, rightResult);
    }

    private static (bool[] leftInLcs, bool[] rightInLcs)
        ComputeLcsFlags(string a, string b)
    {
        int m = a.Length, n = b.Length;

        // Forward pass to compute LCS lengths.
        var dp = new int[m + 1][];
        for (int i = 0; i <= m; i++)
            dp[i] = new int[n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (a[i - 1] == b[j - 1])
                    dp[i][j] = dp[i - 1][j - 1] + 1;
                else
                    dp[i][j] = Math.Max(dp[i - 1][j], dp[i][j - 1]);
            }
        }

        // Backtrack to find which chars are in LCS.
        var leftInLcs = new bool[m];
        var rightInLcs = new bool[n];

        int ii = m, jj = n;
        while (ii > 0 && jj > 0)
        {
            if (a[ii - 1] == b[jj - 1])
            {
                leftInLcs[ii - 1] = true;
                rightInLcs[jj - 1] = true;
                ii--;
                jj--;
            }
            else if (dp[ii - 1][jj] >= dp[ii][jj - 1])
            {
                ii--;
            }
            else
            {
                jj--;
            }
        }

        return (leftInLcs, rightInLcs);
    }

    private static List<CharDiffRange> BuildCharRanges(string text, bool[] inLcs, int offset)
    {
        var ranges = new List<CharDiffRange>();
        int i = 0;
        while (i < text.Length)
        {
            if (!inLcs[i])
            {
                int start = i + offset;
                while (i < text.Length && !inLcs[i])
                    i++;
                ranges.Add(new CharDiffRange(start, i + offset - start));
            }
            else
            {
                i++;
            }
        }
        return ranges;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        // Normalize line endings and split.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return text.Split('\n');
    }

    private enum OpType { Equal, Delete, Insert, Modified }

    private readonly record struct EditOp(OpType Type);

    private readonly record struct MergedOp(OpType Type, int Count);
}
