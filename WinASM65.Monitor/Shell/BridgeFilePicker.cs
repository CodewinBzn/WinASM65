using System.Diagnostics;

namespace WinASM65.Monitor.Shell
{
    internal static class BridgeFilePicker
    {
        public static string Pick()
        {
            ProcessStartInfo start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-STA");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.OpenFileDialog; $d.Title='Select Lua bridge'; $d.Filter='Lua bridge (*.lua)|*.lua|All files (*.*)|*.*'; if($d.ShowDialog() -eq 'OK'){[Console]::Out.Write($d.FileName)}");
            using Process process = Process.Start(start);
            if (process == null) return null;
            string path = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && path.Length > 0 ? path : null;
        }
    }
}
