# Build-Installer.ps1
# Assumes this script lives in the scripts/ folder; paths are resolved relative to the repo root.
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$stagingDir = "$repoRoot\build_staging"
$installerProjectDir = "$stagingDir\installer_project"
$installerPath = "$repoRoot\VibeAlarmInstaller.exe"

# 1. Clean previous staging
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Force -Path $stagingDir
if (Test-Path $installerPath) {
    Remove-Item -Force $installerPath
}

# 2. Publish Project in Release Mode
# IncludeAllContentForSelfExtract bundles the ui\ SPA (and Assets) inside the
# single-file exe — the hybrid's WebView2 content travels with the binary.
Write-Host "Publishing project in Release mode..."
dotnet publish "$repoRoot\VibeAlarm.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeAllContentForSelfExtract=true -o "$stagingDir\publish"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# 3. Copy files to flat staging directory for IExpress
Copy-Item -Path "$stagingDir\publish\VibeAlarm.exe" -Destination "$stagingDir\VibeAlarm.exe"
Copy-Item -Path "$repoRoot\Assets\alarm.wav" -Destination "$stagingDir\alarm.wav"

# 4. Write install.ps1 to staging directory
$installScriptContent = @'
Add-Type -AssemblyName System.Windows.Forms
$installDir = "$env:LOCALAPPDATA\VibeAlarm"
$assetsDir = "$installDir\Assets"

# Create Directories
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Force -Path $installDir
}
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Force -Path $assetsDir
}

# Copy files
$srcDir = $PSScriptRoot
Copy-Item -Path "$srcDir\VibeAlarm.exe" -Destination "$installDir\VibeAlarm.exe" -Force
Copy-Item -Path "$srcDir\alarm.wav" -Destination "$assetsDir\alarm.wav" -Force

# Create Shortcuts
$wshShell = New-Object -ComObject WScript.Shell

# Desktop Shortcut
$desktopPath = [System.Environment]::GetFolderPath("Desktop")
$shortcut = $wshShell.CreateShortcut("$desktopPath\VibeAlarm.lnk")
$shortcut.TargetPath = "$installDir\VibeAlarm.exe"
$shortcut.WorkingDirectory = $installDir
$shortcut.Save()

# Start Menu Shortcut
$startMenuPath = [System.Environment]::GetFolderPath("StartMenu")
$programsPath = "$startMenuPath\Programs"
$shortcutSM = $wshShell.CreateShortcut("$programsPath\VibeAlarm.lnk")
$shortcutSM.TargetPath = "$installDir\VibeAlarm.exe"
$shortcutSM.WorkingDirectory = $installDir
$shortcutSM.Save()

# Registry Auto-Start
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $registryPath -Name "VibeAlarm" -Value "`"$installDir\VibeAlarm.exe`""

# Launch App
Start-Process -FilePath "$installDir\VibeAlarm.exe" -WorkingDirectory $installDir

[System.Windows.Forms.MessageBox]::Show("VibeAlarm has been successfully installed!", "Installation Complete", 0, 64)
'@

$installScriptContent | Out-File -FilePath "$stagingDir\install.ps1" -Encoding utf8

# 5. Create a self-contained Windows installer project.
New-Item -ItemType Directory -Force -Path $installerProjectDir | Out-Null
$installerCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>VibeAlarmInstaller</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="payload\VibeAlarm.exe" LogicalName="VibeAlarm.exe" />
    <EmbeddedResource Include="payload\alarm.wav" LogicalName="alarm.wav" />
  </ItemGroup>
</Project>
'@
$installerProgram = @'
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "VibeAlarm");
var assetsDir = Path.Combine(installDir, "Assets");
Directory.CreateDirectory(installDir);
Directory.CreateDirectory(assetsDir);

ExtractResource("VibeAlarm.exe", Path.Combine(installDir, "VibeAlarm.exe"));
ExtractResource("alarm.wav", Path.Combine(assetsDir, "alarm.wav"));

CreateShortcut(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VibeAlarm.lnk"),
    Path.Combine(installDir, "VibeAlarm.exe"),
    installDir);

var programsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
    "Programs");
Directory.CreateDirectory(programsDir);
CreateShortcut(
    Path.Combine(programsDir, "VibeAlarm.lnk"),
    Path.Combine(installDir, "VibeAlarm.exe"),
    installDir);

using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
{
    runKey?.SetValue("VibeAlarm", '"' + Path.Combine(installDir, "VibeAlarm.exe") + '"');
}

Process.Start(new ProcessStartInfo
{
    FileName = Path.Combine(installDir, "VibeAlarm.exe"),
    WorkingDirectory = installDir,
    UseShellExecute = true
});

MessageBox.Show("VibeAlarm has been successfully installed!", "Installation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

static void ExtractResource(string resourceName, string destinationPath)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Missing embedded installer payload: {resourceName}");
    using var file = File.Create(destinationPath);
    stream.CopyTo(file);
}

static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell")
        ?? throw new InvalidOperationException("WScript.Shell is not available.");
    dynamic shell = Activator.CreateInstance(shellType)
        ?? throw new InvalidOperationException("Could not create WScript.Shell.");
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = targetPath;
    shortcut.WorkingDirectory = workingDirectory;
    shortcut.Save();
    Marshal.FinalReleaseComObject(shortcut);
    Marshal.FinalReleaseComObject(shell);
}
'@

New-Item -ItemType Directory -Force -Path "$installerProjectDir\payload" | Out-Null
Copy-Item -Path "$stagingDir\VibeAlarm.exe" -Destination "$installerProjectDir\payload\VibeAlarm.exe" -Force
Copy-Item -Path "$stagingDir\alarm.wav" -Destination "$installerProjectDir\payload\alarm.wav" -Force
$installerCsproj | Out-File -FilePath "$installerProjectDir\VibeAlarmInstaller.csproj" -Encoding utf8
$installerProgram | Out-File -FilePath "$installerProjectDir\Program.cs" -Encoding utf8

# 6. Publish the installer executable.
Write-Host "Compiling self-contained installer..."
dotnet publish "$installerProjectDir\VibeAlarmInstaller.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$installerProjectDir\publish"
if ($LASTEXITCODE -ne 0) {
    throw "Installer publish failed with exit code $LASTEXITCODE."
}
$builtInstallerPath = "$installerProjectDir\publish\VibeAlarmInstaller.exe"
if (-not (Test-Path $builtInstallerPath)) {
    throw "Installer publish finished but did not create $builtInstallerPath."
}
Copy-Item -Path $builtInstallerPath -Destination $installerPath -Force
if (-not (Test-Path $installerPath)) {
    throw "Installer was created but could not be copied to $installerPath."
}

Write-Host "Done! Your professional standalone installer is created at: $installerPath"
