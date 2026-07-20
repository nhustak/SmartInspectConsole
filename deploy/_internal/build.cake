///////////////////////////////////////////////////////////////////////////////
// SmartInspectConsole deploy
// c = SureCourt FTP package
// g = CC3 production FTP package
//
// Flow: publish Release self-contained win-x64 -> zip -> FTP upload + copy cmd
///////////////////////////////////////////////////////////////////////////////

#addin nuget:?package=FluentFTP&version=28.0.5

using System.IO.Compression;
using System.Text.Json;
using System.Collections.Generic;

var target = Argument("target", "Deploy");
var environment = Argument("environment", "c").ToLowerInvariant();

if (environment != "c" && environment != "g")
{
    throw new Exception($"Unknown environment '{environment}'. Use c (SureCourt) or g (prod).");
}

var envLabel = environment == "c" ? "SureCourt" : "CC3-Prod";
var archiveName = $"si-{environment}.zip";
var copyScriptName = environment == "c" ? "copy-c.cmd" : "copy-g.cmd";

var deployRoot = MakeAbsolute(Directory("..")).FullPath;      // deploy/
var repoRoot = MakeAbsolute(Directory("../..")).FullPath;     // repo root
var projectPath = $@"{repoRoot}\src\SmartInspectConsole\SmartInspectConsole.csproj";
var publishDir = $@"{repoRoot}\publish";
var artifactsDir = $@"{deployRoot}\artifacts";
var zipPath = $@"{artifactsDir}\{archiveName}";
var secretsFile = $@"{deployRoot}\secrets\deploy.{environment}.json";
var copyScriptPath = $@"{deployRoot}\server\{copyScriptName}";

Dictionary<string, JsonElement> secrets;

void EnsureFtpFolder(string server, string user, string pass, string folder)
{
    var normalized = (folder ?? string.Empty).Replace("\\", "/").Trim('/');
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return;
    }

    using (var client = new FluentFTP.FtpClient(server, user, pass))
    {
        client.AutoConnect();
        client.CreateDirectory(normalized, true);
    }

    Information($"Ensured FTP folder: {normalized}");
}

void UploadFtpFile(string server, string user, string pass, string folder, string localPath, string remoteName)
{
    var normalized = (folder ?? string.Empty).Replace("\\", "/").Trim('/');
    var remotePath = string.IsNullOrWhiteSpace(normalized)
        ? remoteName
        : $"{normalized}/{remoteName}";

    using (var client = new FluentFTP.FtpClient(server, user, pass))
    {
        client.AutoConnect();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            client.CreateDirectory(normalized, true);
        }
        client.UploadFile(localPath, remotePath, FluentFTP.FtpExists.Overwrite, true);
    }
}

Setup(ctx =>
{
    Information("========================================");
    Information($"SmartInspectConsole deploy -> {environment} ({envLabel})");
    Information("========================================");

    if (!FileExists(secretsFile))
    {
        throw new Exception(
            $"Missing secrets: {secretsFile}\n" +
            $"Copy deploy.{environment}.json.template to deploy.{environment}.json and set the FTP password.");
    }

    secrets = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
        System.IO.File.ReadAllText(secretsFile));

    if (secrets == null || !secrets.ContainsKey("ftp"))
    {
        throw new Exception($"Missing 'ftp' section in {secretsFile}");
    }
});

Task("Clean-Publish")
    .Does(() =>
{
    if (DirectoryExists(publishDir))
    {
        Information($"Cleaning {publishDir}");
        CleanDirectory(publishDir);
    }
    else
    {
        CreateDirectory(publishDir);
    }

    if (!DirectoryExists(artifactsDir))
    {
        CreateDirectory(artifactsDir);
    }
});

Task("Publish")
    .IsDependentOn("Clean-Publish")
    .Does(() =>
{
    Information($"Publishing {projectPath}");
    var exit = StartProcess("dotnet", new ProcessSettings
    {
        Arguments =
            $"publish \"{projectPath}\" " +
            "-c Release " +
            "-r win-x64 " +
            "--self-contained true " +
            $"-o \"{publishDir}\"",
        WorkingDirectory = repoRoot
    });

    if (exit != 0)
    {
        throw new Exception($"dotnet publish failed with exit code {exit}");
    }

    var exe = System.IO.Path.Combine(publishDir, "SmartInspectConsole.exe");
    if (!FileExists(exe))
    {
        throw new Exception($"Publish output missing SmartInspectConsole.exe under {publishDir}");
    }

    Information($"Published: {exe}");
});

Task("Zip")
    .IsDependentOn("Publish")
    .Does(() =>
{
    if (FileExists(zipPath))
    {
        DeleteFile(zipPath);
    }

    Information($"Creating {zipPath}");
    ZipFile.CreateFromDirectory(
        publishDir,
        zipPath,
        CompressionLevel.Optimal,
        includeBaseDirectory: false);

    if (!FileExists(zipPath))
    {
        throw new Exception($"Zip was not created: {zipPath}");
    }

    var info = new System.IO.FileInfo(zipPath);
    Information($"Archive size: {info.Length / 1024.0 / 1024.0:F1} MiB");
});

Task("Build")
    .IsDependentOn("Zip");

Task("Deploy")
    .IsDependentOn("Build")
    .Does(() =>
{
    if (!FileExists(copyScriptPath))
    {
        throw new Exception($"Missing server copy script: {copyScriptPath}");
    }

    var ftp = secrets["ftp"];
    var server = ftp.GetProperty("server").GetString().Replace("ftp://", "").Replace("FTP://", "");
    var user = ftp.GetProperty("username").GetString();
    var pass = ftp.GetProperty("password").GetString();
    var folder = ftp.GetProperty("folder").GetString();

    if (string.IsNullOrWhiteSpace(server) ||
        string.IsNullOrWhiteSpace(user) ||
        string.IsNullOrWhiteSpace(pass) ||
        string.IsNullOrWhiteSpace(folder))
    {
        throw new Exception($"Incomplete ftp settings in {secretsFile}");
    }

    if (pass.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
    {
        throw new Exception($"Set a real FTP password in {secretsFile} before Deploy.");
    }

    EnsureFtpFolder(server, user, pass, folder);

    Information($"Uploading {archiveName} -> ftp://{server}/{folder}/{archiveName}");
    UploadFtpFile(server, user, pass, folder, zipPath, archiveName);

    Information($"Uploading {copyScriptName} -> ftp://{server}/{folder}/{copyScriptName}");
    UploadFtpFile(server, user, pass, folder, copyScriptPath, copyScriptName);

    Information("Upload complete.");
    Information($"Server next step: run {copyScriptName} from the FTP package folder.");
});

RunTarget(target);
