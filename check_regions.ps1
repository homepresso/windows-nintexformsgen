$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$lines = Get-Content $path -Raw | Out-String -Stream
$stack = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^\s*#region\b') {
        $stack += $i + 1
        Write-Host "PUSH #region at line $($i+1): $line"
    }
    elseif ($line -match '^\s*#endregion\b') {
        if ($stack.Count -gt 0) {
            $start = $stack[-1]
            $stack = $stack[0..($stack.Count-2)]
            Write-Host "POP  #endregion at line $($i+1): closes region at line $start"
        } else {
            Write-Host "UNMATCHED #endregion at line $($i+1): $line"
        }
    }
}
if ($stack.Count -gt 0) {
    Write-Host "UNMATCHED #region(s) remaining: $($stack.Count)"
    $stack | ForEach-Object { Write-Host "  open at line $_" }
}
else {
    Write-Host "All regions matched"
}
