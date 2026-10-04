# Third-party licenses — ZagoSheetsWin 0.9.13

The application and the original Open in Google attribution remain MIT.
Each dependency retains its own license. NPOI is pinned to 2.7.6; the license
of newer binary packages must not be assumed to be Apache-2.0.

| Component | Version | License / bundled text |
|---|---|---|
| ExcelDataReader | 3.9.0 | MIT — ExcelDataReader-LICENSE.txt |
| NPOI (Tony Qu, NPOI contributors, Nissl LLC) | 2.7.6 | Apache-2.0 — NPOI-LICENSE.txt |
| BouncyCastle.Cryptography | 2.6.2 | MIT — BouncyCastle-LICENSE.txt |
| Enums.NET | 5.0.0 | MIT — Enums.NET-LICENSE.txt |
| ExtendedNumerics.BigDecimal | 2025.1001.2.129 | MIT — BigDecimal-LICENSE.txt |
| MathNet.Numerics.Signed | 5.0.0 | MIT — MathNet-LICENSE.txt |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT — RecyclableMemoryStream-LICENSE.txt |
| NSax | 1.0.2 | LGPL-3.0-only — NSax-LICENSE.txt and GPL-3.0.txt |
| SharpZipLib | 1.4.2 | MIT — SharpZipLib-LICENSE.txt |
| SixLabors.Fonts | 1.0.1 | Apache-2.0 — Fonts-LICENSE.txt |
| SixLabors.ImageSharp | 2.1.13 | Apache-2.0 — ImageSharp-LICENSE.txt |
| ZString | 2.6.0 | MIT — ZString-LICENSE.txt |

NPOI derives from Apache POI (Apache Software Foundation), licensed under
Apache-2.0. Source: https://github.com/nissl-lab/npoi.

NSax is an unmodified dependency, distributed as a separate NSax.dll.
Its use is covered by LGPL-3.0-only. Source corresponding to NuGet 1.0.2:
https://github.com/antony-liu/NSax/tree/b75861cbc49be1ce4b02410e324b5755ffcb17a2

Users may modify/rebuild NSax and replace its DLL with an interface-compatible
version; this application imposes no restriction on that modification or on
reverse engineering for debugging it. The installer does not merge the DLL
into the executable. Build instructions are available in the NSax repository.

System.Security.Cryptography.Xml is explicitly updated to 10.0.12 to avoid
vulnerabilities in NPOI's older default dependency. It and other Microsoft
runtime libraries retain their distributed MIT notices (see the bundled
.NET ThirdPartyNotices.txt in self-contained installations).
