using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FormGenerator.Analyzers.Infopath;

namespace FormGenerator.Services
{
    /// <summary>
    /// PowerShell flavor for the generated SharePoint provisioning script.
    /// </summary>
    public enum SharePointScriptFlavor
    {
        /// <summary>PnP.PowerShell (on-prem SP 2016/2019/SE and SharePoint Online).</summary>
        PnP,
        /// <summary>Legacy server-side Microsoft.SharePoint.PowerShell snap-in (on-prem only).</summary>
        Legacy
    }

    /// <summary>
    /// Generates SharePoint list provisioning artifacts from an analyzed InfoPath form:
    ///   • a CSV of the list columns (display name / internal name / type / required / choices), and
    ///   • a PowerShell script (PnP or legacy SharePoint Management Shell) that creates the main
    ///     list, a child list per repeating section, and a lookup from each child back to the parent.
    /// </summary>
    public class SharePointGeneratorService
    {
        public class SharePointColumn
        {
            public string DisplayName { get; set; }
            public string InternalName { get; set; }
            public string Type { get; set; }             // Text, Note, Choice, DateTime, Boolean, Number, Lookup
            public bool Required { get; set; }
            public List<string> Choices { get; set; } = new List<string>();
            public bool IsLookup => string.Equals(Type, "Lookup", StringComparison.OrdinalIgnoreCase);
        }

        public class SharePointList
        {
            public string Name { get; set; }             // resolved display name of the list
            public bool IsChild { get; set; }
            public string ParentListName { get; set; }   // resolved parent list name (child lists only)
            public string SectionName { get; set; }      // repeating-section name (child lists only)
            public List<SharePointColumn> Columns { get; set; } = new List<SharePointColumn>();
        }

        private const string ParentLookupInternalName = "ParentItem";
        private const string ParentLookupDisplayName = "Parent Item";

        /// <summary>
        /// Builds the main list plus one child list per repeating section. Each child list begins
        /// with a Lookup column back to the parent list's Title.
        /// </summary>
        public List<SharePointList> BuildLists(InfoPathFormDefinition form, string mainListName)
        {
            var lists = new List<SharePointList>();
            if (form?.Data == null) return lists;

            if (string.IsNullOrWhiteSpace(mainListName))
                mainListName = ResolveListName(form);

            // Main list = non-repeating columns.
            var mainList = new SharePointList { Name = mainListName, IsChild = false };
            var mainUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mainUsedDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in form.Data.Where(d => d != null && !d.IsRepeating))
            {
                var col = ToColumn(dc, mainUsed, mainUsedDisplay);
                if (col != null) mainList.Columns.Add(col);
            }
            lists.Add(mainList);

            // Child lists = one per distinct repeating section.
            var sections = form.Data
                .Where(d => d != null && d.IsRepeating && !string.IsNullOrWhiteSpace(d.RepeatingSection))
                .GroupBy(d => d.RepeatingSection, StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                string sectionDisplay = CleanSectionName(section.Key);
                var child = new SharePointList
                {
                    Name = $"{mainListName} - {sectionDisplay}",
                    IsChild = true,
                    ParentListName = mainListName,
                    SectionName = sectionDisplay
                };

                // Lookup back to the parent list.
                child.Columns.Add(new SharePointColumn
                {
                    DisplayName = ParentLookupDisplayName,
                    InternalName = ParentLookupInternalName,
                    Type = "Lookup",
                    Required = false
                });

                var childUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ParentLookupInternalName };
                var childUsedDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ParentLookupDisplayName };
                foreach (var dc in section)
                {
                    var col = ToColumn(dc, childUsed, childUsedDisplay);
                    if (col != null) child.Columns.Add(col);
                }
                lists.Add(child);
            }

            return lists;
        }

        /// <summary>
        /// Generates a CSV describing every list (main + child) and its columns.
        /// </summary>
        public string GenerateColumnsCsv(InfoPathFormDefinition form, string mainListName = null)
        {
            var lists = BuildLists(form, mainListName);

            var sb = new StringBuilder();
            sb.AppendLine("List,IsChildList,ParentList,DisplayName,InternalName,Type,Required,Choices,LookupTo");

            foreach (var list in lists)
            {
                foreach (var c in list.Columns)
                {
                    sb.Append(Csv(list.Name)).Append(',');
                    sb.Append(list.IsChild ? "Yes" : "No").Append(',');
                    sb.Append(Csv(list.ParentListName)).Append(',');
                    sb.Append(Csv(c.DisplayName)).Append(',');
                    sb.Append(Csv(c.InternalName)).Append(',');
                    sb.Append(Csv(c.Type)).Append(',');
                    sb.Append(c.Required ? "Yes" : "No").Append(',');
                    sb.Append(Csv(string.Join("|", c.Choices))).Append(',');
                    sb.Append(Csv(c.IsLookup ? list.ParentListName : string.Empty));
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a PowerShell script that provisions the main list, child lists, columns and
        /// parent lookups, in the requested flavor.
        /// </summary>
        public string GeneratePowerShellScript(InfoPathFormDefinition form, string siteUrl, string listName,
            SharePointScriptFlavor flavor)
        {
            if (string.IsNullOrWhiteSpace(listName)) listName = ResolveListName(form);
            if (string.IsNullOrWhiteSpace(siteUrl)) siteUrl = "https://your-sharepoint-site/sites/yoursite";

            var lists = BuildLists(form, listName);

            return flavor == SharePointScriptFlavor.Legacy
                ? BuildLegacyScript(form, siteUrl, listName, lists)
                : BuildPnPScript(form, siteUrl, listName, lists);
        }

        // ── PnP.PowerShell ───────────────────────────────────────────────────────

        private string BuildPnPScript(InfoPathFormDefinition form, string siteUrl, string listName, List<SharePointList> lists)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<#");
            sb.AppendLine("    SharePoint list provisioning script (PnP PowerShell)");
            sb.AppendLine($"    Generated by FormGenerator from InfoPath form: {form?.FormName ?? listName}");
            sb.AppendLine("    Required module (same cmdlets, different package per platform):");
            sb.AppendLine("      • SharePoint Online / SE :  Install-Module PnP.PowerShell -Scope CurrentUser");
            sb.AppendLine("      • On-prem 2016 / 2019    :  Install-Module SharePointPnPPowerShell2019 -Scope CurrentUser");
            sb.AppendLine("    (For on-prem you can also use the 'Legacy SharePoint Management Shell' script option.)");
            sb.AppendLine("#>");
            sb.AppendLine();
            sb.AppendLine("param(");
            sb.AppendLine($"    [string]$SiteUrl  = \"{PsString(siteUrl)}\",");
            sb.AppendLine($"    [string]$ListName = \"{PsString(listName)}\"");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine();
            sb.AppendLine("# --- Ensure the PnP cmdlets are available ---");
            sb.AppendLine("if (-not (Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue)) {");
            sb.AppendLine("    Import-Module PnP.PowerShell -ErrorAction SilentlyContinue");
            sb.AppendLine("    if (-not (Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue)) {");
            sb.AppendLine("        Import-Module SharePointPnPPowerShell2019 -ErrorAction SilentlyContinue");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("if (-not (Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue)) {");
            sb.AppendLine("    Write-Host 'PnP PowerShell is not installed.' -ForegroundColor Red");
            sb.AppendLine("    Write-Host '  SharePoint Online / SE :  Install-Module PnP.PowerShell -Scope CurrentUser' -ForegroundColor Yellow");
            sb.AppendLine("    Write-Host '  On-prem 2016 / 2019    :  Install-Module SharePointPnPPowerShell2019 -Scope CurrentUser' -ForegroundColor Yellow");
            sb.AppendLine("    Write-Host 'Then re-run this script (or use the Legacy SharePoint Management Shell script).' -ForegroundColor Yellow");
            sb.AppendLine("    return");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("# --- Connect ---");
            sb.AppendLine("# On-prem (current user):  Connect-PnPOnline -Url $SiteUrl -CurrentCredentials");
            sb.AppendLine("# On-prem (prompt):        Connect-PnPOnline -Url $SiteUrl -Credentials (Get-Credential)");
            sb.AppendLine("# SharePoint Online:       Connect-PnPOnline -Url $SiteUrl -Interactive");
            sb.AppendLine("Connect-PnPOnline -Url $SiteUrl -CurrentCredentials");
            sb.AppendLine();

            for (int i = 0; i < lists.Count; i++)
            {
                var list = lists[i];
                string listVar = list.IsChild ? $"$childList{i}" : "$mainList";
                string nameExpr = list.IsChild ? $"\"$ListName - {PsString(list.SectionName)}\"" : "$ListName";

                sb.AppendLine($"# ===== List: {list.Name} =====");
                sb.AppendLine($"$listName{i} = {nameExpr}");
                sb.AppendLine($"{listVar} = Get-PnPList -Identity $listName{i} -ErrorAction SilentlyContinue");
                sb.AppendLine($"if ($null -eq {listVar}) {{");
                sb.AppendLine($"    {listVar} = New-PnPList -Title $listName{i} -Template GenericList -OnQuickLaunch");
                sb.AppendLine($"    Write-Host \"Created list: $listName{i}\" -ForegroundColor Green");
                sb.AppendLine("} else {");
                sb.AppendLine($"    Write-Host \"List already exists: $listName{i}\" -ForegroundColor Yellow");
                sb.AppendLine("}");

                foreach (var c in list.Columns)
                    EmitPnPColumn(sb, $"$listName{i}", c);

                // Ensure every column appears on the list's default view (and set their order).
                var fieldNames = new List<string> { "Title" };
                fieldNames.AddRange(list.Columns.Select(c => c.InternalName));
                string fieldArray = string.Join(", ", fieldNames.Select(n => "\"" + PsString(n) + "\""));
                sb.AppendLine($"$defaultView{i} = (Get-PnPView -List $listName{i} | Where-Object {{ $_.DefaultView }} | Select-Object -First 1).Title");
                sb.AppendLine($"if ($defaultView{i}) {{ Set-PnPView -List $listName{i} -Identity $defaultView{i} -Fields {fieldArray} | Out-Null }}");

                sb.AppendLine();
            }

            sb.AppendLine("Write-Host \"Done provisioning SharePoint lists from the InfoPath form.\" -ForegroundColor Green");
            sb.AppendLine("Disconnect-PnPOnline");
            return sb.ToString();
        }

        private void EmitPnPColumn(StringBuilder sb, string listNameVar, SharePointColumn c)
        {
            sb.AppendLine($"if ($null -eq (Get-PnPField -List {listNameVar} -Identity \"{PsString(c.InternalName)}\" -ErrorAction SilentlyContinue)) {{");

            if (c.IsLookup)
            {
                // Lookup back to the parent list ($mainList) by Title.
                sb.AppendLine($"    $lookupXml = \"<Field Type='Lookup' DisplayName='{PsXmlAttr(c.DisplayName)}' Name='{PsXmlAttr(c.InternalName)}' List='$($mainList.Id)' ShowField='Title' />\"");
                sb.AppendLine($"    Add-PnPFieldFromXml -List {listNameVar} -FieldXml $lookupXml | Out-Null");
            }
            else if (string.Equals(c.Type, "Choice", StringComparison.OrdinalIgnoreCase) && c.Choices.Count > 0)
            {
                string choiceArray = string.Join(", ", c.Choices.Select(ch => "\"" + PsString(ch) + "\""));
                sb.AppendLine($"    Add-PnPField -List {listNameVar} -DisplayName \"{PsString(c.DisplayName)}\" -InternalName \"{PsString(c.InternalName)}\" -Type Choice -Choices {choiceArray} -AddToDefaultView | Out-Null");
            }
            else
            {
                sb.AppendLine($"    Add-PnPField -List {listNameVar} -DisplayName \"{PsString(c.DisplayName)}\" -InternalName \"{PsString(c.InternalName)}\" -Type {c.Type} -AddToDefaultView | Out-Null");
            }
            // Note: view membership is also enforced per-list via Set-PnPView below (covers the lookup field too).

            if (c.Required)
                sb.AppendLine($"    Set-PnPField -List {listNameVar} -Identity \"{PsString(c.InternalName)}\" -Values @{{ Required = $true }} | Out-Null");

            sb.AppendLine($"    Write-Host \"  + {PsString(c.DisplayName)}\"");
            sb.AppendLine("}");
        }

        // ── Legacy Microsoft.SharePoint.PowerShell snap-in (server-side, on-prem) ──

        private string BuildLegacyScript(InfoPathFormDefinition form, string siteUrl, string listName, List<SharePointList> lists)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<#");
            sb.AppendLine("    SharePoint list provisioning script (legacy SharePoint Management Shell)");
            sb.AppendLine($"    Generated by FormGenerator from InfoPath form: {form?.FormName ?? listName}");
            sb.AppendLine("    Run ON the SharePoint server using the SharePoint Management Shell (server-side object model).");
            sb.AppendLine("    On-prem only (SharePoint Server 2013/2016/2019).");
            sb.AppendLine("#>");
            sb.AppendLine();
            sb.AppendLine("param(");
            sb.AppendLine($"    [string]$SiteUrl  = \"{PsString(siteUrl)}\",");
            sb.AppendLine($"    [string]$ListName = \"{PsString(listName)}\"");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("Add-PSSnapin Microsoft.SharePoint.PowerShell -ErrorAction SilentlyContinue");
            sb.AppendLine();
            sb.AppendLine("$web = Get-SPWeb $SiteUrl");
            sb.AppendLine();
            sb.AppendLine("function Ensure-SPList($web, $title) {");
            sb.AppendLine("    $list = $web.Lists.TryGetList($title)");
            sb.AppendLine("    if ($null -eq $list) {");
            sb.AppendLine("        $guid = $web.Lists.Add($title, \"\", [Microsoft.SharePoint.SPListTemplateType]::GenericList)");
            sb.AppendLine("        $list = $web.Lists[$guid]");
            sb.AppendLine("        Write-Host \"Created list: $title\" -ForegroundColor Green");
            sb.AppendLine("    } else {");
            sb.AppendLine("        Write-Host \"List already exists: $title\" -ForegroundColor Yellow");
            sb.AppendLine("    }");
            sb.AppendLine("    return $list");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("try {");

            for (int i = 0; i < lists.Count; i++)
            {
                var list = lists[i];
                string listVar = list.IsChild ? $"$childList{i}" : "$mainList";
                string nameExpr = list.IsChild ? $"\"$ListName - {PsString(list.SectionName)}\"" : "$ListName";

                sb.AppendLine($"    # ===== List: {list.Name} =====");
                sb.AppendLine($"    {listVar} = Ensure-SPList $web {nameExpr}");

                foreach (var c in list.Columns)
                    EmitLegacyColumn(sb, listVar, c);

                sb.AppendLine($"    {listVar}.Update()");
                sb.AppendLine();
            }

            sb.AppendLine("    Write-Host \"Done provisioning SharePoint lists from the InfoPath form.\" -ForegroundColor Green");
            sb.AppendLine("}");
            sb.AppendLine("finally {");
            sb.AppendLine("    if ($web) { $web.Dispose() }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private void EmitLegacyColumn(StringBuilder sb, string listVar, SharePointColumn c)
        {
            sb.AppendLine($"    if (-not {listVar}.Fields.ContainsField(\"{PsString(c.DisplayName)}\")) {{");

            if (c.IsLookup)
            {
                // Lookup back to the parent list ($mainList) by Title.
                sb.AppendLine($"        $internalName = {listVar}.Fields.AddLookup(\"{PsString(c.DisplayName)}\", $mainList.ID, $false)");
                sb.AppendLine($"        $lookupField = {listVar}.Fields.GetFieldByInternalName($internalName)");
                sb.AppendLine("        $lookupField.LookupField = \"Title\"");
                sb.AppendLine("        $lookupField.Update()");
            }
            else if (string.Equals(c.Type, "Choice", StringComparison.OrdinalIgnoreCase) && c.Choices.Count > 0)
            {
                sb.AppendLine($"        $internalName = {listVar}.Fields.Add(\"{PsString(c.DisplayName)}\", [Microsoft.SharePoint.SPFieldType]::Choice, ${(c.Required ? "true" : "false")})");
                sb.AppendLine($"        $f = {listVar}.Fields.GetFieldByInternalName($internalName)");
                sb.AppendLine("        $f.Choices.Clear()");
                foreach (var ch in c.Choices)
                    sb.AppendLine($"        $f.Choices.Add(\"{PsString(ch)}\")");
                sb.AppendLine("        $f.Update()");
            }
            else
            {
                string spType = ToLegacyFieldType(c.Type);
                sb.AppendLine($"        $internalName = {listVar}.Fields.Add(\"{PsString(c.DisplayName)}\", [Microsoft.SharePoint.SPFieldType]::{spType}, ${(c.Required ? "true" : "false")})");
            }

            // Add the new field to the list's default view so it shows up.
            sb.AppendLine($"        $view = {listVar}.DefaultView");
            sb.AppendLine("        if (-not $view.ViewFields.Exists($internalName)) {");
            sb.AppendLine("            $view.ViewFields.Add($internalName)");
            sb.AppendLine("            $view.Update()");
            sb.AppendLine("        }");
            sb.AppendLine($"        Write-Host \"  + {PsString(c.DisplayName)}\"");
            sb.AppendLine("    }");
        }

        // ── Mapping / helpers ──────────────────────────────────────────────────────

        private SharePointColumn ToColumn(DataColumn dc, HashSet<string> usedInternal, HashSet<string> usedDisplay)
        {
            // SharePoint display name = the InfoPath control display name / label.
            // SharePoint internal name = a deterministic, space-free name from the InfoPath field name,
            // so the K2 form generated from this list carries field names that map back to InfoPath.
            string display = !string.IsNullOrWhiteSpace(dc.DisplayName) ? dc.DisplayName : dc.ColumnName;
            if (string.IsNullOrWhiteSpace(display)) return null;

            string internalName = MakeUniqueInternalName(
                ToInternalName(!string.IsNullOrWhiteSpace(dc.ColumnName) ? dc.ColumnName : display), usedInternal);

            // SharePoint requires unique display names within a list; disambiguate collisions so no
            // column is silently dropped on provisioning (which would break the K2 mapping).
            string uniqueDisplay = MakeUniqueDisplayName(display.Trim(), usedDisplay);

            var choices = (dc.ValidValues ?? new List<DataOption>())
                .Select(v => !string.IsNullOrWhiteSpace(v.DisplayText) ? v.DisplayText : v.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SharePointColumn
            {
                DisplayName = uniqueDisplay,
                InternalName = internalName,
                Type = MapToSharePointType(dc, choices.Count > 0),
                Required = dc.IsRequired,
                Choices = choices
            };
        }

        private static string MakeUniqueDisplayName(string baseName, HashSet<string> used)
        {
            string name = baseName;
            int i = 1;
            while (used.Contains(name))
                name = $"{baseName} {++i}";
            used.Add(name);
            return name;
        }

        private static string ResolveListName(InfoPathFormDefinition form)
        {
            if (!string.IsNullOrWhiteSpace(form?.Title)) return form.Title;
            if (!string.IsNullOrWhiteSpace(form?.FormName)) return form.FormName;
            return "InfoPath List";
        }

        private static string CleanSectionName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Items";
            return raw.Replace("_", " ").Trim();
        }

        private static string MapToSharePointType(DataColumn col, bool hasChoices)
        {
            string t = (col.Type ?? string.Empty).ToLowerInvariant();
            string dt = (col.DataType ?? string.Empty).ToLowerInvariant();

            if (hasChoices) return "Choice";
            if (t.Contains("richtext") || t.Contains("multiline")) return "Note";
            if (t.Contains("dropdown") || t.Contains("combobox") || t.Contains("listbox") || t.Contains("radio")) return "Choice";
            if (t.Contains("date") || dt.Contains("date")) return "DateTime";
            if (t.Contains("checkbox") || t.Contains("bool") || dt.Contains("bit") || dt.Contains("bool")) return "Boolean";
            if (t.Contains("number") || t.Contains("decimal") || t.Contains("currency") ||
                dt.Contains("int") || dt.Contains("decimal") || dt.Contains("float") || dt.Contains("double") || dt.Contains("money") || dt.Contains("number"))
                return "Number";
            return "Text";
        }

        private static string ToLegacyFieldType(string spType)
        {
            switch ((spType ?? "Text").ToLowerInvariant())
            {
                case "note": return "Note";
                case "choice": return "Choice";
                case "datetime": return "DateTime";
                case "boolean": return "Boolean";
                case "number": return "Number";
                default: return "Text";
            }
        }

        private static string ToInternalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Field";
            var sb = new StringBuilder();
            foreach (char ch in name)
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);

            string s = sb.ToString();
            if (s.Length == 0) s = "Field";
            if (!char.IsLetter(s[0])) s = "F" + s;
            if (s.Length > 32) s = s.Substring(0, 32);
            return s;
        }

        private static string MakeUniqueInternalName(string baseName, HashSet<string> used)
        {
            string name = baseName;
            int i = 1;
            while (used.Contains(name))
            {
                string suffix = (++i).ToString();
                string root = baseName.Length + suffix.Length > 32 ? baseName.Substring(0, 32 - suffix.Length) : baseName;
                name = root + suffix;
            }
            used.Add(name);
            return name;
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string PsString(string value)
        {
            // Escape for a PowerShell double-quoted string.
            return (value ?? string.Empty).Replace("`", "``").Replace("\"", "`\"").Replace("$", "`$");
        }

        private static string PsXmlAttr(string value)
        {
            // Escape for use inside a single-quoted XML attribute embedded in a PS double-quoted string.
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
