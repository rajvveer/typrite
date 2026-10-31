param([Parameter(Mandatory = $true)][string]$DesktopExe)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This smoke test requires Windows.' }
if (Get-Process -Name 'Typrite.Desktop' -ErrorAction SilentlyContinue) {
    throw 'Close existing TYPRITE instances before running this test.'
}

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class TaskbarSmoke {
    [StructLayout(LayoutKind.Sequential)]
    public struct Data {
        public uint size; public IntPtr hwnd; public uint callback, edge;
        public int left, top, right, bottom; public IntPtr param;
    }
    public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string name, string title);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref Data data);
    public static uint State() {
        var data = new Data { size=(uint)Marshal.SizeOf(typeof(Data)) };
        return (uint)SHAppBarMessage(4, ref data).ToUInt64();
    }
    public static void Restore(uint state) {
        var data = new Data { size=(uint)Marshal.SizeOf(typeof(Data)), hwnd=FindWindow("Shell_TrayWnd", null), param=(IntPtr)state };
        SHAppBarMessage(10, ref data);
    }
    public static IntPtr[] Taskbars() {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, unused) => {
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString()=="Shell_TrayWnd" || name.ToString()=="Shell_SecondaryTrayWnd") result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
    public static void Close(uint processId) {
        EnumWindows((hwnd, unused) => {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid==processId && IsWindowVisible(hwnd)) PostMessage(hwnd, 16, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
}
'@

function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (& $Condition)) {
        if ((Get-Date) -gt $deadline) { throw $Failure }
        Start-Sleep -Milliseconds 100
    }
}

$taskbars = @([TaskbarSmoke]::Taskbars())
if ($taskbars.Count -eq 0) { throw 'Explorer taskbar must be running.' }
$originalState = [TaskbarSmoke]::State()
$originalVisibility = @{}
foreach ($hwnd in $taskbars) { $originalVisibility[$hwnd] = [TaskbarSmoke]::IsWindowVisible($hwnd) }
$demoProcess = $null
try {
    $demoProcess = Start-Process -FilePath (Resolve-Path $DesktopExe).Path -ArgumentList '--taskbar-demo' -PassThru
    if (-not $demoProcess.WaitForInputIdle(30000)) { throw 'Demo did not finish UI startup.' }
    Wait-Until {
        -not $demoProcess.HasExited -and ([TaskbarSmoke]::State() -band 1) -ne 0 -and
            @($taskbars | Where-Object { [TaskbarSmoke]::IsWindowVisible($_) }).Count -eq 0
    } 'Demo did not hide the Windows taskbars and enable auto-hide.'
    [TaskbarSmoke]::Close($demoProcess.Id)
    Wait-Until { $demoProcess.HasExited } 'Demo did not close cleanly.'
    Wait-Until {
        [TaskbarSmoke]::State() -eq $originalState -and
            @($taskbars | Where-Object { [TaskbarSmoke]::IsWindowVisible($_) -ne $originalVisibility[$_] }).Count -eq 0
    } 'Demo did not restore the original taskbar state and visibility.'
    Write-Output 'PASS: demo hides taskbars and restores the original state on close.'
}
finally {
    if ($null -ne $demoProcess -and -not $demoProcess.HasExited) {
        [TaskbarSmoke]::Close($demoProcess.Id)
        if (-not $demoProcess.WaitForExit(5000)) { $demoProcess.Kill(); $demoProcess.WaitForExit() }
    }
    [TaskbarSmoke]::Restore($originalState)
    foreach ($hwnd in $taskbars) {
        $command = if ($originalVisibility[$hwnd]) { 4 } else { 0 }
        [void][TaskbarSmoke]::ShowWindowAsync($hwnd, $command)
    }
}
