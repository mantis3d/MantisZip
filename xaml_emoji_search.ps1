# Search XAML files for emoji/unicode in BMP range (U+2600-U+27BF, etc.)
$xamlFiles = Get-ChildItem -Path 'F:\GitHub\MantisZip\src\MantisZip.UI' -Filter '*.xaml' -Recurse
$results = @()
foreach ($f in $xamlFiles) {
    $lines = Get-Content -Path $f.FullName -Encoding UTF8
    $lineNum = 1
    foreach ($line in $lines) {
        $matches = [regex]::Matches($line, '[\u2600-\u27BF\u2B05-\u2B55\u2300-\u23FF\u2190-\u21FF\u25A0-\u25FF]')
        if ($matches.Count -gt 0) {
            $hexes = @()
            $chars = @()
            foreach ($m in $matches) {
                $cp = [int]$m.Value[0]
                $hexes += 'U+' + $cp.ToString('X4')
                $chars += $m.Value
            }
            $results += '' + $f.FullName.Replace('F:\GitHub\MantisZip\src\MantisZip.UI\', '') + '|' + $lineNum + '|' + ($hexes -join ' ') + '|' + ($chars -join '') + '|' + $line.Trim()
        }
        $lineNum++
    }
}
$results
