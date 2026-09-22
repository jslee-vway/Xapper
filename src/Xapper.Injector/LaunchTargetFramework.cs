using System.Globalization;
using System.Text.Json;

namespace Xapper.Injector;

/// <summary>
/// 아직 띄우지 않은 앱의 .NET 런타임 세대를 exe 옆 <c>runtimeconfig.json</c> 에서 읽는다.
/// 주입 경로는 실행 중인 프로세스를 검사하지만(<c>NativeInjector.DetectTargetFramework</c>), 런치 시점에는
/// 프로세스가 없어 그 길을 쓸 수 없다. 읽지 못하면 null 을 돌려 호출자가 거절하게 한다 — 예외를 던지지 않는다.
/// </summary>
public static class LaunchTargetFramework
{
    #region Public Methods

    /// <summary>exe 가 요구하는 런타임의 주 버전(예: 9). 알아낼 수 없으면 null.</summary>
    /// <param name="exePath">대상 앱의 실행 파일 경로.</param>
    public static int? MajorVersionOf(string exePath)
    {
        var configPath = Path.ChangeExtension(exePath, ".runtimeconfig.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options))
                return null;

            if (options.TryGetProperty("tfm", out var tfm) && MajorOfTfm(tfm) is { } fromTfm)
                return fromTfm;

            int? best = null;
            if (options.TryGetProperty("frameworks", out var frameworks) && frameworks.ValueKind == JsonValueKind.Array)
            {
                foreach (var framework in frameworks.EnumerateArray())
                    best = Larger(best, MajorOfVersion(framework));
            }

            if (options.TryGetProperty("framework", out var single))
                best = Larger(best, MajorOfVersion(single));

            return best;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>"net9.0" 또는 "net9.0-windows" 에서 주 버전을 읽습니다.</summary>
    private static int? MajorOfTfm(JsonElement tfm)
    {
        var text = tfm.ValueKind == JsonValueKind.String ? tfm.GetString() : null;
        if (text is null || !text.StartsWith("net", StringComparison.Ordinal))
            return null;

        var digits = text[3..];
        var end = 0;
        while (end < digits.Length && char.IsDigit(digits[end]))
            end++;

        return end > 0 && int.TryParse(digits[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
            ? major
            : null;
    }

    /// <summary>프레임워크 항목의 "version"("9.0.0")에서 주 버전을 읽습니다.</summary>
    private static int? MajorOfVersion(JsonElement framework)
    {
        if (framework.ValueKind != JsonValueKind.Object
            || !framework.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String)
            return null;

        var text = version.GetString();
        if (string.IsNullOrEmpty(text))
            return null;

        var dot = text.IndexOf('.');
        var head = dot < 0 ? text : text[..dot];
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ? major : null;
    }

    /// <summary>둘 중 큰 쪽을 고릅니다. 한쪽이 null 이면 다른 쪽.</summary>
    private static int? Larger(int? left, int? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return Math.Max(left.Value, right.Value);
    }

    #endregion
}
