$path = 'c:\development\windows-nintexformsgen\FormGenerator\Writers\K2\References\SourceCode.Forms.Client.dll'
$a = [Reflection.Assembly]::LoadFrom($path)
$t = $a.GetType('SourceCode.Forms.Client.FormsClient')
Write-Host "TYPE=$($t -ne $null)"
Write-Host "PUBLIC=$($t.IsPublic)"
