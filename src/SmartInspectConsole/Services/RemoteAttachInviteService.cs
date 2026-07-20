using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SmartInspectConsole.Models;

namespace SmartInspectConsole.Services;

/// <summary>
/// Generates SSH attach invites on a "remote" console (keygen + authorized_keys)
/// and imports them on a local console into RemoteServerProfile + key files.
/// </summary>
public static class RemoteAttachInviteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string KeysFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartInspectConsole",
        "ssh");

    public static string SuggestHostName()
    {
        try
        {
            var host = Dns.GetHostName();
            var entry = Dns.GetHostEntry(host);
            if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName.Contains('.', StringComparison.Ordinal))
                return entry.HostName;
        }
        catch
        {
            // ignore
        }

        var ip = GetPreferredIPv4();
        if (!string.IsNullOrWhiteSpace(ip))
            return ip;

        return Environment.MachineName;
    }

    public static string SuggestDisplayName() => Environment.MachineName;

    public static string SuggestUser() => Environment.UserName;

    /// <summary>
    /// Creates an ed25519 key pair, installs the public key into this machine's
    /// OpenSSH authorized_keys, and returns a clipboard invite including the private key.
    /// </summary>
    public static RemoteAttachInvite CreateInviteWithNewKeys(
        string name,
        string sshHost,
        int sshPort,
        string sshUser,
        int remoteApiPort,
        int catchUpLimit,
        int pollIntervalMs,
        string? keyPassphrase = null)
    {
        Directory.CreateDirectory(KeysFolder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name);
        var keyBase = Path.Combine(KeysFolder, $"invite-{safeName}-{stamp}");
        var privatePath = keyBase;
        var publicPath = keyBase + ".pub";

        try
        {
            RunSshKeyGen(privatePath, keyPassphrase, $"SmartInspectConsole-attach@{Environment.MachineName}");

            if (!File.Exists(privatePath) || !File.Exists(publicPath))
                throw new InvalidOperationException("ssh-keygen did not produce key files.");

            var privateKey = File.ReadAllText(privatePath);
            var publicKey = File.ReadAllText(publicPath).Trim();

            var installed = TryInstallAuthorizedKey(publicKey);

            return new RemoteAttachInvite
            {
                Format = RemoteAttachInvite.FormatId,
                Name = name.Trim(),
                SshHost = sshHost.Trim(),
                SshPort = sshPort > 0 ? sshPort : 22,
                SshUser = sshUser.Trim(),
                AuthMethod = "privateKey",
                RemoteApiPort = remoteApiPort > 0 ? remoteApiPort : 42331,
                CatchUpLimit = catchUpLimit > 0 ? catchUpLimit : 2000,
                PollIntervalMs = pollIntervalMs > 0 ? pollIntervalMs : 750,
                PrivateKeyOpenSsh = privateKey,
                PublicKeyOpenSsh = publicKey,
                PublicKeyInstalledOnRemote = installed,
                GeneratedAtUtc = DateTime.UtcNow,
                GeneratedOnMachine = Environment.MachineName,
                Notes = installed
                    ? "Public key was appended to this machine's OpenSSH authorized_keys. Import this invite on your laptop to attach."
                    : "Key pair generated, but authorized_keys could not be updated automatically. Add PublicKeyOpenSsh to the server authorized_keys manually."
            };
        }
        finally
        {
            // Private key lives in the invite JSON for one-time import; remove temp files on the server.
            TryDelete(privatePath);
            TryDelete(publicPath);
        }
    }

    /// <summary>
    /// Builds an invite without generating keys (host/user/port only) for existing SSH setups.
    /// </summary>
    public static RemoteAttachInvite CreateInviteProfileOnly(
        string name,
        string sshHost,
        int sshPort,
        string sshUser,
        int remoteApiPort,
        int catchUpLimit,
        int pollIntervalMs)
    {
        return new RemoteAttachInvite
        {
            Format = RemoteAttachInvite.FormatId,
            Name = name.Trim(),
            SshHost = sshHost.Trim(),
            SshPort = sshPort > 0 ? sshPort : 22,
            SshUser = sshUser.Trim(),
            AuthMethod = "privateKey",
            RemoteApiPort = remoteApiPort > 0 ? remoteApiPort : 42331,
            CatchUpLimit = catchUpLimit > 0 ? catchUpLimit : 2000,
            PollIntervalMs = pollIntervalMs > 0 ? pollIntervalMs : 750,
            PublicKeyInstalledOnRemote = false,
            GeneratedAtUtc = DateTime.UtcNow,
            GeneratedOnMachine = Environment.MachineName,
            Notes = "Profile only — configure the private key path after import, or re-export with key generation on the server."
        };
    }

    public static string ToClipboardJson(RemoteAttachInvite invite)
        => JsonSerializer.Serialize(invite, JsonOptions);

    public static RemoteAttachInvite ParseClipboardJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Clipboard is empty.");

        var invite = JsonSerializer.Deserialize<RemoteAttachInvite>(json.Trim(), JsonOptions)
            ?? throw new InvalidOperationException("Could not parse attach invite JSON.");

        if (!string.Equals(invite.Format, RemoteAttachInvite.FormatId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported invite format '{invite.Format}'. Expected {RemoteAttachInvite.FormatId}.");

        if (string.IsNullOrWhiteSpace(invite.Name) || string.IsNullOrWhiteSpace(invite.SshHost) || string.IsNullOrWhiteSpace(invite.SshUser))
            throw new InvalidOperationException("Invite is missing Name, SshHost, or SshUser.");

        return invite;
    }

    /// <summary>
    /// Writes the private key (if present) under LocalAppData and returns a RemoteServerProfile.
    /// </summary>
    public static RemoteServerProfile ImportToProfile(RemoteAttachInvite invite)
    {
        Directory.CreateDirectory(KeysFolder);
        var id = Guid.NewGuid().ToString("N");
        string? keyPath = null;

        if (!string.IsNullOrWhiteSpace(invite.PrivateKeyOpenSsh))
        {
            keyPath = Path.Combine(KeysFolder, $"{SanitizeFileName(invite.Name)}-{id}.key");
            // Normalize line endings for OpenSSH
            var keyText = invite.PrivateKeyOpenSsh.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!keyText.EndsWith('\n'))
                keyText += "\n";
            File.WriteAllText(keyPath, keyText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new RemoteServerProfile
        {
            Id = id,
            Name = invite.Name,
            SshHost = invite.SshHost,
            SshPort = invite.SshPort > 0 ? invite.SshPort : 22,
            SshUser = invite.SshUser,
            AuthMethod = "privateKey",
            PrivateKeyPath = keyPath,
            RemoteApiPort = invite.RemoteApiPort > 0 ? invite.RemoteApiPort : 42331,
            CatchUpLimit = invite.CatchUpLimit > 0 ? invite.CatchUpLimit : 2000,
            PollIntervalMs = invite.PollIntervalMs > 0 ? invite.PollIntervalMs : 750,
            Enabled = true
        };
    }

    private static void RunSshKeyGen(string privateKeyPath, string? passphrase, string comment)
    {
        var sshKeyGen = ResolveSshKeyGen();
        if (sshKeyGen == null)
            throw new InvalidOperationException(
                "ssh-keygen was not found. Install OpenSSH Client (Windows Optional Feature) or Git for Windows.");

        // Empty passphrase: -N ""
        var passArg = string.IsNullOrEmpty(passphrase) ? "\"\"" : EscapeArg(passphrase);
        var args = $"-t ed25519 -f \"{privateKeyPath}\" -N {passArg} -C \"{comment}\" -q";

        var psi = new ProcessStartInfo
        {
            FileName = sshKeyGen,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ssh-keygen.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(30_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("ssh-keygen timed out.");
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ssh-keygen failed ({proc.ExitCode}): {stderr}\n{stdout}");
    }

    private static string? ResolveSshKeyGen()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe"),
            @"C:\Windows\System32\OpenSSH\ssh-keygen.exe",
            @"C:\Program Files\Git\usr\bin\ssh-keygen.exe"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        // PATH search
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "ssh-keygen.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool TryInstallAuthorizedKey(string publicKeyLine)
    {
        try
        {
            var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            Directory.CreateDirectory(sshDir);
            var authorizedKeys = Path.Combine(sshDir, "authorized_keys");

            if (File.Exists(authorizedKeys))
            {
                var existing = File.ReadAllText(authorizedKeys);
                if (existing.Contains(publicKeyLine.Trim(), StringComparison.Ordinal))
                    return true;
            }

            var line = publicKeyLine.Trim();
            if (!line.EndsWith('\n'))
                line += Environment.NewLine;

            File.AppendAllText(authorizedKeys, line, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetPreferredIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "server" : s;
    }

    private static string EscapeArg(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
