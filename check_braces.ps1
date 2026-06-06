$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$lines = Get-Content $path
$balance = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    foreach ($c in $line.ToCharArray()) {
        if ($c -eq '{') { $balance++ }
        elseif ($c -eq '}') { $balance-- }
    }
}
Write-Host "Brace balance = $balance"
$open = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match 'class\s+MainWindow') { Write-Host "MainWindow starts at line $($i+1)" }
    if ($line -match 'class\s+AnalyzerFactory') { Write-Host "AnalyzerFactory starts at line $($i+1)" }
}
