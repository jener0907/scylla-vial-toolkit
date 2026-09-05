using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScyllaConfigurator;

public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    short InputReportLength,
    short OutputReportLength,
    short UsagePage,
    short Usage);

public sealed class HidDevice : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private static readonly Guid HidGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private const short RawUsagePage = unchecked((short)0xFF60);
    private const short RawUsage = 0x61;
    private SafeFileHandle _handle;
    private readonly object _ioLock = new();

    public HidDeviceInfo Info { get; }
    public int InputReportLength { get; }
    public int OutputReportLength { get; }

    private HidDevice(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
        InputReportLength = Math.Max(32, (int)info.InputReportLength);
        OutputReportLength = Math.Max(32, (int)info.OutputReportLength);
    }

    public static IReadOnlyList<HidDeviceInfo> FindRaw()
    {
        var result = new List<HidDeviceInfo>();
        var hidGuid = HidGuid;
        var set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref iface)) break;
                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                if (required == 0) continue;
                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, required, ref required, IntPtr.Zero)) continue;
                    // cbSize is 8 on x64, but the variable-length UTF-16
                    // DevicePath still starts immediately after the 4-byte DWORD.
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    // Enumeration only needs metadata. Opening every HID interface with
                    // read/write access makes Windows hide otherwise valid Raw HID devices.
                    using var h = Open(path, 0);
                    if (h is null) continue;
                    if (!GetAttributes(h, out var attrs)) continue;
                    GetCaps(h, out var input, out var output, out var usagePage, out var usage);
                    var isVialUsage = usagePage == RawUsagePage && usage == RawUsage;
                    var isVialReportSize = (input is 32 or 33) && (output is 32 or 33);
                    if (!isVialUsage && !isVialReportSize) continue;
                    result.Add(new HidDeviceInfo(path, attrs.VendorID, attrs.ProductID, input, output, usagePage, usage));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    public static HidDevice Open(HidDeviceInfo info)
    {
        var handle = Open(info.Path, GenericRead | GenericWrite) ?? throw new Win32Exception(Marshal.GetLastWin32Error(), "HID 장치를 열 수 없습니다.");
        return new HidDevice(info, handle);
    }

    private static SafeFileHandle? Open(string path, uint access)
    {
        var h = CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Dispose();
            return null;
        }
        return h;
    }

    public byte[] Exchange(byte[] packet)
    {
        if (packet.Length != 32) throw new ArgumentException("HID 패킷은 32바이트여야 합니다.");
        lock (_ioLock)
        {
            var output = new byte[OutputReportLength];
            Buffer.BlockCopy(packet, 0, output, OutputReportLength == 33 ? 1 : 0, Math.Min(32, output.Length - (OutputReportLength == 33 ? 1 : 0)));
            if (!WriteFile(_handle, output, output.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HID 쓰기 실패");
            var input = new byte[InputReportLength];
            if (!ReadFile(_handle, input, input.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HID 읽기 실패");
            var response = new byte[32];
            Buffer.BlockCopy(input, InputReportLength == 33 ? 1 : 0, response, 0, Math.Min(32, input.Length - (InputReportLength == 33 ? 1 : 0)));
            return response;
        }
    }

    public void Dispose() => _handle.Dispose();

    private static bool GetAttributes(SafeFileHandle h, out HiddAttributes attrs)
    {
        attrs = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        return HidD_GetAttributes(h, ref attrs);
    }

    private static void GetCaps(SafeFileHandle h, out short input, out short output, out short usagePage, out short usage)
    {
        input = 32; output = 32; usagePage = 0; usage = 0;
        if (!HidD_GetPreparsedData(h, out var data)) return;
        try
        {
            // HidP_GetCaps returns an NTSTATUS; every non-negative value is success.
            if (HidP_GetCaps(data, out var caps) >= 0)
            {
                input = caps.InputReportByteLength;
                output = caps.OutputReportByteLength;
                usagePage = caps.UsagePage;
                usage = caps.Usage;
            }
        }
        finally { HidD_FreePreparsedData(data); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct HiddAttributes { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] private struct HidpCaps
    {
        public short Usage; public short UsagePage; public short InputReportByteLength; public short OutputReportByteLength; public short FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public short[] Reserved;
        public short NumberLinkCollectionNodes; public short NumberInputButtonCaps; public short NumberInputValueCaps; public short NumberInputDataIndices;
        public short NumberOutputButtonCaps; public short NumberOutputValueCaps; public short NumberOutputDataIndices; public short NumberFeatureButtonCaps;
        public short NumberFeatureValueCaps; public short NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SpDeviceInterfaceData DeviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SpDeviceInterfaceData DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, int count, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int count, out int written, IntPtr overlapped);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);
}
