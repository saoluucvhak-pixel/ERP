# Saves the first sheet of an Excel report (e.g. a trial balance printed from FAST, .xls) as tab-separated text, read-only.
# Usage: powershell -File xls-to-tsv.ps1 <report.xls> <output.tsv>
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Target)
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false
try {
    $wb = $xl.Workbooks.Open((Resolve-Path $Source).Path, 0, $true)
    $v = $wb.Worksheets.Item(1).UsedRange.Value2
    $lines = for ($r = 1; $r -le $v.GetLength(0); $r++) {
        (1..$v.GetLength(1) | ForEach-Object { $x = $v[$r, $_]; if ($null -eq $x) { '' } else { "$x" } }) -join "`t"
    }
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($Target), $lines, (New-Object Text.UTF8Encoding $false))
    $wb.Close($false)
} finally { $xl.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl) }
