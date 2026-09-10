using System.IO.Pipes;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 주입된 Inspector 자리를 대신하는 테스트용 Named Pipe 서버를 만들어 주는 헬퍼.
/// 파이프는 이름당 한 인스턴스만 허용하므로, 테스트마다 겹치지 않는 번호를 발급해 충돌을 막는다.
/// </summary>
internal static class FakeInspector
{
    private static int _nextProcessId = 500_000;

    /// <summary>테스트마다 겹치지 않는 가짜 프로세스 번호를 발급합니다.</summary>
    public static int NextProcessId() => Interlocked.Increment(ref _nextProcessId);

    /// <summary>지정된 가짜 프로세스 번호에 해당하는 파이프 서버를 만듭니다.</summary>
    /// <param name="processId">파이프 이름을 결정하는 가짜 프로세스 번호.</param>
    public static NamedPipeServerStream Create(int processId) =>
        new(IpcPipeNames.ForProcess(processId), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}
