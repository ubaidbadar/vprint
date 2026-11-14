using System.Diagnostics;
using System.Text;

namespace VPrint.Services;

/// <summary>Runs application-owned PowerShell, not arbitrary user-provided commands.</summary>
internal static class PowerShellRunner
{
    public static async Task<string> RunAsync(string script, bool elevated = false, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = elevated,
            CreateNoWindow = !elevated
        };
        if (elevated) info.Verb = "runas";
        else
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not launch Windows PowerShell.");
        if (elevated)
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"The administrator command failed (code {process.ExitCode}).");
            return string.Empty;
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var (output, error) = (await stdout, await stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Windows printer command failed." : error.Trim());
        return output.Trim();
    }
}
