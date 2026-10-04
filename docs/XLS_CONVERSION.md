# Local XLS conversion — 0.9.13

New XLS imports use ExcelDataReader to validate dimensions and read cached
values, then NPOI 2.7.6 to build an in-memory XLSX. Only the derived XLSX is
uploaded with the XLSX media MIME; Google Sheets remains the destination MIME.
The snapshot, original hash, association identity and backup remain those of
the original binary XLS. XLSX, CSV, TSV and ODS retain their existing paths.

The converter transfers cell values/types, formulas, named ranges, date
system, basic cell styles/fonts/palette colors, merged regions, row heights,
column widths, hyperlinks and hidden tabs/columns. It requests formula recalculation.
This is not a full-fidelity Excel converter: charts, drawings, macros,
conditional formatting, embedded objects, comments, print settings, protection
and rich-text runs are not reproduced. Detected VBA, drawings, conditional
formatting, sheet protection and rich-text runs force copy-only import, preserving the original. Formula exports
continue through the existing copy-only fallback when fidelity cannot be
verified; differing values still block retirement.

Conversion failures happen before initiating a new upload. There is no blind
second upload after a network failure. ZIP entry timestamps and generated
core-property dates are normalized so repeated conversion produces the same
payload for resumable upload. The converted payload is capped at 20 MiB.

Older pending upload sessions remain bound to their original bytes and MIME.
When a session proves its binding to binary XLS, recovery resumes that payload;
it never switches the bytes of an existing session or initiates another one.
Previously created remote candidates are reconciled by their existing marker.
A previously created non-Sheets candidate still needs explicit recovery;
upgrading does not delete or silently replace it.

NPOI is pinned to [2.7.6]. ImageSharp is pinned to [2.1.13] (Apache-2.0), and
System.Security.Cryptography.Xml is explicitly 10.0.12. NuGet audit remains
enabled and dependency lock files are updated. See third-party/NOTICE.md.

Validation includes independent ExcelDataReader round trips, formulas/names,
dates/styles/colors, hidden/merged structures, deterministic payloads,
copy-only handling and upload/recovery tests. The privately supplied XLS was
also converted locally and its eight formulas preserved. It is not committed
as a fixture. Real Google conversion and installer behavior require Windows
CI and user validation.
