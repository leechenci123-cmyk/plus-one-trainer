param(
    [uint32]$Address = 0x00400000,
    [int]$Length = 512,
    [string]$OutputFile
)
# Read-only diagnostic. Does not start the game, patch memory, or read saves.
$ErrorActionPreference = 'Stop'
if ($Length -lt 1 -or $Length -gt 0x500000) { throw 'Invalid read length.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PlusOneReadOnlyProbe {
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, int size, out IntPtr count);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static byte[] Read(int pid, uint address, int length) {
        var handle = OpenProcess(0x410, false, pid);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try {
            var bytes = new byte[length]; IntPtr count;
            if (!ReadProcessMemory(handle, new IntPtr((long)address), bytes, length, out count) || count.ToInt64()!=length)
                throw new System.ComponentModel.Win32Exception();
            return bytes;
        } finally { CloseHandle(handle); }
    }
}
'@
$taskProcesses = @(Get-Process -Name popcapgame1 -ErrorAction SilentlyContinue)
if ($taskProcesses.Count -ne 1) { throw 'Open exactly one Steam PvZ game first.' }
$taskBytes = [PlusOneReadOnlyProbe]::Read($taskProcesses[0].Id, $Address, $Length)
if ($OutputFile) {
    $taskOutput = [IO.Path]::GetFullPath($OutputFile)
    if (Test-Path -LiteralPath $taskOutput) { throw "Will not overwrite existing dump: $taskOutput" }
    [IO.File]::WriteAllBytes($taskOutput, $taskBytes)
    Write-Output "Read $Length bytes at 0x$($Address.ToString('X8')) into $taskOutput"
} else {
    for ($taskIndex=0; $taskIndex -lt $taskBytes.Length; $taskIndex+=16) {
        $taskEnd = [Math]::Min($taskIndex+15, $taskBytes.Length-1)
        '{0:X8}: {1}' -f ($Address+$taskIndex), [BitConverter]::ToString($taskBytes[$taskIndex..$taskEnd]).Replace('-', ' ')
    }
}
