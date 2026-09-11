using System.Security.Cryptography;
using System.Text;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 임베디드 리소스로 내장된 인젝션 페이로드 DLL을 임시 디렉토리에 추출합니다.
/// TFM별 Inspector DLL과 GenericInjector DLL의 파일 경로를 제공.
/// 추출 디렉토리 이름은 페이로드 내용의 해시로 정한다. 페이로드가 바뀌면 자연히 다른 디렉토리에
/// 풀리므로, 낡은 DLL이 그대로 주입되는 일도 없고 이미 주입돼 잠긴 파일을 덮어쓸 일도 없다.
/// </summary>
public sealed class PayloadExtractor
{
    #region Constants

    private const string ResourcePrefix = "Xapper.Payload.";

    private static readonly string[] SupportedTfms =
    [
        "net6.0-windows",
        "net7.0-windows",
        "net8.0-windows",
        "net9.0-windows"
    ];

    private static readonly string[] InspectorFiles =
    [
        "Xapper.Inspector.dll",
        "Xapper.Protocol.dll",
        "Xapper.Inspector.deps.json",
        // 대상 프로세스 안에서 user32 를 후킹해 커서 없이 클릭·드래그하기 위한 인라인 후킹 라이브러리.
        "MinHook.NET.dll"
    ];

    private static readonly string[] GenericInjectorFiles =
    [
        "Snoop.GenericInjector.x64.dll",
        "Snoop.GenericInjector.x86.dll",
        "Snoop.GenericInjector.ARM64.dll"
    ];

    #endregion

    #region Fields

    private readonly string _extractionDir;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="PayloadExtractor"/>의 새 인스턴스를 생성합니다.
    /// 페이로드 내용 해시 기반의 추출 디렉토리 경로를 결정.
    /// </summary>
    public PayloadExtractor()
    {
        var assembly = typeof(PayloadExtractor).Assembly;
        _extractionDir = Path.Combine(Path.GetTempPath(), "Xapper", ComputePayloadHash(assembly));
    }

    #endregion

    #region Properties

    /// <summary>TFM별 Inspector DLL이 추출된 기본 디렉토리 경로.</summary>
    public string InspectorBaseDir => _extractionDir;

    /// <summary>GenericInjector DLL이 추출된 디렉토리 경로.</summary>
    public string GenericInjectorDir => _extractionDir;

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 TFM에 해당하는 Inspector DLL의 전체 경로를 반환합니다.
    /// </summary>
    /// <param name="tfm">대상 프레임워크 모니커 (예: "net9.0-windows").</param>
    /// <returns>추출된 Xapper.Inspector.dll의 전체 경로.</returns>
    public string GetInspectorDllPath(string tfm)
    {
        return Path.Combine(_extractionDir, tfm, "Xapper.Inspector.dll");
    }

    /// <summary>
    /// 모든 페이로드 리소스를 디스크에 추출합니다.
    /// GenericInjector는 루트 디렉토리에, Inspector는 TFM별 하위 디렉토리에 추출.
    /// 이미 존재하는 파일은 건너뜁니다. 디렉토리 이름 자체가 페이로드 내용의 해시이므로,
    /// 파일이 있다는 것은 곧 그 내용이 현재 페이로드와 같다는 뜻이다.
    /// </summary>
    /// <exception cref="InvalidOperationException">임베디드 리소스를 찾을 수 없는 경우.</exception>
    public void ExtractAll()
    {
        Directory.CreateDirectory(_extractionDir);

        var assembly = typeof(PayloadExtractor).Assembly;

        // GenericInjector DLL → 루트 디렉토리
        foreach (var name in GenericInjectorFiles)
            ExtractResource(assembly, name, _extractionDir, name);

        // Inspector DLL → TFM별 하위 디렉토리
        foreach (var tfm in SupportedTfms)
        {
            var tfmDir = Path.Combine(_extractionDir, tfm);
            Directory.CreateDirectory(tfmDir);

            foreach (var name in InspectorFiles)
                ExtractResource(assembly, $"{tfm}.{name}", tfmDir, name);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 내장된 모든 페이로드 리소스의 이름과 내용으로 짧은 해시를 만듭니다.
    /// 추출 디렉토리 이름에 쓰이므로, 페이로드가 한 바이트라도 달라지면 다른 디렉토리로 풀린다.
    /// </summary>
    /// <param name="assembly">페이로드 리소스를 담고 있는 어셈블리.</param>
    /// <returns>16자리 16진수 해시 문자열.</returns>
    /// <exception cref="InvalidOperationException">페이로드 리소스가 하나도 없는 경우.</exception>
    private static string ComputePayloadHash(System.Reflection.Assembly assembly)
    {
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
            throw new InvalidOperationException("No embedded payload resources found in assembly.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];

        foreach (var resourceName in resourceNames)
        {
            // 이름도 함께 섞어, 파일 구성이 바뀌는 경우도 해시에 반영되게 한다.
            hash.AppendData(Encoding.UTF8.GetBytes(resourceName));

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    /// <summary>
    /// 단일 임베디드 리소스를 디스크에 추출합니다.
    /// </summary>
    private static void ExtractResource(System.Reflection.Assembly assembly, string resourceName, string targetDir, string fileName)
    {
        var targetPath = Path.Combine(targetDir, fileName);
        if (File.Exists(targetPath))
            return;

        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourcePrefix + resourceName}' not found in assembly.");

        using var fs = File.Create(targetPath);
        stream.CopyTo(fs);
    }

    #endregion
}
