$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$raw = Get-Content $path -Raw
$idx = $raw.IndexOf('private SourceCode.Forms.Client.Form[] LoadExistingK2Forms()')
Write-Host "IDX=$idx"
if ($idx -ge 0) {
    Write-Host $raw.Substring($idx,200)
}
