using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClaudeUsageMonitor.Controls;

/// <summary>
/// Turns the Windows 10/11 blur-behind effect on or off for a window, for a frosted-glass look.
/// It uses SetWindowCompositionAttribute, which is undocumented; if it is missing the window simply stays unblurred.
/// </summary>
internal static class GlassEffect
{
    private const int AccentPolicyAttribute = 19;

    private enum AccentState
    {
        Disabled = 0,
        BlurBehind = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttributeData data);

    /// <summary>
    /// Enables or disables blur behind the window. Call after the window has a handle (for example in SourceInitialized).
    /// </summary>
    /// <returns>True when the call worked.</returns>
    public static bool SetBlur(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var policy = new AccentPolicy { State = enabled ? AccentState.BlurBehind : AccentState.Disabled };
        var size = Marshal.SizeOf(policy);
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new CompositionAttributeData { Attribute = AccentPolicyAttribute, Data = pointer, SizeOfData = size };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
