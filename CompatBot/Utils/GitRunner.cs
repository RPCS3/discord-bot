using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.Utils;

public class GitRunner
{
    public static async Task<string> Exec(string arguments, CancellationToken cancellationToken)
    {
        using var git = new Process
        {
            StartInfo = new("git", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            },
        };
        git.Start();
        var stdout = await git.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await git.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return stdout;
    }
}