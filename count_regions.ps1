$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$lines = Get-Content $path
$regionCount = ($lines | Where-Object { $_ -match '^\s*#region\b' }).Count
$endregionCount = ($lines | Where-Object { $_ -match '^\s*#endregion\b' }).Count
Write-Host "region=$regionCount endregion=$endregionCount"
$lines | Select-Object -Index (84..90) | ForEach-Object { Write-Host $_ }
