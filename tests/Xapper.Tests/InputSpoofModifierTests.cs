using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// GetKeyState 후크로 수식키를 스푸프하면 WPF <see cref="Keyboard.Modifiers"/> 가 눌림으로 보는지 검증한다.
/// WPF 는 Key.LeftCtrl/RightCtrl(VK_LCONTROL/VK_RCONTROL)로 묻는다 — 일반 VK_CONTROL 만 답하면 None 이 나와
/// 이전에 "수식키 합성 불가"로 오판했던 그 사실을 여기서 고정한다. 후크는 프로세스 전역·영구라 STA 스레드에서
/// InputManager 를 세워 읽는다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class InputSpoofModifierTests
{
    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift)]
    public void BeginModifiers_MakesWpfSeeThoseModifiersHeld(ModifierKeys requested)
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return; // 후킹이 안 되는 환경에서는 검증 대상이 없다.

            try
            {
                InputSpoof.BeginModifiers(requested);
                Assert.Equal(requested, Keyboard.Modifiers);
            }
            finally
            {
                InputSpoof.EndModifiers();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    [Fact]
    public void EndModifiers_StopsSpoofing()
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            try
            {
                InputSpoof.BeginModifiers(ModifierKeys.Control);
                InputSpoof.EndModifiers();
                // 스푸프를 끄면 실제 키보드 상태로 돌아간다(테스트 중 사람이 Ctrl 을 누르고 있지 않다고 가정).
                Assert.False(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }
}
