using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using GameRegionGuard.Models;

namespace GameRegionGuard.Services
{
    public class FirewallService
    {
        // New rule prefixes (used for newly created rules)
        private const string RulePrefixSystemWide = "GameRegionGuard_System_";
        private const string RulePrefixApp = "GameRegionGuard_App_";

        // Legacy prefixes (kept for backwards compatibility so the app can detect/remove older installs)
        private const string LegacyRulePrefixSystemWide = "GameBlock_RU_System_";
        private const string LegacyRulePrefixApp = "GameBlock_RU_App_";

        // Batch size: how many rules per PowerShell process call
        // 500 is a good balance - fast startup overhead vs script size
        private const int BatchSize = 500;

        public async Task<InstallationResult> InstallRulesAsync(
            List<string> ipRanges,
            BlockingMode mode,
            string applicationPath,
            IProgress<(int current, int total, string message)> progress,
            CancellationToken cancellationToken)
        {
            var result = new InstallationResult
            {
                TotalRules = ipRanges.Count,
                SuccessCount = 0,
                FailedCount = 0,
                SkippedCount = 0
            };

            Logger.Info($"Starting installation - Mode: {mode}, Total: {ipRanges.Count}, BatchSize: {BatchSize}");

            if (mode == BlockingMode.SpecificApplication)
                Logger.Info($"Target application: {applicationPath}");

            await Task.Run(() =>
            {
                // Step 1: Get all existing rule names in one fast call
                Logger.Info("Checking for existing rules...");
                var existingRules = GetExistingRuleNames();
                Logger.Info($"Found {existingRules.Count} existing matching rules");

                // Step 2: Filter out already-existing rules
                var rulesToCreate = new List<(string ruleName, string ipRange)>();

                foreach (var ipRange in ipRanges)
                {
                    var ruleName = GenerateRuleName(mode, applicationPath, ipRange);
                    var legacyRuleName = GenerateLegacyRuleName(mode, applicationPath, ipRange);

                    if (existingRules.Contains(ruleName) || existingRules.Contains(legacyRuleName))
                    {
                        result.SkippedCount++;
                    }
                    else
                    {
                        rulesToCreate.Add((ruleName, ipRange));
                    }
                }

                Logger.Info($"Rules to create: {rulesToCreate.Count}, Skipped (exist): {result.SkippedCount}");

                if (rulesToCreate.Count == 0)
                {
                    Logger.Info("All rules already exist, nothing to do");
                    result.SuccessCount = 0;
                    return;
                }

                // Step 3: Process in batches
                int processed = 0;
                int totalToCreate = rulesToCreate.Count;

                for (int batchStart = 0; batchStart < totalToCreate; batchStart += BatchSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.WasCancelled = true;
                        Logger.Warning($"Installation cancelled at {processed}/{totalToCreate}");
                        break;
                    }

                    int batchEnd = Math.Min(batchStart + BatchSize, totalToCreate);
                    var batch = rulesToCreate.GetRange(batchStart, batchEnd - batchStart);

                    Logger.Debug($"Processing batch {batchStart / BatchSize + 1}: rules {batchStart + 1}-{batchEnd}");

                    var (batchSuccess, batchFailed) = ExecuteBatch(batch, mode, applicationPath);
                    result.SuccessCount += batchSuccess;
                    result.FailedCount += batchFailed;
                    processed += batch.Count;

                    // Report progress after each batch
                    int totalDone = result.SuccessCount + result.SkippedCount + result.FailedCount;
                    progress?.Report((totalDone, ipRanges.Count, $"Creating rules: {processed}/{totalToCreate}"));
                }

                Logger.Info($"Installation complete - Created: {result.SuccessCount}, Skipped: {result.SkippedCount}, Failed: {result.FailedCount}");

            }, cancellationToken);

            return result;
        }

        public async Task<InstallationResult> RemoveRulesAsync(
            BlockingMode mode,
            string applicationPath,
            IProgress<(int current, int total, string message)> progress,
            CancellationToken cancellationToken)
        {
            var result = new InstallationResult
            {
                SuccessCount = 0,
                FailedCount = 0
            };

            Logger.Info($"Starting removal - Mode: {mode}");

            if (mode == BlockingMode.SpecificApplication)
                Logger.Info($"Target application: {applicationPath}");

            await Task.Run(() =>
            {
                try
                {
                    progress?.Report((0, 1, "Searching for rules to remove..."));

                    if (mode == BlockingMode.SystemWide)
                    {
                        // Remove everything created by this tool (including older legacy rule names)
                        Logger.Info("Removing all GameRegionGuard / legacy rules");
                        var script = @"
$rules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like 'GameRegionGuard_*' -or $_.DisplayName -like 'GameBlock_RU_*' }
$count = $rules.Count
Write-Output ""FOUND:$count""
$rules | Remove-NetFirewallRule
Write-Output ""DONE""
";
                        var output = RunPowerShellScript(script);
                        Logger.Debug($"Removal output: {output}");

                        // Parse count from output
                        foreach (var line in output.Split('\n'))
                        {
                            if (line.StartsWith("FOUND:") && int.TryParse(line.Replace("FOUND:", "").Trim(), out int count))
                            {
                                result.SuccessCount = count;
                                result.TotalRules = count;
                                Logger.Info($"Removed {count} rules");
                            }
                        }
                    }
                    else
                    {
                        var appName = Path.GetFileNameWithoutExtension(applicationPath)?.Replace(" ", "");
                        var prefixNew = $"{RulePrefixApp}{appName}_";
                        var prefixOld = $"{LegacyRulePrefixApp}{appName}_";
                        Logger.Info($"Removing rules with prefixes: {prefixNew} and {prefixOld}");

                        var script = $@"
$rules = Get-NetFirewallRule | Where-Object {{ $_.DisplayName -like '{EscapeForPowerShell(prefixNew)}*' -or $_.DisplayName -like '{EscapeForPowerShell(prefixOld)}*' }}
$count = $rules.Count
Write-Output ""FOUND:$count""
$rules | Remove-NetFirewallRule
Write-Output ""DONE""
";
                        var output = RunPowerShellScript(script);
                        Logger.Debug($"Removal output: {output}");

                        foreach (var line in output.Split('\n'))
                        {
                            if (line.StartsWith("FOUND:") && int.TryParse(line.Replace("FOUND:", "").Trim(), out int count))
                            {
                                result.SuccessCount = count;
                                result.TotalRules = count;
                                Logger.Info($"Removed {count} rules");
                            }
                        }
                    }

                    progress?.Report((1, 1, "Removal complete"));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Fatal error during removal: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }, cancellationToken);

            return result;
        }

        private (int success, int failed) ExecuteBatch(
            List<(string ruleName, string ipRange)> batch,
            BlockingMode mode,
            string applicationPath)
        {
            try
            {
                // Build a single PowerShell script for the entire batch
                var sb = new StringBuilder();
                sb.AppendLine("$success = 0");
                sb.AppendLine("$failed = 0");
                sb.AppendLine("$policy = New-Object -ComObject HNetCfg.FwPolicy2");

                foreach (var (ruleName, ipRange) in batch)
                {
                    sb.AppendLine("try {");
                    sb.AppendLine("  $rule = New-Object -ComObject HNetCfg.FWRule");
                    sb.AppendLine($"  $rule.Name = '{EscapeForPowerShell(ruleName)}'");
                    sb.AppendLine($"  $rule.Description = 'Blocked region IP range: {ipRange}'");
                    sb.AppendLine("  $rule.Direction = 2"); // Outbound
                    sb.AppendLine("  $rule.Action = 0");    // Block
                    sb.AppendLine($"  $rule.RemoteAddresses = '{ipRange}'");
                    sb.AppendLine("  $rule.Enabled = $true");

                    if (mode == BlockingMode.SpecificApplication && !string.IsNullOrEmpty(applicationPath))
                    {
                        sb.AppendLine($"  $rule.ApplicationName = '{EscapeForPowerShell(applicationPath)}'");
                    }

                    sb.AppendLine("  $policy.Rules.Add($rule)");
                    sb.AppendLine("  $success++");
                    sb.AppendLine("} catch {");
                    sb.AppendLine("  $failed++");
                    sb.AppendLine("}");
                }

                // NOTE: In PowerShell, a colon directly after a variable name can be parsed as a scope/drive qualifier
                // (e.g. $env:Path). Using ${} avoids the parser treating "$success:" as a drive-qualified variable.
                sb.AppendLine("Write-Output \"RESULT:${success}:${failed}\"");

                var output = RunPowerShellScript(sb.ToString());

                // Parse result
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("RESULT:"))
                    {
                        var parts = trimmed.Replace("RESULT:", "").Split(':');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int s) &&
                            int.TryParse(parts[1], out int f))
                        {
                            Logger.Debug($"Batch result: {s} success, {f} failed");
                            return (s, f);
                        }
                    }
                }

                // If no RESULT line parsed, assume all succeeded
                return (batch.Count, 0);
            }
            catch (Exception ex)
            {
                Logger.Error($"Batch execution error: {ex.Message}");
                return (0, batch.Count);
            }
        }

        private HashSet<string> GetExistingRuleNames()
        {
            try
            {
                var script = "Get-NetFirewallRule | Where-Object { $_.DisplayName -like 'GameRegionGuard_*' -or $_.DisplayName -like 'GameBlock_RU_*' } | Select-Object -ExpandProperty DisplayName";
                var output = RunPowerShellScript(script);

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        names.Add(trimmed);
                }
                return names;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not retrieve existing rules: {ex.Message}");
                return new HashSet<string>();
            }
        }

        private string RunPowerShellScript(string script)
        {
            // Write script to temp file to avoid command line length limits
            var tempFile = Path.Combine(Path.GetTempPath(), $"grg_{Guid.NewGuid():N}.ps1");

            try
            {
                File.WriteAllText(tempFile, script, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Logger.Debug($"PowerShell stderr: {error.Trim()}");
                    }

                    if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                    {
                        throw new Exception($"PowerShell exited {process.ExitCode}: {error.Trim()}");
                    }

                    return output;
                }
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private string GenerateRuleName(BlockingMode mode, string applicationPath, string ipRange)
        {
            var sanitizedRange = ipRange.Replace("/", "_").Replace(".", "-");

            if (mode == BlockingMode.SystemWide)
                return $"{RulePrefixSystemWide}{sanitizedRange}";

            var appName = Path.GetFileNameWithoutExtension(applicationPath)?.Replace(" ", "");
            return $"{RulePrefixApp}{appName}_{sanitizedRange}";
        }

        private string GenerateLegacyRuleName(BlockingMode mode, string applicationPath, string ipRange)
        {
            var sanitizedRange = ipRange.Replace("/", "_").Replace(".", "-");

            if (mode == BlockingMode.SystemWide)
                return $"{LegacyRulePrefixSystemWide}{sanitizedRange}";

            var appName = Path.GetFileNameWithoutExtension(applicationPath)?.Replace(" ", "");
            return $"{LegacyRulePrefixApp}{appName}_{sanitizedRange}";
        }

        private string EscapeForPowerShell(string value)
        {
            return value?.Replace("'", "''") ?? string.Empty;
        }
    }
}
