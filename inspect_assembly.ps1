$path = 'c:\development\windows-nintexformsgen\FormGenerator\Writers\K2\References\SourceCode.Forms.Client.dll'
if(-not (Test-Path $path)){
    Write-Host 'MISSING'
    exit 1
}
$a=[Reflection.Assembly]::LoadFrom($path)
$a.GetTypes() | Where-Object { $_.Name -match 'FormsClient|FormServer|Form' } | Select-Object -ExpandProperty FullName | Sort-Object | ForEach-Object { Write-Host $_ }
