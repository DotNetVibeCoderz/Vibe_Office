// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ExcelNet.Validation;

/// <summary>
/// What a locked sheet still allows.
/// </summary>
/// <remarks>
/// <para>
/// Sheet protection is <b>not security</b>. The password is a 16-bit hash that any tool can strip in
/// milliseconds, and the file's contents are readable regardless. It exists to stop someone
/// overwriting a formula column by accident, which is a real and common problem — and that is all
/// it is for.
/// </para>
/// <para>
/// The interaction that surprises people: protection only bites on cells whose style says
/// <c>locked</c>, and <b>every cell is locked by default</b>. So protecting a sheet without first
/// unlocking the input cells freezes the whole thing. <see cref="Worksheet.Protect"/> takes the
/// editable range for exactly that reason.
/// </para>
/// </remarks>
public sealed record SheetProtection
{
    /// <summary>Whether the sheet is protected at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The password hash, or <c>null</c> for protection without one.</summary>
    public string? PasswordHash { get; init; }

    /// <summary>Whether locked cells can be selected. True by default, as Excel does.</summary>
    public bool SelectLockedCells { get; init; } = true;

    /// <summary>Whether unlocked cells can be selected.</summary>
    public bool SelectUnlockedCells { get; init; } = true;

    /// <summary>Whether columns can be formatted.</summary>
    public bool FormatColumns { get; init; }

    /// <summary>Whether rows can be formatted.</summary>
    public bool FormatRows { get; init; }

    /// <summary>Whether cells can be formatted.</summary>
    public bool FormatCells { get; init; }

    /// <summary>Whether rows can be inserted.</summary>
    public bool InsertRows { get; init; }

    /// <summary>Whether columns can be inserted.</summary>
    public bool InsertColumns { get; init; }

    /// <summary>Whether rows can be deleted.</summary>
    public bool DeleteRows { get; init; }

    /// <summary>Whether columns can be deleted.</summary>
    public bool DeleteColumns { get; init; }

    /// <summary>Whether the data can be sorted.</summary>
    public bool Sort { get; init; }

    /// <summary>Whether autofilter dropdowns still work.</summary>
    public bool AutoFilter { get; init; }

    /// <summary>Protection with a password.</summary>
    /// <remarks>
    /// See the class remarks: this deters accidents, it does not protect anything. Do not use it to
    /// keep a secret.
    /// </remarks>
    public static SheetProtection WithPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        return new SheetProtection { PasswordHash = Hash(password) };
    }

    /// <summary>
    /// Excel's legacy password hash, as the format defines it.
    /// </summary>
    /// <remarks>
    /// A 16-bit value, which is why this is not security: there are 65,536 possible hashes, so a
    /// collision is trivial to find and the algorithm has been public since the 1990s. It is
    /// implemented here because Excel expects this exact value and rejects anything else — not
    /// because it protects the sheet.
    /// </remarks>
    internal static string Hash(string password)
    {
        ushort hash = 0;

        for (var i = password.Length - 1; i >= 0; i--)
        {
            hash ^= password[i];
            hash = (ushort)((hash << 1) | (hash >> 15));  // rotate left within 15 bits
            hash &= 0x7FFF;
        }

        hash ^= (ushort)password.Length;
        hash ^= 0xCE4B;

        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Workbook-level protection: which sheets can be added, removed, renamed or reordered.
/// </summary>
/// <remarks>
/// Separate from <see cref="SheetProtection"/> because it answers a different question. Sheet
/// protection guards cells; this guards the structure of the workbook itself. The same caveat
/// applies — it prevents accidents, not access.
/// </remarks>
public sealed record WorkbookProtection
{
    /// <summary>Whether sheets can be added, removed, renamed or reordered.</summary>
    public bool LockStructure { get; init; } = true;

    /// <summary>Whether the workbook's windows can be moved or resized.</summary>
    public bool LockWindows { get; init; }

    /// <summary>The password hash, or <c>null</c> for protection without one.</summary>
    public string? PasswordHash { get; init; }

    /// <summary>Structure protection with a password.</summary>
    public static WorkbookProtection WithPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        return new WorkbookProtection { PasswordHash = SheetProtection.Hash(password) };
    }
}
