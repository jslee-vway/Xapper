using System.Windows;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 두 스크린 좌표 사이를 마우스 왼쪽 버튼을 누른 채 이동하는 드래그 액션을 수행하는 정적 클래스.
/// WPF 드래그앤드롭은 시작과 동시에 대상 앱의 UI 스레드가 중첩 메시지 루프로 진입하므로,
/// 이 액션은 UI 스레드가 아닌 곳에서 실행되어야 하며 각 입력 사이에 대상 앱이 메시지를 처리할 간격을 둔다.
/// AutomationPeer에 대응하는 드래그 패턴이 없어 실제 마우스 입력 주입이 유일한 경로이다.
/// </summary>
public static class DragAction
{
    #region Fields

    /// <summary>출발점에서 도착점까지 나누어 보낼 이동 입력 횟수.</summary>
    private const int StepCount = 10;

    /// <summary>이동 입력 사이 간격 (밀리초). 대상 앱이 메시지를 펌프할 여유를 준다.</summary>
    private const int StepDelayMs = 16;

    /// <summary>버튼을 누른 직후, 떼기 직전, 뗀 직후의 안정화 대기 (밀리초).</summary>
    private const int SettleDelayMs = 60;

    #endregion

    #region Public Methods

    /// <summary>
    /// 출발 지점에서 왼쪽 버튼을 누르고 도착 지점까지 이동한 뒤 버튼을 뗍니다.
    /// </summary>
    /// <param name="startScreenPoint">드래그를 시작할 스크린 좌표.</param>
    /// <param name="endScreenPoint">드래그를 끝낼 스크린 좌표.</param>
    /// <remarks>
    /// 두 지점 사이 거리가 시스템 드래그 임계값(기본 4px)보다 작으면 대상 앱이 드래그로 인식하지 않습니다.
    /// UI 스레드에서 호출하면 대상 앱이 드래그 중 메시지를 처리하지 못해 동작하지 않습니다.
    /// </remarks>
    public static async Task ExecuteAsync(Point startScreenPoint, Point endScreenPoint)
    {
        MouseInput.MoveTo(startScreenPoint);
        await Task.Delay(StepDelayMs);

        MouseInput.LeftDown(startScreenPoint);
        await Task.Delay(SettleDelayMs);

        for (var step = 1; step <= StepCount; step++)
        {
            var progress = (double)step / StepCount;
            MouseInput.MoveTo(new Point(
                startScreenPoint.X + (endScreenPoint.X - startScreenPoint.X) * progress,
                startScreenPoint.Y + (endScreenPoint.Y - startScreenPoint.Y) * progress));
            await Task.Delay(StepDelayMs);
        }

        await Task.Delay(SettleDelayMs);

        MouseInput.LeftUp(endScreenPoint);

        // 버튼을 뗀 입력이 대상 스레드의 메시지 큐에 실릴 때까지 기다린다.
        // 이것이 없으면 호출자가 드롭 처리 전에 응답을 받아, 뒤이은 대기가 아무것도 보장하지 못한다.
        await Task.Delay(SettleDelayMs);
    }

    #endregion
}
