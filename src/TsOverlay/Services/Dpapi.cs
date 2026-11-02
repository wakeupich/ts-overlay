using System.Runtime.InteropServices;
using System.Text;

namespace TsOverlay.Services;

/// <summary>
/// DPAPI (Windows ProtectedData) без NuGet-зависимостей: шифруем ключи API
/// так, что расшифровать их может только текущий пользователь на этой машине.
/// </summary>
public static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = Transform(data, protect: true);
            return "dpapi:" + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plain;
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        if (!stored.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            return stored;
        }

        try
        {
            var bytes = Convert.FromBase64String(stored["dpapi:".Length..]);
            var plain = Transform(bytes, protect: false);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            inBlob.Data = inHandle.AddrOfPinnedObject();
            inBlob.Size = input.Length;

            var ok = protect
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob);

            if (!ok)
            {
                throw new InvalidOperationException("DPAPI вернул ошибку " + Marshal.GetLastWin32Error());
            }

            var result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
            return result;
        }
        finally
        {
            if (outBlob.Data != IntPtr.Zero)
            {
                LocalFree(outBlob.Data);
            }

            inHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
