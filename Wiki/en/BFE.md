# Managing the Base Filtering Engine (BFE) Service

The **Base Filtering Engine (BFE)** service is critical for Windows Defender Firewall network filtering functionality. Stopping this service may cause network security and connectivity issues. Below are verified methods to start and restore the service.

> **Important!** All operations require **local administrator** privileges. Edit the registry with caution – incorrect changes may disrupt system operation.

---

## Standard Startup Method
1. **Open Services Console**  
   Press `Win + R`, type `services.msc`, and confirm with **Enter**.

2. **Locate the Service**  
   Find this entry in the list:  
   `Base Filtering Engine` (displayed as *«Base Filtering Engine»* in English Windows versions).

3. **Check Status**  
   • **Running** – no further action needed.  
   • **Stopped** – right-click → **Start**.  
   • **Disabled** – proceed to Alternative Method.

---

## Alternative Method: Registry Restoration
> **Warning**  
> Registry modification is recommended only when startup via `services.msc` fails. Create a system restore point before proceeding.

1. Launch **Command Prompt as Administrator**:
   ```cmd
   reg add "HKLM\SYSTEM\CurrentControlSet\services\BFE" /v Start /t REG_DWORD /d 2 /f
   ```
   > `2` sets automatic service startup. The `/f` parameter forces changes without confirmation.

2. Reboot the computer and verify service status via `services.msc`.

---

## Diagnostics for Persistent Failures
If the service fails to start after the methods above:

### 1. Malware Check
- Perform full scans using trusted tools:
  - **Kaspersky Virus Removal Tool** (free version)
  - **Microsoft Safety Scanner** (official Microsoft tool)

### 2. System File Repair
In **Administrator Command Prompt**, execute sequentially:
```cmd
:: Verify system file integrity
sfc /scannow

:: Diagnose system image health
DISM /Online /Cleanup-Image /CheckHealth
DISM /Online /Cleanup-Image /ScanHealth
DISM /Online /Cleanup-Image /RestoreHealth
```
> **Reboot the PC after completing all commands.**

---

## Last Resort Measures
If the issue persists:
1. **Update Windows** via Windows Update.
2. **Check software conflicts**:  
   Disable third-party antivirus/firewalls via **Safe Mode**.
3. **Restore System** to a point where the service functioned correctly.

> **Windows reinstallation** is the final step for critically damaged system components. Before proceeding, back up user data and create partition backups.