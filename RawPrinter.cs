using System.Runtime.InteropServices;

namespace FafoshopPrintAgentNet;

/// <summary>
/// Gửi byte thô thẳng tới máy in qua Win32 <c>WritePrinter</c> (chế độ RAW,
/// bỏ qua hoàn toàn GDI/driver rendering) — dùng cho
/// <see cref="PrinterService.PrintRaw"/>, tương đương đường
/// <c>DocFlavor.BYTE_ARRAY.AUTOSENSE</c> của bản Java tham chiếu
/// (<c>PrinterService.print()</c>). CHỈ dùng để test nhanh bằng curl, KHÔNG
/// dùng cho bill/nhãn thật (xem README.md).
/// </summary>
internal static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] ref DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static void SendBytesToPrinter(string printerName, byte[] bytes)
    {
        if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
        {
            throw new PrinterService.PrinterNotFoundException(printerName);
        }

        try
        {
            var docInfo = new DOCINFOA
            {
                pDocName = "fafoshop-print-agent raw content",
                pOutputFile = null,
                pDataType = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                throw new InvalidOperationException($"StartDocPrinter thất bại (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    throw new InvalidOperationException($"StartPagePrinter thất bại (Win32 error {Marshal.GetLastWin32Error()}).");
                }

                try
                {
                    IntPtr unmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, unmanagedBytes, bytes.Length);
                        if (!WritePrinter(hPrinter, unmanagedBytes, bytes.Length, out int written) || written != bytes.Length)
                        {
                            throw new InvalidOperationException($"WritePrinter thất bại (Win32 error {Marshal.GetLastWin32Error()}).");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(unmanagedBytes);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
