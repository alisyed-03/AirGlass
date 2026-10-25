# AirGlass 2.7 installer

`AirGlass-Setup-2.7.1.exe` is a complete per-user Windows installer. Rebuilt setup accepts Windows 10/11 x64 and Windows 11 ARM64 through x64 emulation (`x64compatible`); Windows 10 ARM is rejected. Existing published setup binaries are unchanged. See `WINDOWS-ARM.md` for ARM validation status. It installs the full app under LocalAppData/Programs/AirGlass by default, creates a Start menu shortcut, offers a desktop shortcut, and provides a Windows Apps uninstall entry. An existing AirGlass process must close before setup proceeds.

Setup checks .NET Framework 4.8 before installation. If absent, interactive setup offers the official Microsoft page and stops without installing. Bonjour is not redistributed: if absent, setup explains the requirement and offers Apple's official download page on completion. Silent setup never opens a website or launches the app. Missing-prerequisite branches were inspected in source; they were not exercised by removing system prerequisites from this PC.

The installed app retains its automatic decoder selection, receiver startup and discovery fixes. No system driver, firewall or network settings are changed. Uninstall removes owned application files and shortcuts; saved settings/keys under LocalAppData/AirGlass are outside the installation and are retained.

Build using Inno Setup 6.7+ with `source/build-installer.ps1`. This release used the verified publisher-signed Inno Setup 6.7.3 compiler. The resulting AirGlass setup is unsigned. Compiler source/tool: https://jrsoftware.org/isinfo.php

The 2.5 installer engine previously passed the following install/uninstall checks. The 2.6 app payload is tested separately above and adds cleanup of the old NVIDIA plugin path:

```text
PASS: per-user unattended install into an isolated Unicode/spaced folder
PASS: 155 shipped binary/artwork/source-archive payload files match their installed bytes
PASS: Windows per-user uninstall entry targets the isolated installation
PASS: Start menu application shortcut created
PASS: installed app self-tests, including receiver startup, settings and graceful shutdown, with a clean PATH
PASS: uninstaller removed application files, shortcut and its uninstall registration
```

This is not a clean Windows VM or a test of every hardware configuration. The earlier positive 2.5 iPhone result was followed by a recurrence; 2.6 adds further lifecycle checks.
