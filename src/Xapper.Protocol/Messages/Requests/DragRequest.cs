namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 마우스 왼쪽 버튼을 누른 채 두 지점 사이를 이동하는 드래그 액션 요청 메시지.
/// 출발 지점과 도착 지점은 각각 요소 기준 또는 화면 좌표 기준으로 지정할 수 있습니다.
/// </summary>
public sealed class DragRequest
{

    /// <summary>드래그를 시작할 요소의 참조 번호. null이면 <see cref="SourceX"/>/<see cref="SourceY"/>를 화면 좌표로 해석.</summary>
    public int? SourceRef { get; set; }

    /// <summary>출발 X. <see cref="SourceRef"/>가 있으면 요소 내 상대 비율(0.0~1.0, 기본 0.5), 없으면 화면 픽셀 좌표.</summary>
    public double? SourceX { get; set; }

    /// <summary>출발 Y. <see cref="SourceRef"/>가 있으면 요소 내 상대 비율(0.0~1.0, 기본 0.5), 없으면 화면 픽셀 좌표.</summary>
    public double? SourceY { get; set; }

    /// <summary>드롭 대상 요소의 참조 번호. null이면 오프셋 또는 화면 좌표로 도착 지점을 결정.</summary>
    public int? TargetRef { get; set; }

    /// <summary>도착 X. <see cref="TargetRef"/>가 있으면 요소 내 상대 비율(기본 0.5), 대상과 오프셋이 모두 없으면 화면 픽셀 좌표.</summary>
    public double? TargetX { get; set; }

    /// <summary>도착 Y. <see cref="TargetRef"/>가 있으면 요소 내 상대 비율(기본 0.5), 대상과 오프셋이 모두 없으면 화면 픽셀 좌표.</summary>
    public double? TargetY { get; set; }

    /// <summary>출발 지점 기준 수평 이동 픽셀. <see cref="TargetRef"/>가 없을 때 사용.</summary>
    public double? OffsetX { get; set; }

    /// <summary>출발 지점 기준 수직 이동 픽셀. <see cref="TargetRef"/>가 없을 때 사용.</summary>
    public double? OffsetY { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}
