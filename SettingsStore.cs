using System.Text.Json;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public SettingsStore(string path) => this.path = path;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(path)) return new(new LumaSettings(), SettingsLoadState.Missing, null);

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<LumaSettings>(json, JsonOptions);
            if (settings is null) return Recover("Файл настроек пустой или содержит null.");
            if (settings.SchemaVersion > LumaSettings.CurrentSchemaVersion)
                return new(new LumaSettings(), SettingsLoadState.FutureVersion,
                    $"Версия настроек {settings.SchemaVersion} новее этой версии Luma. Исходный файл сохранён без изменений.");

            settings.Normalize();
            return new(settings, SettingsLoadState.Loaded, null);
        }
        catch (JsonException ex)
        {
            return Recover($"JSON настроек повреждён: {ex.Message}");
        }
        catch (IOException ex)
        {
            return new(new LumaSettings(), SettingsLoadState.ReadError, $"Не удалось прочитать настройки: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(new LumaSettings(), SettingsLoadState.ReadError, $"Нет доступа к файлу настроек: {ex.Message}");
        }
    }

    private SettingsLoadResult Recover(string message)
    {
        try
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            File.Copy(path, backup, false);
            return new(new LumaSettings(), SettingsLoadState.CorruptBackedUp, $"{message} Копия сохранена: {backup}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(new LumaSettings(), SettingsLoadState.CorruptBackupFailed, $"{message} Не удалось создать резервную копию: {ex.Message}");
        }
    }

    public SettingsSaveResult Save(LumaSettings settings)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            settings.Normalize();
            temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                var backup = path + ".bak";
                File.Replace(temp, path, backup, true);
            }
            else File.Move(temp, path);
            return new(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new(false, ex.Message);
        }
        finally
        {
            if (temp is not null)
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}

internal enum SettingsLoadState { Missing, Loaded, CorruptBackedUp, CorruptBackupFailed, ReadError, FutureVersion }
internal sealed record SettingsLoadResult(LumaSettings Settings, SettingsLoadState State, string? Warning);
internal sealed record SettingsSaveResult(bool Success, string? Error);
