Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    Select-String -SimpleMatch -Path $_.FullName -Pattern 'new FormsClient()','private Form[] LoadExistingK2Forms','Cast<SourceCode.Forms.Client.Form>','Array.Empty<SourceCode.Forms.Client.Form>' |
      ForEach-Object { Write-Host "$($_.Path):$($_.LineNumber): $($_.Line)" }
}
