using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace XrpadDetector
{
    public class Detector : IDisposable
    {
        static Detector()
        {
            var errorCode = Acquisition_EnableLogging(true);
            CheckError(errorCode);
            errorCode = Acquisition_SetFileLogging($"Log-{DateTime.Today:yyyyMMdd}.txt", true);
            CheckError(errorCode);
            errorCode = Acquisition_SetLogLevel(XislLoggingLevels.LEVEL_INFO);
            CheckError(errorCode);
            errorCode = Acquisition_TogglePerformanceLogging(true);
            CheckError(errorCode);
        }

        public static IEnumerable<DetectorInfo> GetInfos()
        {
            try
            {
                var errorCode = Acquisition_GbIF_GetDeviceCnt(out int deviceCount);
                CheckError(errorCode);
                if (deviceCount > 0)
                {
                    var deviceParams = new GBIF_DEVICE_PARAM[deviceCount];
                    errorCode = Acquisition_GbIF_GetDeviceList(deviceParams, deviceCount);
                    CheckError(errorCode);
                    return deviceParams.Select(deviceParam => deviceParam.ToInfo());
                }
            }
            catch
            {
            }
            return Enumerable.Empty<DetectorInfo>();
        }

        public Detector(string name)
        {
            try
            {
                Initialize(name);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        IntPtr m_AcqDesc;

        CallbackDelegate m_EndFrameCallback;
        CallbackDelegate m_EndAcqCallback;

        UnmanagedBuffer m_ImageData;
        int m_ImageWidth;
        int m_ImageHeight;
        int m_ImagePitch;

        UnmanagedBuffer m_OffsetMap;
        UnmanagedBuffer m_GainMap;
        UnmanagedBuffer m_PixelMap;

        private void Initialize(string name)
        {
            var errorCode = Acquisition_GbIF_Init(out m_AcqDesc, 0, true, 0, 0, true, false, HIS_GbIF_NAME, name);
            CheckError(errorCode);
            errorCode = Acquisition_GbIF_GetDevice(name, HIS_GbIF_NAME, out GBIF_DEVICE_PARAM deviceParam);
            CheckError(errorCode);
            Info = deviceParam.ToInfo();

            m_EndFrameCallback = EndFrameCallback;
            m_EndAcqCallback = EndAcqCallback;
            errorCode = Acquisition_SetCallbacksAndMessages(m_AcqDesc, IntPtr.Zero, 0, 0, Marshal.GetFunctionPointerForDelegate(m_EndFrameCallback),
                Marshal.GetFunctionPointerForDelegate(m_EndAcqCallback));
            CheckError(errorCode);

            errorCode = Acquisition_SetCameraTriggerMode(m_AcqDesc, 3);
            CheckError(errorCode);

            var info = new uint[24];
            var infoEx = new ushort[32];
            errorCode = Acquisition_GetHwHeaderInfoEx(m_AcqDesc, info, infoEx);
            CheckError(errorCode);
            m_ImageWidth = infoEx[5];
            m_ImageHeight = infoEx[4];
            m_ImagePitch = infoEx[2];
            m_ImageData = new UnmanagedBuffer(m_ImageWidth * m_ImageHeight * 2);
            errorCode = Acquisition_DefineDestBuffers(m_AcqDesc, m_ImageData, 1, m_ImageHeight, m_ImageWidth);
            CheckError(errorCode);
        }

        public DetectorInfo Info { get; private set; }

        public void Dispose()
        {
            if (m_AcqDesc != IntPtr.Zero)
            {
                var errorCode = Acquisition_Close(m_AcqDesc);
                CheckError(errorCode);
                m_AcqDesc = IntPtr.Zero;
            }

            if (m_ImageData != null)
            {
                m_ImageData.Dispose();
                m_ImageData = null;
            }

            ClearOffsetMap();
            ClearGainMap();
            ClearPixelMap();
        }

        public void SetAedMode(bool mode)
        {
            var errorCode = Acquisition_SetFrameSyncMode(m_AcqDesc, mode ? HIS_SYNCMODE_AUTO_TRIGGER : HIS_SYNCMODE_SOFT_TRIGGER);
            CheckError(errorCode);
        }

        int m_AcquisitionTime;

        public void SetAcquisitionTime(int ms)
        {
            var errorCode = Acquisition_SetFrameSyncTimeMode(m_AcqDesc, 0, ms);
            CheckError(errorCode);
            m_AcquisitionTime = ms;
        }

        public async void StartAcquisition(int frameCount)
        {
            var errorCode = Acquisition_Acquire_Image(m_AcqDesc, frameCount, 0, HIS_SEQ_AVERAGE, m_OffsetMap, m_GainMap, m_PixelMap);
            CheckError(errorCode);
            await LoopFramesAsync(frameCount);

            ImageReady?.Invoke(this, new ImageEventArgs(m_ImageData, m_ImageWidth, m_ImageHeight, m_ImagePitch));
        }

        public async void StartOffsetCalibration(int frameCount)
        {
            ClearOffsetMap();
            m_OffsetMap = new UnmanagedBuffer(m_ImageWidth * m_ImageHeight * 2);

            var errorCode = Acquisition_Acquire_OffsetImage(m_AcqDesc, m_OffsetMap, m_ImageHeight, m_ImageWidth, frameCount);
            CheckError(errorCode);
            await LoopFramesAsync(frameCount);
        }

        public async void StartGainCalibration(int frameCount)
        {
            ClearGainMap();
            m_GainMap = new UnmanagedBuffer(m_ImageWidth * m_ImageHeight * 4);

            var errorCode = Acquisition_Acquire_GainImage(m_AcqDesc, m_OffsetMap, m_GainMap, m_ImageHeight, m_ImageWidth, frameCount);
            CheckError(errorCode);
            await LoopFramesAsync(frameCount);
        }

        private void LoopFrames(int frameCount)
        {
            for (var i = 0; i < frameCount; ++i)
            {
                var errorCode = Acquisition_SetFrameSync(m_AcqDesc);
                CheckError(errorCode);
                Thread.Sleep(m_AcquisitionTime + 1000);
            }
        }

        private Task LoopFramesAsync(int frameCount) => new Task(() => LoopFrames(frameCount));

        public void LoadOffsetMap(string filePath)
        {
            ClearOffsetMap();
            m_OffsetMap = UnmanagedBuffer.Load(filePath);
        }

        public void LoadGainMap(string filePath)
        {
            ClearGainMap();
            m_GainMap = UnmanagedBuffer.Load(filePath);
        }

        public void LoadPixelMap(string filePath)
        {
            ClearPixelMap();
            m_PixelMap = UnmanagedBuffer.Load(filePath, 100);
        }

        public void SaveOffsetMap(string filePath)
        {
            if (m_OffsetMap != null)
            {
                m_OffsetMap.Save(filePath);
            }
        }

        public void SaveGainMap(string filePath)
        {
            if (m_GainMap != null)
            {
                m_GainMap.Save(filePath);
            }
        }

        public void ClearOffsetMap()
        {
            if (m_OffsetMap != null)
            {
                m_OffsetMap.Dispose();
                m_OffsetMap = null;
            }
        }

        public void ClearGainMap()
        {
            if (m_GainMap != null)
            {
                m_GainMap.Dispose();
                m_GainMap = null;
            }
        }

        public void ClearPixelMap()
        {
            if (m_PixelMap != null)
            {
                m_PixelMap.Dispose();
                m_PixelMap = null;
            }
        }

        private void EndFrameCallback(IntPtr acqDesc)
        {
        }

        private void EndAcqCallback(IntPtr acqDesc)
        {
            Acquired?.Invoke(this, EventArgs.Empty);
        }

        private static void CheckError(int errorCode)
        {
            if (errorCode != 0)
            {
                throw new InvalidOperationException($"Detector error #{errorCode}");
            }
        }

        public event EventHandler<ImageEventArgs> ImageReady;

        public event EventHandler Acquired;

        #region P/Invoke

        [DllImport("XISL.dll")]
        private extern static int Acquisition_EnableLogging(bool onOff);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetLogLevel(XislLoggingLevels logLevel);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_TogglePerformanceLogging(bool onOff);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetFileLogging(string filename, bool enableLogging);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_GbIF_GetDeviceCnt(out int deviceCount);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_GbIF_GetDeviceList([Out] GBIF_DEVICE_PARAM[] deviceParams, int deviceCount);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_GbIF_GetDevice(string address, int addressType, out GBIF_DEVICE_PARAM deviceParam);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_GbIF_Init(out IntPtr acqDesc, int channelNum, bool enableIrq, int rows, int columns, bool selfInit, bool alwaysOpen, int initType,
            [MarshalAs(UnmanagedType.LPStr)] string address);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_Close(IntPtr acqDesc);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetCallbacksAndMessages(IntPtr acqDesc, IntPtr hwnd, int errorMsg, int loosingFramesMsg, IntPtr endFrameCallback, IntPtr endAcqCallback);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetCameraTriggerMode(IntPtr acqDesc, int mode);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetFrameSyncMode(IntPtr acqDesc, int mode);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetFrameSyncTimeMode(IntPtr acqDesc, int mode, int delayTime);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_SetFrameSync(IntPtr acqDesc);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_GetHwHeaderInfoEx(IntPtr acqDesc, [Out] uint[] info, [Out] ushort[] infoEx);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_DefineDestBuffers(IntPtr acqDesc, IntPtr processedData, int frames, int rows, int columns);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_Acquire_Image(IntPtr acqDesc, int frames, int skipFrames, int opt, IntPtr offsetData, IntPtr gainData, IntPtr pixelData);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_Acquire_OffsetImage(IntPtr acqDesc, IntPtr offsetData, int rows, int columns, int frames);

        [DllImport("XISL.dll")]
        private extern static int Acquisition_Acquire_GainImage(IntPtr acqDesc, IntPtr offsetData, IntPtr gainData, int rows, int columns, int frames);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct GBIF_DEVICE_PARAM
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string MacAddress;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string IP;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string SubnetMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Gateway;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string AdapterIP;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string AdapterMask;
            public uint IPCurrentBootOptions;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ManufacturerName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ModelName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string GBIFFirmwareVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string DeviceName;

            public DetectorInfo ToInfo() => new DetectorInfo { Model = ModelName, Name = DeviceName, Mac = MacAddress, IP = IP };
        }

        const int HIS_GbIF_IP = 1;
        const int HIS_GbIF_MAC = 2;
        const int HIS_GbIF_NAME = 3;

        const int HIS_SYNCMODE_SOFT_TRIGGER = 1;
        const int HIS_SYNCMODE_INTERNAL_TIMER = 2;
        const int HIS_SYNCMODE_EXTERNAL_TRIGGER = 3;
        const int HIS_SYNCMODE_FREE_RUNNING = 4;
        const int HIS_SYNCMODE_AUTO_TRIGGER = 8;

        const int HIS_SEQ_TWO_BUFFERS = 0x01;
        const int HIS_SEQ_ONE_BUFFER = 0x02;
        const int HIS_SEQ_AVERAGE = 0x04;
        const int HIS_SEQ_DEST_ONE_FRAME = 0x08;

        delegate void CallbackDelegate(IntPtr acqDesc);

        enum XislLoggingLevels
        {
            LEVEL_TRACE,
            LEVEL_DEBUG,
            LEVEL_INFO,
            LEVEL_WARN,
            LEVEL_ERROR,
            LEVEL_FATAL,
            LEVEL_ALL,
            LEVEL_NONE
        }

        #endregion
    }
}
