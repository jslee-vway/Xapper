using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Xapper.TestApp;

/// <summary>
/// Xapper E2E 테스트용 샘플 WPF 창.
/// 왼쪽은 클릭/입력/토글 검증용 로그인 폼, 오른쪽은 드래그 검증용 영역(목록 간 드래그앤드롭,
/// 분할선 끌기, 캔버스 요소 이동)으로 구성된다.
/// </summary>
public partial class MainWindow : Window
{
    #region Fields

    /// <summary>목록 드래그 판정을 위해 기록해 두는 버튼 누름 지점.</summary>
    private Point _listDragOrigin;

    /// <summary>캔버스 요소를 끄는 중인지 여부.</summary>
    private bool _isBoxDragging;

    /// <summary>캔버스 요소를 잡은 지점의 요소 내부 오프셋.</summary>
    private Point _boxGrabOffset;

    /// <summary>드래그 경로 진단용 이벤트 발생 횟수.</summary>
    private int _downCount, _moveCount, _startCount, _overCount, _dropCount;

    /// <summary>드래그 시작 시점에 목록에서 선택돼 있던 항목 문구.</summary>
    private string _lastSelection = "none";

    /// <summary>투명 오버레이가 실제 마우스 클릭을 가로챈 횟수.</summary>
    private int _blockedCount;

    #endregion

    #region Constructor

    public MainWindow()
    {
        InitializeComponent();
    }

    #endregion

    #region Login

    /// <summary>사용자 이름 입력 여부에 따라 환영 문구 또는 오류 문구를 표시합니다.</summary>
    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var username = txtUsername.Text;
        var remember = chkRemember.IsChecked == true;

        if (string.IsNullOrWhiteSpace(username))
        {
            txtStatus.Text = "Please enter a username.";
            txtStatus.Foreground = System.Windows.Media.Brushes.Red;
        }
        else
        {
            txtStatus.Text = $"Welcome, {username}!" + (remember ? " (remembered)" : "");
            txtStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
    }

    /// <summary>
    /// 로그인 버튼을 덮고 있는 투명 오버레이가 실제 마우스 클릭을 가로챈 횟수를 기록합니다.
    /// 이벤트 발생 방식의 클릭은 히트테스트를 거치지 않으므로 이 값이 늘지 않는다.
    /// </summary>
    private void ClickBlocker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _blockedCount++;
        txtBlockerHits.Text = $"blocked={_blockedCount}";
    }

    #endregion

    #region Drag And Drop

    /// <summary>드래그 임계값 판정의 기준점을 기록합니다.</summary>
    private void LstSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _listDragOrigin = e.GetPosition(null);
        _downCount++;
        UpdateDragLog();
    }

    /// <summary>
    /// 버튼을 누른 채 시스템 드래그 임계값을 넘어 움직이면 선택된 항목으로 드래그앤드롭을 시작합니다.
    /// </summary>
    private void LstSource_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        _moveCount++;

        var moved = _listDragOrigin - e.GetPosition(null);
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            UpdateDragLog();
            return;
        }

        _lastSelection = (lstSource.SelectedItem as ListBoxItem)?.Content?.ToString() ?? "null";
        UpdateDragLog();

        if (lstSource.SelectedItem is not ListBoxItem selected)
            return;

        var payload = selected.Content?.ToString();
        if (string.IsNullOrEmpty(payload))
            return;

        _startCount++;
        UpdateDragLog();
        DragDrop.DoDragDrop(lstSource, payload, DragDropEffects.Copy);
    }

    /// <summary>문자열 데이터가 실려 있을 때만 복사 효과를 허용합니다.</summary>
    private void LstTarget_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
        _overCount++;
        UpdateDragLog();
    }

    /// <summary>드롭된 항목을 대상 목록에 추가하고 결과 문구를 갱신합니다.</summary>
    private void LstTarget_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is not string payload)
            return;

        lstTarget.Items.Add(payload);
        txtDropStatus.Text = payload;
        _dropCount++;
        UpdateDragLog();
    }

    /// <summary>드래그 경로에서 어느 단계까지 도달했는지 진단 문구로 갱신합니다.</summary>
    private void UpdateDragLog()
    {
        txtDragLog.Text =
            $"down={_downCount} move={_moveCount} start={_startCount} over={_overCount} drop={_dropCount} sel={_lastSelection}";
    }

    #endregion

    #region Splitter

    /// <summary>분할선 이동으로 바뀐 원본 목록의 실제 너비를 표시합니다.</summary>
    private void LstSource_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        txtSourceWidth.Text = $"{lstSource.ActualWidth:F0}";
    }

    #endregion

    #region Canvas Box

    /// <summary>캔버스 요소를 잡고 마우스를 캡처합니다.</summary>
    private void DragBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isBoxDragging = true;
        _boxGrabOffset = e.GetPosition(dragBox);
        dragBox.CaptureMouse();
    }

    /// <summary>잡은 지점을 유지한 채 캔버스 요소를 따라 이동시킵니다.</summary>
    private void DragBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isBoxDragging)
            return;

        var cursor = e.GetPosition(cnvBoard);
        Canvas.SetLeft(dragBox, cursor.X - _boxGrabOffset.X);
        Canvas.SetTop(dragBox, cursor.Y - _boxGrabOffset.Y);
        UpdateBoxPosition();
    }

    /// <summary>캔버스 요소를 놓고 마우스 캡처를 해제합니다.</summary>
    private void DragBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isBoxDragging)
            return;

        _isBoxDragging = false;
        dragBox.ReleaseMouseCapture();
        UpdateBoxPosition();
    }

    /// <summary>캔버스 요소의 현재 좌표를 문구로 갱신합니다.</summary>
    private void UpdateBoxPosition()
    {
        txtBoxPosition.Text = $"Box: {Canvas.GetLeft(dragBox):F0},{Canvas.GetTop(dragBox):F0}";
    }

    #endregion
}
