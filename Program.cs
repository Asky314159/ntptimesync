using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NtpTimeSync
{
    class Program
    {
        static void Main(string[] args)
        {
            SetSystemTime(GetNetworkTime());
        }

        private static DateTime GetNetworkTime()
        {
            //default Windows time server
            const string ntpServer = "time.windows.com";

            // NTP message size - 16 bytes of the digest (RFC 2030)
            var ntpData = new byte[48];

            //Setting the Leap Indicator, Version Number and Mode values
            ntpData[0] = 0x1B; //LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

            var addresses = Dns.GetHostEntry(ntpServer).AddressList;

            //The UDP port number assigned to NTP is 123
            var ipEndPoint = new IPEndPoint(addresses[0], 123);
            //NTP uses UDP

            using (var socket = new Socket(addresses[0].AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect(ipEndPoint);

                //Stops code hang if NTP is blocked
                socket.ReceiveTimeout = 3000;

                socket.Send(ntpData);
                socket.Receive(ntpData);
                socket.Close();
            }

            //Offset to get to the "Transmit Timestamp" field (time at which the reply 
            //departed the server for the client, in 64-bit timestamp format."
            const byte serverReplyTime = 40;

            //Get the seconds part
            ulong intPart = BitConverter.ToUInt32(ntpData, serverReplyTime);

            //Get the seconds fraction
            ulong fractPart = BitConverter.ToUInt32(ntpData, serverReplyTime + 4);

            //Convert From big-endian to little-endian
            intPart = SwapEndianness(intPart);
            fractPart = SwapEndianness(fractPart);

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

            //**UTC** time
            var networkDateTime = (new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds((long)milliseconds);

            return networkDateTime;
        }

        // stackoverflow.com/a/3294698/162671
        private static uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) +
                           ((x & 0x0000ff00) << 8) +
                           ((x & 0x00ff0000) >> 8) +
                           ((x & 0xff000000) >> 24));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        private static void SetSystemTime(DateTime time)
        {
            SystemTime systemTime = new SystemTime();
            systemTime.wYear = (ushort)time.Year;
            systemTime.wMonth = (ushort)time.Month;
            systemTime.wDayOfWeek = 0; // Ignored by SetSystemTime
            systemTime.wDay = (ushort)time.Day;
            systemTime.wHour = (ushort)time.Hour;
            systemTime.wMinute = (ushort)time.Minute;
            systemTime.wSecond = (ushort)time.Second;
            systemTime.wMilliseconds = (ushort)time.Millisecond;

            IntPtr timePtr = Marshal.AllocHGlobal(Marshal.SizeOf(systemTime));
            Marshal.StructureToPtr(systemTime, timePtr, true);

            NativeMethods.SetSystemTime(timePtr);

            Marshal.FreeHGlobal(timePtr);
        }

        private static class NativeMethods
        {
            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetSystemTime(IntPtr lpSystemTime);
        }
    }
}
