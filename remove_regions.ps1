$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$backup = $path + '.bak2'
Copy-Item -Path $path -Destination $backup -Force
$lines = Get-Content $path
$filtered = $lines | Where-Object { $_ -notmatch '^\s*#(region|endregion)\b' }
$filtered | Set-Content $path
Write-Host "Removed $($lines.Count - $filtered.Count) region lines from $path; backup at $backup"