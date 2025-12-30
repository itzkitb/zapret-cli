# Configuring TCP Timestamps in Windows

**TCP Timestamps** are a critical network stack feature enabling precise round-trip time (RTT) measurement, protection against sequence number wrapping, and compatibility with modern networking standards. Disabling this option may cause connection issues with certain services, especially when operating behind proxies or under high network latency conditions.

> **Important!** All operations require **local administrator** privileges. Changes take effect immediately without reboot, but may impact network performance in specific scenarios.

---

## Standard Method: via netsh
1. **Open Command Prompt or PowerShell as Administrator**  
   Press `Win + X` and select "Windows Terminal (Admin)" or "Command Prompt (Admin)".

2. **Enable TCP Timestamps**  
   Execute the command:
   ```cmd
   netsh interface tcp set global timestamps=enabled
   ```
   > `enabled` activates TCP timestamps usage.

3. **Verify Status**  
   ```cmd
   netsh interface tcp show global
   ```
   In the output, locate the `Timestamps` parameter – the value should be `enabled`.

---

## Alternative Method: via PowerShell
> **Note**  
> This method is preferred for Windows 10/11 and Server 2016+ as it uses native PowerShell cmdlets.

1. Launch **PowerShell as Administrator**.

2. Execute the command:
   ```powershell
   Set-NetTCPSetting -SettingName InternetCustom -Timestamps enabled
   ```
   > Use `InternetCustom` for external connections or `DatacenterCustom` for internal networks.

3. Confirm changes:
   ```powershell
   Get-NetTCPSetting | Where-Object { $_.SettingName -like "*Custom*" } | Select-Object SettingName, Timestamps
   ```

---

## Troubleshooting
If TCP Timestamps fail to enable or automatically disable:

### 1. Group Policy Verification
- Run `gpedit.msc` and check the path:  
  `Computer Configuration → Administrative Templates → Network → TCP/IP Settings`  
- Ensure the policy **"Enable TCP Timestamps"** isn't overridden.

### 2. Network Driver Analysis
- Update network adapter drivers to the latest version from the manufacturer's website.
- Disable driver-specific features that may conflict with TCP Timestamps (TCP Chimney Offload, Large Send Offload):
  ```powershell
  Get-NetAdapter | Disable-NetAdapterLso
  ```

### 3. Firewall and Antivirus Check
- Temporarily disable third-party network filters:
  ```powershell
  Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False
  ```
  > Remember to re-enable settings after diagnostics!

---

## Optimization and Security
While TCP Timestamps improve performance, they can be exploited to determine system uptime. For balanced security and performance:

```powershell
Set-NetTCPSetting -SettingName InternetCustom `
    -Timestamps enabled `
    -CongestionProvider CTCP `
    -InitialRtoMs 2000 `
    -EcnCapability Disabled
```
> **Critical:** For public servers, disable ECN when using timestamps.

---

## Restoring Default Settings
If changes cause network issues:
```cmd
netsh int tcp set global default
```
Or for specific parameters:
```powershell
Set-NetTCPSetting -SettingName InternetCustom -Timestamps default
```