$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$lines = Get-Content $path
$start = -1
$end = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($start -eq -1 -and $lines[$i] -match 'private SourceCode\.Forms\.Client\.Form\[\] LoadExistingK2Forms') {
        $start = $i
    }
    if ($lines[$i] -match '^private string BuildK2FormServerConnectionString') {
        $end = $i
        break
    }
}
if ($start -lt 0 -or $end -lt 0) {
    Write-Host "START_OR_END_NOT_FOUND start=$start end=$end"
    exit 1
}
$newLines = @(
    '        private SourceCode.Forms.Client.Form[] LoadExistingK2Forms()',
    '        {',
    '            var connectionString = BuildK2FormServerConnectionString();',
    '',
    '            using var formsClient = new SourceCode.Forms.Client.FormsClient();',
    '',
    '            if (!formsClient.Open(connectionString))',
    '            {',
    '                throw new InvalidOperationException("Unable to open K2 Forms client connection.");',
    '            }',
    '',
    '            var formCollection = formsClient.GetForms((string[])null);',
    '            return formCollection?.Cast<SourceCode.Forms.Client.Form>().ToArray() ?? Array.Empty<SourceCode.Forms.Client.Form>();',
    '        }'
)
$before = if ($start -gt 0) { $lines[0..($start-1)] } else { @() }
$after = if ($end -lt $lines.Count) { $lines[$end..($lines.Count-1)] } else { @() }
$fixed = $before + $newLines + $after
Set-Content -Path $path -Value $fixed
Write-Host "REPLACED start=$start end=$end lines=$($fixed.Count)"
