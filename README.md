# GameRegionGuard

<img width="836" height="738" alt="image" src="https://github.com/user-attachments/assets/d1af3c96-600c-4b5e-8117-ef124eadc421" />

GameRegionGuard is a Windows tool that creates and removes outbound Windows Firewall rules from CIDR IP blocklists. It helps gamers reduce unwanted matchmaking and improve session quality by limiting connections to selected IP ranges, system-wide or per application.

Default list used by the project:
- https://www.ipdeny.com/ipblocks/data/countries/ru.zone

## Features

- Download CIDR ranges and install outbound block rules in batches
- System-wide mode (affects all applications)
- Per-application mode (affects only a chosen .exe)
- Detailed logging with copy-to-clipboard for issues/debugging
- Preserves previous run log file

## Warnings

- This tool blocks by IP ranges only, it does not block by domain name or server name.
- Blocking IP ranges can break access to legitimate services hosted within those ranges.
- System-wide mode affects everything on the machine.
- Administrator privileges are required to install/remove firewall rules.
- If something stops working, whitelist the ranges that you need OR remove the rules entirely.

## Requirements

Runtime:
- Windows 10 or Windows 11
- Administrator privileges (required for firewall rule changes)

Build:
- .NET 8 SDK
- Visual Studio 2022 with ".NET Desktop Development" workload (recommended) or the `dotnet` CLI

## Build from source

Visual Studio:
1. Open `GameRegionGuard.sln`.
2. Build and run.

Alternatively, you can compile a self-contained executable using `build.bat` file inside the root folder without having to install .NET  
*Note: the EXE is large because it includes the .NET runtime.*

## Logs

`GameRegionGuard.log` - current run of the program
`GameRegionGuard.previous.log` - previous run (overwritten at startup)

When opening an issue, include the relevant log output.


## FAQ

### Can I get banned for this?

No, you should be absolutely safe in using this tool, it does not modify any game files, it creates a set of rules locally on your network which should not break any games' Terms of Services.

### Which games are supported?

You can apply the firewall rules to any game or application (including non-Steam titles). So far, CS2.exe (Counter-Strike 2) and Deadlock.exe have been tested on Steam, and in those tests the setup reduced connections to the targeted IP ranges by roughly 90%.

### I installed rules and now a site or service does not work. What do I do?

You have two options:
- Whitelist only what you need (keep blocking while allowing a specific service)
- Remove all the rules

### How do I find the IP of a site/service?

Option A (nslookup):
```powershell
nslookup example.com
```

Option B (Resolve-DnsName):
```powershell
Resolve-DnsName example.com | Select-Object -ExpandProperty IPAddress
```

Notes:
- Many services return multiple IPs (CDNs), test multiple returned IPs.
- Some services use different domains (api.example.com, cdn.example.com), check the domains involved.

### How do I whitelist a specific IP range?

The tool blocks by CIDR ranges - to whitelist, you typically disable or remove the specific firewall rule(s) that match the CIDR range you want to allow.

Recommended approach:
1. Identify the IP(s) you need (see above Option A | Option B).
2. Check your firewall rules created by GameRegionGuard and locate the rule that contains or matches the relevant CIDR range.
3. Disable that single rule first (preferred) - if it solves the issue, keep it disabled.

Disable rules inside PowerShell:

Temporarily disable all rules created by the tool:
```powershell
Disable-NetFirewallRule -DisplayName "GameRegionGuard*"
```

Re-enable them:
```powershell
Enable-NetFirewallRule -DisplayName "GameRegionGuard*"
```

Disable a single rule by its exact display name:
```powershell
Disable-NetFirewallRule -DisplayName "PUT_THE_RULE_NAME_HERE"
```

Tip:
Use Windows Defender Firewall with Advanced Security to view rules, sort by name, and disable individual entries safely.

### Does this guarantee I will never connect to servers in a specific region?

No. It only blocks the IP ranges included in the blocklist you installed. If a service uses IP space outside that list (cloud regions, relays, VPN endpoints), it may still connect.

## Bugs & Errors

Please [open an issue](https://github.com/xVoidByte/GameRegionGuard/issues) on GitHub with detailed information, relevant log output, and steps to reproduce.  

## License

[MIT License](https://github.com/xVoidByte/GameRegionGuard?tab=MIT-1-ov-file)

