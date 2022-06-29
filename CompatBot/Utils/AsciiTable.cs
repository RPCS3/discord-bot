using System;
using System.Collections.Generic;
using System.Text;

namespace CompatBot.Utils;

public sealed class AsciiTable
{
    private readonly string[] columns;
    private readonly bool[] alignToRight;
    private readonly bool[] disabled;
    private readonly int[] maxWidth;
    private readonly int[] width;
    private readonly List<string[]> rows = new();

    public AsciiTable(params string[] columns)
    {
        if (columns == null)
            throw new ArgumentNullException(nameof(columns));

        if (columns.Length == 0)
            throw new ArgumentException("Expected at least one column", nameof(columns));

        this.columns = columns;
        alignToRight = new bool[columns.Length];
        disabled = new bool[columns.Length];
        maxWidth = new int[columns.Length];
        width = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            maxWidth[i] = 80;
            width[i] = columns[i].GetVisibleLength();
        }
    }

    public AsciiTable(params AsciiColumn[] columns)
    {
        if (columns == null)
            throw new ArgumentNullException(nameof(columns));

        if (columns.Length == 0)
            throw new ArgumentException("Expected at least one column", nameof(columns));

        this.columns = new string[columns.Length];
        alignToRight = new bool[columns.Length];
        disabled = new bool[columns.Length];
        maxWidth = new int[columns.Length];
        width = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            this.columns[i] = columns[i].Name ?? "";
            disabled[i] = columns[i].Disabled;
            maxWidth[i] = columns[i].MaxWidth;
            width[i] = columns[i].Name.GetVisibleLength();
            alignToRight[i] = columns[i].AlignToRight;
        }
    }

    public void DisableColumn(int idx)
    {
        if (idx < 0 || idx > columns.Length)
            throw new IndexOutOfRangeException();

        disabled[idx] = true;
    }

    public void DisableColumn(string column)
    {
        var idx = column.IndexOf(column, StringComparison.InvariantCultureIgnoreCase);
        if (idx < 0)
            throw new ArgumentException($"There's no such column as '{column}'", nameof(column));

        DisableColumn(idx);
    }

    public void SetMaxWidth(int idx, int length)
    {
        if (idx < 0 || idx > columns.Length)
            throw new IndexOutOfRangeException();

        maxWidth[idx] = length;
    }

    public void SetMaxWidth(string column, int length)
    {
        var idx = column.IndexOf(column, StringComparison.InvariantCultureIgnoreCase);
        if (idx < 0)
            throw new ArgumentException($"There's no such column as '{column}'", nameof(column));

        SetMaxWidth(idx, length);
    }

    public void SetAlignment(int idx, bool toRight)
    {
        if (idx < 0 || idx > columns.Length)
            throw new IndexOutOfRangeException();

        alignToRight[idx] = toRight;
    }

    public void SetAlignment(string column, bool toRight)
    {
        var idx = column.IndexOf(column, StringComparison.InvariantCultureIgnoreCase);
        if (idx < 0)
            throw new ArgumentException($"There's no such column as '{column}'", nameof(column));

        SetAlignment(idx, toRight);
    }

    public void Add(params string[] row)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));

        if (row.Length != columns.Length)
            throw new ArgumentException($"Expected row with {columns.Length} cells, but received row with {row.Length} cells");

        rows.Add(row);
        for (var i = 0; i < row.Length; i++)
            width[i] = Math.Max(width[i], row[i].GetVisibleLength());
    }

    public override string ToString() => ToString(true);
        
    public string ToString(bool wrapInCodeBlock)
    {
        for (var i = 0; i < columns.Length; i++)
            width[i] = Math.Min(width[i], maxWidth[i]);

        var result = new StringBuilder();
        if (wrapInCodeBlock)
            result.AppendLine("```");
        var firstIdx = Array.IndexOf(disabled, false);
        if (firstIdx < 0)
            throw new InvalidOperationException("Can't format table as every column is disabled");

        // header
        result.Append(columns[firstIdx].TrimVisible(maxWidth[firstIdx]).PadRightVisible(width[firstIdx]));
        for (var i = firstIdx+1; i < columns.Length; i++)
            if (!disabled[i])
                result.Append(" │ ").Append(columns[i].TrimVisible(maxWidth[i]).PadRightVisible(width[i])); // header is always aligned to the left
        result.AppendLine();
        //header separator
        result.Append("".PadRight(width[firstIdx], '─'));
        for (var i = firstIdx+1; i < columns.Length; i++)
            if (!disabled[i])
                result.Append("─┼─").Append("".PadRight(width[i], '─'));
        result.AppendLine();
        //rows
        foreach (var row in rows)
        {
            var cell = row[firstIdx].TrimVisible(maxWidth[firstIdx]);
            result.Append(alignToRight[firstIdx] ? cell.PadLeftVisible(width[firstIdx]) : cell.PadRightVisible(width[firstIdx]));
            for (var i = firstIdx+1; i < row.Length; i++)
                if (!disabled[i])
                {
                    cell = row[i].TrimVisible(maxWidth[i]);
                    result.Append(" │ ").Append(alignToRight[i] ?cell.PadLeftVisible(width[i]) : cell.PadRightVisible(width[i]));
                }
            result.AppendLine();
        }
        if (wrapInCodeBlock)
            result.Append("```");
        return result.ToString();
    }
}