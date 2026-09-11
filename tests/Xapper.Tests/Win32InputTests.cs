using System.Runtime.InteropServices;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>SendInput 이 요구하는 INPUT 크기(union 포함)가 Win32 정의와 같은지 고정한다. 틀리면 SendInput 이 조용히 실패한다.</summary>
public class Win32InputTests
{
    [Fact]
    public void InputStruct_HasTheWin32Size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<Win32Input.INPUT>());
    }
}
