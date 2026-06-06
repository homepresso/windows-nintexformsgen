$path = 'c:\development\windows-nintexformsgen\FormGenerator\Views\MainWindow.xaml.cs'
$lines = Get-Content $path
$idx = 820
if ($idx -ge $lines.Count) { Write-Host "INDEX_OUT_OF_RANGE"; exit 1 }
$before = if ($idx -gt 0) { $lines[0..($idx-1)] } else { @() }
after = if (($idx + 1) -lt $lines.Count) { $lines[($idx + 1)..($lines.Count - 1)] } else { @() }
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
$fixed = $before + $newLines + $after
Set-Content -Path $path -Value $fixed
Write-Host "REPLACED_LINE_820"