using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ModTheSpire2.Bootstrap;

internal sealed class NativePicture : IDisposable
{
    private readonly IPicture _picture;

    private NativePicture(IPicture picture)
    {
        _picture = picture;
    }

    public IntPtr Handle => new(_picture.Handle);

    public static NativePicture? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);

        try
        {
            CreateStreamOnHGlobal(memory, true, out var stream);
            var iid = typeof(IPicture).GUID;
            var result = OleLoadPicture(stream, bytes.Length, false, ref iid, out var picture);
            Marshal.ReleaseComObject(stream);
            if (result != 0 || picture is null) return null;
            return new NativePicture(picture);
        }
        catch
        {
            Marshal.FreeHGlobal(memory);
            throw;
        }
    }

    public void Dispose()
    {
        Marshal.FinalReleaseComObject(_picture);
    }

    [ComImport]
    [Guid("7BF80980-BF32-101A-8BBB-00AA00300CAB")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IPicture
    {
        [DispId(0)]
        int Handle { get; }
    }

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool deleteOnRelease, out IStream stream);

    [DllImport("oleaut32.dll")]
    private static extern int OleLoadPicture(
        IStream stream,
        int size,
        bool runMode,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPicture picture);
}
