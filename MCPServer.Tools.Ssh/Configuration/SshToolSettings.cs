using Microsoft.Extensions.Configuration;

namespace MCPServer.Tools.Ssh.Configuration;

public sealed class SshToolSettings
{
    public const string ConfigurationSectionName = "McpTools:Ssh";

    public bool Enabled { get; set; }

    public bool RequireExplicitProfileAllowlist { get; set; } = true;

    public bool AllowUnknownHostKeys { get; set; }

    public bool AllowShellInterpreterInlineCommands { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxOutputChars { get; set; } = 20_000;

    public string? ProfilePath { get; set; }

    public string? TraceDirectory { get; set; }

    public List<string> AllowedCommands { get; set; } =
    [
        "cat",
        "dotnet",
        "echo",
        "git",
        "ls",
        "pwd",
        "uname",
        "whoami"
    ];

    public List<string> DeniedCommands { get; set; } =
    [
        "chmod",
        "chown",
        "dd",
        "fdisk",
        "mkfs",
        "mount",
        "nc",
        "netcat",
        "passwd",
        "reboot",
        "rm",
        "shutdown",
        "su",
        "sudo",
        "umount",
        "useradd",
        "userdel",
        "usermod"
    ];

    public static SshToolSettings Normalize(SshToolSettings? settings)
    {
        settings ??= new SshToolSettings();

        return new SshToolSettings
        {
            Enabled = settings.Enabled,
            RequireExplicitProfileAllowlist = settings.RequireExplicitProfileAllowlist,
            AllowUnknownHostKeys = settings.AllowUnknownHostKeys,
            AllowShellInterpreterInlineCommands = settings.AllowShellInterpreterInlineCommands,
            TimeoutSeconds = Math.Clamp(settings.TimeoutSeconds <= 0 ? 60 : settings.TimeoutSeconds, 1, 600),
            MaxOutputChars = Math.Clamp(settings.MaxOutputChars <= 0 ? 20_000 : settings.MaxOutputChars, 1, 1_000_000),
            ProfilePath = TrimToNull(settings.ProfilePath),
            TraceDirectory = TrimToNull(settings.TraceDirectory),
            AllowedCommands = NormalizeStringList(settings.AllowedCommands,
            [
                "cat",
                "dotnet",
                "echo",
                "git",
                "ls",
                "pwd",
                "uname",
                "whoami"
            ]),
            DeniedCommands = NormalizeStringList(settings.DeniedCommands,
            [
                "chmod",
                "chown",
                "dd",
                "fdisk",
                "mkfs",
                "mount",
                "nc",
                "netcat",
                "passwd",
                "reboot",
                "rm",
                "shutdown",
                "su",
                "sudo",
                "umount",
                "useradd",
                "userdel",
                "usermod"
            ])
        };
    }

    public static SshToolSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new SshToolSettings
        {
            Enabled = ReadBool(configuration, "Enabled", defaultValue: false),
            RequireExplicitProfileAllowlist = ReadBool(configuration, "RequireExplicitProfileAllowlist", defaultValue: true),
            AllowUnknownHostKeys = ReadBool(configuration, "AllowUnknownHostKeys", defaultValue: false),
            AllowShellInterpreterInlineCommands = ReadBool(configuration, "AllowShellInterpreterInlineCommands", defaultValue: false),
            TimeoutSeconds = Math.Clamp(ReadInt(configuration, "TimeoutSeconds", defaultValue: 60), 1, 600),
            MaxOutputChars = Math.Clamp(ReadInt(configuration, "MaxOutputChars", defaultValue: 20_000), 1, 1_000_000),
            ProfilePath = TrimToNull(configuration["ProfilePath"]),
            TraceDirectory = TrimToNull(configuration["TraceDirectory"]),
            AllowedCommands = ReadStringArray(configuration.GetSection("AllowedCommands"),
            [
                "cat",
                "dotnet",
                "echo",
                "git",
                "ls",
                "pwd",
                "uname",
                "whoami"
            ]),
            DeniedCommands = ReadStringArray(configuration.GetSection("DeniedCommands"),
            [
                "chmod",
                "chown",
                "dd",
                "fdisk",
                "mkfs",
                "mount",
                "nc",
                "netcat",
                "passwd",
                "reboot",
                "rm",
                "shutdown",
                "su",
                "sudo",
                "umount",
                "useradd",
                "userdel",
                "usermod"
            ])
        };
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool defaultValue)
    {
        var raw = configuration[key];
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
    {
        var raw = configuration[key];
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static List<string> ReadStringArray(IConfigurationSection section, IReadOnlyList<string> defaultValues)
    {
        var values = section.GetChildren()
            .Select(child => TrimToNull(child.Value))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? defaultValues.ToList() : values.ToList();
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values, IReadOnlyList<string> defaultValues)
    {
        var normalized = values is null
            ? Array.Empty<string>()
            : values
                .Select(TrimToNull)
                .Where(static value => value is not null)
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return normalized.Length == 0 ? defaultValues.ToList() : normalized.ToList();
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
