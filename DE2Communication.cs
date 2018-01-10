////////////////////////////////////////////////////////////////////////////////
// Module: DE2Communication
// Author: Artem Perepelitson
// Date  : 2018.01.10 23:23
// Brief : C# part of interface for connection Altera DE2 board to computer
////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace de2_communication
{
    public class DE2Data
    {
        public const byte TagMask = 0x7F;

        public DE2Data(byte tag7bit, byte[] data7bytes)
        {
            Tag7bit = tag7bit;
            SetData(data7bytes);
        }

        public DE2Data(byte tag7bit, byte data8bit, ushort data16bit, uint data32bit)
        {
            Tag7bit = tag7bit;
            Data8bit = data8bit;
            Data16bit = data16bit;
            Data32bit = data32bit;
        }
        private byte tag7bit;
        public byte Tag7bit
        {
            get { return tag7bit; }
            set { tag7bit = GetTag(value); }
        }
        private byte data8bit;
        public byte Data8bit
        {
            get { return data8bit; }
            set { data8bit = value; }
        }
        private ushort data16bit;
        public ushort Data16bit
        {
            get { return data16bit; }
            set { data16bit = value; }
        }
        private uint data32bit;
        public uint Data32bit
        {
            get { return data32bit; }
            set { data32bit = value; }
        }

        public byte[] GetData()
        {
            List<byte> data = new List<byte>();
            data.Add(data8bit);
            data.AddRange(BitConverter.GetBytes(data16bit));
            data.AddRange(BitConverter.GetBytes(data32bit));
            return data.ToArray();
        }
        public byte[] GetTagAndData()
        {
            // Tag max value and bytes order: 0x7F, 0x01, 0x0302, 0x07060504
            List<byte> data = new List<byte>();
            data.Add(tag7bit);
            data.Add(data8bit);
            data.AddRange(BitConverter.GetBytes(data16bit));
            data.AddRange(BitConverter.GetBytes(data32bit));
            return data.ToArray();
        }

        public void SetData(byte[] data7bytes)
        {
            Data8bit = data7bytes[0];
            Data16bit = BitConverter.ToUInt16(data7bytes, 1);
            Data32bit = BitConverter.ToUInt32(data7bytes, 3);
        }
        public void SetTagAndData(byte[] data8bytes)
        {
            Tag7bit = data8bytes[0];
            Data8bit = data8bytes[1];
            Data16bit = BitConverter.ToUInt16(data8bytes, 2);
            Data32bit = BitConverter.ToUInt32(data8bytes, 4);
        }

        static public bool GetFlagBit(byte flagAndTag)
        {
            return (byte)(flagAndTag & (~TagMask)) == (byte)(byte.MaxValue & (~TagMask));
        }
        static public byte GetTag(byte flagAndTag)
        {
            return (byte)(flagAndTag & TagMask); ;
        }
    }

    public class DE2Communication
    {
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_Close(uint ftHandle);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_GetQueueStatus(uint ftHandle, ref uint lpdwAmountInRxQueue);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_ListDevices(ref uint pvArg1, IntPtr pvArg2, uint dwFlags);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_Open(uint uiPort, ref uint ftHandle);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_Read(uint ftHandle, IntPtr lpBuffer, uint dwBytesToRead, ref uint lpdwBytesReturned);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_SetLatencyTimer(uint ftHandle, byte ucTimer);
        [DllImport("FTD2XX.dll")]
        private static extern unsafe int FT_Write(uint ftHandle, IntPtr lpBuffer, int dwBytesToWrite, ref uint lpdwBytesWritten);

        private const uint FT_LIST_NUMBER_ONLY = 0x80000000;
        private const uint FT_LIST_BY_INDEX = 0x40000000;
        private const uint FT_LIST_ALL = 0x20000000;
        private const int FT_OK = 0;

        private uint ftHandle;

        private uint portNumber;
        public int PortNumber
        {
            get { return (int)portNumber; }
        }

        private bool connected;
        public bool Connected
        {
            get { return connected; }
        }
        
        public int GetDeviceCount()
        {
            uint countOfDevices = 0;
            IntPtr pvArg2 = IntPtr.Zero;
            int resultStatus = FT_ListDevices(ref countOfDevices, pvArg2, FT_LIST_NUMBER_ONLY);
            return (int)countOfDevices;
        }

        private bool OpenConnection()
        {
            bool result = FT_Open(portNumber, ref ftHandle) == FT_OK;
            if (result)
            {
                FT_SetLatencyTimer(ftHandle, 2);
            }
            connected = result;
            return result;
        }

        private bool CheckConnection(byte flagAndTag)
        {
            UpdateConnection();
            Thread.Sleep(0);
            OpenConnection();
            byte[] bBuffer = {0x26, 0x27, 0x26, 0x81, 0x00};
            bBuffer[4] = flagAndTag;
            IntPtr lpBuffer = Marshal.AllocHGlobal(bBuffer.Length);
            Marshal.Copy(bBuffer, 0, lpBuffer, bBuffer.Length);
            uint dwBytesWritten = 0;
            int status = FT_Write(ftHandle, lpBuffer, 5, ref dwBytesWritten);
            Marshal.FreeHGlobal(lpBuffer);
            return status == FT_OK;
        }

        private bool UpdateConnection()
        {
            bool result;
            byte[] bBuffer = { 0x1F };
            IntPtr lpBuffer = Marshal.AllocHGlobal(bBuffer.Length);
            Marshal.Copy(bBuffer, 0, lpBuffer, bBuffer.Length);
            uint dwBytesWritten = 0;
            result = FT_Write(ftHandle, lpBuffer, 1, ref dwBytesWritten) == FT_OK;
            Marshal.FreeHGlobal(lpBuffer);
            if (result)
            {
                result = FT_Close(ftHandle) == FT_OK;
            }
            return result;
        }

        private bool ReceiveByte(out byte received)
        {
            received = 0;
            byte[] buffer = {0xC1, 0x00};
            IntPtr lpBuffer = Marshal.AllocHGlobal(buffer.Length);
            Marshal.Copy(buffer, 0, lpBuffer, buffer.Length);
            uint dwBytesWritten = 0;
            bool result = FT_Write(ftHandle, lpBuffer, 2, ref dwBytesWritten) == FT_OK;
            if (result)
            {
                result = FT_Read(ftHandle, lpBuffer, 1, ref dwBytesWritten) == FT_OK && dwBytesWritten == 1;
                Marshal.Copy(lpBuffer, buffer, 0, buffer.Length);
                received = buffer[0];
            }
            Marshal.FreeHGlobal(lpBuffer);
            return result;
        }

        public bool Receive(byte request7bit, out DE2Data data)
        {
            data = null;
            bool result = CheckConnection((byte)(0x80 | DE2Data.GetTag(request7bit))); // Reading flag and 7 bits of request
            if (result)
            {
                Thread.Sleep(0);
                byte tag;
                result = ReceiveByte(out tag);
                if (result && DE2Data.GetFlagBit(tag))
                {
                    byte[] received = new byte[7];
                    for (int i = 0; i < received.Length && result; i++)
                    {
                        result = ReceiveByte(out received[i]);
                    }
                    if (result)
                    {
                        data = new DE2Data(tag, received);
                    }
                }
            }
            return result;
        }

        public bool Send(DE2Data data)
        {
            bool result = CheckConnection((byte)(0x00 | data.Tag7bit)); // Writing flag and 7 bits of tag
            if (result)
            {
                List<byte> message = new List<byte>(new byte[] { 0x88, 0x83 });
                message.AddRange(data.GetData());
                IntPtr lpBuffer = Marshal.AllocHGlobal(message.Count);
                Marshal.Copy(message.ToArray(), 0, lpBuffer, message.Count);
                uint dwBytesWritten = 0;
                result = FT_Write(ftHandle, lpBuffer, message.Count, ref dwBytesWritten) == FT_OK;
                Marshal.FreeHGlobal(lpBuffer);
            }
            return result;
        }

        public DE2Communication(int openUSBPortNumber)
        {
            portNumber = (uint)openUSBPortNumber;
            ftHandle = 0;
            OpenConnection();
        }
    }
}
