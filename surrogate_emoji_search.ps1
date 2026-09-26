# Search XAML+CS for surrogate pair emoji (U+1Fxxx range)
$allFiles = Get-ChildItem -Path 'F:\GitHub\MantisZip\src\MantisZip.UI' -Include '*.xaml','*.cs' -Recurse
$results = @()
foreach ($f in $allFiles) {
    $lines = Get-Content -Path $f.FullName -Encoding UTF8
    $lineNum = 1
    foreach ($line in $lines) {
        $matches = [regex]::Matches($line, '[\uD800-\uDBFF][\uDC00-\uDFFF]')
        if ($matches.Count -gt 0) {
            $hexes = @()
            $chars = @()
            foreach ($m in $matches) {
                $bytes = [System.Text.Encoding]::UTF32.GetBytes($m.Value)
                $cp = [System.BitConverter]::ToInt32($bytes, 0)
                $hexes += 'U+' + $cp.ToString('X')
                $chars += $m.Value
            }
            $results += '' + $f.FullName.Replace('F:\GitHub\MantisZip\src\MantisZip.UI\', '') + '|' + $lineNum + '|' + ($hexes -join ' ') + '|' + ($chars -join '') + '|' + $line.Trim()
        }
        $lineNum++
    }
}
$results
