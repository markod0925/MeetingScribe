using System.Text.Json;
using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Settings;

public sealed class SettingsService
{
    private const int CurrentSchema = 1;

    public async Task<AppSettings> LoadAsync(string appDir, CancellationToken ct)
    {
        var primary = Path.Combine(appDir, "config", "settings.json");
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingScribe", "settings.json");

        var path = File.Exists(primary) ? primary : fallback;
        if (!File.Exists(path)) return new AppSettings();

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: ct) ?? new AppSettings();
        if (settings.SchemaVersion != CurrentSchema)
        {
            settings = Migrate(settings);
            await SaveAsync(primary, settings, ct);
        }

        return settings;
    }

    public async Task SaveAsync(string path, AppSettings settings, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: ct, options: new JsonSerializerOptions { WriteIndented = true });
    }

    public AppSettings Migrate(AppSettings old)
    {
        old.SchemaVersion = CurrentSchema;
        return old;
    }
}
